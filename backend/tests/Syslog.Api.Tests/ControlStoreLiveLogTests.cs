using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Syslog.Api.Models;
using Syslog.Api.Service.impl;
using Xunit;

namespace Syslog.Api.Tests;

public sealed partial class ControlStoreTests
{
    [Fact]
    public async Task LiveLogMigrationAddsExactlyOneTableAndPreservesExistingControlFeatures()
    {
        await using var db = await TestDatabase.OpenAsync(initialize: false);
        await ApplyLiveLogLegacySchemaAsync(db, 1);
        var admin = await db.BootstrapAsync();
        var token = (await db.Store.LoginAsync("admin", Password, Ct)).Data!.Token;
        Assert.Equal("live_logs_migration_required", (await db.Store.ReadLiveLogSettingsAsync(Ct)).Code);
        Assert.Equal("live_logs_migration_required", (await db.Store.SaveLiveLogSettingsAsync(new("1", 5000, 536870912), admin.Id, token, Ct)).Code);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Store.MigrateLiveLogsAsync(Ct));
        await db.Store.MigrateListenersAsync(Ct);
        await db.Store.MigrateSourceFiltersAsync(Ct);
        await db.Store.MigrateNotificationsAsync(Ct);
        await db.Store.MigrateMcpAsync(Ct);
        var source = (await db.Store.CreateSourceAsync(Source with { IgnoredSeverities = [3, 6] }, admin.Id, Ct)).Data!;
        Assert.True((await db.Store.SaveSmtpSettingsAsync(SyntheticSmtp, admin.Id, token, Ct)).Success);
        Assert.True((await db.Store.SetMcpEnabledAsync(new("1", true), admin.Id, token, Ct)).Success);
        var tablesBefore = Convert.ToInt64(await db.ScalarAsync("SELECT count(*) FROM sqlite_master WHERE type='table';"), CultureInfo.InvariantCulture);
        await db.Store.InitializeAsync(Ct);
        Assert.False(await db.Store.IsSetupRequiredAsync(Ct));
        Assert.Equal(5L, await db.ScalarAsync("SELECT max(version) FROM schema_versions;"));
        Assert.Equal("live_logs_migration_required", (await db.Store.ReadLiveLogSettingsAsync(Ct)).Code);
        await db.Store.MigrateLiveLogsAsync(Ct);
        await db.Store.MigrateLiveLogsAsync(Ct);
        Assert.Equal(6L, await db.ScalarAsync("SELECT max(version) FROM schema_versions;"));
        Assert.Equal(6L, await db.ScalarAsync("SELECT count(*) FROM schema_versions;"));
        Assert.Equal(tablesBefore + 1, await db.ScalarAsync("SELECT count(*) FROM sqlite_master WHERE type='table';"));
        Assert.Equal(new SavedLiveLogSettings("1", 5000, 536870912), (await db.Store.ReadLiveLogSettingsAsync(Ct)).Data);
        var migratedSource = Assert.Single(await db.Store.ListAsync(Ct));
        Assert.Equal(source.Id, migratedSource.Id);
        Assert.Equal(new[] { 3, 6 }, migratedSource.IgnoredSeverities!);
        Assert.Equal(SyntheticSmtp.Host, (await db.Store.GetSmtpSettingsAsync(Ct)).Data!.Host);
        Assert.True((await db.Store.ReadListenerSettingsAsync(Ct)).Success);
        Assert.True((await db.Store.ReadMcpSettingsAsync(Ct)).Data!.Enabled);
        Assert.True((await db.Store.ListMcpKeysAsync(Ct)).Success);
        Assert.Equal(admin, await db.Store.ValidateSessionAsync(token, Ct));
        Assert.False(await db.Store.IsSetupRequiredAsync(Ct));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task LiveLogReadsAndSetupProbeNeverMigrateOlderSchemas(int version)
    {
        await using var db = await TestDatabase.OpenAsync(initialize: false);
        await ApplyLiveLogLegacySchemaAsync(db, version);
        Assert.Equal("live_logs_migration_required", (await db.Store.ReadLiveLogSettingsAsync(Ct)).Code);
        Assert.True(await db.Store.IsSetupRequiredAsync(Ct));
        await db.Store.InitializeAsync(Ct);
        Assert.Equal((long)version, await db.ScalarAsync("SELECT max(version) FROM schema_versions;"));
        Assert.Equal(0L, await db.ScalarAsync("SELECT count(*) FROM sqlite_master WHERE name='live_log_settings';"));
    }

