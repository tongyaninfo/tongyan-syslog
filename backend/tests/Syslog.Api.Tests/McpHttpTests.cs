using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using Syslog.Api.Common;
using Syslog.Api.Middleware;
using Syslog.Api.Models;
using Syslog.Api.Options;
using Syslog.Api.Service;
using Syslog.Api.Service.impl;
using Syslog.Api.Workers;
using Syslog.Shared.Models;
using Syslog.Shared.Service;
using Xunit;

namespace Syslog.Api.Tests;

public sealed partial class McpHttpTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Theory]
    [InlineData("http", "2025-11-25")]
    [InlineData("https", "2025-11-25")]
    [InlineData("https", null)]
    public async Task OfficialClientDiscoversAndCallsOnlyGrantedTools(string scheme, string? version)
    {
        await using var app = await Harness.StartAsync(scheme);
        await using var client = await app.ConnectAsync(version);
        var tools = await client.ListToolsAsync(cancellationToken: Ct);
        Assert.Equal(new[] { "syslog_mcp_identity_get", "syslog_sources_list" }, tools.Select(tool => tool.Name).Order().ToArray());
        // 旧版本通过 initialize/ping 握手；2026-07-28 已移除 ping，按 SDK 协商的版本使用工具调用。
        if (version is not null) await client.PingAsync(cancellationToken: Ct);
        var identity = await client.CallToolAsync("syslog_mcp_identity_get", cancellationToken: Ct);
        Assert.NotEqual(true, identity.IsError);
        Assert.True(identity.StructuredContent!.Value.GetProperty("success").GetBoolean());
        var denied = await client.CallToolAsync("syslog_sources_create", new Dictionary<string, object?>
        {
            ["name"] = "must-not-create", ["peerAddress"] = "192.0.2.22", ["timeZoneId"] = "Asia/Shanghai", ["retentionDays"] = 30
        }, cancellationToken: Ct);
        Assert.True(denied.IsError);
        Assert.Empty(await app.Control.ListAsync(Ct));
        var calls = (await app.Control.ListMcpCallsAsync(new(PageSize: 100), Ct)).Data!;
        Assert.Contains(calls.Items, item => item.Method == "tools/list" && item.Outcome == "success");
        Assert.Contains(calls.Items, item => item.ToolName == "syslog_sources_create" && item.Outcome == "failed");
    }

    [Fact]
    public async Task CompleteExchangeRetainsHeadersBodyAndExactResponse()
    {
        await using var app = await Harness.StartAsync();
        const string body = "{\"jsonrpc\":\"2.0\",\"id\":77,\"method\":\"tools/call\",\"params\":{\"name\":\"syslog_mcp_identity_get\",\"arguments\":{}}}";
        using var request = app.Request(body);
        request.Headers.Add("X-Synthetic-Secret", "test-value-only");
        request.Headers.Add("Cookie", "synthetic-private=sample");
        using var response = await app.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var id = response.Headers.GetValues("X-Syslog-Mcp-Call-Id").Single();
        var detail = (await app.Control.GetMcpCallAsync(id, Ct)).Data!;
        Assert.Equal(body, detail.RequestBody);
        Assert.Equal(text, detail.ResponseBody);
        Assert.Equal("Bearer " + app.Secret, detail.RequestHeaders["Authorization"].Single());
        Assert.Equal("test-value-only", detail.RequestHeaders["X-Synthetic-Secret"].Single());
        Assert.Equal("synthetic-private=sample", detail.RequestHeaders["Cookie"].Single());
        Assert.True(detail.Summary.RequestComplete);
        Assert.True(detail.Summary.ResponseComplete);
        Assert.Equal(Encoding.UTF8.GetByteCount(text), detail.Summary.ResponseBytes);
    }

    [Fact]
    public async Task CookieCannotReplaceBearerAndKeyCannotUseManagementApi()
    {
        await using var app = await Harness.StartAsync();
        using var request = app.Request("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}", bearer: false);
        request.Headers.Add("Cookie", SessionAuthenticationHandler.CookieName + "=" + app.Token);
        using var response = await app.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("mcp_authentication_required", await ErrorCodeAsync(response));
        using var management = new HttpRequestMessage(HttpMethod.Get, "/api/admin/v1/system/mcp/calls");
        management.Headers.Authorization = new AuthenticationHeaderValue("Bearer", app.Secret);
        using var rejected = await app.Client.SendAsync(management);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
    }

    [Fact]
    public async Task RevocationIsCheckedOnEveryRequestAndPreservesAuditIdentity()
    {
        await using var app = await Harness.StartAsync();
        using var first = await app.Client.SendAsync(app.Request("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.True((await app.Control.RevokeMcpKeyAsync(app.KeyId, app.UserId, app.Token, Ct)).Success);
        using var next = await app.Client.SendAsync(app.Request("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}"));
        Assert.Equal(HttpStatusCode.Unauthorized, next.StatusCode);
        var calls = (await app.Control.ListMcpCallsAsync(new(KeyId: app.KeyId), Ct)).Data!;
        Assert.Contains(calls.Items, item => item.KeyName == "isolated-client");
    }

    [Fact]
    public async Task UntrustedOriginAndUrlCredentialsAreRejectedAndAudited()
    {
        await using var app = await Harness.StartAsync();
        using var request = app.Request("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}");
        request.Headers.Add("Origin", "https://untrusted.example.test");
        using var response = await app.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var urlRequest = app.Request("{}", path: "/mcp?api_key=synthetic-query-value");
        using var urlResponse = await app.Client.SendAsync(urlRequest);
        Assert.Equal(HttpStatusCode.BadRequest, urlResponse.StatusCode);
        var calls = (await app.Control.ListMcpCallsAsync(new(PageSize: 100), Ct)).Data!;
        Assert.All(calls.Items, item => Assert.Equal("rejected", item.Outcome));
    }

    [Fact]
    public async Task AuditFailurePreventsAnyToolExecution()
    {
        await using var app = await Harness.StartAsync(permissions: ["syslog_sources_create"], mode: "read_write");
        await app.ExecuteAsync("CREATE TRIGGER reject_mcp_audit BEFORE INSERT ON mcp_call_audits BEGIN SELECT RAISE(ABORT,'synthetic'); END;");
        using var request = app.Request("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"syslog_sources_create\",\"arguments\":{\"name\":\"blocked\",\"peerAddress\":\"192.0.2.20\",\"timeZoneId\":\"Asia/Shanghai\",\"retentionDays\":30}}}");
        using var response = await app.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("mcp_audit_unavailable", await ErrorCodeAsync(response));
        Assert.Empty(await app.Control.ListAsync(Ct));
    }

    [Fact]
    public async Task StopRejectsNewCallsButLetsAlreadyRunningReadFinish()
    {
        await using var app = await Harness.StartAsync(permissions: ["syslog_logs_search"]);
        Assert.True((await app.Control.CreateSourceAsync(new("fixture", "192.0.2.19", "Asia/Shanghai", 30), app.UserId, Ct)).Success);
        app.Logs.Block = true;
        const string body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"syslog_logs_search\",\"arguments\":{\"from\":\"2026-11-10T00:00:00Z\",\"to\":\"2026-11-11T00:00:00Z\"}}}";
        var pending = app.Client.SendAsync(app.Request(body));
        await app.Logs.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var settings = (await app.Control.ReadMcpSettingsAsync(Ct)).Data!;
        var stopped = await app.Runtime.SetEnabledAsync(new(settings.Version, false), app.UserId, app.Token, Ct);
        Assert.Equal("stopping", stopped.Data!.State);
        using var denied = await app.Client.SendAsync(app.Request(body));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, denied.StatusCode);
        Assert.Equal("mcp_disabled", await ErrorCodeAsync(denied));
        app.Logs.Release.TrySetResult();
        using var completed = await pending;
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.Equal("stopped", (await app.Runtime.GetViewAsync(Ct)).Data!.State);
    }

    [Fact]
    public async Task OversizedRequestIsExplicitlyIncompleteAndNeverExecuted()
    {
        await using var app = await Harness.StartAsync();
        using var request = app.Request(new string('x', McpHttpProtocol.MaximumRequestBytes + 30));
        using var response = await app.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var detail = (await app.Control.GetMcpCallAsync(response.Headers.GetValues("X-Syslog-Mcp-Call-Id").Single(), Ct)).Data!;
        Assert.False(detail.Summary.RequestComplete);
        Assert.Contains("request_too_large", detail.Summary.CaptureError);
        Assert.Equal("rejected", detail.Summary.Outcome);
    }

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("error").GetProperty("data").GetProperty("code").GetString()!;
    }

    private sealed class Harness(IHost host, SqliteConnection keeper, HttpClient client, ControlStore control,
        SessionTicket ticket, CreatedMcpKey key, FakeLogs logs) : IAsyncDisposable
    {
        public HttpClient Client => client;
        public IServiceProvider Services => host.Services;
        public ControlStore Control => control;
        public McpRuntime Runtime => host.Services.GetRequiredService<McpRuntime>();
        public string Secret => key.Secret;
        public string KeyId => key.Key.Id;
        public long UserId => ticket.User.Id;
        public string Token => ticket.Token;
        public FakeLogs Logs => logs;

        public static async Task<Harness> StartAsync(string scheme = "https", string[]? permissions = null, string mode = "read")
        {
            var connectionString = $"Data Source=mcp-http-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False";
            var keeper = new SqliteConnection(connectionString);
            await keeper.OpenAsync(Ct);
            var control = new ControlStore(new ControlStoreOptions { ConnectionString = connectionString });
            await control.InitializeAsync(Ct);
            Assert.True((await control.BootstrapAdminAsync("admin", "synthetic-test-password", Ct)).Success);
            var ticket = (await control.LoginAsync("admin", "synthetic-test-password", Ct)).Data!;
            var key = (await control.CreateMcpKeyAsync(new("isolated-client", mode,
                permissions ?? ["syslog_mcp_identity_get", "syslog_sources_list"], null), ticket.User.Id, ticket.Token, Ct)).Data!;
            var settings = (await control.ReadMcpSettingsAsync(Ct)).Data!;
            Assert.True((await control.SetMcpEnabledAsync(new(settings.Version, true), ticket.User.Id, ticket.Token, Ct)).Success);
            var logs = new FakeLogs();
            var host = await new HostBuilder().ConfigureLogging(logging => logging.ClearProviders()).ConfigureWebHost(web => web.UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddSyslog(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["HttpsCertificate:Directory"] = Path.Combine(AppContext.BaseDirectory, "TestResults", "mcp-cert-" + Guid.NewGuid().ToString("N")),
                        ["Ingest:UnregisteredDevices:Enabled"] = "true",
                        ["Ingest:UnregisteredDevices:AllowedNetworks:0"] = "192.0.2.0/24"
                    }).Build());
                    // 业务测试仅检查邮件排队，不能让后台发送器接触任何 SMTP 服务。
                    foreach (var worker in services.Where(item => item.ServiceType == typeof(IHostedService)
                        && item.ImplementationType == typeof(NotificationWorker)).ToArray()) services.Remove(worker);
                    services.AddSingleton(control);
                    services.AddSingleton<IControlStore>(control);
                    services.AddSingleton<IMcpStore>(control);
                    services.AddSingleton<ILogStore>(logs);
                }).Configure(app =>
                {
                    app.UseMiddleware<McpBoundaryMiddleware>(); app.UseMiddleware<ApiBoundaryMiddleware>();
                    app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter();
                    app.UseEndpoints(endpoints => { endpoints.MapControllers(); endpoints.MapSyslogMcp(); });
                })).StartAsync();
            var client = host.GetTestClient();
            client.BaseAddress = new Uri(scheme + "://localhost");
            return new(host, keeper, client, control, ticket, key, logs);
        }

        public HttpRequestMessage Request(string body, bool bearer = true, string path = "/mcp")
        {
            var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            request.Headers.Add("MCP-Protocol-Version", "2025-11-25");
            if (bearer) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Secret);
            return request;
        }

        public Task<McpClient> ConnectAsync(string? version) => McpClient.CreateAsync(new HttpClientTransport(new()
        {
            Endpoint = new Uri(client.BaseAddress!, "/mcp"), Name = "isolated-test", TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + Secret }
        }, client), new McpClientOptions { ProtocolVersion = version, InitializationTimeout = TimeSpan.FromSeconds(10) }, cancellationToken: Ct);

        public async Task ExecuteAsync(string sql)
        {
            await using var command = keeper.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(Ct);
        }
        public async ValueTask DisposeAsync()
        {
            Logs.Release.TrySetResult();
            client.Dispose(); await host.StopAsync(Ct); host.Dispose(); await keeper.DisposeAsync();
        }
    }

    private sealed class FakeLogs : ILogStore
    {
        public bool Block;
        public int ClearCount;
        public List<string> ClearedSources { get; } = [];
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task WriteBatchAsync(IReadOnlyList<LogEvent> events, CancellationToken ct) => throw new InvalidOperationException("Test must not ingest");
        public async Task<LogSearchPage> SearchAsync(LogSearch query, IReadOnlyCollection<string> scope, CancellationToken ct)
        {
            Entered.TrySetResult();
            if (Block) await Release.Task.WaitAsync(ct);
            return new([], null, false);
        }
        public Task<LogEvent?> GetAsync(long id, DateTimeOffset at, IReadOnlyCollection<string> scope, CancellationToken ct) =>
            Task.FromResult<LogEvent?>(scope.Count == 0 ? null : new LogEvent
            {
                EventId = id, SourceId = scope.First(), ReceivedAt = at, Severity = 3,
                Message = "synthetic error", RawBytes = Encoding.UTF8.GetBytes("<3>synthetic error")
            });
        public Task<LogReport> GetReportAsync(DateTimeOffset from, DateTimeOffset to, IReadOnlyCollection<string> scope, CancellationToken ct) =>
            Task.FromResult(new LogReport(scope.Select(id => new LogReportDeviceCounts(id, 1, 0, 0, 0, 1)).ToArray(),
                scope.Select(id => new LogReportEntry(9007199254740993, id, from, 3, "synthetic error")).ToArray()));
        public Task ClearAsync(CancellationToken ct) { ClearCount++; return Task.CompletedTask; }
        public Task ClearSourceAsync(string sourceId, CancellationToken ct) { ClearedSources.Add(sourceId); return Task.CompletedTask; }
    }
}
