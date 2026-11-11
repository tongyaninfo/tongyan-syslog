using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Syslog.Api.Common;
using Syslog.Api.Hubs;
using Syslog.Api.Middleware;
using Syslog.Api.Models;
using Syslog.Api.Options;
using Syslog.Api.Service;
using Syslog.Api.Service.impl;
using Syslog.Api.Workers;
using Syslog.Ingest.Service.impl;
using Syslog.Shared.Models;
using Syslog.Shared.Service;
using Xunit;

namespace Syslog.Api.Tests;

/// <summary>真实 HTTP/SignalR 与隔离内存 SQLite；不绑定宿主端口，不连接 ClickHouse 或真实设备。</summary>
public sealed class LiveLogsTests
{
    private const string Password = "Synthetic-Password-2026";
    private const string BasePath = "/api/admin/v1/live-logs";
    private const string HubPath = "/hubs/live-logs";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task SettingsApiPersistsAndAppliesLimitImmediately()
    {
        await using var app = await Harness.StartAsync();
        using var initial = await app.SendAsync(HttpMethod.Get, BasePath + "/settings");
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        var first = await BodyAsync(initial);
        Assert.Equal(5000, first.GetProperty("data").GetProperty("settings").GetProperty("perDeviceLimit").GetInt32());
        for (var index = 0; index < 150; index++) Assert.True(app.Buffer.Capture(Event("1", "retained " + index), false));
        using var saved = await app.SendAsync(HttpMethod.Put, BasePath + "/settings", new SaveLiveLogSettingsRequest("1", 100, 16777216));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var data = (await BodyAsync(saved)).GetProperty("data");
        Assert.Equal("2", data.GetProperty("settings").GetProperty("version").GetString());
        Assert.Equal(100, data.GetProperty("cache").GetProperty("entries").GetInt64());
        Assert.Equal(50, data.GetProperty("cache").GetProperty("evicted").GetInt64());
        Assert.Equal(16777216, app.Buffer.GetStats().MaxBytes);
        Assert.Equal(new SavedLiveLogSettings("2", 100, 16777216), (await app.Store.ReadLiveLogSettingsAsync(CancellationToken.None)).Data);
        using var conflict = await app.SendAsync(HttpMethod.Put, BasePath + "/settings", new SaveLiveLogSettingsRequest("1", 5000, 536870912));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(100, app.Buffer.GetStats().PerDeviceLimit);
    }

    [Theory]
    [InlineData("anonymous", 401)]
    [InlineData("non-admin", 403)]
    [InlineData("http", 400)]
    [InlineData("external-origin", 403)]
    [InlineData("missing-guard", 403)]
    public async Task SettingsWritesEnforceAuthenticationAndRequestBoundary(string scenario, int expected)
    {
        await using var app = await Harness.StartAsync();
        app.ForceNonAdmin = scenario == "non-admin";
        using var response = await app.SendAsync(HttpMethod.Put,
            (scenario == "http" ? "http://localhost" : "") + BasePath + "/settings",
            new SaveLiveLogSettingsRequest("1", 100, 16777216), authenticated: scenario != "anonymous",
            origin: scenario == "external-origin" ? "https://example.invalid" : null, guarded: scenario != "missing-guard");
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal(5000, app.Buffer.GetStats().PerDeviceLimit);
    }

