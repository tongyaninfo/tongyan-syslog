using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Syslog.Api.Common;
using Syslog.Api.Controllers;
using Syslog.Api.Middleware;
using Syslog.Api.Models;
using Syslog.Api.Options;
using Syslog.Api.Service;
using Syslog.Api.Service.impl;
using Syslog.Shared.Models;
using Xunit;

namespace Syslog.Api.Tests;

public sealed class McpManagementApiTests
{
    private const string Base = "/api/admin/v1/system/mcp";
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task KeyLifecycleHasNoEditEndpointAndDeletionRequiresRevocation()
    {
        await using var app = await Harness.StartAsync();
        using var response = await app.SendAsync(HttpMethod.Post, "/keys", new
            { name = "合成管理客户端", accessMode = "read", permissions = new[] { "syslog_sources_list" }, expiresAt = (string?)null });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<ApiResult<CreatedMcpKey>>())!.Data!;
        Assert.Null(created.Key.ExpiresAt);
        using var listResponse = await app.SendAsync(HttpMethod.Get, "/keys");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var listJson = await listResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(created.Secret, listJson, StringComparison.Ordinal);
        using var copied = await app.SendAsync(HttpMethod.Post, $"/keys/{created.Key.Id}/connection-material");
        Assert.Equal(created.Secret, (await copied.Content.ReadFromJsonAsync<ApiResult<McpConnectionMaterial>>())!.Data!.Secret);
        Assert.Contains("no-store", copied.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
        using var put = await app.SendAsync(HttpMethod.Put, $"/keys/{created.Key.Id}", new { name = "must-not-edit" });
        using var patch = await app.SendAsync(HttpMethod.Patch, $"/keys/{created.Key.Id}", new { name = "must-not-edit" });
        Assert.Equal(HttpStatusCode.MethodNotAllowed, put.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, patch.StatusCode);
        using var prematureDelete = await app.SendAsync(HttpMethod.Delete, $"/keys/{created.Key.Id}");
        Assert.Equal(HttpStatusCode.Conflict, prematureDelete.StatusCode);
        Assert.Equal("mcp_key_revoke_required", await CodeAsync(prematureDelete));
        using var revoked = await app.SendAsync(HttpMethod.Post, $"/keys/{created.Key.Id}/revoke");
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        using var inactiveCopy = await app.SendAsync(HttpMethod.Post, $"/keys/{created.Key.Id}/connection-material");
        Assert.Equal(HttpStatusCode.Conflict, inactiveCopy.StatusCode);
        using var deleted = await app.SendAsync(HttpMethod.Delete, $"/keys/{created.Key.Id}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.False((await app.Store.AuthenticateMcpAsync(created.Secret, Ct)).Success);
    }

    [Fact]
    public async Task ManagementSettingsRequireExplicitFieldsVersionsAndOriginalRequestProtection()
    {
        await using var app = await Harness.StartAsync();
        using var state = await app.SendAsync(HttpMethod.Get, "");
        var initial = (await state.Content.ReadFromJsonAsync<ApiResult<McpServiceView>>())!.Data!;
        Assert.Equal("stopped", initial.State);
        Assert.Equal(39, initial.ToolCount);
        using var omitted = await app.SendAsync(HttpMethod.Put, "/enabled", new { version = "1" });
        Assert.Equal(HttpStatusCode.BadRequest, omitted.StatusCode);
        using var enabled = await app.SendAsync(HttpMethod.Put, "/enabled", new { version = "1", enabled = true });
        Assert.Equal("running", (await enabled.Content.ReadFromJsonAsync<ApiResult<McpServiceView>>())!.Data!.State);
        using var stale = await app.SendAsync(HttpMethod.Put, "/audit-retention", new { version = "1", auditRetentionDays = 7 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var retained = await app.SendAsync(HttpMethod.Put, "/audit-retention", new { version = "2", auditRetentionDays = 7 });
        Assert.Equal(new McpSettingsView("3", true, 7), (await retained.Content.ReadFromJsonAsync<ApiResult<McpSettingsView>>())!.Data);
        using var noHeader = await app.SendAsync(HttpMethod.Put, "/enabled", new { version = "3", enabled = false }, protection: false);
        Assert.Equal(HttpStatusCode.Forbidden, noHeader.StatusCode);
        Assert.Equal("csrf_rejected", await CodeAsync(noHeader));
        using var httpWrite = await app.SendAsync(HttpMethod.Put, "/enabled", new { version = "3", enabled = false }, useHttp: true);
        Assert.Equal(HttpStatusCode.BadRequest, httpWrite.StatusCode);
        Assert.Equal("https_required", await CodeAsync(httpWrite));
        Assert.True((await app.Store.ReadMcpSettingsAsync(Ct)).Data!.Enabled);
    }

    [Fact]
    public async Task PermissionsAndCallDetailsUseAdministratorOnlyContracts()
    {
        await using var app = await Harness.StartAsync();
        using var anonymous = await app.SendAsync(HttpMethod.Get, "/calls", authenticated: false);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var permissions = await app.SendAsync(HttpMethod.Get, "/permissions");
        using var permissionJson = JsonDocument.Parse(await permissions.Content.ReadAsStringAsync());
        Assert.Equal(39, permissionJson.RootElement.GetProperty("data").GetArrayLength());
        Assert.DoesNotContain("inputSchema", await permissions.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var id = (await app.Store.BeginMcpCallAsync(new(null, "synthetic", "1", "tools/list", null, "192.0.2.1", "POST",
            "https://syslog.example.test/mcp", "HTTP/1.1", new() { ["Authorization"] = ["Bearer synthetic-only"] }, "{}", 2), Ct)).Data!;
        await app.Store.CompleteMcpCallAsync(id, new(401, [], "{\"error\":\"synthetic\"}", 21, "rejected", "mcp_authentication_required", 1), Ct);
        using var history = await app.SendAsync(HttpMethod.Get, "/calls?outcome=rejected&pageSize=1");
        var page = (await history.Content.ReadFromJsonAsync<ApiResult<McpCallPage>>())!.Data!;
        Assert.Single(page.Items);
        Assert.Equal(id, page.Items[0].Id);
        using var detailResponse = await app.SendAsync(HttpMethod.Get, "/calls/" + id);
        var detail = (await detailResponse.Content.ReadFromJsonAsync<ApiResult<McpCallDetail>>())!.Data!;
        Assert.Equal(["Bearer synthetic-only"], detail.RequestHeaders["Authorization"]);
        using var invalid = await app.SendAsync(HttpMethod.Get, "/calls?pageSize=0");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    private static async Task<string> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiResult<JsonElement>>())!.Code;

    private sealed class Harness(IHost host, SqliteConnection anchor, HttpClient client, ControlStore store, SessionTicket ticket) : IAsyncDisposable
    {
        public HttpClient Client => client;
        public ControlStore Store => store;

        public static async Task<Harness> StartAsync()
        {
            var connection = $"Data Source=mcp-management-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False";
            var anchor = new SqliteConnection(connection);
            await anchor.OpenAsync(Ct);
            var store = new ControlStore(new ControlStoreOptions { ConnectionString = connection });
            await store.InitializeAsync(Ct);
            await store.BootstrapAdminAsync("admin", "synthetic-management-password", Ct);
            var ticket = (await store.LoginAsync("admin", "synthetic-management-password", Ct)).Data!;
            var host = await new HostBuilder().ConfigureLogging(logging => logging.ClearProviders()).ConfigureWebHost(web => web.UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IControlStore>(store);
                    services.AddSingleton<IMcpStore>(store);
                    services.AddSingleton<IMcpRuntime, McpRuntime>();
                    services.AddAuthentication(SessionAuthenticationHandler.SchemeName).AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationHandler.SchemeName, _ => { });
                    services.AddAuthorization();
                    services.AddRateLimiter(options => options.AddFixedWindowLimiter("user-management", limiter =>
                        { limiter.PermitLimit = 100; limiter.Window = TimeSpan.FromMinutes(1); }));
                    services.AddControllers().AddApplicationPart(typeof(McpManagementController).Assembly)
                        .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = _ =>
                            new BadRequestObjectResult(ApiResult<object>.Fail("invalid_request", "请求参数无效。")));
                }).Configure(app =>
                {
                    app.UseMiddleware<ApiBoundaryMiddleware>();
                    app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                })).StartAsync();
            var client = host.GetTestClient();
            client.BaseAddress = new Uri("https://localhost");
            return new(host, anchor, client, store, ticket);
        }

        public Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? payload = null, bool authenticated = true, bool protection = true, bool useHttp = false)
        {
            var request = new HttpRequestMessage(method, (useHttp ? "http://localhost" : "") + Base + path);
            if (payload is not null) request.Content = JsonContent.Create(payload);
            if (authenticated) request.Headers.Add("Cookie", SessionAuthenticationHandler.CookieName + "=" + ticket.Token);
            if (protection) request.Headers.Add("X-Syslog-Request", "1");
            return client.SendAsync(request, Ct);
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await host.StopAsync(Ct);
            host.Dispose();
            await anchor.DisposeAsync();
        }
    }
}
