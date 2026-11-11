using System.Text.Json;
using Microsoft.Data.Sqlite;
using Syslog.Api.Common;
using Syslog.Api.Models;
using Syslog.Api.Service.impl;
using Xunit;

namespace Syslog.Api.Tests;

public sealed partial class ControlStoreTests
{
    [Fact]
    public async Task ListenerMigrationIsExplicitIdempotentAndKeepsLegacyAccounts()
    {
        await using var database = await TestDatabase.OpenAsync(initialize: false);
        await using var resource = typeof(ControlStore).Assembly.GetManifestResourceStream("Syslog.Api.Data.001_control.sql")!;
        using var reader = new StreamReader(resource);
        await database.ExecuteAsync(await reader.ReadToEndAsync());
        var admin = await database.BootstrapAsync();
        var session = (await database.Store.LoginAsync(admin.Username, Password, Ct)).Data!;
        Assert.False(await database.Store.IsSetupRequiredAsync(Ct));
        Assert.Equal("listener_migration_required", (await database.Store.ReadListenerSettingsAsync(Ct)).Code);
        Assert.Equal("listener_migration_required", (await database.Store.SaveListenerSettingsAsync(new("0", ListenerConfiguration.Default()), admin.Id, session.Token, Ct)).Code);
        await database.Store.InitializeAsync(Ct);
        Assert.Equal(1L, await database.ScalarAsync("SELECT max(version) FROM schema_versions;"));
        Assert.Equal(0L, await database.ScalarAsync("SELECT count(*) FROM sqlite_master WHERE name='listener_settings';"));
        await database.Store.MigrateListenersAsync(Ct);
        await database.Store.MigrateListenersAsync(Ct);
        Assert.Equal(2L, await database.ScalarAsync("SELECT max(version) FROM schema_versions;"));
        Assert.Equal(2L, await database.ScalarAsync("SELECT count(*) FROM schema_versions;"));
        Assert.Equal(admin, await database.Store.ValidateSessionAsync(session.Token, Ct));
        Assert.Equal("0", (await database.Store.ReadListenerSettingsAsync(Ct)).Data!.Version);
        Assert.False(await database.Store.IsSetupRequiredAsync(Ct));
    }

    [Fact]
    public async Task ListenerMigrationNeverCreatesMissingSchemaOrAcceptsUnknownVersion()
    {
        await using var missing = await TestDatabase.OpenAsync(initialize: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => missing.Store.MigrateListenersAsync(Ct));
        Assert.Equal(0L, await missing.ScalarAsync("SELECT count(*) FROM sqlite_master WHERE type='table';"));
        await using var unknown = await TestDatabase.OpenAsync();
        await unknown.ExecuteAsync("UPDATE schema_versions SET version=99 WHERE version=2;");
        await Assert.ThrowsAsync<InvalidOperationException>(() => unknown.Store.MigrateListenersAsync(Ct));
        Assert.Equal(99L, await unknown.ScalarAsync("SELECT max(version) FROM schema_versions;"));
    }

    [Fact]
    public async Task ListenerSettingsPersistWithAuditAndRejectStaleConcurrentEdits()
    {
        await using var database = await TestDatabase.OpenAsync();
        var admin = await database.BootstrapAsync();
        var session = (await database.Store.LoginAsync(admin.Username, Password, Ct)).Data!;
        var first = (await database.Store.ReadListenerSettingsAsync(Ct)).Data!;
        Assert.Equal("0", first.Version);
        Assert.False(first.Configuration.Enabled);
        Assert.Equal(4, first.Configuration.Listeners.Count);
        var configuration = first.Configuration with
        {
            Enabled = true,
            Listeners = first.Configuration.Listeners.Select(item => item.Protocol == "udp"
                ? item with { Enabled = true, Address = "::ffff:127.0.0.1", Port = 15514 } : item).ToArray()
        };
        var saved = await database.Store.SaveListenerSettingsAsync(new("0", configuration), admin.Id, session.Token, Ct);
        Assert.True(saved.Success);
        Assert.Equal("1", saved.Data!.Version);
        Assert.Equal("127.0.0.1", saved.Data.Configuration.Listeners[0].Address);
        var reopened = new ControlStore(database.Options, database.Clock);
        var restored = (await reopened.ReadListenerSettingsAsync(Ct)).Data!;
        Assert.Equal(JsonSerializer.Serialize(saved.Data), JsonSerializer.Serialize(restored));
        Assert.Equal("version_conflict", (await reopened.SaveListenerSettingsAsync(new("0", configuration), admin.Id, session.Token, Ct)).Code);
        Assert.Equal("1", (await reopened.SaveListenerSettingsAsync(new("1", configuration), admin.Id, session.Token, Ct)).Data!.Version);
        Assert.Equal(1L, await database.ScalarAsync("SELECT count(*) FROM audit_entries WHERE action='listener.configure';"));
        var audit = Assert.IsType<string>(await database.ScalarAsync("SELECT details_json FROM audit_entries WHERE action='listener.configure';"));
        Assert.Contains("applyReception", audit);
        Assert.DoesNotContain(session.Token, audit);
        Assert.DoesNotContain(Password, audit);
    }

