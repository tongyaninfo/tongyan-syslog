using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
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
using Syslog.Api.Common;
using Syslog.Api.Middleware;
using Syslog.Api.Models;
using Syslog.Api.Options;
using Syslog.Api.Service;
using Syslog.Api.Service.impl;
using Syslog.Ingest.Service;
using Syslog.Shared.Models;
using Syslog.Shared.Service;
using Xunit;

namespace Syslog.Api.Tests;

/// <summary>使用进程内 HTTP 管线和独立内存 SQLite，覆盖暂存日志的权限和登记边界。</summary>
public sealed class UnregisteredDevicesApiTests
{
    private const string Path = "/api/admin/v1/unregistered-devices";
    private const string SourcesPath = "/api/admin/v1/sources";
    private const string Peer = "192.0.2.10";
    private const string KeyPrefix = "0123456789abcdef0123456789abcdef-";
    private const string Key = KeyPrefix + "12";
    private const string LogsPath = Path + "/logs?peerAddress=" + Peer;
    private const string SamplePath = Path + "/sample?peerAddress=" + Peer + "&sampleKey=" + Key;
    private static readonly string[] ReadPaths = [Path, LogsPath, SamplePath];
    private static readonly byte[] Raw = [0, 255, 60, 62];

    [Fact]
    public void AbsentAllowedNetworksKeepsTheBuiltInDefaults()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ingest:Enabled"] = "true",
        }).Build();
        using var services = new ServiceCollection().AddSyslog(configuration).BuildServiceProvider();
        var buffer = services.GetRequiredService<IUnregisteredDeviceBuffer>();
        Assert.Contains("192.168.0.0/16", buffer.Snapshot().Status.AllowedNetworks);
        Assert.True(buffer.IsAllowed(IPAddress.Parse("192.168.1.10")));
        Assert.False(buffer.IsAllowed(IPAddress.Parse(Peer)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitAllowedNetworksReplacesDefaultsAndLowerPriorityArray(bool empty)
    {
        string[] networks = empty ? [] : ["192.0.2.0/24"];
        var json = JsonSerializer.Serialize(new { Ingest = new { UnregisteredDevices = new { AllowedNetworks = networks } } });
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ingest:Enabled"] = "true",
            ["Ingest:UnregisteredDevices:AllowedNetworks:0"] = "10.0.0.0/8",
            ["Ingest:UnregisteredDevices:AllowedNetworks:1"] = "172.16.0.0/12",
        }).AddJsonStream(stream).Build();
        using var services = new ServiceCollection().AddSyslog(configuration).BuildServiceProvider();
        var buffer = services.GetRequiredService<IUnregisteredDeviceBuffer>();
        Assert.Equal(networks, buffer.Snapshot().Status.AllowedNetworks);
        Assert.Equal(!empty, buffer.IsAllowed(IPAddress.Parse(Peer)));
        Assert.False(buffer.IsAllowed(IPAddress.Parse("192.168.1.10")));
        Assert.False(buffer.IsAllowed(IPAddress.Parse("172.16.1.10")));
    }

    [Fact]
    public async Task AllReadsRequireAnAdministrator()
    {
        await using var app = await Harness.StartAsync();
        foreach (var path in ReadPaths)
            await ExpectErrorAsync(await app.Client.GetAsync(path), HttpStatusCode.Unauthorized, "authentication_required");
        await app.LoginAsync();
        app.NonAdmin = true;
        foreach (var path in ReadPaths)
            await ExpectErrorAsync(await app.Client.GetAsync(path), HttpStatusCode.Forbidden, "forbidden");
        Assert.Equal(0, app.Buffer.Reads);
    }

    [Fact]
    public async Task OnlyDetailReturnsRawBytesWithoutAlteringTheBase64Evidence()
    {
        await using var app = await Harness.StartAsync();
        await app.LoginAsync();
        var page = await app.Client.GetFromJsonAsync<ApiResult<UnregisteredDevicePage>>(Path);
        Assert.Equal(Peer, Assert.Single(page!.Data!.Items).PeerAddress);
        Assert.Equal(1, page.Data.Status.SampleCount);
        var normalizedPeer = Uri.EscapeDataString($"::ffff:{Peer}");
        using var summaryResponse = await app.Client.GetAsync($"{Path}/logs?peerAddress={normalizedPeer}");
        summaryResponse.EnsureSuccessStatusCode();
        using var summary = JsonDocument.Parse(await summaryResponse.Content.ReadAsStringAsync());
        var item = Assert.Single(summary.RootElement.GetProperty("data").EnumerateArray());
        Assert.Equal(Key, item.GetProperty("sampleKey").GetString());
        Assert.Equal(Raw.Length, item.GetProperty("byteCount").GetInt32());
        Assert.False(item.TryGetProperty("rawBytes", out _));

        using var response = await app.Client.GetAsync(SamplePath);
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl!.NoStore);
        using var detail = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(Raw, detail.RootElement.GetProperty("data").GetProperty("rawBytes").GetBytesFromBase64());
    }

    [Fact]
    public async Task RegistrationHidesStaleSamplesAndDuplicateCannotEnableADisabledSource()
    {
        await using var app = await Harness.StartAsync();
        await app.LoginAsync();
        Assert.Equal(HttpStatusCode.OK, (await app.Client.GetAsync(SamplePath)).StatusCode);
        var source = new SourceInput("示例来源", $"::ffff:{Peer}", "UTC", 365, false);
        using var created = await app.Client.PostAsJsonAsync(SourcesPath, source);
        created.EnsureSuccessStatusCode();
        var saved = (await created.Content.ReadFromJsonAsync<ApiResult<LogSource>>())!.Data!;
        Assert.Equal(Peer, saved.PeerAddress);
        Assert.False(saved.Enabled);

        // 接收监听关闭，测试替身仍留有样本，管理 API 必须用已提交的登记事实立即过滤。
        var page = (await app.Client.GetFromJsonAsync<ApiResult<UnregisteredDevicePage>>(Path))!.Data!;
        Assert.Empty(page.Items);
        Assert.Equal(0, page.Status.DeviceCount);
        Assert.Equal(0, page.Status.SampleCount);
        await ExpectErrorAsync(await app.Client.GetAsync(LogsPath), HttpStatusCode.NotFound, "not_found");
        await ExpectErrorAsync(await app.Client.GetAsync(SamplePath), HttpStatusCode.NotFound, "not_found");
        await ExpectErrorAsync(await app.Client.PostAsJsonAsync(SourcesPath, source with { PeerAddress = Peer, Enabled = true }),
            HttpStatusCode.Conflict, "CONFLICT");
        var sources = (await app.Client.GetFromJsonAsync<ApiResult<LogSource[]>>(SourcesPath))!.Data!;
        Assert.Equal(saved, Assert.Single(sources));

        await using var audit = app.Anchor.CreateCommand();
        audit.CommandText = "SELECT target_id FROM audit_entries WHERE action='source.create' ORDER BY id;";
        await using var reader = await audit.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(saved.Id, reader.GetString(0));
        Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task SourceRegistryFailureReturnsSanitizedUnavailableWithoutReadingSamples()
    {
        await using var app = await Harness.StartAsync();
        await app.LoginAsync();
        app.Registry.Fail = true;
        foreach (var path in ReadPaths)
        {
            using var response = await app.Client.GetAsync(path);
            var body = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("synthetic-registry-internal-detail", body);
            Assert.DoesNotContain(Peer, body);
            await ExpectErrorAsync(response, HttpStatusCode.ServiceUnavailable, "service_unavailable");
        }
        Assert.Equal(0, app.Buffer.Reads);
    }

    [Fact]
    public async Task InvalidQueriesAndExpiredSamplesReturnMeaningfulStatus()
    {
        await using var app = await Harness.StartAsync();
        await app.LoginAsync();
        foreach (var path in new[] { Path + "/logs", Path + "/logs?peerAddress=example.invalid", Path + "/sample?peerAddress=" + Peer,
                     SamplePath + "x", WithSampleKey("12"), WithSampleKey(KeyPrefix + "012"), WithSampleKey(KeyPrefix + "9223372036854775808") })
            await ExpectErrorAsync(await app.Client.GetAsync(path), HttpStatusCode.BadRequest, "invalid_query");
        Assert.Equal(0, app.Buffer.Reads);

        await ExpectErrorAsync(await app.Client.GetAsync(WithSampleKey(KeyPrefix + "13")),
            HttpStatusCode.NotFound, "not_found");
        await ExpectErrorAsync(await app.Client.GetAsync(WithSampleKey("11111111111111111111111111111111-12")),
            HttpStatusCode.NotFound, "not_found");
        app.Buffer.Available = false;
        await ExpectErrorAsync(await app.Client.GetAsync(LogsPath), HttpStatusCode.NotFound, "not_found");
        await ExpectErrorAsync(await app.Client.GetAsync(SamplePath), HttpStatusCode.NotFound, "not_found");
    }

    private static string WithSampleKey(string sampleKey) => SamplePath.Replace(Key, sampleKey, StringComparison.Ordinal);

    private static async Task ExpectErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        using (response)
        {
            Assert.Equal(status, response.StatusCode);
            var result = await response.Content.ReadFromJsonAsync<ApiResult<object>>();
            Assert.False(result!.Success);
            Assert.Equal(code, result.Code);
            Assert.Null(result.Data);
        }
    }

    private sealed class Harness(IHost host, SqliteConnection anchor, FakeBuffer buffer, Registry registry) : IAsyncDisposable
    {
        private const string Password = "Synthetic-Password-2026";
        public SqliteConnection Anchor { get; } = anchor;
        public FakeBuffer Buffer { get; } = buffer;
        public Registry Registry { get; } = registry;
        public bool NonAdmin { get; set; }
        public HttpClient Client { get; } = host.GetTestClient();

        public static async Task<Harness> StartAsync()
        {
            var options = new ControlStoreOptions { ConnectionString = $"Data Source=unregistered-api-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False" };
            var anchor = new SqliteConnection(options.ConnectionString);
            await anchor.OpenAsync();
            var store = new ControlStore(options);
            await store.InitializeAsync(CancellationToken.None);
            var buffer = new FakeBuffer();
            var registry = new Registry(store);
            Harness? harness = null;
            var host = await new HostBuilder().ConfigureLogging(logging => logging.ClearProviders()).ConfigureWebHost(web => web.UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddSyslog(new ConfigurationBuilder().Build());
                    services.AddSingleton<IControlStore>(store);
                    services.AddSingleton<ISourceRegistry>(registry);
                    services.AddSingleton<IUnregisteredDeviceBuffer>(buffer);
                }).Configure(app =>
                {
                    app.UseMiddleware<ApiBoundaryMiddleware>();
                    app.UseRouting();
                    app.UseAuthentication();
                    app.Use(async (context, next) =>
                    {
                        if (harness?.NonAdmin == true && context.User.Identity?.IsAuthenticated == true)
                            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "viewer")], "synthetic"));
                        await next();
                    });
                    app.UseAuthorization();
                    app.UseRateLimiter();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                })).StartAsync();
            harness = new Harness(host, anchor, buffer, registry);
            harness.Client.BaseAddress = new Uri("https://localhost");
            harness.Client.DefaultRequestHeaders.Add("X-Syslog-Request", "1");
            return harness;
        }

        public async Task LoginAsync()
        {
            Assert.True((await Registry.Store.SetupAdminAsync("admin", Password, CancellationToken.None)).Success);
            using var response = await Client.PostAsJsonAsync("/api/admin/v1/session/login", new { username = "admin", password = Password });
            response.EnsureSuccessStatusCode();
            Client.DefaultRequestHeaders.Add("Cookie", Assert.Single(response.Headers.GetValues("Set-Cookie")).Split(';')[0]);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            host.Dispose();
            await Anchor.DisposeAsync();
        }
    }

    private sealed class Registry(ControlStore store) : ISourceRegistry
    {
        public ControlStore Store { get; } = store;
        public bool Fail { get; set; }

        public Task<IReadOnlyList<LogSource>> ListAsync(CancellationToken cancellationToken) => Fail
            ? Task.FromException<IReadOnlyList<LogSource>>(new InvalidOperationException("synthetic-registry-internal-detail"))
            : Store.ListAsync(cancellationToken);
    }

    private sealed class FakeBuffer : IUnregisteredDeviceBuffer
    {
        public void SetReceptionEnabled(bool enabled) { }
        private static readonly IPAddress Address = IPAddress.Parse(Peer);
        private static readonly DateTimeOffset ReceivedAt = new(2026, 11, 10, 1, 0, 0, TimeSpan.Zero);
        public bool Available { get; set; } = true;
        public int Reads { get; private set; }

        public UnregisteredDevicePage Snapshot()
        {
            Reads++;
            return new(Available ? [new(Peer, ReceivedAt, ReceivedAt, ["udp"], 1, 1)] : [],
                new(true, ["192.0.2.0/24"], Available ? 1 : 0, Available ? 1 : 0, Available ? Raw.Length : 0, 1024, 10, 1, 0, 0));
        }

        public IReadOnlyList<UnregisteredLogSummary> GetLogs(IPAddress address)
        {
            Reads++;
            return Available && Address.Equals(address) ? [new(Key, ReceivedAt, "udp", Raw.Length, "合成日志")] : [];
        }

        public UnregisteredLogDetail? GetSample(IPAddress address, string sampleKey)
        {
            Reads++;
            return Available && Address.Equals(address) && sampleKey == Key ? new(Key, ReceivedAt, "udp", Raw) : null;
        }

        public bool TryCapture(ReadOnlySpan<byte> bytes, IPAddress address, string transport) => throw new NotSupportedException();
        public bool IsAllowed(IPAddress address) => throw new NotSupportedException();
        public bool TryReserveConnection(IPAddress address) => throw new NotSupportedException();
        public void ReleaseConnection(IPAddress address) => throw new NotSupportedException();
        public void RemoveRegistered(IReadOnlyCollection<IPAddress> addresses) => throw new NotSupportedException();
    }
}
