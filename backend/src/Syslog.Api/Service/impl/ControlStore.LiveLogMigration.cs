using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Syslog.Api.Service.impl;

public sealed partial class ControlStore
{
    public async Task MigrateLiveLogsAsync(CancellationToken ct)
    {
        await _writeGate.WaitAsync(ct);
        try
        {
            await using var connection = await OpenAsync(false, ct);
            using var transaction = connection.BeginTransaction();
            // 正常启动不会调用迁移；异常旧库或部分迁移一律失败，不自动补表。
            await ValidateSetupSchemaAsync(connection, transaction, ct, fullCheck: true);
            var version = await ReadSchemaVersionAsync(connection, transaction, ct);
            if (version < 5) throw new InvalidOperationException("请先完成 MCP 控制库显式迁移。");
            if (version == 5) await ApplyLiveLogMigrationAsync(connection, transaction, ct);
            await ValidateSetupSchemaAsync(connection, transaction, ct, fullCheck: true);
            transaction.Commit();
        }
        finally { _writeGate.Release(); }
    }

    private static async Task ApplyLiveLogMigrationAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        await using var resource = typeof(ControlStore).Assembly.GetManifestResourceStream("Syslog.Api.Data.006_live_log_settings.sql")
            ?? throw new InvalidOperationException("实时日志配置迁移资源缺失。");
        using var reader = new StreamReader(resource);
        await ExecuteAsync(connection, transaction, await reader.ReadToEndAsync(ct), ct);
    }

    private static async Task ValidateLiveLogSchemaAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        await using (var command = Command(connection, transaction,
            "SELECT singleton,version,per_device_limit,max_bytes,updated_by,updated_at FROM live_log_settings LIMIT 0;"))
        await using (var reader = await command.ExecuteReaderAsync(ct)) { }
        if (Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT count(*) FROM live_log_settings;", ct), CultureInfo.InvariantCulture) != 1)
            throw new InvalidOperationException("实时日志配置状态不完整。");
        await ReadLiveLogSettingsRecordAsync(connection, transaction, ct);
    }
}