    [Fact]
    public async Task ListenerSaveRollsBackConfigurationAndVersionWhenAuditFails()
    {
        await using var database = await TestDatabase.OpenAsync();
        var admin = await database.BootstrapAsync();
        var session = (await database.Store.LoginAsync(admin.Username, Password, Ct)).Data!;
        var allocation = await database.ScalarAsync("SELECT next_id FROM id_allocations WHERE name='global';");
        await database.RejectAuditAsync();
        await Assert.ThrowsAsync<SqliteException>(() => database.Store.SaveListenerSettingsAsync(new("0", ListenerConfiguration.Default()), admin.Id, session.Token, Ct));
        Assert.Equal(0L, await database.ScalarAsync("SELECT count(*) FROM listener_settings;"));
        Assert.Equal(allocation, await database.ScalarAsync("SELECT next_id FROM id_allocations WHERE name='global';"));
        Assert.Equal("0", (await database.Store.ReadListenerSettingsAsync(Ct)).Data!.Version);
    }

    [Fact]
    public async Task ListenerSaveRechecksSessionAndNeverPersistsInvalidSettings()
    {
        await using var database = await TestDatabase.OpenAsync();
        var admin = await database.BootstrapAsync();
        var session = (await database.Store.LoginAsync(admin.Username, Password, Ct)).Data!;
        var request = new SaveListenerSettingsRequest("0", ListenerConfiguration.Default());
        Assert.Equal("invalid_request", (await database.Store.SaveListenerSettingsAsync(request with { Version = "00" }, admin.Id, session.Token, Ct)).Code);
        Assert.Equal("invalid_listener_configuration", (await database.Store.SaveListenerSettingsAsync(request with { Configuration = new(true, []) }, admin.Id, session.Token, Ct)).Code);
        await database.Store.LogoutAsync(session.Token, Ct);
        Assert.Equal("authentication_required", (await database.Store.SaveListenerSettingsAsync(request, admin.Id, session.Token, Ct)).Code);
        Assert.Equal(0L, await database.ScalarAsync("SELECT count(*) FROM listener_settings;"));
        Assert.Equal(0L, await database.ScalarAsync("SELECT count(*) FROM audit_entries WHERE action='listener.configure';"));
    }

    [Fact]
    public void ListenerValidationSeparatesUdpAndTcpAndRejectsWildcardStreamCollisions()
    {
        var defaults = ListenerConfiguration.Default();
        var samePort = defaults with { Enabled = true, Listeners = defaults.Listeners.Select(item => item.Protocol is "udp" or "tcp" ? item with { Enabled = true } : item).ToArray() };
        Assert.True(ListenerSettingsValidation.TryNormalize(samePort, out _, out _));
        var conflict = samePort with { Listeners = samePort.Listeners.Select(item => item.Protocol == "relp" ? item with { Enabled = true, Address = "0.0.0.0", Port = 514 } : item).ToArray() };
        Assert.False(ListenerSettingsValidation.TryNormalize(conflict, out _, out var code));
        Assert.Equal("listener_port_conflict", code);
        var separateFamily = conflict with { Listeners = conflict.Listeners.Select(item => item.Protocol == "relp" ? item with { Address = "::" } : item).ToArray() };
        Assert.True(ListenerSettingsValidation.TryNormalize(separateFamily, out _, out _));
        var missingCertificate = defaults with { Listeners = defaults.Listeners.Select(item => item.Protocol == "tls" ? item with { Enabled = true } : item).ToArray() };
        // 独立开关可保存 TLS 的开启意向，证书是否可加载由运行时单独判定。
        Assert.True(ListenerSettingsValidation.TryNormalize(missingCertificate, out _, out _));
        Assert.False(ListenerSettingsValidation.TryNormalize(defaults with { Listeners = [defaults.Listeners[0], defaults.Listeners[0], defaults.Listeners[2], defaults.Listeners[3]] }, out _, out _));
    }
}
