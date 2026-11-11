using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Syslog.Api.Common;
using Syslog.Api.Middleware;
using Syslog.Api.Models;
using Syslog.Api.Options;
using Syslog.Api.Service;
using Syslog.Api.Service.impl;
using Syslog.Shared.Models;
using Syslog.Shared.Service;
using Xunit;

namespace Syslog.Api.Tests;

/// <summary>真实控制服务配合独立内存 SQLite 和 TestServer，不绑定任何网络端口。</summary>
public sealed partial class UserManagementApiTests
{
    private const string Password = "Synthetic-Password-2026";
    private const string NewPassword = "Synthetic-New-Password-2026";
    private const string SetupPath = "/api/admin/v1/setup/admin";
    private const string UsersPath = "/api/admin/v1/users";

    [Fact]
    public async Task LocalSetupCompletesOnceAndNeverIssuesAnAnonymousSession()
    {
        await using var app = await Harness.StartAsync();
        app.Client.DefaultRequestHeaders.Add("Cookie", "__Host-syslog=synthetic-stale-cookie");
        var before = await app.Client.GetFromJsonAsync<ApiResult<SetupStatus>>("/api/admin/v1/setup/status");
        Assert.Equal(new SetupStatus(true, true), before!.Data);
        var response = await app.Client.PostAsJsonAsync(SetupPath, new { username = "admin", password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.DoesNotContain(Password, await response.Content.ReadAsStringAsync());
        var after = await app.Client.GetFromJsonAsync<ApiResult<SetupStatus>>("/api/admin/v1/setup/status");
        Assert.Equal(new SetupStatus(false, false), after!.Data);
        var repeated = await app.Client.PostAsJsonAsync(SetupPath, new { username = "other", password = Password });
        Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        Assert.Equal("setup_already_completed", await CodeAsync(repeated));
        await app.LoginAsync();
        Assert.Equal(HttpStatusCode.OK, (await app.Client.GetAsync(UsersPath)).StatusCode);
    }

    [Fact]
    public async Task SetupRequiresDirectLoopbackHttpsAndRejectsForwardedConnections()
    {
        await using var app = await Harness.StartAsync();
        var input = new { username = "admin", password = Password };
        var insecure = await app.Client.PostAsJsonAsync("http://localhost" + SetupPath, input);
        Assert.Equal("https_required", await CodeAsync(insecure));
        foreach (var header in new[] { "Forwarded", "X-Forwarded-For", "X-Forwarded-Proto", "X-Real-IP" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, SetupPath) { Content = JsonContent.Create(input) };
            request.Headers.Add(header, "synthetic");
            var rejected = await app.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
            Assert.Equal("setup_local_required", await CodeAsync(rejected));
        }
        await using var remote = await Harness.StartAsync(remote: IPAddress.Parse("192.0.2.20"));
        var status = await remote.Client.GetFromJsonAsync<ApiResult<SetupStatus>>("/api/admin/v1/setup/status");
        Assert.Equal(new SetupStatus(true, false), status!.Data);
        Assert.Equal("setup_local_required", await CodeAsync(await remote.Client.PostAsJsonAsync(SetupPath, input)));

        await using var host = await Harness.StartAsync();
        var remoteHost = await host.Client.PostAsJsonAsync("https://syslog.example.com" + SetupPath, input);
        Assert.Equal("setup_local_required", await CodeAsync(remoteHost));
        Assert.True(await host.Store.IsSetupRequiredAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SetupRequiresRequestGuardAndRateLimitsRepeatedAttempts()
    {
        await using var app = await Harness.StartAsync();
        var input = new { username = "admin", password = "short" };
        app.Client.DefaultRequestHeaders.Remove("X-Syslog-Request");
        Assert.Equal("csrf_rejected", await CodeAsync(await app.Client.PostAsJsonAsync(SetupPath, input)));
        app.Client.DefaultRequestHeaders.Add("X-Syslog-Request", "1");
        app.Client.DefaultRequestHeaders.Add("Origin", "https://attacker.invalid");
        Assert.Equal("csrf_rejected", await CodeAsync(await app.Client.PostAsJsonAsync(SetupPath, input)));
        app.Client.DefaultRequestHeaders.Remove("Origin");
        for (var i = 0; i < 5; i++)
            Assert.Equal("password_policy", await CodeAsync(await app.Client.PostAsJsonAsync(SetupPath, input)));
        var limited = await app.Client.PostAsJsonAsync(SetupPath, input);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("rate_limited", await CodeAsync(limited));
    }

    [Fact]
    public async Task BrokenControlStoreIsAnUnavailableStateAndDoesNotAutoRepair()
    {
        await using var app = await Harness.StartAsync(initialize: false);
        var status = await app.Client.GetAsync("/api/admin/v1/setup/status");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, status.StatusCode);
        Assert.Equal("service_unavailable", await CodeAsync(status));
        Assert.DoesNotContain("setupRequired", await status.Content.ReadAsStringAsync());
        var submit = await app.Client.PostAsJsonAsync(SetupPath, new { username = "admin", password = Password });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, submit.StatusCode);
        await using var command = app.Anchor.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table';";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task UsersRequireAuthenticationAdminRoleHttpsAndSameOriginProtection()
    {
        await using var app = await Harness.StartAsync();
        var create = new { username = "operator", password = Password };
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client.GetAsync(UsersPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client.PostAsJsonAsync(UsersPath, create)).StatusCode);
        await app.BootstrapAndLoginAsync();
        Assert.Equal("https_required", await CodeAsync(await app.Client.PostAsJsonAsync("http://localhost" + UsersPath, create)));
        app.Client.DefaultRequestHeaders.Remove("X-Syslog-Request");
        Assert.Equal("csrf_rejected", await CodeAsync(await app.Client.PostAsJsonAsync(UsersPath, create)));
        app.Client.DefaultRequestHeaders.Add("X-Syslog-Request", "1");
        app.Client.DefaultRequestHeaders.Add("Origin", "https://attacker.invalid");
        Assert.Equal("csrf_rejected", await CodeAsync(await app.Client.PostAsJsonAsync(UsersPath, create)));
        app.Client.DefaultRequestHeaders.Remove("Origin");
        app.ForceNonAdmin = true;
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Client.GetAsync(UsersPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Client.PostAsJsonAsync(UsersPath, create)).StatusCode);
    }

    [Fact]
    public async Task UsersApiReturnsStringIdsVersionsAndMeaningfulConflicts()
    {
        await using var app = await Harness.StartAsync();
        await app.BootstrapAndLoginAsync();
        var response = await app.Client.PostAsJsonAsync(UsersPath, new { username = "operator", password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = json.RootElement.GetProperty("data");
        Assert.Equal(JsonValueKind.String, data.GetProperty("id").ValueKind);
        Assert.Equal("1", data.GetProperty("version").GetString());
        Assert.False(data.TryGetProperty("passwordHash", out _));
        var id = data.GetProperty("id").GetString();
        var duplicate = await app.Client.PostAsJsonAsync(UsersPath, new { username = "OPERATOR", password = Password });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("username_conflict", await CodeAsync(duplicate));
        var page = await app.Client.GetFromJsonAsync<ApiResult<ManagedUserPage>>(UsersPath + "?search=opera&page=1&pageSize=1&enabled=true");
        Assert.Equal(1, page!.Data!.Total);
        Assert.Single(page.Data.Items);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Client.GetAsync(UsersPath + "?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Client.GetAsync(UsersPath + "?enabled=invalid")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await app.Client.PutAsJsonAsync(UsersPath + "/999999", new { username = "missing", enabled = true, expectedVersion = "1" })).StatusCode);
        var saved = await app.Client.PutAsJsonAsync(UsersPath + "/" + id, new { username = "renamed", enabled = true, expectedVersion = "1" });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var conflict = await app.Client.PutAsJsonAsync(UsersPath + "/" + id, new { username = "outdated", enabled = true, expectedVersion = "1" });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("version_conflict", await CodeAsync(conflict));
        var reset = await app.Client.PostAsJsonAsync(UsersPath + "/" + id + "/password", new { newPassword = NewPassword, expectedVersion = "2" });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.DoesNotContain(NewPassword, await reset.Content.ReadAsStringAsync());
        var session = await app.Client.GetFromJsonAsync<ApiResult<ControlUser>>("/api/admin/v1/session");
        var self = await app.Client.PutAsJsonAsync(UsersPath + "/" + session!.Data!.Id,
            new { username = "admin", enabled = false, expectedVersion = "1" });
        Assert.Equal(HttpStatusCode.Forbidden, self.StatusCode);
        Assert.Equal("self_disable_forbidden", await CodeAsync(self));
    }

    [Fact]
    public async Task OmittingEnabledRejectsUpdateWithoutChangingUserOrAudit()
    {
        await using var app = await Harness.StartAsync();
        await app.BootstrapAndLoginAsync();
        var created = await app.Client.PostAsJsonAsync(UsersPath, new { username = "operator", password = Password });
        var user = (await created.Content.ReadFromJsonAsync<ApiResult<ManagedUser>>())!.Data!;
        await using var command = app.Anchor.CreateCommand();
        command.CommandText = "SELECT count(*) FROM audit_entries;";
        var auditCount = await command.ExecuteScalarAsync();

        var response = await app.Client.PutAsJsonAsync(UsersPath + "/" + user.Id,
            new { username = "accidental-change", expectedVersion = "1" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", await CodeAsync(response));
        var page = await app.Client.GetFromJsonAsync<ApiResult<ManagedUserPage>>(UsersPath + "?search=operator");
        Assert.Equal(user, Assert.Single(page!.Data!.Items));
        Assert.True(page.Data.Items[0].Enabled);
        Assert.Equal(auditCount, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task UserManagementRateLimitAppliesPerAuthenticatedActor()
    {
        await using var app = await Harness.StartAsync();
        await app.BootstrapAndLoginAsync();
        for (var i = 0; i < 20; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await app.Client.PostAsJsonAsync(UsersPath, new { username = "operator", password = "short" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await app.Client.PostAsJsonAsync(UsersPath, new { username = "operator", password = "short" })).StatusCode);
    }

    private static async Task<string> CodeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("code").GetString()!;
    }

    private sealed class Harness : IAsyncDisposable
    {
        public required HttpClient Client { get; init; }
        public required SqliteConnection Anchor { get; init; }
        public required ControlStore Store { get; init; }
        public required IHost Host { get; init; }
        public bool ForceNonAdmin { get; set; }

        public static async Task<Harness> StartAsync(bool initialize = true, IPAddress? remote = null,
            Syslog.Ingest.Service.IReceptionRuntime? reception = null, ILogStore? logs = null)
        {
            var options = new ControlStoreOptions { ConnectionString = $"Data Source=user-api-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False" };
            var anchor = new SqliteConnection(options.ConnectionString);
            await anchor.OpenAsync();
            var store = new ControlStore(options);
            if (initialize) await store.InitializeAsync(CancellationToken.None);
            Harness? harness = null;
            var host = await new HostBuilder().ConfigureLogging(logging => logging.ClearProviders()).ConfigureWebHost(web => web.UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddSyslog(new ConfigurationBuilder().Build());
                    services.AddSingleton<IControlStore>(store);
                    if (reception is not null) services.AddSingleton(reception);
                    if (logs is not null) services.AddSingleton(logs);
                }).Configure(app =>
                {
                    app.Use(async (context, next) => { context.Connection.RemoteIpAddress = remote ?? IPAddress.Loopback; await next(); });
                    app.UseMiddleware<ApiBoundaryMiddleware>();
                    app.UseRouting();
                    app.UseAuthentication();
                    app.Use(async (context, next) =>
                    {
                        // 仅测试角色门禁，模拟未来合法非管理员身份，不改变生产授权机制。
                        if (harness?.ForceNonAdmin == true && context.User.Identity?.IsAuthenticated == true)
                            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, "viewer")], "synthetic"));
                        await next();
                    });
                    app.UseAuthorization();
                    app.UseRateLimiter();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                })).StartAsync();
            var client = host.GetTestClient();
            client.BaseAddress = new Uri("https://localhost");
            client.DefaultRequestHeaders.Add("X-Syslog-Request", "1");
            harness = new Harness { Client = client, Host = host, Store = store, Anchor = anchor };
            return harness;
        }

        public async Task BootstrapAndLoginAsync()
        {
            Assert.True((await Store.SetupAdminAsync("admin", Password, CancellationToken.None)).Success);
            await LoginAsync();
        }

        public async Task LoginAsync()
        {
            var response = await Client.PostAsJsonAsync("/api/admin/v1/session/login", new { username = "admin", password = Password });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Client.DefaultRequestHeaders.Remove("Cookie");
            Client.DefaultRequestHeaders.Add("Cookie", Assert.Single(response.Headers.GetValues("Set-Cookie")).Split(';')[0]);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            Host.Dispose();
            await Anchor.DisposeAsync();
        }
    }
}
