using Microsoft.Data.Sqlite;
using Syslog.Api.Models;
using Syslog.Shared.Models;

namespace Syslog.Api.Service.impl;

public sealed partial class ControlStore : ILiveLogSettingsStore
{
    public async Task<ApiResult<SavedLiveLogSettings>> ReadLiveLogSettingsAsync(CancellationToken ct)
    {
        await using var connection = await OpenAsync(false, ct);
        using var transaction = connection.BeginTransaction(deferred: true);
        if (!await SupportsLiveLogsAsync(connection, transaction, ct)) return LiveLogMigrationRequired();
        return ApiResult<SavedLiveLogSettings>.Ok(await ReadLiveLogSettingsRecordAsync(connection, transaction, ct));
    }

    public async Task<ApiResult<SavedLiveLogSettings>> SaveLiveLogSettingsAsync(SaveLiveLogSettingsRequest input,
        long actorId, string token, CancellationToken ct)
    {
        if (input is null || !TryPositiveId(input.Version, out var expected) || Id(expected) != input.Version
            || input.PerDeviceLimit is < 100 or > 50000 || input.MaxBytes is < 16777216 or > 2147483648L)
            return ApiResult<SavedLiveLogSettings>.Fail("invalid_request", "每个设备缓存条数需为 100～50000，缓存总容量需为 16 MiB～2 GiB，配置版本须有效。");
        await _writeGate.WaitAsync(ct);
        try
        {
            await using var connection = await OpenAsync(false, ct);
            using var transaction = connection.BeginTransaction();
            // 写事务内复核会话，避免已撤销管理员利用此前的 HTTP 身份快照修改设置。
            if (!await IsCurrentAdminAsync(connection, transaction, actorId, token, ct))
                return ApiResult<SavedLiveLogSettings>.Fail("authentication_required", "管理员会话已失效。");
            if (!await SupportsLiveLogsAsync(connection, transaction, ct)) return LiveLogMigrationRequired();
            var previous = await ReadLiveLogSettingsRecordAsync(connection, transaction, ct);
            if (previous.Version != input.Version)
                return ApiResult<SavedLiveLogSettings>.Fail("version_conflict", "实时日志配置已改变，请刷新后重试。");
            if (previous.PerDeviceLimit == input.PerDeviceLimit && previous.MaxBytes == input.MaxBytes)
                return ApiResult<SavedLiveLogSettings>.Ok(previous);
            if (expected == long.MaxValue)
                return ApiResult<SavedLiveLogSettings>.Fail("version_exhausted", "实时日志配置版本已耗尽。");
            var saved = new SavedLiveLogSettings(Id(expected + 1), input.PerDeviceLimit, input.MaxBytes);
            var auditId = await ReserveIdBlockAsync(connection, transaction, 1, ct);
            await ExecuteAsync(connection, transaction,
                "UPDATE live_log_settings SET version=version+1,per_device_limit=$limit,max_bytes=$bytes,updated_by=$actor,updated_at=$now WHERE singleton=1;",
                ct, ("$limit", input.PerDeviceLimit), ("$bytes", input.MaxBytes), ("$actor", actorId), ("$now", Now()));
            // 配置、版本、发号与管理审计同事务；任何审计失败都会回滚配置。
            await AuditAsync(connection, transaction, auditId, actorId, "live_logs.configure", "1",
                new { before = previous, after = saved }, ct);
            transaction.Commit();
            return ApiResult<SavedLiveLogSettings>.Ok(saved);
        }
        finally { _writeGate.Release(); }
    }

    private static async Task<SavedLiveLogSettings> ReadLiveLogSettingsRecordAsync(SqliteConnection connection,
        SqliteTransaction transaction, CancellationToken ct)
    {
        await using var command = Command(connection, transaction,
            "SELECT version,per_device_limit,max_bytes FROM live_log_settings WHERE singleton=1;");
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("实时日志配置状态不完整。");
        var version = reader.GetInt64(0);
        var limit = reader.GetInt64(1);
        var bytes = reader.GetInt64(2);
        if (version <= 0 || limit is < 100 or > 50000 || bytes is < 16777216 or > 2147483648L)
            throw new InvalidOperationException("实时日志配置状态无效。");
        return new(Id(version), (int)limit, bytes);
    }

    private static async Task<bool> SupportsLiveLogsAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct) =>
        (await ReadSchemaVersionAsync(connection, transaction, ct)) switch
        {
            1 or 2 or 3 or 4 or 5 => false,
            6 => true,
            _ => throw new InvalidOperationException("控制库版本不受支持。")
        };

    private static ApiResult<SavedLiveLogSettings> LiveLogMigrationRequired() =>
        ApiResult<SavedLiveLogSettings>.Fail("live_logs_migration_required", "请先执行实时日志配置控制库显式迁移。");
}
