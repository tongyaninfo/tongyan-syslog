using Microsoft.Data.Sqlite;
using Syslog.Api.Models;
using Syslog.Shared.Models;

namespace Syslog.Api.Service.impl;

public sealed partial class ControlStore
{
    public async Task MigrateMcpAsync(CancellationToken ct)
    {
        await _writeGate.WaitAsync(ct);
        try
        {
            await using var connection = await OpenAsync(false, ct);
            using var transaction = connection.BeginTransaction();
            await ValidateSetupSchemaAsync(connection, transaction, ct, fullCheck: true);
            var version = await ReadSchemaVersionAsync(connection, transaction, ct);
            if (version < 4) throw new InvalidOperationException("请先完成邮件通知控制库迁移。");
            if (version == 4) await ApplyMcpMigrationAsync(connection, transaction, ct);
            await ValidateSetupSchemaAsync(connection, transaction, ct, fullCheck: true);
            transaction.Commit();
        }
        finally { _writeGate.Release(); }
    }

    private static async Task ApplyMcpMigrationAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        await using var resource = typeof(ControlStore).Assembly.GetManifestResourceStream("Syslog.Api.Data.005_mcp_service.sql")
            ?? throw new InvalidOperationException("MCP 迁移资源缺失。");
        using var reader = new StreamReader(resource);
        await ExecuteAsync(connection, transaction, await reader.ReadToEndAsync(ct), ct);
    }

    private static async Task ValidateMcpSchemaAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        foreach (var query in new[]
        {
            "SELECT singleton,version,enabled,audit_retention_days,updated_by,updated_at FROM mcp_settings LIMIT 0;",
            "SELECT id,name,display_prefix,secret,secret_hash,access_mode,permissions_json,created_by,created_at,expires_at,revoked_by,revoked_at,last_used_at FROM mcp_api_keys LIMIT 0;",
            "SELECT id,key_id,key_name,actor_id,actor_name,trace_id,rpc_id,method,tool_name,peer_ip,http_method,url,http_protocol,request_headers_json,request_body,request_bytes,request_complete,response_headers_json,response_body,response_bytes,response_complete,http_status,started_at,completed_at,duration_ms,outcome,code,capture_error FROM mcp_call_audits LIMIT 0;"
        })
        {
            await using var command = Command(connection, transaction, query);
            await using var reader = await command.ExecuteReaderAsync(ct);
        }
        if (Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT count(*) FROM mcp_settings WHERE singleton=1;", ct)) != 1)
            throw new InvalidOperationException("MCP 配置记录不完整。");
    }

    private static async Task<bool> SupportsMcpAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct) =>
        (await ReadSchemaVersionAsync(connection, transaction, ct)) switch
        {
            1 or 2 or 3 or 4 => false,
            5 or 6 => true,
            _ => throw new InvalidOperationException("控制库版本不受支持。")
        };

    private static ApiResult<T> McpMigrationRequired<T>() =>
        ApiResult<T>.Fail("mcp_migration_required", "请先执行 MCP 控制库显式迁移。");
}
