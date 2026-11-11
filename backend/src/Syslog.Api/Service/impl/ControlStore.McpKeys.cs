using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Syslog.Api.Common;
using Syslog.Api.Models;
using Syslog.Shared.Models;

namespace Syslog.Api.Service.impl;

public sealed partial class ControlStore
{
    private const string McpKeyColumns = "k.id,k.name,k.display_prefix,k.access_mode,k.permissions_json,k.created_by,k.created_at,k.expires_at,k.revoked_at,k.last_used_at,u.enabled,u.role";

    public async Task<ApiResult<McpSettingsView>> ReadMcpSettingsAsync(CancellationToken ct)
    {
        await using var connection = await OpenAsync(false, ct);
        using var transaction = connection.BeginTransaction(deferred: true);
        if (!await SupportsMcpAsync(connection, transaction, ct)) return McpMigrationRequired<McpSettingsView>();
        return ApiResult<McpSettingsView>.Ok(await ReadMcpSettingsRecordAsync(connection, transaction, ct));
    }

    public Task<ApiResult<McpSettingsView>> SetMcpEnabledAsync(SetMcpEnabledRequest input, long actorId, string token, CancellationToken ct) =>
        ChangeMcpSettingsAsync(input.Version, input.Enabled, null, actorId, token, ct);

    public Task<ApiResult<McpSettingsView>> SetMcpRetentionAsync(SetMcpRetentionRequest input, long actorId, string token, CancellationToken ct) =>
        ChangeMcpSettingsAsync(input.Version, null, input.AuditRetentionDays, actorId, token, ct);

    private async Task<ApiResult<McpSettingsView>> ChangeMcpSettingsAsync(string version, bool? enabled, int? retentionDays,
        long actorId, string token, CancellationToken ct)
    {
        if (!TryPositiveId(version, out var expected) || Id(expected) != version || retentionDays is < 1 or > 3650)
            return ApiResult<McpSettingsView>.Fail("invalid_request", "MCP 配置版本或保留天数无效。");
        await _writeGate.WaitAsync(ct);
        try
        {
            await using var connection = await OpenAsync(false, ct);
            using var transaction = connection.BeginTransaction();
            if (!await IsCurrentAdminAsync(connection, transaction, actorId, token, ct))
                return ApiResult<McpSettingsView>.Fail("authentication_required", "管理员会话已失效。");
            if (!await SupportsMcpAsync(connection, transaction, ct)) return McpMigrationRequired<McpSettingsView>();
            var old = await ReadMcpSettingsRecordAsync(connection, transaction, ct);
            if (old.Version != version) return ApiResult<McpSettingsView>.Fail("version_conflict", "MCP 配置已改变，请刷新后重试。");
            var nextEnabled = enabled ?? old.Enabled;
            var nextRetention = retentionDays ?? old.AuditRetentionDays;
            if (old.Enabled == nextEnabled && old.AuditRetentionDays == nextRetention) return ApiResult<McpSettingsView>.Ok(old);
            if (expected == long.MaxValue) return ApiResult<McpSettingsView>.Fail("version_exhausted", "MCP 配置版本已耗尽。");
            var auditId = await ReserveIdBlockAsync(connection, transaction, 1, ct);
            await ExecuteAsync(connection, transaction,
                "UPDATE mcp_settings SET version=version+1,enabled=$enabled,audit_retention_days=$days,updated_by=$actor,updated_at=$now WHERE singleton=1;",
                ct, ("$enabled", nextEnabled ? 1 : 0), ("$days", nextRetention), ("$actor", actorId), ("$now", Now()));
            await AuditAsync(connection, transaction, auditId, actorId,
                enabled.HasValue ? "mcp.service.configure" : "mcp.audit.retention", "1",
                new { enabled = nextEnabled, auditRetentionDays = nextRetention }, ct);
            transaction.Commit();
            return ApiResult<McpSettingsView>.Ok(new(Id(expected + 1), nextEnabled, nextRetention));
        }
        finally { _writeGate.Release(); }
    }

    public async Task<ApiResult<IReadOnlyList<McpKeyView>>> ListMcpKeysAsync(CancellationToken ct)
    {
        await using var connection = await OpenAsync(false, ct);
        using var transaction = connection.BeginTransaction(deferred: true);
        if (!await SupportsMcpAsync(connection, transaction, ct)) return McpMigrationRequired<IReadOnlyList<McpKeyView>>();
        await using var command = Command(connection, transaction,
            "SELECT " + McpKeyColumns + " FROM mcp_api_keys k JOIN users u ON u.id=k.created_by ORDER BY k.created_at DESC,k.id DESC LIMIT 500;");
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<McpKeyView>();
        var now = Now();
        while (await reader.ReadAsync(ct)) result.Add(ReadMcpKey(reader, now));
        return ApiResult<IReadOnlyList<McpKeyView>>.Ok(result);
    }

