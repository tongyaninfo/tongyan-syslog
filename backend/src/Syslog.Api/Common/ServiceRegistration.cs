using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Syslog.Api.Options;
using Syslog.Api.Service;
using Syslog.Api.Service.impl;
using Syslog.Api.Workers;
using Syslog.Engine.Service.impl;
using Syslog.Ingest.Models;
using Syslog.Ingest.Options;
using Syslog.Ingest.Service;
using Syslog.Ingest.Service.impl;
using Syslog.Ingest.Workers;
using Syslog.Shared.Models;
using Syslog.Shared.Service;

namespace Syslog.Api.Common;

public static class ServiceRegistration
{
    public static IServiceCollection AddSyslog(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.Configure<ControlStoreOptions>(configuration.GetSection("ControlStore"));
        services.AddSingleton(sp => new ControlStore(sp.GetRequiredService<IOptions<ControlStoreOptions>>().Value, sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IControlStore>(sp => sp.GetRequiredService<ControlStore>());
        services.AddSingleton<ISourceRegistry>(sp => sp.GetRequiredService<IControlStore>());
        services.AddSingleton<IIdService>(sp => sp.GetRequiredService<IControlStore>());
        services.AddSingleton<INotificationStore>(sp => sp.GetRequiredService<IControlStore>() as INotificationStore
            ?? sp.GetRequiredService<ControlStore>());
        services.AddSingleton<NotificationSecrets>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<MailContentBuilder>();
        services.AddSingleton<SmtpMailSender>();
        services.AddHostedService<NotificationWorker>();
        services.Configure<ClickHouseOptions>(configuration.GetSection(ClickHouseOptions.SectionName));
        services.AddSingleton<ClickHouseStorageState>();
        services.AddSingleton<LogClearService>();
        services.AddHttpClient<ILogStore, ClickHouseLogStore>(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        services.Configure<IngestOptions>(configuration.GetSection("Ingest"));
        services.Configure<IngestOptions>(options =>
        {
            var networks = ReadAllowedNetworks(configuration);
            if (networks is not null) options.UnregisteredDevices.AllowedNetworks = networks;
        });
        services.AddSingleton<ISyslogParser, SyslogParser>();
        services.AddSingleton<IngestStatus>();
        services.AddSingleton<IngressQueue>();
        services.AddSingleton<ILiveLogBuffer, LiveLogBuffer>();
        services.AddSingleton<ILiveLogSettingsStore>(sp => sp.GetRequiredService<IControlStore>() as ILiveLogSettingsStore
            ?? sp.GetRequiredService<ControlStore>());
        services.AddSingleton<LiveLogConfigurationService>();
        services.AddHostedService(sp => sp.GetRequiredService<LiveLogConfigurationService>());
        services.AddSingleton<IUnregisteredDeviceBuffer, UnregisteredDeviceBuffer>();
        services.AddSingleton(sp =>
        {
            var environment = sp.GetRequiredService<IWebHostEnvironment>();
            var configuredDirectory = configuration["HttpsCertificate:Directory"];
            var directory = Path.GetFullPath(string.IsNullOrWhiteSpace(configuredDirectory)
                ? Path.Combine("run-data", "https-certificate") : configuredDirectory, environment.ContentRootPath);
            if (environment.WebRootPath is { Length: > 0 } webRoot)
            {
                var relative = Path.GetRelativePath(webRoot, directory);
                if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new InvalidOperationException("HTTPS 证书目录必须位于静态文件根目录之外。");
            }
            return new SystemCertificateStore(directory, sp.GetRequiredService<TimeProvider>())
            {
                CanUpload = !SystemHttpsStartup.HasExternalCertificate(configuration)
            };
        });
        services.AddSingleton<IListenerCertificateProvider>(sp => sp.GetRequiredService<SystemCertificateStore>());
        services.AddSingleton<SystemHttpsStartup>();
        services.AddSingleton<ListenerConfigurationService>();
        services.AddSingleton<IListenerConfigurationService>(sp => sp.GetRequiredService<ListenerConfigurationService>());
        services.AddSingleton<IEventSpool, FileEventSpool>();
        services.AddSingleton<IngestWorker>();
        services.AddSingleton<ReceptionRuntime>();
        services.AddSingleton<IReceptionRuntime>(sp => sp.GetRequiredService<ReceptionRuntime>());
        services.AddHostedService(sp => sp.GetRequiredService<ReceptionRuntime>());
        services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = false;
            options.MaximumReceiveMessageSize = 1024;
            options.MaximumParallelInvocationsPerClient = 1;
        }).AddHubOptions<Syslog.Api.Hubs.LiveLogsHub>(options => options.MaximumReceiveMessageSize = 8192);
        services.AddSingleton<LiveLogFeed>();
        services.AddHostedService<LiveLogPushWorker>();
        services.AddSingleton<OverviewDatabaseSize>();
        services.AddSingleton<OverviewFeed>();
        services.AddHostedService<OverviewPushWorker>();
        services.AddAuthentication(SessionAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationHandler.SchemeName, _ => { });
        services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        services.AddRateLimiter(options =>
        {
            options.AddPolicy("setup", _ => RateLimitPartition.GetFixedWindowLimiter("global-setup", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy("user-management", context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy("login", _ => RateLimitPartition.GetFixedWindowLimiter("global-login", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy("password-change", context => RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy("https-certificate", _ => RateLimitPartition.GetFixedWindowLimiter(
                "https-certificate", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy("notification-test", _ => RateLimitPartition.GetFixedWindowLimiter(
                "notification-test", _ => new FixedWindowRateLimiterOptions
                { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            options.AddPolicy("search", _ => RateLimitPartition.GetConcurrencyLimiter("global-search", _ => new ConcurrencyLimiterOptions
                { PermitLimit = 4, QueueLimit = 0 }));
            options.OnRejected = async (context, token) =>
            {
                context.HttpContext.Response.StatusCode = 429;
                await context.HttpContext.Response.WriteAsJsonAsync(ApiResult<object>.Fail("rate_limited", "请求过于频繁，请稍后重试。"), token);
            };
        });
        services.AddControllers().AddApplicationPart(typeof(ServiceRegistration).Assembly);
        services.Configure<ApiBehaviorOptions>(options => options.InvalidModelStateResponseFactory = _ =>
            new BadRequestObjectResult(ApiResult<object>.Fail("invalid_request", "请求参数无效。")));
        services.AddSyslogMcp();
        return services;
    }

    private static string[]? ReadAllowedNetworks(IConfiguration configuration)
    {
        const string path = "Ingest:UnregisteredDevices:AllowedNetworks";
        // 许可网段必须整体替换。常规绑定会追加属性默认数组，跨配置层还会保留未覆盖的数组索引。
        if (configuration is IConfigurationRoot root)
        {
            foreach (var provider in root.Providers.Reverse())
            {
                var keys = provider.GetChildKeys([], path).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (keys.Length > 0)
                    return keys.Select(key => provider.TryGet(ConfigurationPath.Combine(path, key), out var network) && network is not null
                        ? network : throw new ArgumentException("未登记设备许可网段必须配置为 CIDR 数组。")).ToArray();
                if (provider.TryGet(path, out var value))
                    return string.IsNullOrEmpty(value) ? [] : throw new ArgumentException("未登记设备许可网段必须配置为 CIDR 数组。");
            }
            return null;
        }

        var section = configuration.GetSection("Ingest:UnregisteredDevices").GetChildren()
            .FirstOrDefault(item => string.Equals(item.Key, "AllowedNetworks", StringComparison.OrdinalIgnoreCase));
        return section is null ? null : section.Get<string[]>() ?? [];
    }
}
