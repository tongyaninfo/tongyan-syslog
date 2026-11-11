using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Syslog.Api.Models;
using Syslog.Api.Service.impl;
using Syslog.Ingest.Models;
using Syslog.Ingest.Options;
using Syslog.Ingest.Service;
using Syslog.Shared.Models;
using Xunit;

namespace Syslog.Api.Tests;

public sealed partial class UserManagementApiTests
{
    private const string ListenersPath = "/api/admin/v1/system/listeners";
    private const string ReceptionPath = ListenersPath + "/reception";

    [Fact]
    public async Task ListenerSettingsRequireAdminHttpsAndSameOriginRequestGuard()
    {
        await using var app = await Harness.StartAsync();
        var input = new SaveListenerSettingsRequest("0", ListenerConfiguration.Default());
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client.GetAsync(ListenersPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client.PutAsJsonAsync(ListenersPath, input)).StatusCode);
        await app.BootstrapAndLoginAsync();
        app.ForceNonAdmin = true;
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Client.GetAsync(ListenersPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Client.PutAsJsonAsync(ListenersPath, input)).StatusCode);
        app.ForceNonAdmin = false;
        Assert.Equal("https_required", await CodeAsync(await app.Client.PutAsJsonAsync("http://localhost" + ListenersPath, input)));
        app.Client.DefaultRequestHeaders.Remove("X-Syslog-Request");
        Assert.Equal("csrf_rejected", await CodeAsync(await app.Client.PutAsJsonAsync(ListenersPath, input)));
        app.Client.DefaultRequestHeaders.Add("X-Syslog-Request", "1");
        app.Client.DefaultRequestHeaders.Add("Origin", "https://attacker.invalid");
        Assert.Equal("csrf_rejected", await CodeAsync(await app.Client.PutAsJsonAsync(ListenersPath, input)));
        Assert.Equal("0", (await app.Store.ReadListenerSettingsAsync(CancellationToken.None)).Data!.Version);
    }

    [Fact]
    public async Task ListenerApiReturnsSavedRunningAndPendingWithoutChangingLiveOptions()
    {
        await using var app = await Harness.StartAsync();
        await app.BootstrapAndLoginAsync();
        var before = (await app.Client.GetFromJsonAsync<ApiResult<ListenerSettingsView>>(ListenersPath))!.Data!;
        Assert.Equal("0", before.Saved.Version);
        Assert.False(before.RestartRequired);
        Assert.Equal(4, before.States.Count);
        Assert.Empty(before.ConnectionErrors);
        var configuration = before.Saved.Configuration with
        {
            Enabled = true,
            Listeners = before.Saved.Configuration.Listeners.Select(item => item.Protocol == "relp"
                ? item with { Enabled = true, Port = 12514 } : item).ToArray()
        };
        var request = new SaveListenerSettingsRequest(before.Saved.Version, configuration);
        var response = await app.Client.PutAsJsonAsync(ListenersPath, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = (await response.Content.ReadFromJsonAsync<ApiResult<ListenerSettingsView>>())!.Data!;
        Assert.Equal("1", saved.Saved.Version);
        Assert.True(saved.Saved.Configuration.Enabled);
        Assert.False(saved.Running.Enabled);
        Assert.True(saved.RestartRequired);
        Assert.Equal(12514, saved.Saved.Configuration.Listeners.Single(item => item.Protocol == "relp").Port);
        var stale = await app.Client.PutAsJsonAsync(ListenersPath, request);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("version_conflict", await CodeAsync(stale));
        var invalid = await app.Client.PutAsJsonAsync(ListenersPath, request with { Version = "1", Configuration = new(true, []) });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("invalid_listener_configuration", await CodeAsync(invalid));
        var after = (await app.Client.GetFromJsonAsync<ApiResult<ListenerSettingsView>>(ListenersPath))!.Data!;
        Assert.Equal("1", after.Saved.Version);
        Assert.True(after.RestartRequired);
    }

    [Fact]
    public async Task ListenerApiReturnsSafeConnectionMessageWithoutChangingListeningState()
    {
        await using var app = await Harness.StartAsync();
        await app.BootstrapAndLoginAsync();
        var status = app.Host.Services.GetRequiredService<IngestStatus>();
        status.SetListenerState("tcp", "0.0.0.0", 514, "listening", "connection_io_failed");

        var result = await View(await app.Client.GetAsync(ListenersPath));
        var tcp = Assert.Single(result.States, endpoint => endpoint.Protocol == "tcp");
        Assert.Equal("listening", tcp.State);
        Assert.Equal("connection_io_failed", tcp.FailureCode);
        Assert.Contains("该连接在读写时中断或失败", tcp.FailureMessage);
        Assert.All(result.States.Where(endpoint => endpoint.Protocol != "tcp"),
            endpoint => Assert.Empty(endpoint.FailureMessage));
    }

    [Fact]
    public async Task ListenerApiReportsExplicitMigrationRequiredWithoutChangingLegacySchema()
    {
        await using var app = await Harness.StartAsync(initialize: false);
        await using var resource = typeof(ControlStore).Assembly.GetManifestResourceStream("Syslog.Api.Data.001_control.sql")!;
        using var reader = new StreamReader(resource);
        await using (var command = app.Anchor.CreateCommand())
        {
            command.CommandText = await reader.ReadToEndAsync();
            await command.ExecuteNonQueryAsync();
        }
        await app.BootstrapAndLoginAsync();
        var get = await app.Client.GetAsync(ListenersPath);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, get.StatusCode);
        Assert.Equal("listener_migration_required", await CodeAsync(get));
        var save = await app.Client.PutAsJsonAsync(ListenersPath, new SaveListenerSettingsRequest("0", ListenerConfiguration.Default()));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, save.StatusCode);
        Assert.Equal("listener_migration_required", await CodeAsync(save));
        await using var check = app.Anchor.CreateCommand();
        check.CommandText = "SELECT count(*) FROM sqlite_master WHERE name='listener_settings';";
        Assert.Equal(0L, await check.ExecuteScalarAsync());
    }

    [Fact]
    public async Task ReceptionAppliesLatestSavedPortsAfterOffAndOnInTheSameHost()
    {
        var runtime = new RecordingReceptionRuntime();
        await using var app = await Harness.StartAsync(reception: runtime);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client.PutAsJsonAsync(ReceptionPath, new { enabled = true })).StatusCode);
        await app.BootstrapAndLoginAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await app.Client.PutAsJsonAsync(ReceptionPath, new { })).StatusCode);
        var configuration = ListenerConfiguration.Default();
        Assert.All(configuration.Listeners, item => Assert.Equal("0.0.0.0", item.Address));
        var first = configuration with
        {
            Listeners = configuration.Listeners.Select(item => item.Protocol == "udp"
                ? item with { Enabled = true, Port = 15514 } : item).ToArray()
        };
        var saved = await View(await app.Client.PutAsJsonAsync(ListenersPath, new SaveListenerSettingsRequest("0", first)));
        Assert.Empty(runtime.Calls);
        var running = await View(await app.Client.PutAsJsonAsync(ReceptionPath, new { enabled = true }));
        Assert.True(running.Running.Enabled);
        Assert.True(running.Saved.Configuration.Enabled);
        Assert.False(running.RestartRequired);
        Assert.Equal(15514, Assert.Single(runtime.Calls).Listeners.Single(item => item.Transport == "udp").Port);
        var second = running.Saved.Configuration with
        {
            Listeners = running.Saved.Configuration.Listeners.Select(item => item.Protocol switch
            {
                "udp" => item with { Port = 15515 },
                "tcp" => item with { Enabled = true, Framing = "auto" },
                _ => item
            }).ToArray()
        };
        saved = await View(await app.Client.PutAsJsonAsync(ListenersPath, new SaveListenerSettingsRequest(running.Saved.Version, second)));
        Assert.True(saved.RestartRequired);
        Assert.Single(runtime.Calls);

        var stopped = await View(await app.Client.PutAsJsonAsync(ReceptionPath, new { enabled = false }));
        Assert.False(stopped.Running.Enabled);
        Assert.False(stopped.Saved.Configuration.Enabled);
        Assert.Equal(15515, stopped.Saved.Configuration.Listeners.Single(item => item.Protocol == "udp").Port);
        Assert.Equal(15514, stopped.Running.Listeners.Single(item => item.Protocol == "udp").Port);
        var reopened = await View(await app.Client.PutAsJsonAsync(ReceptionPath, new { enabled = true }));
        Assert.False(reopened.RestartRequired);
        Assert.Equal(15515, reopened.Running.Listeners.Single(item => item.Protocol == "udp").Port);
        Assert.Equal(new[] { true, false, true }, runtime.Calls.Select(call => call.Enabled));
        Assert.Equal(15515, runtime.Calls[^1].Listeners.Single(item => item.Transport == "udp").Port);
        Assert.Equal("auto", runtime.Calls[^1].Listeners.Single(item => item.Transport == "tcp").Framing);
        await using var audit = app.Anchor.CreateCommand();
        audit.CommandText = "SELECT count(*) FROM audit_entries WHERE action='listener.reception.request';";
        Assert.Equal(3L, await audit.ExecuteScalarAsync());
    }

    [Fact]
    public async Task ReceptionStaysOffWhenItsAuditCannotCommit()
    {
        var runtime = new RecordingReceptionRuntime();
        await using var app = await Harness.StartAsync(reception: runtime);
        await app.BootstrapAndLoginAsync();
        var defaults = ListenerConfiguration.Default();
        var draft = defaults with { Listeners = defaults.Listeners.Select(item => item.Protocol == "udp"
            ? item with { Enabled = true } : item).ToArray() };
        var saved = await View(await app.Client.PutAsJsonAsync(ListenersPath, new SaveListenerSettingsRequest("0", draft)));
        await using var trigger = app.Anchor.CreateCommand();
        trigger.CommandText = """
            CREATE TRIGGER reject_reception_audit BEFORE INSERT ON audit_entries
            WHEN NEW.action='listener.reception.request' BEGIN SELECT RAISE(ABORT, 'synthetic failure'); END;
            """;
        await trigger.ExecuteNonQueryAsync();
        var response = await app.Client.PutAsJsonAsync(ReceptionPath, new { enabled = true });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(runtime.Calls);
        var after = await View(await app.Client.GetAsync(ListenersPath));
        Assert.False(after.Running.Enabled);
        Assert.False(after.Saved.Configuration.Enabled);
        Assert.Equal(saved.Saved.Version, after.Saved.Version);
    }

    [Fact]
    public async Task ProtocolSwitchesPreserveIntentWhilePausedAndApplyOnlyTheTargetWhenOn()
    {
        var runtime = new RecordingReceptionRuntime();
        await using var app = await Harness.StartAsync(reception: runtime);
        await app.BootstrapAndLoginAsync();
        var before = await View(await app.Client.GetAsync(ListenersPath));
        var configuration = before.Saved.Configuration with
        {
            Listeners = before.Saved.Configuration.Listeners.Select(item => item.Protocol == "tcp"
                ? item with { Port = 15144 } : item).ToArray()
        };
        var saved = await View(await app.Client.PutAsJsonAsync(ListenersPath, new SaveListenerSettingsRequest(before.Saved.Version, configuration)));
        var queued = await View(await SetProtocolAsync("tcp", true, saved.Saved.Version));
        Assert.True(queued.Saved.Configuration.Listeners.Single(item => item.Protocol == "tcp").Enabled);
        Assert.False(queued.Running.Enabled);
        Assert.Empty(runtime.Calls);
        Assert.Empty(runtime.ProtocolCalls);
        Assert.Equal(HttpStatusCode.Conflict, (await SetProtocolAsync("tcp", false, saved.Saved.Version)).StatusCode);

        var active = await View(await app.Client.PutAsJsonAsync(ReceptionPath, new { enabled = true, version = queued.Saved.Version }));
        Assert.True(active.Running.Enabled);
        Assert.Equal(15144, Assert.Single(runtime.Calls).Listeners.Single(item => item.Enabled).Port);
        var relp = await View(await SetProtocolAsync("relp", true, active.Saved.Version));
        Assert.True(relp.Running.Listeners.Single(item => item.Protocol == "tcp").Enabled);
        Assert.Equal("relp", Assert.Single(runtime.ProtocolCalls).Transport);
        Assert.True(runtime.ProtocolCalls[0].Enabled);
        var relpOff = await View(await SetProtocolAsync("relp", false, relp.Saved.Version));
        var tcpOff = await View(await SetProtocolAsync("tcp", false, relpOff.Saved.Version));
        Assert.True(tcpOff.Running.Enabled);
        Assert.True(tcpOff.Saved.Configuration.Enabled);
        Assert.DoesNotContain(tcpOff.Running.Listeners, item => item.Enabled);
        Assert.Equal(["relp", "relp", "tcp"], runtime.ProtocolCalls.Select(call => call.Transport));
        Assert.Single(runtime.Calls);

        var allOff = await View(await app.Client.PutAsJsonAsync(ReceptionPath, new { enabled = false, version = tcpOff.Saved.Version }));
        var emptyOn = await View(await app.Client.PutAsJsonAsync(ReceptionPath, new { enabled = true, version = allOff.Saved.Version }));
        Assert.True(emptyOn.Running.Enabled);
        Assert.DoesNotContain(emptyOn.Running.Listeners, item => item.Enabled);
        Assert.True(long.Parse(emptyOn.Revision) > long.Parse(before.Revision));

        await using var audit = app.Anchor.CreateCommand();
        audit.CommandText = "SELECT count(*) FROM audit_entries WHERE action='listener.protocol.request';";
        Assert.Equal(4L, await audit.ExecuteScalarAsync());

        Task<HttpResponseMessage> SetProtocolAsync(string protocol, bool enabled, string version) =>
            app.Client.PutAsJsonAsync($"{ReceptionPath}/{protocol}", new { enabled, version });
    }

    [Fact]
    public async Task TlsWithoutACertificateCanBeQueuedAndDoesNotBlockGlobalReception()
    {
        var runtime = new RecordingReceptionRuntime();
        await using var app = await Harness.StartAsync(reception: runtime);
        await app.BootstrapAndLoginAsync();
        var before = await View(await app.Client.GetAsync(ListenersPath));
        var tls = await View(await app.Client.PutAsJsonAsync(ReceptionPath + "/tls", new { enabled = true, version = before.Saved.Version }));
        var udp = await View(await app.Client.PutAsJsonAsync(ReceptionPath + "/udp", new { enabled = true, version = tls.Saved.Version }));
        Assert.Empty(runtime.ProtocolCalls);
        Assert.Empty(runtime.Calls);
        var active = await View(await app.Client.PutAsJsonAsync(ReceptionPath, new { enabled = true, version = udp.Saved.Version }));
        Assert.True(active.Running.Enabled);
        var enabled = Assert.Single(runtime.Calls).Listeners.Where(item => item.Enabled).ToArray();
        Assert.Equal(["udp", "tls"], enabled.Select(item => item.Transport));
        Assert.Empty(enabled.Single(item => item.Transport == "tls").CertificateId);
    }

    [Fact]
    public async Task StartingAProtocolChecksOtherProtocolsCurrentPortsWhileDraftChangesArePending()
    {
        var runtime = new RecordingReceptionRuntime();
        await using var app = await Harness.StartAsync(reception: runtime);
        await app.BootstrapAndLoginAsync();
        var before = await View(await app.Client.GetAsync(ListenersPath));
        var tcp = await View(await app.Client.PutAsJsonAsync(ReceptionPath + "/tcp", new { enabled = true, version = before.Saved.Version }));
        var running = await View(await app.Client.PutAsJsonAsync(ReceptionPath, new { enabled = true, version = tcp.Saved.Version }));
        var desired = running.Saved.Configuration with
        {
            Listeners = running.Saved.Configuration.Listeners.Select(item => item.Protocol switch
            {
                "tcp" => item with { Port = 15144 },
                "relp" => item with { Port = 514 },
                _ => item,
            }).ToArray()
        };
        var draft = await View(await app.Client.PutAsJsonAsync(ListenersPath, new SaveListenerSettingsRequest(running.Saved.Version, desired)));
        var response = await app.Client.PutAsJsonAsync(ReceptionPath + "/relp", new { enabled = true, version = draft.Saved.Version });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("listener_port_conflict", await CodeAsync(response));
        Assert.Empty(runtime.ProtocolCalls);
        Assert.Equal(draft.Saved.Version, (await View(await app.Client.GetAsync(ListenersPath))).Saved.Version);
    }

    private static async Task<ListenerSettingsView> View(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiResult<ListenerSettingsView>>())!.Data!;
    }
}

internal sealed class RecordingReceptionRuntime : IReceptionRuntime
{
    public List<(bool Enabled, IReadOnlyList<ListenerOptions> Listeners)> Calls { get; } = [];
    public List<ListenerOptions> ProtocolCalls { get; } = [];

    public Task ApplyAsync(bool enabled, IReadOnlyList<ListenerOptions> listeners, CancellationToken ct)
    {
        Calls.Add((enabled, listeners.ToArray()));
        return Task.CompletedTask;
    }

    public Task ApplyProtocolAsync(ListenerOptions listener, CancellationToken ct)
    {
        ProtocolCalls.Add(listener);
        return Task.CompletedTask;
    }
}