    public async Task<ApiResult<CreatedMcpKey>> CreateMcpKeyAsync(CreateMcpKeyRequest input, long actorId, string token, CancellationToken ct)
    {
        var name = input.Name?.Trim() ?? "";
        if (name.Length is < 1 or > 128 || name.Any(char.IsControl)
            || input.AccessMode is not ("read" or "read_write") || input.Permissions is not { Length: > 0 and <= 128 }
            || input.Permissions.Any(string.IsNullOrWhiteSpace))
            return ApiResult<CreatedMcpKey>.Fail("invalid_request", "请填写名称、授权模式并选择至少一个权限。");
        var permissions = input.Permissions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        foreach (var permission in permissions)
        {
            var definition = McpToolCatalog.All.FirstOrDefault(x => x.Name == permission);
            if (definition is null || input.AccessMode == "read" && definition.IsWrite)
                return ApiResult<CreatedMcpKey>.Fail("mcp_permission_invalid", "授权包含不存在的功能或只读模式不允许的写入权限。");
        }
        if (input.ExpiresAt.HasValue && input.ExpiresAt.Value <= _clock.GetUtcNow())
            return ApiResult<CreatedMcpKey>.Fail("mcp_expiry_invalid", "有效期必须晚于当前时间，留空表示永久。");
        await _writeGate.WaitAsync(ct);
        try
        {
            await using var connection = await OpenAsync(false, ct);
            using var transaction = connection.BeginTransaction();
            if (!await IsCurrentAdminAsync(connection, transaction, actorId, token, ct))
                return ApiResult<CreatedMcpKey>.Fail("authentication_required", "管理员会话已失效。");
            if (!await SupportsMcpAsync(connection, transaction, ct)) return McpMigrationRequired<CreatedMcpKey>();
            if (Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT count(*) FROM mcp_api_keys;", ct), CultureInfo.InvariantCulture) >= 500)
                return ApiResult<CreatedMcpKey>.Fail("mcp_key_limit", "API Key 数量已达上限，请删除不再使用的已吊销记录。");
            var now = Now();
            if (input.ExpiresAt.HasValue && input.ExpiresAt.Value.ToUnixTimeMilliseconds() <= now)
                return ApiResult<CreatedMcpKey>.Fail("mcp_expiry_invalid", "有效期必须晚于当前时间。");
            var id = await ReserveIdBlockAsync(connection, transaction, 2, ct);
            var secret = "syslog_mcp_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var prefix = secret[..20];
            await ExecuteAsync(connection, transaction,
                "INSERT INTO mcp_api_keys(id,name,display_prefix,secret,secret_hash,access_mode,permissions_json,created_by,created_at,expires_at) " +
                "VALUES($id,$name,$prefix,$secret,$hash,$mode,$permissions,$actor,$now,$expires);", ct,
                ("$id", id), ("$name", name), ("$prefix", prefix), ("$secret", secret), ("$hash", HashToken(secret)),
                ("$mode", input.AccessMode), ("$permissions", JsonSerializer.Serialize(permissions)), ("$actor", actorId),
                ("$now", now), ("$expires", input.ExpiresAt?.ToUnixTimeMilliseconds()));
            // 管理审计不复制凭据；仅 /mcp 专用调用表按已确认要求保留完整交换。
            await AuditAsync(connection, transaction, id + 1, actorId, "mcp.key.create", Id(id),
                new { name, accessMode = input.AccessMode, permissions, expiresAt = input.ExpiresAt }, ct);
            transaction.Commit();
            var view = new McpKeyView(Id(id), name, prefix, input.AccessMode, permissions, Id(actorId),
                DateTimeOffset.FromUnixTimeMilliseconds(now), input.ExpiresAt?.ToUniversalTime(), null, null, "active");
            return ApiResult<CreatedMcpKey>.Ok(new(view, secret));
        }
        finally { _writeGate.Release(); }
    }

    public Task<ApiResult<bool>> RevokeMcpKeyAsync(string keyId, long actorId, string token, CancellationToken ct) =>
        MutateMcpKeyAsync(keyId, actorId, token, false, ct);

    public Task<ApiResult<bool>> DeleteMcpKeyAsync(string keyId, long actorId, string token, CancellationToken ct) =>
        MutateMcpKeyAsync(keyId, actorId, token, true, ct);

    private async Task<ApiResult<bool>> MutateMcpKeyAsync(string keyId, long actorId, string token, bool delete, CancellationToken ct)
    {
        if (!TryPositiveId(keyId, out var id)) return ApiResult<bool>.Fail("invalid_request", "Key 标识无效。");
        await _writeGate.WaitAsync(ct);
        try
        {
            await using var connection = await OpenAsync(false, ct);
            using var transaction = connection.BeginTransaction();
            if (!await IsCurrentAdminAsync(connection, transaction, actorId, token, ct))
                return ApiResult<bool>.Fail("authentication_required", "管理员会话已失效。");
            if (!await SupportsMcpAsync(connection, transaction, ct)) return McpMigrationRequired<bool>();
            var key = await ReadMcpKeyRecordAsync(connection, transaction, id, ct);
            if (key is null) return ApiResult<bool>.Fail("not_found", "API Key 不存在。");
            if (delete && key.RevokedAt is null) return ApiResult<bool>.Fail("mcp_key_revoke_required", "请先吊销 API Key，再删除记录。");
            if (!delete && key.RevokedAt is not null) return ApiResult<bool>.Ok(true);
            var auditId = await ReserveIdBlockAsync(connection, transaction, 1, ct);
            await ExecuteAsync(connection, transaction, delete ? "DELETE FROM mcp_api_keys WHERE id=$id;" :
                "UPDATE mcp_api_keys SET revoked_by=$actor,revoked_at=$now WHERE id=$id AND revoked_at IS NULL;",
                ct, ("$id", id), ("$actor", actorId), ("$now", Now()));
            await AuditAsync(connection, transaction, auditId, actorId, delete ? "mcp.key.delete" : "mcp.key.revoke", Id(id), new { key.Name }, ct);
            transaction.Commit();
            return ApiResult<bool>.Ok(true);
        }
        finally { _writeGate.Release(); }
    }

    public async Task<ApiResult<McpConnectionMaterial>> GetMcpConnectionMaterialAsync(string keyId, long actorId, string token, CancellationToken ct)
    {
        if (!TryPositiveId(keyId, out var id)) return ApiResult<McpConnectionMaterial>.Fail("invalid_request", "Key 标识无效。");
        await _writeGate.WaitAsync(ct);
        try
        {
            await using var connection = await OpenAsync(false, ct);
            using var transaction = connection.BeginTransaction();
            if (!await IsCurrentAdminAsync(connection, transaction, actorId, token, ct))
                return ApiResult<McpConnectionMaterial>.Fail("authentication_required", "管理员会话已失效。");
            if (!await SupportsMcpAsync(connection, transaction, ct)) return McpMigrationRequired<McpConnectionMaterial>();
            var key = await ReadMcpKeyRecordAsync(connection, transaction, id, ct);
            if (key is null) return ApiResult<McpConnectionMaterial>.Fail("not_found", "API Key 不存在。");
            if (key.Status != "active") return ApiResult<McpConnectionMaterial>.Fail("mcp_key_inactive", "该 API Key 已失效，不能生成接入指令。");
            var secret = (string)(await ScalarAsync(connection, transaction, "SELECT secret FROM mcp_api_keys WHERE id=$id;", ct, ("$id", id)))!;
            var auditId = await ReserveIdBlockAsync(connection, transaction, 1, ct);
            await AuditAsync(connection, transaction, auditId, actorId, "mcp.key.copy", Id(id), new { key.Name }, ct);
            transaction.Commit();
            return ApiResult<McpConnectionMaterial>.Ok(new(key.Id, key.Name, secret, key.AccessMode, key.Permissions, key.ExpiresAt));
        }
        finally { _writeGate.Release(); }
    }

    public async Task<ApiResult<McpPrincipal>> AuthenticateMcpAsync(string secret, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(secret) || secret.Length > 128 || !secret.StartsWith("syslog_mcp_", StringComparison.Ordinal))
            return ApiResult<McpPrincipal>.Fail("mcp_authentication_required", "API Key 无效、已过期或已吊销。");
        await using var connection = await OpenAsync(false, ct);
        using var transaction = connection.BeginTransaction(deferred: true);
        if (!await SupportsMcpAsync(connection, transaction, ct)) return McpMigrationRequired<McpPrincipal>();
        var hash = HashToken(secret);
        await using var command = Command(connection, transaction,
            "SELECT k.id,k.created_by,k.name,u.username,k.access_mode,k.permissions_json,k.expires_at,k.secret_hash " +
            "FROM mcp_api_keys k JOIN users u ON u.id=k.created_by WHERE k.secret_hash=$hash " +
            "AND k.revoked_at IS NULL AND (k.expires_at IS NULL OR k.expires_at>$now) AND u.enabled=1 AND u.role='admin';",
            ("$hash", hash), ("$now", Now()));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || !CryptographicOperations.FixedTimeEquals(hash, (byte[])reader[7]))
            return ApiResult<McpPrincipal>.Fail("mcp_authentication_required", "API Key 无效、已过期或已吊销。");
        return ApiResult<McpPrincipal>.Ok(new(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), JsonSerializer.Deserialize<string[]>(reader.GetString(5))!, ReadNullableTime(reader, 6)));
    }

    public async Task<ApiResult<McpPrincipal>> ValidateMcpPermissionAsync(McpPrincipal principal, string permission, CancellationToken ct)
    {
        await using var connection = await OpenAsync(false, ct);
        using var transaction = connection.BeginTransaction(deferred: true);
        if (!await SupportsMcpAsync(connection, transaction, ct)) return McpMigrationRequired<McpPrincipal>();
        if (!await IsCurrentMcpActorAsync(connection, transaction, principal.UserId, principal.KeyId, permission, ct))
            return ApiResult<McpPrincipal>.Fail("mcp_permission_denied", "MCP 服务、API Key 或所需权限已不可用。");
        return ApiResult<McpPrincipal>.Ok(principal);
    }

    // 与业务写事务共享连接：不能仅相信入口处已经读取过的权限快照。
    private async Task<bool> IsCurrentMcpActorAsync(SqliteConnection connection, SqliteTransaction transaction,
        long userId, long keyId, string permission, CancellationToken ct)
    {
        if (!await SupportsMcpAsync(connection, transaction, ct)) return false;
        var definition = McpToolCatalog.All.FirstOrDefault(x => x.Name == permission);
        if (definition is null) return false;
        await using var command = Command(connection, transaction,
            "SELECT k.access_mode,k.permissions_json FROM mcp_api_keys k JOIN users u ON u.id=k.created_by " +
            "JOIN mcp_settings s ON s.singleton=1 WHERE k.id=$id AND k.created_by=$user AND s.enabled=1 " +
            "AND u.enabled=1 AND u.role='admin' AND k.revoked_at IS NULL AND (k.expires_at IS NULL OR k.expires_at>$now);",
            ("$id", keyId), ("$user", userId), ("$now", Now()));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || definition.IsWrite && reader.GetString(0) != "read_write") return false;
        return JsonSerializer.Deserialize<string[]>(reader.GetString(1))!.Contains(permission, StringComparer.Ordinal);
    }

    private static async Task<McpSettingsView> ReadMcpSettingsRecordAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        await using var command = Command(connection, transaction, "SELECT version,enabled,audit_retention_days FROM mcp_settings WHERE singleton=1;");
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("MCP 配置记录不完整。");
        return new(Id(reader.GetInt64(0)), reader.GetInt64(1) != 0, reader.GetInt32(2));
    }

    private async Task<McpKeyView?> ReadMcpKeyRecordAsync(SqliteConnection connection, SqliteTransaction transaction, long id, CancellationToken ct)
    {
        await using var command = Command(connection, transaction,
            "SELECT " + McpKeyColumns + " FROM mcp_api_keys k JOIN users u ON u.id=k.created_by WHERE k.id=$id;", ("$id", id));
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadMcpKey(reader, Now()) : null;
    }

    private static McpKeyView ReadMcpKey(SqliteDataReader reader, long now)
    {
        var expires = ReadNullableTime(reader, 7);
        var revoked = ReadNullableTime(reader, 8);
        var status = revoked.HasValue ? "revoked" : expires?.ToUnixTimeMilliseconds() <= now ? "expired"
            : reader.GetInt64(10) != 1 || reader.GetString(11) != "admin" ? "owner_disabled" : "active";
        return new(Id(reader.GetInt64(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            JsonSerializer.Deserialize<string[]>(reader.GetString(4))!, Id(reader.GetInt64(5)),
            DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(6)), expires, revoked, ReadNullableTime(reader, 9), status);
    }

    private static DateTimeOffset? ReadNullableTime(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(ordinal));
}
