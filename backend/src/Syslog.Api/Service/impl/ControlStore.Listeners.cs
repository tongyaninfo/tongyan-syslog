using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Syslog.Api.Common;
using Syslog.Api.Models;
using Syslog.Shared.Models;

namespace Syslog.Api.Service.impl;

public sealed partial class ControlStore
{
    private static readonly JsonSerializerOptions ListenerJsonOptions = new(JsonSerializerDefaults.Web)
        { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public async Task MigrateListenersAsync(CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(false, cancellationToken);
            using var transaction = connection.BeginTransaction();
            // 先完整验证旧结构，异常库或未知版本一律不自动修补。
            await ValidateSetupSchemaAsync(connection, transaction, cancellationToken, fullCheck: true);
            var version = await ReadSchemaVersionAsync(connection, transaction, cancellationToken);
            if (version == 1)
            {
                if (Convert.ToInt64(await ScalarAsync(connection, transaction,
                    "SELECT count(*) FROM sqlite_master WHERE name='listener_settings';", cancellationToken), CultureInfo.InvariantCulture) != 0)
                    throw new InvalidOperationException("监听配置迁移状态不一致，请先显式处理。");
                await ApplyListenerMigrationAsync(connection, transaction, cancellationToken);
            }
            await ValidateSetupSchemaAsync(connection, transaction, cancellationToken, fullCheck: true);
            transaction.Commit();
        }
        finally { _writeGate.Release(); }
    }

    public async Task<ApiResult<SavedListenerSettings>> ReadListenerSettingsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(false, cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: true);
        await ValidateSetupSchemaAsync(connection, transaction, cancellationToken);
        if (await ReadSchemaVersionAsync(connection, transaction, cancellationToken) < 2) return ListenerMigrationRequired();
        return ApiResult<SavedListenerSettings>.Ok(await ReadListenerSettingsRecordAsync(connection, transaction, cancellationToken));
    }

    public Task<ApiResult<SavedListenerSettings>> SaveListenerSettingsAsync(SaveListenerSettingsRequest input,
        long actorId, string token, CancellationToken cancellationToken, bool applyReception = false, string? protocol = null) =>
        SaveListenerSettingsAsync(input, OperationActor.Session(actorId, token), cancellationToken, applyReception, protocol);

