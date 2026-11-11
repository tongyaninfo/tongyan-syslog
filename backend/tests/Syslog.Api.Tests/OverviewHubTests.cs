using System.Net;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Http.Features;
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
using Syslog.Ingest.Models;
using Syslog.Ingest.Options;
using Syslog.Ingest.Service.impl;
using Syslog.Shared.Models;
using Syslog.Shared.Service;
using Xunit;

namespace Syslog.Api.Tests;

/// <summary>真实 SignalR 协议和内存控制库；TestServer 不绑定宿主端口，不使用真实设备数据。</summary>
public sealed class OverviewHubTests
{
    private const string Password = "Synthetic-Password-2026";
    private const string NewPassword = "Synthetic-New-Password-2026";
    private const string HubPath = "/hubs/overview";

    [Theory]
    [InlineData("anonymous", 401)]
    [InlineData("forged-cookie", 401)]
    [InlineData("bearer-only", 401)]
    [InlineData("url-token", 403)]
    [InlineData("external-origin", 403)]
    [InlineData("missing-guard", 403)]
    [InlineData("non-admin", 403)]
    [InlineData("http", 400)]
    public async Task NegotiationRejectsInvalidCredentialsAndOrigins(string scenario, int expected)
    {
        await using var app = await Harness.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post,
            (scenario == "http" ? "http://localhost" : "https://localhost") + HubPath + "/negotiate?negotiateVersion=1" +
            (scenario == "url-token" ? "&access_token=synthetic" : ""));
        if (scenario != "missing-guard") request.Headers.Add("X-Syslog-Request", "1");
        if (scenario is not ("anonymous" or "bearer-only"))
            request.Headers.Add("Cookie", Cookie(scenario == "forged-cookie" ? "synthetic-forged" : app.Session.Token));
        if (scenario == "bearer-only") request.Headers.Add("Authorization", "Bearer " + app.Session.Token);
        if (scenario == "external-origin") request.Headers.Add("Origin", "https://example.invalid");
        app.ForceNonAdmin = scenario == "non-admin";
        using var response = await app.Client.SendAsync(request);
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal(0, app.Sources.Reads);
    }

    [Fact]
    public async Task NegotiationAllowsOnlyWebSocketsForCurrentAdminCookie()
    {
        await using var app = await Harness.StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, HubPath + "/negotiate?negotiateVersion=1");
        request.Headers.Add("Cookie", Cookie(app.Session.Token));
        request.Headers.Add("X-Syslog-Request", "1");
        using var response = await app.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("WebSockets", Assert.Single(json.RootElement.GetProperty("availableTransports").EnumerateArray()).GetProperty("transport").GetString());
        Assert.Equal(0, app.Sources.Reads);
    }

    [Theory]
    [InlineData("anonymous", 401)]
    [InlineData("forged-cookie", 401)]
    [InlineData("bearer-only", 401)]
    [InlineData("non-admin", 403)]
    [InlineData("external-origin", 403)]
    [InlineData("missing-origin", 403)]
    public async Task ExtendedConnectStillRequiresCurrentAdminCookieAndOrigin(string scenario, int expected)
    {
        await using var app = await Harness.StartAsync(extendedConnect: true);
        using var request = new HttpRequestMessage(HttpMethod.Connect, HubPath);
        if (scenario is not ("anonymous" or "bearer-only"))
            request.Headers.Add("Cookie", Cookie(scenario == "forged-cookie" ? "synthetic-forged" : app.Session.Token));
        if (scenario == "bearer-only") request.Headers.Add("Authorization", "Bearer " + app.Session.Token);
        if (scenario != "missing-origin")
            request.Headers.Add("Origin", scenario == "external-origin" ? "https://example.invalid" : "https://localhost");
        app.ForceNonAdmin = scenario == "non-admin";
        using var response = await app.Client.SendAsync(request);
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal(0, app.Sources.Reads);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://example.invalid")]
    [InlineData("https://localhost/path")]
    public async Task WebSocketUpgradeRequiresAnExactSameOrigin(string? origin)
    {
        await using var app = await Harness.StartAsync();
        var client = app.Host.GetTestServer().CreateWebSocketClient();
        client.ConfigureRequest = request =>
        {
            request.Headers.Cookie = Cookie(app.Session.Token);
            if (origin is not null) request.Headers.Origin = origin;
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConnectAsync(new Uri("wss://localhost" + HubPath), CancellationToken.None));
        Assert.Equal(0, app.Sources.Reads);
    }

    [Fact]
    public async Task FirstAndPeriodicSnapshotsUseRealStatusAndSharedSourceRead()
    {
        await using var app = await Harness.StartAsync();
        app.Status.Received = 7;
        using var first = await app.ConnectAsync();
        var firstSnapshot = await first.EventAsync("OverviewUpdated");
        Assert.Equal(7, firstSnapshot.GetProperty("status").GetProperty("received").GetInt64());
        Assert.Equal(2, firstSnapshot.GetProperty("sources").GetProperty("total").GetInt32());
        Assert.Equal(1, firstSnapshot.GetProperty("sources").GetProperty("enabled").GetInt32());
        Assert.Equal(JsonValueKind.Null, firstSnapshot.GetProperty("sourceError").ValueKind);
        Assert.Equal(JsonValueKind.Null, firstSnapshot.GetProperty("sourceTraffic").ValueKind);
        Assert.Equal(TimeSpan.Zero, firstSnapshot.GetProperty("observedAt").GetDateTimeOffset().Offset);
        using var second = await app.ConnectAsync();
        await second.EventAsync("OverviewUpdated");
        var previous = app.Sources.Reads;
        app.Status.Received = 11;
        await app.Feed.PublishAsync(CancellationToken.None);
        Assert.Equal(previous + 1, app.Sources.Reads);
        Assert.Equal(11, (await first.EventAsync("OverviewUpdated")).GetProperty("status").GetProperty("received").GetInt64());
        Assert.Equal(11, (await second.EventAsync("OverviewUpdated")).GetProperty("status").GetProperty("received").GetInt64());
    }

    [Fact]
    public async Task SourceFilterScopesTrafficWithoutChangingGlobalStatus()
    {
        await using var app = await Harness.StartAsync();
        var queue = new IngressQueue(Microsoft.Extensions.Options.Options.Create(new IngestOptions()), app.Status);
        var now = DateTimeOffset.UtcNow;
        var context = new ParseContext("1", "192.0.2.10", "udp", now, TimeZoneInfo.Utc, now.AddDays(1));
        Assert.True(queue.TryEnqueue(new ReceivedMessage([1], context)));
        Assert.True(queue.TryEnqueue(new ReceivedMessage([1], context with { SourceId = "2", PeerAddress = "192.0.2.11" })));
        Assert.True(queue.TryEnqueue(new ReceivedMessage([1], context with { SourceId = "2", PeerAddress = "192.0.2.11" })));

        using var selected = await app.ConnectAsync(sourceId: "1");
        using var unknown = await app.ConnectAsync(sourceId: "999");
        using var global = await app.ConnectAsync();
        var scoped = await selected.EventAsync("OverviewUpdated");
        var traffic = scoped.GetProperty("sourceTraffic");
        Assert.Equal("1", traffic.GetProperty("sourceId").GetString());
        Assert.Equal(1, traffic.GetProperty("received").GetInt64());
        Assert.Equal(0, traffic.GetProperty("written").GetInt64());
        Assert.Equal(0, traffic.GetProperty("ignored").GetInt64());
        Assert.Equal(3, scoped.GetProperty("status").GetProperty("received").GetInt64());
        Assert.Equal(JsonValueKind.Null, (await unknown.EventAsync("OverviewUpdated")).GetProperty("sourceTraffic").ValueKind);
        Assert.Equal(JsonValueKind.Null, (await global.EventAsync("OverviewUpdated")).GetProperty("sourceTraffic").ValueKind);

        Assert.True(queue.TryEnqueue(new ReceivedMessage([1], context)));
        await app.Feed.PublishAsync(CancellationToken.None);
        Assert.Equal(2, (await selected.EventAsync("OverviewUpdated")).GetProperty("sourceTraffic").GetProperty("received").GetInt64());
        Assert.Equal(4, (await global.EventAsync("OverviewUpdated")).GetProperty("status").GetProperty("received").GetInt64());
    }

    [Fact]
    public async Task BackgroundWorkerPushesWithoutBrowserPolling()
    {
        await using var app = await Harness.StartAsync(periodic: true);
        using var socket = await app.ConnectAsync();
        await socket.EventAsync("OverviewUpdated");
        app.Status.Written = 19;
        var next = await socket.EventAsync("OverviewUpdated");
        Assert.Equal(19, next.GetProperty("status").GetProperty("written").GetInt64());
    }

    [Fact]
    public async Task ListenerViewStreamsCompleteRuntimeWithoutReadingSourcesAndStopsAfterLogout()
    {
        await using var app = await Harness.StartAsync();
        app.Status.SetListenerState("udp", "0.0.0.0", 514, "starting");
        using var socket = await app.ConnectAsync(view: "listeners");
        var first = await socket.EventAsync("ListenerRuntimeUpdated");
        Assert.Equal(TimeSpan.Zero, first.GetProperty("observedAt").GetDateTimeOffset().Offset);
        Assert.Equal("starting", UdpState(first));
        Assert.Equal(0, app.Sources.Reads);

        app.Status.SetListenerState("udp", "0.0.0.0", 514, "listening");
        await app.Feed.PublishAsync(CancellationToken.None);
        var next = await socket.EventAsync("ListenerRuntimeUpdated");
        Assert.Equal("listening", UdpState(next));
        Assert.True(Revision(next) > Revision(first));
        Assert.Empty(next.GetProperty("runtime").GetProperty("connectionErrors").EnumerateArray());
        Assert.Equal(0, app.Sources.Reads);

        using var reconnected = await app.ConnectAsync(view: "listeners");
        Assert.Equal("listening", UdpState(await reconnected.EventAsync("ListenerRuntimeUpdated")));
        Assert.True((await app.Store.LogoutAsync(app.Session.Token, CancellationToken.None)).Success);
        await app.Feed.PublishAsync(CancellationToken.None);
        await socket.EventAsync("SessionExpired");
        await socket.AssertClosedAsync();
        await reconnected.EventAsync("SessionExpired");
        await reconnected.AssertClosedAsync();

        static string? UdpState(JsonElement snapshot) => snapshot.GetProperty("runtime").GetProperty("states")
            .EnumerateArray().Single(state => state.GetProperty("protocol").GetString() == "udp").GetProperty("state").GetString();
        static long Revision(JsonElement snapshot) => long.Parse(snapshot.GetProperty("runtime").GetProperty("revision").GetString()!);
    }

    [Theory]
    [InlineData("unrecognized", null)]
    [InlineData("listeners", "1")]
    public async Task RejectsInvalidViewOrSourceFilterForListenerView(string view, string? sourceId)
    {
        await using var app = await Harness.StartAsync();
        using var socket = await app.ConnectAsync(sourceId: sourceId, view: view);
        await socket.AssertClosedAsync();
        Assert.Equal(0, app.Sources.Reads);
    }

    [Fact]
    public async Task NoSubscribersMeansNoSourceReads()
    {
        await using var app = await Harness.StartAsync();
        await app.Feed.PublishAsync(CancellationToken.None);
        await app.Feed.PublishAsync(CancellationToken.None);
        Assert.Equal(0, app.Sources.Reads);
    }

    [Fact]
    public async Task SourceFailureKeepsRealCountersAndReportsUnknownSourceCount()
    {
        await using var app = await Harness.StartAsync();
        app.Sources.Read = _ => throw new InvalidOperationException("synthetic private backend detail");
        app.Status.Dropped = 3;
        using var socket = await app.ConnectAsync();
        var snapshot = await socket.EventAsync("OverviewUpdated");
        Assert.Equal(3, snapshot.GetProperty("status").GetProperty("dropped").GetInt64());
        Assert.Equal(JsonValueKind.Null, snapshot.GetProperty("sources").ValueKind);
        Assert.Equal("service_unavailable", snapshot.GetProperty("sourceError").GetString());
        Assert.DoesNotContain("private", snapshot.GetRawText());
    }

    [Fact]
    public async Task IgnoredCancellationNeverStartsUnboundedSourceReads()
    {
        await using var app = await Harness.StartAsync();
        var pending = new TaskCompletionSource<IReadOnlyList<LogSource>>(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Sources.Read = _ => pending.Task;
        using var socket = await app.ConnectAsync();
        Assert.Equal("service_unavailable", (await socket.EventAsync("OverviewUpdated")).GetProperty("sourceError").GetString());
        await app.Feed.PublishAsync(CancellationToken.None);
        Assert.Equal("service_unavailable", (await socket.EventAsync("OverviewUpdated")).GetProperty("sourceError").GetString());
        Assert.Equal(1, app.Sources.Reads);
        pending.SetResult([]);
    }

    [Theory]
    [InlineData("logout")]
    [InlineData("password-change")]
    [InlineData("password-reset")]
    [InlineData("disable")]
    [InlineData("expiry")]
    public async Task RevokedSessionsAreClosedBeforeTheNextSnapshot(string action)
    {
        await using var app = await Harness.StartAsync();
        var user = (await app.Store.CreateUserAsync(new("operator", Password), app.Session.User.Id, app.Session.Token, CancellationToken.None)).Data!;
        var session = (await app.Store.LoginAsync(user.Username, Password, CancellationToken.None)).Data!;
        using var socket = await app.ConnectAsync(session.Token);
        await socket.EventAsync("OverviewUpdated");
        if (action == "expiry") app.Clock.Current = session.ExpiresAt;
        var outcome = action switch
        {
            "logout" => (await app.Store.LogoutAsync(session.Token, CancellationToken.None)).Success,
            "password-change" => (await app.Store.ChangePasswordAsync(user.Id, session.Token, Password, NewPassword, CancellationToken.None)).Success,
            "password-reset" => (await app.Store.ResetUserPasswordAsync(user.Id.ToString(), new(NewPassword, "1"), app.Session.User.Id, app.Session.Token, CancellationToken.None)).Success,
            "expiry" => true,
            _ => (await app.Store.UpdateUserAsync(user.Id.ToString(), new(user.Username, false, "1"), app.Session.User.Id, app.Session.Token, CancellationToken.None)).Success
        };
        Assert.True(outcome);
        await app.Feed.PublishAsync(CancellationToken.None);
        // 若 SessionExpired 之前出现业务快照，EventAsync 会立即失败，不跳过泄漏的数据。
        await socket.EventAsync("SessionExpired");
        await socket.AssertClosedAsync();
        var previous = app.Sources.Reads;
        await app.Feed.PublishAsync(CancellationToken.None);
        Assert.Equal(previous, app.Sources.Reads);
    }

    [Fact]
    public async Task RevocationDuringInitialSourceReadPreventsFirstSnapshot()
    {
        await using var app = await Harness.StartAsync();
        app.Sources.Read = async _ =>
        {
            Assert.True((await app.Store.LogoutAsync(app.Session.Token, CancellationToken.None)).Success);
            return [];
        };
        using var socket = await app.ConnectAsync();
        await socket.EventAsync("SessionExpired");
        await socket.AssertClosedAsync();
    }

    [Fact]
    public async Task SessionStoreFailureStopsDataWithoutClaimingExpiry()
    {
        await using var app = await Harness.StartAsync();
        using var socket = await app.ConnectAsync();
        await socket.EventAsync("OverviewUpdated");
        // 释放唯一内存库连接以模拟控制库不可用，不接触任何磁盘数据库。
        await app.Anchor.CloseAsync();
        await app.Feed.PublishAsync(CancellationToken.None);
        await socket.AssertClosedAsync();
    }

    private static string Cookie(string token) => SessionAuthenticationHandler.CookieName + "=" + token;

    private sealed class MetricsStore : ILogStore
    {
        public Task<long?> GetDatabaseSizeAsync(CancellationToken ct) => Task.FromResult<long?>(2048);
        public Task WriteBatchAsync(IReadOnlyList<LogEvent> events, CancellationToken ct) => throw new NotSupportedException();
        public Task<LogSearchPage> SearchAsync(LogSearch query, IReadOnlyCollection<string> allowedSourceIds, CancellationToken ct) => throw new NotSupportedException();
        public Task<LogEvent?> GetAsync(long eventId, DateTimeOffset receivedAt, IReadOnlyCollection<string> allowedSourceIds, CancellationToken ct) => throw new NotSupportedException();
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
        public required TestClock Clock { get; init; }
        public SourceRegistry Sources { get; init; } = new();
        public bool ForceNonAdmin { get; set; }
        public OverviewFeed Feed => Host.Services.GetRequiredService<OverviewFeed>();
        public IngestStatus Status => Host.Services.GetRequiredService<IngestStatus>();

        public static async Task<Harness> StartAsync(bool periodic = false, bool extendedConnect = false)
        {
            var options = new ControlStoreOptions { ConnectionString = $"Data Source=overview-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False" };
            var anchor = new SqliteConnection(options.ConnectionString);
            await anchor.OpenAsync();
            var clock = new TestClock();
            var store = new ControlStore(options, clock);
            await store.InitializeAsync(CancellationToken.None);
            Assert.True((await store.SetupAdminAsync("admin", Password, CancellationToken.None)).Success);
            var session = (await store.LoginAsync("admin", Password, CancellationToken.None)).Data!;
            var sources = new SourceRegistry();
            Harness? harness = null;
            var host = await new HostBuilder().ConfigureLogging(logging => logging.ClearProviders()).ConfigureWebHost(web => web.UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddSyslog(new ConfigurationBuilder().Build());
                    services.RemoveAll<IHostedService>();
                    if (periodic) services.AddHostedService<OverviewPushWorker>();
                    services.AddSingleton<IControlStore>(store);
                    services.AddSingleton<ISourceRegistry>(sources);
                    services.AddSingleton<ILogStore, MetricsStore>();
                    services.AddSingleton<TimeProvider>(clock);
                }).Configure(app =>
                {
                    if (extendedConnect) app.Use(async (context, next) =>
                    {
                        context.Features.Set<IHttpExtendedConnectFeature>(new SyntheticExtendedConnectFeature());
                        await next();
                    });
                    app.UseMiddleware<ApiBoundaryMiddleware>();
                    app.UseRouting();
                    app.UseAuthentication();
                    app.Use(async (context, next) =>
                    {
                        // 只模拟未来非管理员角色，仍使用真实 Cookie 会话验证。
                        if (harness?.ForceNonAdmin == true && context.User.Identity?.IsAuthenticated == true)
                            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Role, "viewer")], "synthetic"));
                        await next();
                    });
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapHub<OverviewHub>(HubPath, options => options.Transports = HttpTransportType.WebSockets));
                })).StartAsync();
            var client = host.GetTestClient();
            client.BaseAddress = new Uri("https://localhost");
            harness = new Harness { Host = host, Client = client, Anchor = anchor, Store = store, Session = session, Sources = sources, Clock = clock };
            return harness;
        }

        public async Task<HubSocket> ConnectAsync(string? token = null, string? sourceId = null, string? view = null)
        {
            var client = Host.GetTestServer().CreateWebSocketClient();
            client.ConfigureRequest = request =>
            {
                request.Headers.Origin = "https://localhost";
                request.Headers.Cookie = Cookie(token ?? Session.Token);
            };
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var url = new UriBuilder("wss://localhost" + HubPath);
            url.Query = string.Join('&', new[] { ("sourceId", sourceId), ("view", view) }
                .Where(item => item.Item2 is not null).Select(item => item.Item1 + "=" + Uri.EscapeDataString(item.Item2!)));
            var socket = await client.ConnectAsync(url.Uri, timeout.Token);
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

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Current { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Current;
    }

    private sealed class HubSocket(WebSocket socket) : IDisposable
    {
        private readonly Queue<JsonElement> _messages = new();
        private string _pending = "";

        public async Task<JsonElement> EventAsync(string expected)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (true)
            {
                var message = await ReadAsync(timeout.Token);
                Assert.NotNull(message);
                if (!message.Value.TryGetProperty("target", out var target)) continue; // 握手或协议心跳。
                Assert.Equal(expected, target.GetString());
                var arguments = message.Value.GetProperty("arguments");
                return arguments.GetArrayLength() == 0 ? default : arguments[0];
            }
        }

        public async Task AssertClosedAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (await ReadAsync(timeout.Token) is { } message)
                Assert.False(message.TryGetProperty("target", out _), "会话撤销后不能再出现业务事件。");
        }

        private async Task<JsonElement?> ReadAsync(CancellationToken ct)
        {
            while (_messages.Count == 0)
            {
                var bytes = new byte[8192];
                var result = await socket.ReceiveAsync(bytes.AsMemory(), ct);
                if (result.MessageType == WebSocketMessageType.Close) return null;
                _pending += Encoding.UTF8.GetString(bytes, 0, result.Count);
                var records = _pending.Split('\u001e');
                foreach (var record in records[..^1])
                {
                    using var json = JsonDocument.Parse(record);
                    _messages.Enqueue(json.RootElement.Clone());
                }
                _pending = records[^1];
            }
            return _messages.Dequeue();
        }

        public void Dispose() => socket.Dispose();
    }
}
