using System.Text.Json;
using Microsoft.Data.Sqlite;
using Syslog.Api.Service.impl;
using Xunit;

namespace Syslog.Api.Tests;

public sealed partial class ControlStoreTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SourceFilterMigrationPreservesLegacySourcesAndRequiresAnExplicitUpgrade(int version)
    {
        await using var database = await TestDatabase.OpenAsync(initialize: false);
        await using var resource = typeof(ControlStore).Assembly.GetManifestResourceStream("Syslog.Api.Data.001_control.sql")!;
        using var reader = new StreamReader(resource);
        await database.ExecuteAsync(await reader.ReadToEndAsync(Ct));
        if (version == 2) await database.Store.MigrateListenersAsync(Ct);

        var admin = await database.BootstrapAsync();
        var created = (await database.Store.CreateSourceAsync(Source, admin.Id, Ct)).Data!;
        Assert.Empty(created.IgnoredSeverities!);
        var filtered = Source with { IgnoredSeverities = [6, 7] };
        Assert.Equal("source_filter_migration_required", (await database.Store.UpdateSourceAsync(created.Id, filtered, admin.Id, Ct)).Code);
        Assert.Equal("source_filter_migration_required", (await database.Store.CreateSourceAsync(filtered with { PeerAddress = "192.0.2.11" }, admin.Id, Ct)).Code);
        Assert.True((await database.Store.UpdateSourceAsync(created.Id, Source with { RetentionDays = 30 }, admin.Id, Ct)).Success);
        await database.Store.InitializeAsync(Ct);
        Assert.Equal((long)version, await database.ScalarAsync("SELECT max(version) FROM schema_versions;"));
        Assert.Empty(Assert.Single(await database.Store.ListAsync(Ct)).IgnoredSeverities!);

        if (version == 1)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => database.Store.MigrateSourceFiltersAsync(Ct));
            await database.Store.MigrateListenersAsync(Ct);
        }
        await database.Store.MigrateSourceFiltersAsync(Ct);
        await database.Store.MigrateSourceFiltersAsync(Ct);
        Assert.Equal(3L, await database.ScalarAsync("SELECT max(version) FROM schema_versions;"));
        Assert.Equal(3L, await database.ScalarAsync("SELECT count(*) FROM schema_versions;"));
        var migrated = Assert.Single(await database.Store.ListAsync(Ct));
        Assert.Equal(created.Id, migrated.Id);
        Assert.Equal(30, migrated.RetentionDays);
        Assert.Empty(migrated.IgnoredSeverities!);
        Assert.True((await database.Store.UpdateSourceAsync(created.Id, filtered, admin.Id, Ct)).Success);
        Assert.Equal(new[] { 6, 7 }, Assert.Single(await database.Store.ListAsync(Ct)).IgnoredSeverities);
    }

    [Fact]
    public async Task SourceFiltersValidatePersistAndClearWithAudit()
    {
        await using var database = await TestDatabase.OpenAsync();
        var admin = await database.BootstrapAsync();
        var invalid = Source with { IgnoredSeverities = [6, 8] };
        Assert.Equal("INVALID_INPUT", (await database.Store.CreateSourceAsync(invalid, admin.Id, Ct)).Code);

        var configured = Source with { IgnoredSeverities = [7, 0, 4, 7] };
        var source = (await database.Store.CreateSourceAsync(configured, admin.Id, Ct)).Data!;
        Assert.Equal(new[] { 0, 4, 7 }, source.IgnoredSeverities);
        var oldClient = (await database.Store.UpdateSourceAsync(source.Id, Source with { Name = "renamed" }, admin.Id, Ct)).Data!;
        Assert.Equal(source.IgnoredSeverities, oldClient.IgnoredSeverities);
        Assert.Equal(source.IgnoredSeverities, Assert.Single(await database.Store.ListAsync(Ct)).IgnoredSeverities);
        var cleared = (await database.Store.UpdateSourceAsync(source.Id, Source with { IgnoredSeverities = [] }, admin.Id, Ct)).Data!;
        Assert.Empty(cleared.IgnoredSeverities!);

        var details = Assert.IsType<string>(await database.ScalarAsync("SELECT details_json FROM audit_entries WHERE action='source.update' ORDER BY id DESC LIMIT 1;"));
        using var audit = JsonDocument.Parse(details);
        Assert.Equal(3, audit.RootElement.GetProperty("before").GetProperty("IgnoredSeverities").GetArrayLength());
        Assert.Equal(0, audit.RootElement.GetProperty("after").GetProperty("IgnoredSeverities").GetArrayLength());

        await database.RejectAuditAsync();
        await Assert.ThrowsAsync<SqliteException>(() => database.Store.UpdateSourceAsync(source.Id, configured, admin.Id, Ct));
        Assert.Empty(Assert.Single(await database.Store.ListAsync(Ct)).IgnoredSeverities!);
    }
}