public async Task<ApiResult<SavedListenerSettings>> SaveListenerSettingsAsync(SaveListenerSettingsRequest input,
        OperationActor actor, CancellationToken cancellationToken, bool applyReception = false, string? protocol = null)
    {
        var actorId = actor.UserId;
        if (input is null || !long.TryParse(input.Version, NumberStyles.None, CultureInfo.InvariantCulture, out var expected)
            || expected < 0 || Id(expected) != input.Version)
            return ApiResult<SavedListenerSettings>.Fail("invalid_request", "监听配置版本无效。");
        if (!ListenerSettingsValidation.TryNormalize(input.Configuration, out var normalized, out var code))
            return ApiResult<SavedListenerSettings>.Fail(code, "监听配置无效或存在端口冲突。");
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(false, cancellationToken);
            using var transaction = connection.BeginTransaction();
            if (!await IsCurrentActorAsync(connection, transaction, actor, cancellationToken, "syslog_listeners_configure", "syslog_reception_set_enabled", "syslog_protocol_set_enabled"))
                return ApiResult<SavedListenerSettings>.Fail("authentication_required", "管理员会话已失效。");
            await ValidateSetupSchemaAsync(connection, transaction, cancellationToken);
            if (await ReadSchemaVersionAsync(connection, transaction, cancellationToken) < 2) return ListenerMigrationRequired();
            var previous = await ReadListenerSettingsRecordAsync(connection, transaction, cancellationToken);
            if (previous.Version != input.Version)
                return ApiResult<SavedListenerSettings>.Fail("version_conflict", "监听配置已被修改，请刷新后重试。");
            if (actor.IsMcp && !McpListenerChangesAllowed(actor.Permission, previous.Configuration, normalized, applyReception, protocol))
                return ApiResult<SavedListenerSettings>.Fail("mcp_permission_denied", "监听变更包含未授权字段。");
            var serialized = JsonSerializer.Serialize(normalized, ListenerJsonOptions);
            var unchanged = expected > 0 && serialized == JsonSerializer.Serialize(previous.Configuration, ListenerJsonOptions);
            if (unchanged && !applyReception && protocol is null)
                return ApiResult<SavedListenerSettings>.Ok(previous);
            if (!unchanged && expected == long.MaxValue)
                return ApiResult<SavedListenerSettings>.Fail("version_exhausted", "配置版本已耗尽，请联系管理员。");
            var version = unchanged ? expected : checked(expected + 1);
            if (!unchanged)
                await ExecuteAsync(connection, transaction,
                    "INSERT INTO listener_settings(singleton,version,configuration_json,updated_by,updated_at) VALUES(1,$version,$json,$actor,$now) " +
                    "ON CONFLICT(singleton) DO UPDATE SET version=excluded.version,configuration_json=excluded.configuration_json,updated_by=excluded.updated_by,updated_at=excluded.updated_at;",
                    cancellationToken, ("$version", version), ("$json", serialized), ("$actor", actorId), ("$now", Now()));
            var saved = new SavedListenerSettings(Id(version), normalized);
            // 与配置共用一次事务；审计失败时配置、版本和发号均回滚。
            var auditId = await ReserveIdBlockAsync(connection, transaction, 1, cancellationToken);
            await AuditAsync(connection, transaction, auditId, actorId,
                protocol is not null ? "listener.protocol.request" : applyReception ? "listener.reception.request" : "listener.configure", "listeners",
                new { before = previous, after = saved, applyReception, protocol }, cancellationToken);
            transaction.Commit();
            return ApiResult<SavedListenerSettings>.Ok(saved);
        }
        finally { _writeGate.Release(); }
    }

    private static bool McpListenerChangesAllowed(string? permission, ListenerConfiguration before, ListenerConfiguration after,
        bool applyReception, string? protocol)
    {
        if (permission == "syslog_listeners_configure")
            return !applyReception && protocol is null && before.Enabled == after.Enabled
                && before.Listeners.All(old => after.Listeners.Any(item => item.Protocol == old.Protocol && item.Enabled == old.Enabled));
        if (permission == "syslog_reception_set_enabled")
            return applyReception && protocol is null && before.Listeners.SequenceEqual(after.Listeners);
        if (permission == "syslog_protocol_set_enabled")
            return !applyReception && protocol is not null && before.Enabled == after.Enabled
                && before.Listeners.All(old => after.Listeners.Any(item => item.Protocol == old.Protocol
                    && (old.Protocol == protocol ? item with { Enabled = old.Enabled } == old : item == old)));
        return false;
    }

    private static async Task<SavedListenerSettings> ReadListenerSettingsRecordAsync(SqliteConnection connection,
        SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, "SELECT version,configuration_json FROM listener_settings WHERE singleton=1;");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return new("0", ListenerConfiguration.Default());
        var version = reader.GetInt64(0);
        var json = reader.GetString(1);
        if (version <= 0 || json.Length > 16384) throw new InvalidOperationException("监听配置数据无效。");
        var configuration = JsonSerializer.Deserialize<ListenerConfiguration>(json, ListenerJsonOptions);
        if (!ListenerSettingsValidation.TryNormalize(configuration, out var normalized, out _))
            throw new InvalidOperationException("监听配置数据无效。");
        return new(Id(version), normalized);
    }

    private static ApiResult<SavedListenerSettings> ListenerMigrationRequired() =>
        ApiResult<SavedListenerSettings>.Fail("listener_migration_required", "请先执行监听配置显式迁移。");

    private static async Task<long> ReadSchemaVersionAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct) =>
        Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT coalesce(max(version),0) FROM schema_versions;", ct), CultureInfo.InvariantCulture);

    private static async Task ApplyListenerMigrationAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        await using var resource = typeof(ControlStore).Assembly.GetManifestResourceStream("Syslog.Api.Data.002_listener_settings.sql")
            ?? throw new InvalidOperationException("监听配置迁移资源缺失。");
        using var reader = new StreamReader(resource);
        await ExecuteAsync(connection, transaction, await reader.ReadToEndAsync(ct), ct);
    }
}
