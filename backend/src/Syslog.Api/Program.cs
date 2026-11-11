using Syslog.Api.Common;
using Syslog.Api.Middleware;
using Syslog.Api.Hubs;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.Http.Connections;
using Syslog.Shared.Models;
using Syslog.Api.Service.impl;

var initialize = args.Contains("--initialize-control", StringComparer.Ordinal);
var bootstrap = args.Contains("--bootstrap-admin", StringComparer.Ordinal);
var migrateListeners = args.Contains("--migrate-listeners", StringComparer.Ordinal);
var migrateSourceFilters = args.Contains("--migrate-source-filters", StringComparer.Ordinal);
var migrateNotifications = args.Contains("--migrate-notifications", StringComparer.Ordinal);
var migrateMcp = args.Contains("--migrate-mcp", StringComparer.Ordinal);
var migrateLiveLogs = args.Contains("--migrate-live-logs", StringComparer.Ordinal);
var webArguments = args.Where(x => x is not "--initialize-control" and not "--bootstrap-admin" and not "--migrate-listeners"
    and not "--migrate-source-filters" and not "--migrate-notifications" and not "--migrate-mcp" and not "--migrate-live-logs").ToArray();
var builder = WebApplication.CreateBuilder(webArguments);
// 私有覆盖文件已被仓库忽略；环境变量与显式命令参数优先，不把账户密码写进源码。
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables().AddCommandLine(webArguments);
// 管理台允许通过任意域名或 IP 访问；旧部署配置中的 Host 白名单不再生效。
builder.Services.PostConfigure<HostFilteringOptions>(options => options.AllowedHosts = ["*"]);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 64 * 1024;
    options.ConfigureHttpsDefaults(https => options.ApplicationServices.GetRequiredService<SystemHttpsStartup>().Apply(https));
});
builder.Services.AddSyslog(builder.Configuration);
await using var app = builder.Build();
if (initialize || bootstrap || migrateListeners || migrateSourceFilters || migrateNotifications || migrateMcp || migrateLiveLogs)
    return await ControlCommands.RunAsync(app.Services, initialize, bootstrap, migrateListeners, migrateSourceFilters, migrateNotifications, migrateMcp, migrateLiveLogs);
var systemHttps = app.Services.GetRequiredService<SystemHttpsStartup>();
systemHttps.InitializeListenerCertificates();
// 接收组件启动前准备初始快照，之后由运行期开关重新加载已保存配置。
await app.Services.GetRequiredService<ListenerConfigurationService>().InitializeAsync(CancellationToken.None);
app.Lifetime.ApplicationStarted.Register(systemHttps.MarkStarted);

app.UseMiddleware<McpBoundaryMiddleware>();
app.UseMiddleware<ApiBoundaryMiddleware>();
app.UseSyslogStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapGet("/health/live", () => Results.Json(ApiResult<object>.Ok(new { state = "alive" }))).AllowAnonymous();
app.MapControllers();
app.MapSyslogMcp();
app.MapHub<OverviewHub>("/hubs/overview", options => options.Transports = HttpTransportType.WebSockets);
app.MapHub<LiveLogsHub>("/hubs/live-logs", options => options.Transports = HttpTransportType.WebSockets);
app.MapSyslogUi();
await app.RunAsync();
return 0;

public partial class Program;
