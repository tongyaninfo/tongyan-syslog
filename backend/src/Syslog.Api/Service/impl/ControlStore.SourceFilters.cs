using Microsoft.Data.Sqlite;

namespace Syslog.Api.Service.impl;

public sealed partial class ControlStore
{
    public async Task MigrateSourceFiltersAsync(CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(false, cancellationToken);
            using var transaction = connection.BeginTransaction();
            await ValidateSetupSchemaAsync(connection, transaction, cancellationToken, fullCheck: true);
            var version = await ReadSchemaVersionAsync(connection, transaction, cancellationToken);
            if (version == 1) throw new InvalidOperationException("请先执行监听配置迁移。");
            if (version == 2)
            {
                await ApplySourceFilterMigrationAsync(connection, transaction, cancellationToken);
                await ValidateSetupSchemaAsync(connection, transaction, cancellationToken, fullCheck: true);
            }
            transaction.Commit();
        }
        finally { _writeGate.Release(); }
    }

    private static async Task ApplySourceFilterMigrationAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        await using var resource = typeof(ControlStore).Assembly.GetManifestResourceStream("Syslog.Api.Data.003_source_filters.sql")
            ?? throw new InvalidOperationException("来源筛选迁移资源缺失。");
        using var reader = new StreamReader(resource);
        await ExecuteAsync(connection, transaction, await reader.ReadToEndAsync(ct), ct);
    }

    private static async Task<bool> SupportsSourceFiltersAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct) =>
        (await ReadSchemaVersionAsync(connection, transaction, ct)) switch
        {
            1 or 2 => false,
            3 or 4 or 5 or 6 => true,
            _ => throw new InvalidOperationException("控制库版本不受支持。")
        };

    private static string SelectSourceColumns(bool supportsFilters) =>
        "SELECT s.id,s.name,s.peer_address,s.time_zone_id,r.retention_days,s.enabled,"
        + (supportsFilters ? "s.ignored_severity_mask" : "0")
        + " FROM sources s JOIN retention_policies r ON r.source_id=s.id";

    private static int[] DecodeIgnoredSeverities(int mask) => mask == 0
        ? Array.Empty<int>()
        : Enumerable.Range(0, 8).Where(severity => (mask & (1 << severity)) != 0).ToArray();
}