    [Fact]
    public async Task QueryAndDetailIncludeIgnoredEvidenceButOnlyAllowedSources()
    {
        await using var app = await Harness.StartAsync();
        Assert.True(app.Buffer.Capture(Event("1", "ordinary message"), false));
        Assert.True(app.Buffer.Capture(Event("1", "ignored <script>alert(1)</script>") with { EventId = 99 }, true));
        Assert.True(app.Buffer.Capture(Event("secret", "hidden message"), true));
        using var response = await app.SendAsync(HttpMethod.Post, BasePath + "/query", new LiveLogQueryRequest(new() { Disposition = "ignored" }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single((await BodyAsync(response)).GetProperty("data").GetProperty("items").EnumerateArray());
        Assert.Equal("ignored", item.GetProperty("disposition").GetString());
        Assert.Equal("0", item.GetProperty("log").GetProperty("eventId").GetString());
        Assert.Equal("1", item.GetProperty("log").GetProperty("sourceId").GetString());
        var id = item.GetProperty("id").GetString();
        using var detailResponse = await app.SendAsync(HttpMethod.Get, BasePath + "/entries/" + Uri.EscapeDataString(id!));
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = (await BodyAsync(detailResponse)).GetProperty("data").GetProperty("log");
        Assert.Equal("ignored <script>alert(1)</script>", detail.GetProperty("message").GetString());
        Assert.Equal(Encoding.UTF8.GetBytes("synthetic raw evidence"), detail.GetProperty("rawBytes").GetBytesFromBase64());
        using var forbidden = await app.SendAsync(HttpMethod.Post, BasePath + "/query", new LiveLogQueryRequest(new() { SourceId = "secret" }));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var hidden = app.Buffer.ReadSnapshot(new() { SourceId = "secret" }, ["secret"]).Data!.Items.Single();
        using var hiddenDetail = await app.SendAsync(HttpMethod.Get, BasePath + "/entries/" + Uri.EscapeDataString(hidden.Id));
        Assert.Equal(HttpStatusCode.NotFound, hiddenDetail.StatusCode);
        using var stale = await app.SendAsync(HttpMethod.Post, BasePath + "/query", new LiveLogQueryRequest(new(), "previous-epoch"));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task QueryRejectsAnonymousInvalidFilterAndMidQueryRevocation()
    {
        await using var app = await Harness.StartAsync();
        using var anonymous = await app.SendAsync(HttpMethod.Post, BasePath + "/query", new LiveLogQueryRequest(new()), authenticated: false);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var invalid = await app.SendAsync(HttpMethod.Post, BasePath + "/query", new LiveLogQueryRequest(new() { Severity = 8 }));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        app.Sources.Read = async _ =>
        {
            await app.Store.LogoutAsync(app.Session.Token, CancellationToken.None);
            return [new("1", "Synthetic switch", "192.0.2.10", "UTC", 365, true)];
        };
        using var revoked = await app.SendAsync(HttpMethod.Post, BasePath + "/query", new LiveLogQueryRequest(new()));
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        Assert.Equal("authentication_required", (await BodyAsync(revoked)).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("anonymous", 401)]
    [InlineData("forged-cookie", 401)]
    [InlineData("bearer-only", 401)]
    [InlineData("url-token", 403)]
    [InlineData("external-origin", 403)]
    [InlineData("missing-guard", 403)]
    [InlineData("non-admin", 403)]
    [InlineData("http", 400)]
    public async Task HubNegotiationEnforcesCookieSameOriginAndHttps(string scenario, int expected)
    {
        await using var app = await Harness.StartAsync();
        app.ForceNonAdmin = scenario == "non-admin";
        using var request = new HttpRequestMessage(HttpMethod.Post,
            (scenario == "http" ? "http://localhost" : "https://localhost") + HubPath + "/negotiate?negotiateVersion=1" +
            (scenario == "url-token" ? "&access_token=synthetic" : ""));
        if (scenario != "missing-guard") request.Headers.Add("X-Syslog-Request", "1");
        if (scenario is not ("anonymous" or "bearer-only")) request.Headers.Add("Cookie", Cookie(scenario == "forged-cookie" ? "synthetic-forged" : app.Session.Token));
        if (scenario == "bearer-only") request.Headers.Add("Authorization", "Bearer " + app.Session.Token);
        if (scenario == "external-origin") request.Headers.Add("Origin", "https://example.invalid");
        using var response = await app.Client.SendAsync(request);
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal(0, app.Sources.Reads);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://example.invalid")]
    [InlineData("https://localhost/path")]
    public async Task HubUpgradeRequiresExactSameOrigin(string? origin)
    {
        await using var app = await Harness.StartAsync();
        var client = app.Host.GetTestServer().CreateWebSocketClient();
        client.ConfigureRequest = request =>
        {
            request.Headers.Cookie = Cookie(app.Session.Token);
            if (origin is not null) request.Headers.Origin = origin;
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync(new Uri("wss://localhost" + HubPath), CancellationToken.None));
    }

    [Fact]
    public async Task HubSendsSnapshotAndIgnoredAppendsThenReplacesFilter()
    {
        await using var app = await Harness.StartAsync();
        Assert.True(app.Buffer.Capture(Event("1", "before subscription"), false));
        using var socket = await app.ConnectAsync();
        Assert.True((await socket.SubscribeAsync(new("first", new()))).GetProperty("success").GetBoolean());
        var snapshot = await socket.EventAsync("LiveLogsUpdated");
        Assert.Equal("snapshot", snapshot.GetProperty("kind").GetString());
        Assert.Equal("first", snapshot.GetProperty("subscriptionId").GetString());
        Assert.Single(snapshot.GetProperty("items").EnumerateArray());
        Assert.Equal(TimeSpan.Zero, snapshot.GetProperty("observedAt").GetDateTimeOffset().Offset);
        Assert.True(app.Buffer.Capture(Event("1", "ignored after subscription"), true));
        await app.Feed.PublishAsync(CancellationToken.None);
        var append = await socket.EventAsync("LiveLogsUpdated");
        Assert.Equal("append", append.GetProperty("kind").GetString());
        var ignored = Assert.Single(append.GetProperty("items").EnumerateArray());
        Assert.Equal("ignored", ignored.GetProperty("disposition").GetString());
        Assert.Equal("0", ignored.GetProperty("log").GetProperty("eventId").GetString());

        Assert.True((await socket.SubscribeAsync(new("second", new() { SourceId = "2", Disposition = "ignored" }))).GetProperty("success").GetBoolean());
        var replaced = await socket.EventAsync("LiveLogsUpdated");
        Assert.Equal("second", replaced.GetProperty("subscriptionId").GetString());
        Assert.Equal("snapshot", replaced.GetProperty("kind").GetString());
        Assert.Empty(replaced.GetProperty("items").EnumerateArray());
        Assert.True(app.Buffer.Capture(Event("1", "wrong source"), true));
        Assert.True(app.Buffer.Capture(Event("2", "wrong disposition"), false));
        Assert.True(app.Buffer.Capture(Event("2", "matching new filter"), true));
        await app.Feed.PublishAsync(CancellationToken.None);
        var filtered = await socket.EventAsync("LiveLogsUpdated");
        Assert.Equal("second", filtered.GetProperty("subscriptionId").GetString());
        Assert.Equal("matching new filter", Assert.Single(filtered.GetProperty("items").EnumerateArray()).GetProperty("log").GetProperty("message").GetString());
    }

    [Fact]
    public async Task HubResumeReturnsOnlyNewEntriesAndEpochMismatchResetsWithReason()
    {
        await using var app = await Harness.StartAsync();
        Assert.True(app.Buffer.Capture(Event("1", "before"), false));
        using var first = await app.ConnectAsync();
        await first.SubscribeAsync(new("initial", new()));
        var snapshot = await first.EventAsync("LiveLogsUpdated");
        var epoch = snapshot.GetProperty("epoch").GetString()!;
        var cursor = snapshot.GetProperty("cursor").GetString()!;
        Assert.True(app.Buffer.Capture(Event("1", "during disconnect"), true));
        using var resumed = await app.ConnectAsync();
        Assert.True((await resumed.SubscribeAsync(new("resumed", new(), epoch, cursor))).GetProperty("success").GetBoolean());
        var delta = await resumed.EventAsync("LiveLogsUpdated");
        Assert.Equal("append", delta.GetProperty("kind").GetString());
        Assert.Equal("during disconnect", Assert.Single(delta.GetProperty("items").EnumerateArray()).GetProperty("log").GetProperty("message").GetString());
        using var stale = await app.ConnectAsync();
        Assert.True((await stale.SubscribeAsync(new("stale", new(), new string('0', 32), "0"))).GetProperty("success").GetBoolean());
        var reset = await stale.EventAsync("LiveLogsUpdated");
        Assert.Equal("snapshot", reset.GetProperty("kind").GetString());
        Assert.Equal("epoch_changed", reset.GetProperty("resetReason").GetString());
        Assert.Equal(epoch, reset.GetProperty("epoch").GetString());
        Assert.Equal(2, reset.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task HubEvictedResumeDoesNotClaimCompleteAndSendsReplacementSnapshot()
    {
        await using var app = await Harness.StartAsync();
        app.Buffer.ApplySettings(new(100, 16777216));
        Assert.True(app.Buffer.Capture(Event("1", "old"), false));
        using var first = await app.ConnectAsync();
        await first.SubscribeAsync(new("old", new()));
        var snapshot = await first.EventAsync("LiveLogsUpdated");
        for (var index = 0; index < 105; index++) Assert.True(app.Buffer.Capture(Event("1", "new " + index), index % 2 == 0));
        using var resumed = await app.ConnectAsync();
        Assert.True((await resumed.SubscribeAsync(new("resumed", new(), snapshot.GetProperty("epoch").GetString(), snapshot.GetProperty("cursor").GetString())))
            .GetProperty("success").GetBoolean());
        var reset = await resumed.EventAsync("LiveLogsUpdated");
        Assert.Equal("snapshot", reset.GetProperty("kind").GetString());
        Assert.Equal("cache_evicted", reset.GetProperty("resetReason").GetString());
        Assert.Equal(100, reset.GetProperty("items").GetArrayLength());
        Assert.True(reset.GetProperty("cache").GetProperty("evicted").GetInt64() > 0);
    }

    [Fact]
    public async Task HubRejectsOutOfScopeFilterAndInvalidCursorWithoutLeakingEntries()
    {
        await using var app = await Harness.StartAsync();
        Assert.True(app.Buffer.Capture(Event("secret", "hidden"), true));
        using var socket = await app.ConnectAsync();
        var forbidden = await socket.SubscribeAsync(new("forbidden", new() { SourceId = "secret" }));
        Assert.False(forbidden.GetProperty("success").GetBoolean());
        Assert.Equal("source_forbidden", forbidden.GetProperty("code").GetString());
        var invalid = await socket.SubscribeAsync(new("invalid", new(), app.Buffer.GetStats().Epoch, "999999"));
        Assert.False(invalid.GetProperty("success").GetBoolean());
        Assert.Equal("invalid_query", invalid.GetProperty("code").GetString());
        Assert.True((await socket.SubscribeAsync(new("valid", new()))).GetProperty("success").GetBoolean());
        Assert.Empty((await socket.EventAsync("LiveLogsUpdated")).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task HubStopsBeforeNextLogAfterLogout()
    {
        await using var app = await Harness.StartAsync();
        using var socket = await app.ConnectAsync();
        await socket.SubscribeAsync(new("current", new()));
        await socket.EventAsync("LiveLogsUpdated");
        Assert.True((await app.Store.LogoutAsync(app.Session.Token, CancellationToken.None)).Success);
        Assert.True(app.Buffer.Capture(Event("1", "must not be sent"), true));
        await app.Feed.PublishAsync(CancellationToken.None);
        await socket.EventAsync("SessionExpired");
        await socket.AssertClosedAsync();
    }

    [Fact]
    public async Task BackgroundWorkerDeliversWithoutBrowserPolling()
    {
        await using var app = await Harness.StartAsync(periodic: true);
        using var socket = await app.ConnectAsync();
        await socket.SubscribeAsync(new("periodic", new()));
        await socket.EventAsync("LiveLogsUpdated");
        Assert.True(app.Buffer.Capture(Event("1", "pushed automatically"), true));
        var update = await socket.EventAsync("LiveLogsUpdated");
        Assert.Equal("append", update.GetProperty("kind").GetString());
        Assert.Equal("pushed automatically", Assert.Single(update.GetProperty("items").EnumerateArray()).GetProperty("log").GetProperty("message").GetString());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task EpochChangeDuringSnapshotNeverSkipsEntriesInTheNewCache(int changedReads)
    {
        var buffer = new EpochRaceBuffer(changedReads);
        await using var app = await Harness.StartAsync(buffer: buffer);
        Assert.True(buffer.Capture(Event("1", "new epoch evidence"), true));
        using var socket = await app.ConnectAsync();
        Assert.True((await socket.SubscribeAsync(new("epoch-race", new()))).GetProperty("success").GetBoolean());
        var snapshot = await socket.EventAsync("LiveLogsUpdated");
        Assert.Equal(2, buffer.SnapshotReads);
        Assert.Equal("epoch_changed", snapshot.GetProperty("resetReason").GetString());
        if (changedReads == 1)
        {
            Assert.Equal("new epoch evidence", Assert.Single(snapshot.GetProperty("items").EnumerateArray()).GetProperty("log").GetProperty("message").GetString());
        }
        else
        {
            // 连续两次切换时停止重试，用零水位继续补收，不能把新缓存最新水位误当成已经显示。
            Assert.Empty(snapshot.GetProperty("items").EnumerateArray());
            Assert.Equal("0", snapshot.GetProperty("cursor").GetString());
            await app.Feed.PublishAsync(CancellationToken.None);
            var recovered = await socket.EventAsync("LiveLogsUpdated");
            Assert.Equal("append", recovered.GetProperty("kind").GetString());
            Assert.Equal("new epoch evidence", Assert.Single(recovered.GetProperty("items").EnumerateArray()).GetProperty("log").GetProperty("message").GetString());
        }
    }

    [Fact]
    public async Task FirstSubscriptionReloadsSettingsAfterStartupWasUnavailable()
    {
        await using var app = await Harness.StartAsync();
        app.Buffer.SetAvailable(false);
        using var socket = await app.ConnectAsync();
        Assert.True((await socket.SubscribeAsync(new("after-setup", new()))).GetProperty("success").GetBoolean());
        Assert.True((await socket.EventAsync("LiveLogsUpdated")).GetProperty("cache").GetProperty("available").GetBoolean());
        Assert.True(app.Buffer.Capture(Event("1", "after settings became ready"), true));
        await app.Feed.PublishAsync(CancellationToken.None);
        Assert.Single((await socket.EventAsync("LiveLogsUpdated")).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task OldSchemaAllowsHostStartupAndReportsExplicitMigrationRequirement()
    {
        await using var app = await Harness.StartAsync(legacy: true);
        Assert.False(app.Buffer.GetStats().Available);
        using var response = await app.SendAsync(HttpMethod.Get, BasePath + "/settings");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("live_logs_migration_required", (await BodyAsync(response)).GetProperty("code").GetString());
        using var socket = await app.ConnectAsync();
        var subscription = await socket.SubscribeAsync(new("legacy", new()));
        Assert.False(subscription.GetProperty("success").GetBoolean());
        Assert.Equal("live_logs_migration_required", subscription.GetProperty("code").GetString());
        Assert.True((await app.Store.ReadMcpSettingsAsync(CancellationToken.None)).Success);
        await using var command = app.Anchor.CreateCommand();
        command.CommandText = "SELECT max(version) FROM schema_versions;";
        Assert.Equal(5L, await command.ExecuteScalarAsync());
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE name='live_log_settings';";
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task StartupConfigurationFailureDisablesOnlyCacheAndHonorsCancellation()
    {
        var buffer = new LiveLogBuffer();
        buffer.SetAvailable(true);
        var service = new LiveLogConfigurationService(new UnavailableSettingsStore(), buffer);
        await service.StartAsync(CancellationToken.None);
        Assert.False(buffer.GetStats().Available);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.StartAsync(cancelled.Token));
    }

    [Fact]
    public async Task HungSourceProviderCannotAccumulateQueriesAcrossSubscriptions()
    {
        await using var app = await Harness.StartAsync();
        var neverCompletes = new TaskCompletionSource<IReadOnlyList<LogSource>>(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Sources.Read = _ => neverCompletes.Task;
        var firstAborted = false;
        Assert.True(app.Feed.Connect("synthetic-slow-one", app.Session.User.Id, app.Session.Token, () => firstAborted = true));
        var first = await app.Feed.SubscribeAsync("synthetic-slow-one", new("first", new()), CancellationToken.None);
        Assert.Equal("service_unavailable", first.Code);
        Assert.True(firstAborted);
        var secondAborted = false;
        Assert.True(app.Feed.Connect("synthetic-slow-two", app.Session.User.Id, app.Session.Token, () => secondAborted = true));
        var second = await app.Feed.SubscribeAsync("synthetic-slow-two", new("second", new()), CancellationToken.None);
        Assert.Equal("service_unavailable", second.Code);
        Assert.True(secondAborted);
        Assert.Equal(1, app.Sources.Reads);
        neverCompletes.SetResult([]);
    }

    private static LogEvent Event(string sourceId, string message) => new()
    {
        EventId = 1, SourceId = sourceId, PeerAddress = "192.0.2.10", Transport = "tcp",
        ReceivedAt = DateTimeOffset.Parse("2026-01-01T00:00:00.000Z", System.Globalization.CultureInfo.InvariantCulture),
        Message = message, Hostname = "synthetic-host", AppName = "synthetic-app", Severity = 3,
        RawBytes = Encoding.UTF8.GetBytes("synthetic raw evidence"), ParseStatus = "parsed"
    };

    private static string Cookie(string token) => SessionAuthenticationHandler.CookieName + "=" + token;
    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private sealed class SourceRegistry : ISourceRegistry
    {
        public int Reads;
        public Func<CancellationToken, Task<IReadOnlyList<LogSource>>> Read { get; set; } = _ => Task.FromResult<IReadOnlyList<LogSource>>(
            [new("1", "Synthetic switch", "192.0.2.10", "UTC", 365, true), new("2", "Synthetic host", "192.0.2.11", "UTC", 365, false)]);
        public Task<IReadOnlyList<LogSource>> ListAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref Reads);
            return Read(ct);
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        public required IHost Host { get; init; }
        public required HttpClient Client { get; init; }
        public required SqliteConnection Anchor { get; init; }
        public required ControlStore Store { get; init; }
        public required SessionTicket Session { get; init; }
        public required SourceRegistry Sources { get; init; }
        public bool ForceNonAdmin { get; set; }
        public ILiveLogBuffer Buffer => Host.Services.GetRequiredService<ILiveLogBuffer>();
        public LiveLogFeed Feed => Host.Services.GetRequiredService<LiveLogFeed>();

        public static async Task<Harness> StartAsync(bool periodic = false, ILiveLogBuffer? buffer = null, bool legacy = false)
        {
            var options = new ControlStoreOptions { ConnectionString = $"Data Source=live-logs-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False" };
            var anchor = new SqliteConnection(options.ConnectionString);
            await anchor.OpenAsync();
            var store = new ControlStore(options);
            if (legacy)
            {
                foreach (var name in new[] { "001_control", "002_listener_settings", "003_source_filters", "004_notification_settings", "005_mcp_service" })
                {
                    await using var resource = typeof(ControlStore).Assembly.GetManifestResourceStream("Syslog.Api.Data." + name + ".sql")!;
                    using var reader = new StreamReader(resource);
                    await using var command = anchor.CreateCommand();
                    command.CommandText = await reader.ReadToEndAsync();
                    await command.ExecuteNonQueryAsync();
                }
            }
            else await store.InitializeAsync(CancellationToken.None);
            Assert.True((await store.SetupAdminAsync("admin", Password, CancellationToken.None)).Success);
            var session = (await store.LoginAsync("admin", Password, CancellationToken.None)).Data!;
            var sources = new SourceRegistry();
            Harness? harness = null;
            var host = await new HostBuilder().ConfigureLogging(logging => logging.ClearProviders()).ConfigureWebHost(web => web.UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddSyslog(new ConfigurationBuilder().Build());
                    services.RemoveAll<IHostedService>();
                    if (periodic) services.AddHostedService<LiveLogPushWorker>();
                    services.AddSingleton<IControlStore>(store);
                    services.AddSingleton<ILiveLogSettingsStore>(store);
                    services.AddSingleton<ISourceRegistry>(sources);
                    if (buffer is not null) services.AddSingleton(buffer);
                }).Configure(app =>
                {
                    app.UseMiddleware<ApiBoundaryMiddleware>();
                    app.UseRouting();
                    app.UseAuthentication();
                    app.Use(async (context, next) =>
                    {
                        if (harness?.ForceNonAdmin == true && context.User.Identity?.IsAuthenticated == true)
                            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, "viewer")], "synthetic"));
                        await next();
                    });
                    app.UseAuthorization();
                    app.UseRateLimiter();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapControllers();
                        endpoints.MapHub<LiveLogsHub>(HubPath, options => options.Transports = HttpTransportType.WebSockets);
                    });
                })).StartAsync();
            await host.Services.GetRequiredService<LiveLogConfigurationService>().StartAsync(CancellationToken.None);
            var client = host.GetTestClient();
            client.BaseAddress = new Uri("https://localhost");
            harness = new Harness { Host = host, Client = client, Anchor = anchor, Store = store, Session = session, Sources = sources };
            return harness;
        }

        public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? data = null,
            bool authenticated = true, string? origin = null, bool guarded = true)
        {
            using var request = new HttpRequestMessage(method, path);
            if (authenticated) request.Headers.Add("Cookie", Cookie(Session.Token));
            if (guarded) request.Headers.Add("X-Syslog-Request", "1");
            if (origin is not null) request.Headers.Add("Origin", origin);
            if (data is not null) request.Content = JsonContent.Create(data, options: Json);
            return await Client.SendAsync(request);
        }

        public async Task<HubSocket> ConnectAsync()
        {
            var client = Host.GetTestServer().CreateWebSocketClient();
            client.ConfigureRequest = request =>
            {
                request.Headers.Origin = "https://localhost";
                request.Headers.Cookie = Cookie(Session.Token);
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var socket = await client.ConnectAsync(new Uri("wss://localhost" + HubPath), timeout.Token);
            await socket.SendAsync(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e"), WebSocketMessageType.Text, true, timeout.Token);
            return new HubSocket(socket);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await Host.StopAsync();
            Host.Dispose();
            await Anchor.DisposeAsync();
        }
    }

    private sealed class UnavailableSettingsStore : ILiveLogSettingsStore
    {
        public Task<ApiResult<SavedLiveLogSettings>> ReadLiveLogSettingsAsync(CancellationToken ct) => throw new InvalidOperationException("synthetic unavailable control database");
        public Task<ApiResult<SavedLiveLogSettings>> SaveLiveLogSettingsAsync(SaveLiveLogSettingsRequest input, long actorId, string token, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class EpochRaceBuffer(int changedReads) : ILiveLogBuffer
    {
        private readonly LiveLogBuffer inner = new();
        public int SnapshotReads { get; private set; }
        public bool Capture(LogEvent item, bool ignored) => inner.Capture(item, ignored);
        public void SetAvailable(bool available) => inner.SetAvailable(available);
        public void ApplySettings(LiveLogCacheSettings settings) => inner.ApplySettings(settings);
        public LiveLogCacheStats GetStats() => inner.GetStats();
        public ApiResult<LiveLogSnapshot> ReadSnapshot(LiveLogFilter filter, IReadOnlyCollection<string> allowedSourceIds,
            string? beforeSequence = null, int take = 100, int maxBytes = 512 * 1024)
        {
            if (++SnapshotReads > changedReads) return inner.ReadSnapshot(filter, allowedSourceIds, beforeSequence, take, maxBytes);
            var stats = inner.GetStats();
            return ApiResult<LiveLogSnapshot>.Ok(new([], stats.Epoch, "0", null, false, false, stats, "epoch_changed"));
        }
        public ApiResult<LiveLogDelta> ReadAfter(LiveLogFilter filter, IReadOnlyCollection<string> allowedSourceIds,
            string epoch, string afterSequence, int take = 200, int maxBytes = 512 * 1024) => inner.ReadAfter(filter, allowedSourceIds, epoch, afterSequence, take, maxBytes);
        public ApiResult<LiveLogRecord> GetDetail(string id, IReadOnlyCollection<string> allowedSourceIds) => inner.GetDetail(id, allowedSourceIds);
    }

    private sealed class HubSocket(WebSocket socket) : IDisposable
    {
        private readonly Queue<JsonElement> messages = new();
        private readonly Queue<JsonElement> events = new();
        private readonly Dictionary<string, JsonElement> completions = new();
        private string pending = "";
        private int invocationId;

        public async Task<JsonElement> SubscribeAsync(LiveLogSubscribeRequest request)
        {
            var id = (++invocationId).ToString(System.Globalization.CultureInfo.InvariantCulture);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await socket.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = 1, invocationId = id, target = "Subscribe", arguments = new[] { request } }, Json) + '\u001e'),
                WebSocketMessageType.Text, true, timeout.Token);
            while (!completions.ContainsKey(id))
            {
                var message = await ReadAsync(timeout.Token);
                Assert.NotNull(message);
                QueuePayload(message.Value);
            }
            var completion = completions[id];
            completions.Remove(id);
            Assert.False(completion.TryGetProperty("error", out var error), error.ToString());
            return completion.GetProperty("result");
        }

        public async Task<JsonElement> EventAsync(string expected)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (events.Count == 0)
            {
                var message = await ReadAsync(timeout.Token);
                Assert.NotNull(message);
                QueuePayload(message.Value);
            }
            var result = events.Dequeue();
            Assert.Equal(expected, result.GetProperty("target").GetString());
            var arguments = result.GetProperty("arguments");
            return arguments.GetArrayLength() == 0 ? default : arguments[0];
        }

        private void QueuePayload(JsonElement message)
        {
            if (message.TryGetProperty("target", out _)) events.Enqueue(message);
            else if (message.TryGetProperty("type", out var type) && type.GetInt32() == 3)
                completions[message.GetProperty("invocationId").GetString()!] = message;
        }

        public async Task AssertClosedAsync()
        {
            Assert.Empty(events);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (await ReadAsync(timeout.Token) is { } message)
                Assert.False(message.TryGetProperty("target", out _), "退出后不能继续推送日志正文。");
        }

        private async Task<JsonElement?> ReadAsync(CancellationToken ct)
        {
            while (messages.Count == 0)
            {
                var bytes = new byte[8192];
                var result = await socket.ReceiveAsync(bytes.AsMemory(), ct);
                if (result.MessageType == WebSocketMessageType.Close) return null;
                pending += Encoding.UTF8.GetString(bytes, 0, result.Count);
                var records = pending.Split('\u001e');
                foreach (var record in records[..^1])
                {
                    using var json = JsonDocument.Parse(record);
                    messages.Enqueue(json.RootElement.Clone());
                }
                pending = records[^1];
            }
            return messages.Dequeue();
        }

        public void Dispose() => socket.Dispose();
    }
}
