using System.Globalization;
using Microsoft.Data.Sqlite;
using Syslog.Api.Models;
using Syslog.Shared.Models;

namespace Syslog.Api.Service.impl;

public sealed partial class ControlStore
{
    private readonly SemaphoreSlim _setupGate = new(1, 1);

    public async Task<bool> IsSetupRequiredAsync(CancellationToken cancellationToken) =>
        await ReadSetupStateAsync(cancellationToken) != SetupState.Ready;

    public async Task<ApiResult<ControlUser>> SetupAdminAsync(string username, string password, CancellationToken cancellationToken)
    {
        username = username?.Trim() ?? "";
        if (!ValidUsername(username)) return ApiResult<ControlUser>.Fail("username_invalid", "账号格式无效。");
        if (!ValidPassword(password)) return ApiResult<ControlUser>.Fail("password_policy", "密码须为 8～1024 位且不能全为空白。");
        await _setupGate.WaitAsync(cancellationToken);
        try
        {
            var state = await ReadSetupStateAsync(cancellationToken);
            if (state == SetupState.Ready) return ApiResult<ControlUser>.Fail("setup_already_completed", "管理员已初始化。");
            var hash = PasswordHasher.Hash(password);
            var freshFile = false;
            if (state == SetupState.Missing)
            {
                // CreateNew 原子认领新文件，不覆盖已存在文件。其他进程抢先创建时重新检查，
                // 不把零字节文件或半成品当成可以自动修复的控制库。
                try
                {
                    await using var file = new FileStream(DatabasePath(), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite);
                    freshFile = true;
                }
                catch (IOException)
                {
                    if (await ReadSetupStateAsync(cancellationToken) == SetupState.Ready)
                        return ApiResult<ControlUser>.Fail("setup_already_completed", "管理员已初始化。");
                    throw;
                }
            }
            await _writeGate.WaitAsync(cancellationToken);
            try
            {
                await using var connection = await OpenAsync(false, cancellationToken);
                if (freshFile) await ExecuteAsync(connection, null, "PRAGMA journal_mode=WAL;", cancellationToken);
                using var transaction = connection.BeginTransaction();
                if (freshFile)
                {
                    // 已认领文件仍须为空，避免两个进程之间存在检查后使用的覆盖窗口。
                    if (Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT count(*) FROM sqlite_master;", cancellationToken), CultureInfo.InvariantCulture) != 0)
                        throw new InvalidOperationException("初始化目标状态已改变。");
                    await using var resource = typeof(ControlStore).Assembly.GetManifestResourceStream("Syslog.Api.Data.001_control.sql")
                        ?? throw new InvalidOperationException("控制库初始化资源缺失。");
                    using var reader = new StreamReader(resource);
                    await ExecuteAsync(connection, transaction, await reader.ReadToEndAsync(cancellationToken), cancellationToken);
                    await ApplyListenerMigrationAsync(connection, transaction, cancellationToken);
                    await ApplySourceFilterMigrationAsync(connection, transaction, cancellationToken);
                    await ApplyNotificationMigrationAsync(connection, transaction, cancellationToken);
                    await ApplyMcpMigrationAsync(connection, transaction, cancellationToken);
                    await ApplyLiveLogMigrationAsync(connection, transaction, cancellationToken);
                }
                await ValidateSetupSchemaAsync(connection, transaction, cancellationToken, fullCheck: true);
                var count = Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT EXISTS(SELECT 1 FROM users);", cancellationToken), CultureInfo.InvariantCulture);
                if (count != 0) return ApiResult<ControlUser>.Fail("setup_already_completed", "管理员已初始化。");
                // 与集中发号共享同一持久高水位。本次仅预留两号，使新库结构、账号、审计原子提交。
                var first = await ReserveIdBlockAsync(connection, transaction, 2, cancellationToken);
                await ExecuteAsync(connection, transaction,
                    "INSERT INTO users(id,username,normalized_username,password_hash,role,enabled,created_at) VALUES($id,$name,$normalized,$hash,'admin',1,$now);",
                    cancellationToken, ("$id", first), ("$name", username), ("$normalized", username.ToUpperInvariant()), ("$hash", hash), ("$now", Now()));
                await AuditAsync(connection, transaction, checked(first + 1), first, "admin.bootstrap", Id(first), new { username }, cancellationToken);
                transaction.Commit();
                return ApiResult<ControlUser>.Ok(new(first, username, "admin"));
            }
            finally { _writeGate.Release(); }
        }
        finally { _setupGate.Release(); }
    }

    private async Task<SetupState> ReadSetupStateAsync(CancellationToken ct)
    {
        var builder = new SqliteConnectionStringBuilder(_options.ConnectionString);
        if (builder.Mode != SqliteOpenMode.Memory && builder.DataSource != ":memory:")
        {
            var path = DatabasePath();
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.Directory) != 0)
                    throw new InvalidOperationException("控制库路径无效。");
            }
            catch (FileNotFoundException)
            {
                // 不使用 File.Exists，它会把权限错误吞成 false。父目录缺失也作为配置错误处理。
                if (!Directory.Exists(Path.GetDirectoryName(path))) throw new InvalidOperationException("控制库目录不可用。");
                return SetupState.Missing;
            }
            builder.Mode = SqliteOpenMode.ReadOnly;
        }
        builder.ForeignKeys = true;
        await using var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(ct);
        using var transaction = connection.BeginTransaction(deferred: true);
        await ValidateSetupSchemaAsync(connection, transaction, ct);
        var count = Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT EXISTS(SELECT 1 FROM users);", ct), CultureInfo.InvariantCulture);
        return count == 0 ? SetupState.Empty : SetupState.Ready;
    }

    private string DatabasePath()
    {
        var source = new SqliteConnectionStringBuilder(_options.ConnectionString).DataSource;
        // Web 安装只接受普通文件路径，URI / 临时库必须由显式维护流程处理。
        if (string.IsNullOrWhiteSpace(source) || source.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("控制库路径不支持网页初始化。");
        return Path.GetFullPath(source);
    }

    private static async Task ValidateSetupSchemaAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct, bool fullCheck = false)
    {
        var expected = new Dictionary<string, string>
        {
            ["schema_versions"] = "version,applied_at",
            ["id_allocations"] = "name,next_id",
            ["users"] = "id,username,normalized_username,password_hash,role,enabled,session_version,created_at",
            ["sessions"] = "token_hash,user_id,user_version,issued_at,expires_at,revoked",
            ["sources"] = "id,name,peer_address,time_zone_id,enabled,created_at,updated_at",
            ["retention_policies"] = "source_id,retention_days,updated_at",
            ["audit_entries"] = "id,actor_user_id,action,target_id,details_json,created_at"
        };
        foreach (var (table, columns) in expected)
        {
            var exists = await ScalarAsync(connection, transaction, "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=$table;", ct, ("$table", table));
            if (Convert.ToInt64(exists, CultureInfo.InvariantCulture) != 1) throw new InvalidOperationException("控制库结构不完整。");
            // 标识符均来自上方固定定义，不拼接用户输入。缺列立即失败，不自动补表。
            await using var command = Command(connection, transaction, $"SELECT {columns} FROM {table} LIMIT 0;");
            await using var reader = await command.ExecuteReaderAsync(ct);
        }
        var version = await ReadSchemaVersionAsync(connection, transaction, ct);
        if (version is not (1 or 2 or 3 or 4 or 5 or 6) ||
            Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT count(*) FROM schema_versions;", ct), CultureInfo.InvariantCulture) != version ||
            Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT count(*) FROM schema_versions WHERE version BETWEEN 1 AND 6;", ct), CultureInfo.InvariantCulture) != version ||
            Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT count(*) FROM id_allocations WHERE name='global' AND next_id>0;", ct), CultureInfo.InvariantCulture) != 1)
            throw new InvalidOperationException("控制库版本或发号状态不受支持。");
        if (version >= 2)
        {
            await using var command = Command(connection, transaction,
                "SELECT singleton,version,configuration_json,updated_by,updated_at FROM listener_settings LIMIT 0;");
            await using var reader = await command.ExecuteReaderAsync(ct);
        }
        if (version >= 3)
        {
            await using var command = Command(connection, transaction, "SELECT ignored_severity_mask FROM sources LIMIT 0;");
            await using var reader = await command.ExecuteReaderAsync(ct);
        }
        if (version >= 4)
        {
            await ValidateNotificationSchemaAsync(connection, transaction, ct);
        }
        if (version >= 5) await ValidateMcpSchemaAsync(connection, transaction, ct);
        if (version >= 6) await ValidateLiveLogSchemaAsync(connection, transaction, ct);
        // 完整性扫描仅用于显式初始化提交，匿名状态检查保持有界元数据查询。
        if (fullCheck && !string.Equals(await ScalarAsync(connection, transaction, "PRAGMA quick_check(1);", ct) as string, "ok", StringComparison.Ordinal))
            throw new InvalidOperationException("控制库完整性检查失败。");
    }

    private static async Task<long> ReserveIdBlockAsync(SqliteConnection connection, SqliteTransaction transaction, int size, CancellationToken ct)
    {
        var start = await ScalarAsync(connection, transaction,
            "UPDATE id_allocations SET next_id=next_id+$size WHERE name='global' AND next_id<=$limit RETURNING next_id-$size;",
            ct, ("$size", size), ("$limit", long.MaxValue - size));
        if (start is null or DBNull) throw new InvalidOperationException("控制库尚未初始化或可分配 ID 已耗尽。");
        return Convert.ToInt64(start, CultureInfo.InvariantCulture);
    }

    private enum SetupState { Missing, Empty, Ready }
}