    [Fact]
    public async Task LiveLogFreshInitializationProvidesDefaultsAndRejectsMissingSchemaWithoutRepair()
    {
        await using var fresh = await TestDatabase.OpenAsync();
        Assert.Equal(6L, await fresh.ScalarAsync("SELECT max(version) FROM schema_versions;"));
        Assert.Equal(new SavedLiveLogSettings("1", 5000, 536870912), (await fresh.Store.ReadLiveLogSettingsAsync(Ct)).Data);

        await using var missing = await TestDatabase.OpenAsync(initialize: false);
        await Assert.ThrowsAsync<SqliteException>(() => missing.Store.ReadLiveLogSettingsAsync(Ct));
        Assert.Equal(0L, await missing.ScalarAsync("SELECT count(*) FROM sqlite_master WHERE type='table';"));

        await using var partial = await TestDatabase.OpenAsync(initialize: false);
        await ApplyLiveLogLegacySchemaAsync(partial, 5);
        await partial.ExecuteAsync("INSERT INTO schema_versions(version,applied_at) VALUES(6,0);");
        await Assert.ThrowsAsync<SqliteException>(() => partial.Store.MigrateLiveLogsAsync(Ct));
        await Assert.ThrowsAsync<SqliteException>(() => partial.Store.IsSetupRequiredAsync(Ct));
        Assert.Equal(0L, await partial.ScalarAsync("SELECT count(*) FROM sqlite_master WHERE name='live_log_settings';"));
    }

    [Fact]
    public async Task LiveLogPartialTableIsNotOverwrittenOrMarkedMigrated()
    {
        await using var db = await TestDatabase.OpenAsync(initialize: false);
        await ApplyLiveLogLegacySchemaAsync(db, 5);
        await db.ExecuteAsync("CREATE TABLE live_log_settings(synthetic_marker TEXT); INSERT INTO live_log_settings VALUES('preserve');");
        await Assert.ThrowsAsync<SqliteException>(() => db.Store.MigrateLiveLogsAsync(Ct));
        Assert.Equal(5L, await db.ScalarAsync("SELECT max(version) FROM schema_versions;"));
        Assert.Equal("preserve", await db.ScalarAsync("SELECT synthetic_marker FROM live_log_settings;"));
    }

    [Fact]
    public async Task LiveLogSettingsRequireCurrentSessionAndOptimisticVersionAndPersistAfterReopen()
    {
        await using var db = await TestDatabase.OpenAsync();
        var admin = await db.BootstrapAsync();
        var token = (await db.Store.LoginAsync("admin", Password, Ct)).Data!.Token;
        Assert.Equal("authentication_required", (await db.Store.SaveLiveLogSettingsAsync(new("1", 100, 16777216), admin.Id, "invalid", Ct)).Code);
        var saved = (await db.Store.SaveLiveLogSettingsAsync(new("1", 100, 16777216), admin.Id, token, Ct)).Data!;
        Assert.Equal(new SavedLiveLogSettings("2", 100, 16777216), saved);
        Assert.Equal("version_conflict", (await db.Store.SaveLiveLogSettingsAsync(new("1", 50000, 2147483648), admin.Id, token, Ct)).Code);
        Assert.Equal(saved, (await db.Store.SaveLiveLogSettingsAsync(new("2", 100, 16777216), admin.Id, token, Ct)).Data);
        Assert.Equal(1L, await db.ScalarAsync("SELECT count(*) FROM audit_entries WHERE action='live_logs.configure';"));
        var maximum = new SavedLiveLogSettings("3", 50000, 2147483648);
        Assert.Equal(maximum, (await db.Store.SaveLiveLogSettingsAsync(new("2", 50000, 2147483648), admin.Id, token, Ct)).Data);
        var reopened = new ControlStore(db.Options, db.Clock);
        Assert.Equal(maximum, (await reopened.ReadLiveLogSettingsAsync(Ct)).Data);
        await db.Store.LogoutAsync(token, Ct);
        Assert.Equal("authentication_required", (await reopened.SaveLiveLogSettingsAsync(new("3", 5000, 536870912), admin.Id, token, Ct)).Code);
        Assert.Equal(maximum, (await reopened.ReadLiveLogSettingsAsync(Ct)).Data);
    }

