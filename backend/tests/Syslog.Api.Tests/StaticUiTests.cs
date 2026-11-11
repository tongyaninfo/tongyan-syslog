using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Syslog.Api.Common;
using Syslog.Api.Middleware;
using Syslog.Shared.Models;
using Xunit;

namespace Syslog.Api.Tests;

/// <summary>合成静态文件保留在忽略目录；TestServer 不绑定端口，不读配置库和真实证书。</summary>
public sealed class StaticUiTests
{
    [Fact]
    public async Task KnownPagesAndAssetsAreAvailableWithoutSession()
    {
        using var host = await StartAsync(withUi: true);
        using var client = host.GetTestClient();
        foreach (var path in new[] { "/", "/overview", "/logs", "/unregistered", "/sources", "/listeners", "/retention", "/users", "/user/setup", "/user/login", "/index.html" })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
            Assert.Contains("Synthetic Syslog UI", await response.Content.ReadAsStringAsync());
        }
        Assert.Equal("synthetic asset", await client.GetStringAsync("/assets/app.js"));
    }

    [Theory]
    [InlineData("/api/missing")]
    [InlineData("/api/admin/v1/unknown")]
    [InlineData("/api/accidental.txt")]
    public async Task UnknownApiNeverFallsBackToHtmlOrStaticFiles(string path)
    {
        using var host = await StartAsync(withUi: true);
        using var client = host.GetTestClient();
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("not_found", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ApiAuthorizationAndRequestGuardRemainEffective()
    {
        using var host = await StartAsync(withUi: true);
        using var client = host.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/protected")).StatusCode);
        var rejected = await client.PostAsync("/api/missing", null);
        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        using var json = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        Assert.Equal("csrf_rejected", json.RootElement.GetProperty("code").GetString());
        client.DefaultRequestHeaders.Add("X-Syslog-Request", "1");
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync("/api/missing", null)).StatusCode);
    }

    [Fact]
    public async Task MissingBuildReturnsExplicit404WithoutCreatingWebRoot()
    {
        using var host = await StartAsync(withUi: false);
        using var client = host.GetTestClient();
        var response = await client.GetAsync("/overview");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ui_not_built", json.RootElement.GetProperty("code").GetString());
        Assert.False(Directory.Exists(host.Services.GetRequiredService<IWebHostEnvironment>().WebRootPath));
    }

    [Fact]
    public async Task UnknownBrowserRouteDoesNotReturnIndex()
    {
        using var host = await StartAsync(withUi: true);
        using var client = host.GetTestClient();
        var response = await client.GetAsync("/unregistered-page");
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Synthetic Syslog UI", await response.Content.ReadAsStringAsync());
    }

    private static async Task<IHost> StartAsync(bool withUi)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestResults", "static-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var webRoot = Path.Combine(root, "wwwroot");
        if (withUi)
        {
            Directory.CreateDirectory(Path.Combine(webRoot, "assets"));
            Directory.CreateDirectory(Path.Combine(webRoot, "api"));
            await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"), "<!doctype html><title>Synthetic Syslog UI</title>");
            await File.WriteAllTextAsync(Path.Combine(webRoot, "assets", "app.js"), "synthetic asset");
            await File.WriteAllTextAsync(Path.Combine(webRoot, "api", "accidental.txt"), "must not be served");
        }
        return await new HostBuilder().ConfigureLogging(logging => logging.ClearProviders()).ConfigureWebHost(web =>
        {
            web.UseContentRoot(root);
            // 显式 UseWebRoot 本身会创建目标目录；缺失构建场景沿用源码宿主默认检测行为。
            if (withUi) web.UseWebRoot(webRoot);
            web.UseTestServer().ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddAuthentication("empty").AddScheme<AuthenticationSchemeOptions, EmptyAuthenticationHandler>("empty", _ => { });
                services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
            }).Configure(app =>
            {
                app.UseMiddleware<ApiBoundaryMiddleware>();
                app.UseSyslogStaticFiles();
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet("/api/protected", () => ApiResult<object>.Ok(new { })).RequireAuthorization();
                    endpoints.MapSyslogUi();
                });
            });
        }).StartAsync();
    }

    private sealed class EmptyAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
    }
}