    [Theory]
    [InlineData("0", 5000, 536870912L)]
    [InlineData("01", 5000, 536870912L)]
    [InlineData("1", 99, 536870912L)]
    [InlineData("1", 50001, 536870912L)]
    [InlineData("1", 5000, 16777215L)]
    [InlineData("1", 5000, 2147483649L)]
    public async Task LiveLogSettingsRejectInvalidVersionAndBudgets(string version, int perDeviceLimit, long maxBytes)
    {
        await using var db = await TestDatabase.OpenAsync();
        var admin = await db.BootstrapAsync();
        var token = (await db.Store.LoginAsync("admin", Password, Ct)).Data!.Token;
        Assert.Equal("invalid_request", (await db.Store.SaveLiveLogSettingsAsync(new(version, perDeviceLimit, maxBytes), admin.Id, token, Ct)).Code);
        Assert.Equal(new SavedLiveLogSettings("1", 5000, 536870912), (await db.Store.ReadLiveLogSettingsAsync(Ct)).Data);
        Assert.Equal(0L, await db.ScalarAsync("SELECT count(*) FROM audit_entries WHERE action='live_logs.configure';"));
    }

    [Fact]
    public async Task LiveLogSettingsAuditFailureRollsBackConfigurationAndIdReservation()
    {
        await using var db = await TestDatabase.OpenAsync();
        var admin = await db.BootstrapAsync();
        var token = (await db.Store.LoginAsync("admin", Password, Ct)).Data!.Token;
        var beforeId = await db.ScalarAsync("SELECT next_id FROM id_allocations WHERE name='global';");
        await db.RejectAuditAsync();
        await Assert.ThrowsAsync<SqliteException>(() => db.Store.SaveLiveLogSettingsAsync(new("1", 100, 16777216), admin.Id, token, Ct));
        Assert.Equal(new SavedLiveLogSettings("1", 5000, 536870912), (await db.Store.ReadLiveLogSettingsAsync(Ct)).Data);
        Assert.Equal(beforeId, await db.ScalarAsync("SELECT next_id FROM id_allocations WHERE name='global';"));
        Assert.Equal(0L, await db.ScalarAsync("SELECT count(*) FROM audit_entries WHERE action='live_logs.configure';"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"version\":\"1\",\"perDeviceLimit\":5000}")]
    [InlineData("{\"version\":\"1\",\"maxBytes\":536870912}")]
    [InlineData("{\"perDeviceLimit\":5000,\"maxBytes\":536870912}")]
    public void LiveLogSettingsRequestRequiresExplicitFields(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SaveLiveLogSettingsRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private static async Task ApplyLiveLogLegacySchemaAsync(TestDatabase db, int version)
    {
        var migrations = new[] { "001_control", "002_listener_settings", "003_source_filters", "004_notification_settings", "005_mcp_service" };
        foreach (var name in migrations.Take(version))
        {
            await using var resource = typeof(ControlStore).Assembly.GetManifestResourceStream("Syslog.Api.Data." + name + ".sql")!;
            using var reader = new StreamReader(resource);
            await db.ExecuteAsync(await reader.ReadToEndAsync(Ct));
        }
    }
}
