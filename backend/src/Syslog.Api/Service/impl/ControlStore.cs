using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Syslog.Api.Models;
using Syslog.Api.Options;
using Syslog.Shared.Models;

namespace Syslog.Api.Service.impl;

/// <summary>内嵌控制库。变更与可靠审计同事务，日志正文由独立 ClickHouse 存储负责。</summary>
public sealed partial class ControlStore : IControlStore, INotificationStore, IMcpStore
{
    private readonly ControlStoreOptions _options;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _idGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private long _nextId;
    private long _lastId;

    public ControlStore(ControlStoreOptions options, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.IdBlockSize is < 1 or > 1_000_000 || options.SessionLifetimeMinutes is < 1 or > 1440)
            throw new ArgumentException("控制库的号段或会话期限超出允许范围。", nameof(options));
        // 拷贝配置，避免运行期间修改同一对象破坏号段边界。
        _options = new ControlStoreOptions
        {
            ConnectionString = options.ConnectionString,
            IdBlockSize = options.IdBlockSize,
            SessionLifetimeMinutes = options.SessionLifetimeMinutes
        };
        _clock = clock ?? TimeProvider.System;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(true, cancellationToken);
            // WAL 仅用于控制库；内存测试库会由 SQLite 保持 memory 模式。
            await ExecuteAsync(connection, null, "PRAGMA journal_mode=WAL;", cancellationToken);
            using var transaction = connection.BeginTransaction();
            var exists = await ScalarAsync(connection, transaction,
                "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='schema_versions';", cancellationToken);
            if (Convert.ToInt64(exists, CultureInfo.InvariantCulture) != 0)
            {
                // 已有库只验证，升级需通过对应的显式迁移入口。
                await ValidateSetupSchemaAsync(connection, transaction, cancellationToken, fullCheck: true);
                transaction.Commit();
                return;
            }
            await using var resource = typeof(ControlStore).Assembly.GetManifestResourceStream("Syslog.Api.Data.001_control.sql")
                ?? throw new InvalidOperationException("控制库初始化资源缺失。");
            using var reader = new StreamReader(resource);
            var sql = await reader.ReadToEndAsync(cancellationToken);
            await ExecuteAsync(connection, transaction, sql, cancellationToken);
            await ApplyListenerMigrationAsync(connection, transaction, cancellationToken);
            await ApplySourceFilterMigrationAsync(connection, transaction, cancellationToken);
            await ApplyNotificationMigrationAsync(connection, transaction, cancellationToken);
            await ApplyMcpMigrationAsync(connection, transaction, cancellationToken);
            await ApplyLiveLogMigrationAsync(connection, transaction, cancellationToken);
            await ValidateSetupSchemaAsync(connection, transaction, cancellationToken, fullCheck: true);
            transaction.Commit();
        }
        finally { _writeGate.Release(); }
    }

    public async Task<long> NextAsync(CancellationToken cancellationToken)
    {
        await _idGate.WaitAsync(cancellationToken);
        try
        {
            if (_nextId == 0 || _nextId > _lastId)
            {
                await using var connection = await OpenAsync(false, cancellationToken);
                using var transaction = connection.BeginTransaction();
                var first = await ReserveIdBlockAsync(connection, transaction, _options.IdBlockSize, cancellationToken);
                transaction.Commit();
                // 先提交预留号段再发号。重启允许空洞，不允许重新使用已预留 ID。
                _nextId = first;
                _lastId = checked(first + _options.IdBlockSize - 1);
            }
            return _nextId++;
        }
        finally { _idGate.Release(); }
    }

    public async Task<ApiResult<ControlUser>> BootstrapAdminAsync(string username, string password, CancellationToken cancellationToken)
    {
        username = username?.Trim() ?? "";
        if (!ValidUsername(username) || !ValidPassword(password))
            return ApiResult<ControlUser>.Fail("INVALID_INPUT", "账号需为 3～64 位字母、数字、点、横线或下划线，密码需为 8～1024 位且不能全为空白。");
        var passwordHash = PasswordHasher.Hash(password);
        var id = await NextAsync(cancellationToken);
        var auditId = await NextAsync(cancellationToken);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(false, cancellationToken);
            using var transaction = connection.BeginTransaction();
            var count = await ScalarAsync(connection, transaction, "SELECT count(*) FROM users;", cancellationToken);
            if (Convert.ToInt64(count, CultureInfo.InvariantCulture) != 0)
                return ApiResult<ControlUser>.Fail("ALREADY_INITIALIZED", "管理员已初始化。");
            await ExecuteAsync(connection, transaction,
                "INSERT INTO users(id,username,normalized_username,password_hash,role,enabled,created_at) VALUES($id,$name,$normalized,$hash,'admin',1,$now);",
                cancellationToken, ("$id", id), ("$name", username), ("$normalized", username.ToUpperInvariant()), ("$hash", passwordHash), ("$now", Now()));
            await AuditAsync(connection, transaction, auditId, id, "admin.bootstrap", Id(id), new { username }, cancellationToken);
            transaction.Commit();
            return ApiResult<ControlUser>.Ok(new(id, username, "admin"));
        }
        finally { _writeGate.Release(); }
    }

    public async Task<ApiResult<SessionTicket>> LoginAsync(string username, string password, CancellationToken cancellationToken)
    {
        username = username?.Trim() ?? "";
        if (username.Length > 64 || password is null || password.Length > 1024)
            return ApiResult<SessionTicket>.Fail("AUTH_FAILED", "账号或密码无效。");
        await using var connection = await OpenAsync(false, cancellationToken);
        var user = await ReadAuthUserAsync(connection, null, username.ToUpperInvariant(), cancellationToken);
        var valid = PasswordHasher.Verify(password, user?.PasswordHash ?? PasswordHasher.DummyHash);
        var auditId = await NextAsync(cancellationToken);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            using var transaction = connection.BeginTransaction();
            // 派生密码期间不占写锁，但提交前必须复查账号状态与凭据版本。
            var current = await ReadAuthUserAsync(connection, transaction, username.ToUpperInvariant(), cancellationToken);
            if (!valid || user is null || current is null || !current.Enabled || current.PasswordHash != user.PasswordHash || current.Version != user.Version)
            {
                await AuditAsync(connection, transaction, auditId, current?.User.Id, "auth.login_failed", username, new { }, cancellationToken);
                transaction.Commit();
                return ApiResult<SessionTicket>.Fail("AUTH_FAILED", "账号或密码无效。");
            }
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var now = _clock.GetUtcNow();
            var expiresAt = now.AddMinutes(_options.SessionLifetimeMinutes);
            await ExecuteAsync(connection, transaction,
                "INSERT INTO sessions(token_hash,user_id,user_version,issued_at,expires_at) VALUES($hash,$user,$version,$issued,$expires);",
                cancellationToken, ("$hash", HashToken(token)), ("$user", current.User.Id), ("$version", current.Version),
                ("$issued", now.ToUnixTimeMilliseconds()), ("$expires", expiresAt.ToUnixTimeMilliseconds()));
            await AuditAsync(connection, transaction, auditId, current.User.Id, "auth.login", Id(current.User.Id), new { }, cancellationToken);
            transaction.Commit();
            return ApiResult<SessionTicket>.Ok(new(token, expiresAt, current.User));
        }
        finally { _writeGate.Release(); }
    }

    public async Task<ControlUser?> ValidateSessionAsync(string token, CancellationToken cancellationToken)
    {
        if (!ValidToken(token)) return null;
        await using var connection = await OpenAsync(false, cancellationToken);
        await using var command = Command(connection, null,
            "SELECT u.id,u.username,u.role FROM sessions s JOIN users u ON u.id=s.user_id WHERE s.token_hash=$hash AND s.revoked=0 AND s.expires_at>$now AND u.enabled=1 AND u.session_version=s.user_version;",
            ("$hash", HashToken(token)), ("$now", Now()));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)) : null;
    }

    public async Task<ApiResult<bool>> LogoutAsync(string token, CancellationToken cancellationToken)
    {
        if (!ValidToken(token)) return ApiResult<bool>.Ok(true);
        var auditId = await NextAsync(cancellationToken);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(false, cancellationToken);
            using var transaction = connection.BeginTransaction();
            var userId = await ScalarAsync(connection, transaction, "UPDATE sessions SET revoked=1 WHERE token_hash=$hash AND revoked=0 RETURNING user_id;", cancellationToken, ("$hash", HashToken(token)));
            if (userId is not null and not DBNull)
            {
                var id = Convert.ToInt64(userId, CultureInfo.InvariantCulture);
                await AuditAsync(connection, transaction, auditId, id, "auth.logout", Id(id), new { }, cancellationToken);
            }
            transaction.Commit();
            return ApiResult<bool>.Ok(true);
        }
        finally { _writeGate.Release(); }
    }

    public async Task<ApiResult<bool>> ChangePasswordAsync(long userId, string token, string? currentPassword,
        string? newPassword, CancellationToken cancellationToken)
    {
        if (!ValidToken(token)) return ApiResult<bool>.Fail("authentication_required", "请先登录。");
        if (currentPassword is null || currentPassword.Length is < 1 or > 1024 || !ValidPassword(newPassword))
            return ApiResult<bool>.Fail("password_policy", "请输入当前密码，新密码须为 8～1024 位且不能全为空白。");
        if (newPassword == currentPassword)
            return ApiResult<bool>.Fail("password_unchanged", "新密码不能与当前密码相同。");

        var tokenHash = HashToken(token);
        await using var connection = await OpenAsync(false, cancellationToken);
        var currentHash = await ReadSessionPasswordHashAsync(connection, null, userId, tokenHash, cancellationToken);
        if (currentHash is null) return ApiResult<bool>.Fail("authentication_required", "请先登录。");
        // 密码验证和派生较耗时，先在写事务外完成，提交前再复核会话与密码散列。
        var passwordMatches = PasswordHasher.Verify(currentPassword, currentHash);
        var newHash = passwordMatches ? PasswordHasher.Hash(newPassword) : null;
        var auditId = await NextAsync(cancellationToken);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            using var transaction = connection.BeginTransaction();
            if (await ReadSessionPasswordHashAsync(connection, transaction, userId, tokenHash, cancellationToken) != currentHash)
                return ApiResult<bool>.Fail("authentication_required", "请先登录。");
            if (newHash is null)
            {
                await AuditAsync(connection, transaction, auditId, userId, "auth.password_change_failed", Id(userId), new { }, cancellationToken);
                transaction.Commit();
                return ApiResult<bool>.Fail("current_password_invalid", "当前密码不正确。");
            }
            // 同一事务内更新密码、废止该账号所有会话并写审计，审计失败时整体回滚。
            await ExecuteAsync(connection, transaction,
                "UPDATE users SET password_hash=$hash,session_version=session_version+1 WHERE id=$user;", cancellationToken,
                ("$hash", newHash), ("$user", userId));
            await ExecuteAsync(connection, transaction,
                "UPDATE sessions SET revoked=1 WHERE user_id=$user AND revoked=0;", cancellationToken, ("$user", userId));
            await AuditAsync(connection, transaction, auditId, userId, "auth.password_change", Id(userId), new { }, cancellationToken);
            transaction.Commit();
            return ApiResult<bool>.Ok(true);
        }
        finally { _writeGate.Release(); }
    }

    public async Task<IReadOnlyList<LogSource>> ListAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(false, cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: true);
        var supportsFilters = await SupportsSourceFiltersAsync(connection, transaction, cancellationToken);
        await using var command = Command(connection, transaction, SelectSourceColumns(supportsFilters) + " ORDER BY s.id;");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var sources = new List<LogSource>();
        while (await reader.ReadAsync(cancellationToken)) sources.Add(ReadSource(reader));
        return sources;
    }

    public Task<ApiResult<LogSource>> CreateSourceAsync(SourceInput input, long actorId, CancellationToken cancellationToken) =>
        SaveSourceAsync(null, input, actorId, cancellationToken);

    public Task<ApiResult<LogSource>> UpdateSourceAsync(string sourceId, SourceInput input, long actorId, CancellationToken cancellationToken) =>
        SaveSourceAsync(sourceId, input, actorId, cancellationToken);

    private async Task<ApiResult<LogSource>> SaveSourceAsync(string? sourceId, SourceInput input, long actorId, CancellationToken cancellationToken,
        OperationActor? actor = null, SourcePatch? patch = null)
    {
        var normalized = input;
        if (patch is null && !TryNormalizeSource(input, out normalized))
            return ApiResult<LogSource>.Fail("INVALID_INPUT", "来源需提供名称、有效 IP 与时区，保留天数为 1～3650。");
        if (patch is null && normalized.IgnoredSeverities?.Any(severity => severity is < 0 or > 7) == true)
            return ApiResult<LogSource>.Fail("INVALID_INPUT", "忽略严重级别必须为 0～7。");
        long id;
        if (sourceId is null) id = await NextAsync(cancellationToken);
        else if (!long.TryParse(sourceId, NumberStyles.None, CultureInfo.InvariantCulture, out id) || id <= 0)
            return ApiResult<LogSource>.Fail("NOT_FOUND", "来源不存在。");
        var auditId = await NextAsync(cancellationToken);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(false, cancellationToken);
            using var transaction = connection.BeginTransaction();
            var authorized = actor is null
                ? Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT count(*) FROM users WHERE id=$id AND enabled=1 AND role='admin';", cancellationToken, ("$id", actorId)), CultureInfo.InvariantCulture) == 1
                : await IsCurrentActorAsync(connection, transaction, actor, cancellationToken, sourceId is null
                    ? ["syslog_sources_create", "syslog_unregistered_register"]
                    : ["syslog_sources_update", "syslog_sources_set_enabled", "syslog_sources_set_ignored_severities", "syslog_source_retention_set"]);
            if (!authorized)
                return ApiResult<LogSource>.Fail("FORBIDDEN", "此操作需要有效管理员权限。");
            var supportsFilters = await SupportsSourceFiltersAsync(connection, transaction, cancellationToken);
            if (patch is null && !supportsFilters && normalized.IgnoredSeverities is { Length: > 0 })
                return ApiResult<LogSource>.Fail("source_filter_migration_required", "请先执行来源筛选显式迁移。");
            LogSource? previous = null;
            if (sourceId is not null)
            {
                await using var command = Command(connection, transaction,
                    SelectSourceColumns(supportsFilters) + " WHERE s.id=$id;", ("$id", id));
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken)) return ApiResult<LogSource>.Fail("NOT_FOUND", "来源不存在。");
                previous = ReadSource(reader);
            }
            // 窄操作在写事务内合并，避免读后整对象覆盖并发修改或绕过其他功能权限。
            if (patch is not null)
            {
                if (previous is null || actor is null || !patch.TryApply(previous, actor.Permission, out var changed)
                    || !TryNormalizeSource(changed, out normalized)
                    || normalized.IgnoredSeverities?.Any(value => value is < 0 or > 7) == true)
                    return ApiResult<LogSource>.Fail("invalid_request", "来源变更字段与授权动作不匹配或内容无效。");
                if (!supportsFilters && normalized.IgnoredSeverities is { Length: > 0 })
                    return ApiResult<LogSource>.Fail("source_filter_migration_required", "请先执行来源筛选显式迁移。");
            }
            var duplicate = await ScalarAsync(connection, transaction, "SELECT count(*) FROM sources WHERE peer_address=$address AND id<>$id;", cancellationToken, ("$address", normalized.PeerAddress), ("$id", id));
            if (Convert.ToInt64(duplicate, CultureInfo.InvariantCulture) != 0)
                return ApiResult<LogSource>.Fail("CONFLICT", "该来源地址已登记。");
            var sql = sourceId is null
                ? "INSERT INTO sources(id,name,peer_address,time_zone_id,enabled,created_at,updated_at) VALUES($id,$name,$address,$zone,$enabled,$now,$now);"
                : "UPDATE sources SET name=$name,peer_address=$address,time_zone_id=$zone,enabled=$enabled,updated_at=$now WHERE id=$id;";
            await ExecuteAsync(connection, transaction, sql, cancellationToken,
                ("$id", id), ("$name", normalized.Name), ("$address", normalized.PeerAddress), ("$zone", normalized.TimeZoneId), ("$enabled", normalized.Enabled ? 1 : 0), ("$now", Now()));
            // 旧客户端省略字段时保留当前设置，显式空数组清空。去重和排序由掩码统一处理。
            var ignoredSeverities = previous?.IgnoredSeverities ?? Array.Empty<int>();
            if (supportsFilters && normalized.IgnoredSeverities is { } requestedSeverities)
            {
                var mask = 0;
                foreach (var severity in requestedSeverities) mask |= 1 << severity;
                await ExecuteAsync(connection, transaction,
                    "UPDATE sources SET ignored_severity_mask=$mask WHERE id=$id;", cancellationToken, ("$id", id), ("$mask", mask));
                ignoredSeverities = DecodeIgnoredSeverities(mask);
            }
            await ExecuteAsync(connection, transaction,
                "INSERT INTO retention_policies(source_id,retention_days,updated_at) VALUES($id,$days,$now) ON CONFLICT(source_id) DO UPDATE SET retention_days=excluded.retention_days,updated_at=excluded.updated_at;",
                cancellationToken, ("$id", id), ("$days", normalized.RetentionDays), ("$now", Now()));
            var saved = new LogSource(Id(id), normalized.Name, normalized.PeerAddress, normalized.TimeZoneId, normalized.RetentionDays,
                normalized.Enabled, ignoredSeverities);
            // 保留期只定义后续接收事件的 expires_at；此处不删除或重写已有日志。
            await AuditAsync(connection, transaction, auditId, actorId, sourceId is null ? "source.create" : "source.update", Id(id), new { before = previous, after = saved }, cancellationToken);
            transaction.Commit();
            return ApiResult<LogSource>.Ok(saved);
        }
        finally { _writeGate.Release(); }
    }

    private static bool TryNormalizeSource(SourceInput input, out SourceInput normalized)
    {
        normalized = input;
        if (input is null || string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 128 ||
            !IPAddress.TryParse(input.PeerAddress, out var address) || input.RetentionDays is < 1 or > 3650 || string.IsNullOrWhiteSpace(input.TimeZoneId) || input.TimeZoneId.Length > 128) return false;
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(input.TimeZoneId); }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        normalized = input with { Name = input.Name.Trim(), PeerAddress = address.ToString() };
        return true;
    }

    private async Task<SqliteConnection> OpenAsync(bool initialize, CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder(_options.ConnectionString);
        if (builder.Mode != SqliteOpenMode.Memory) builder.Mode = initialize ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite;
        builder.ForeignKeys = true;
        var connection = new SqliteConnection(builder.ToString());
        try
        {
            await connection.OpenAsync(cancellationToken);
            await ExecuteAsync(connection, null, "PRAGMA busy_timeout=5000; PRAGMA synchronous=FULL;", cancellationToken);
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    private async Task AuditAsync(SqliteConnection connection, SqliteTransaction transaction, long id, long? actorId, string action, string target, object details, CancellationToken cancellationToken) =>
        await ExecuteAsync(connection, transaction,
            "INSERT INTO audit_entries(id,actor_user_id,action,target_id,details_json,created_at) VALUES($id,$actor,$action,$target,$details,$now);", cancellationToken,
            ("$id", id), ("$actor", actorId), ("$action", action), ("$target", target), ("$details", JsonSerializer.Serialize(details)), ("$now", Now()));

    private static async Task<AuthUser?> ReadAuthUserAsync(SqliteConnection connection, SqliteTransaction? transaction, string username, CancellationToken cancellationToken)
    {
        await using var command = Command(connection, transaction, "SELECT id,username,role,password_hash,enabled,session_version FROM users WHERE normalized_username=$name;", ("$name", username));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)), reader.GetString(3), reader.GetBoolean(4), reader.GetInt64(5))
            : null;
    }

    private async Task<string?> ReadSessionPasswordHashAsync(SqliteConnection connection, SqliteTransaction? transaction,
        long userId, byte[] tokenHash, CancellationToken cancellationToken) =>
        await ScalarAsync(connection, transaction,
            "SELECT u.password_hash FROM sessions s JOIN users u ON u.id=s.user_id WHERE u.id=$user AND s.token_hash=$hash AND s.revoked=0 AND s.expires_at>$now AND u.enabled=1 AND u.session_version=s.user_version;",
            cancellationToken, ("$user", userId), ("$hash", tokenHash), ("$now", Now())) as string;

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        return command;
    }

    private static async Task<int> ExecuteAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(connection, transaction, sql, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<object?> ScalarAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(connection, transaction, sql, parameters);
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    private static LogSource ReadSource(SqliteDataReader reader) => new(Id(reader.GetInt64(0)), reader.GetString(1), reader.GetString(2),
        reader.GetString(3), reader.GetInt32(4), reader.GetBoolean(5), DecodeIgnoredSeverities(reader.GetInt32(6)));
    private static bool ValidUsername(string value) => value.Length is >= 3 and <= 64 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');
    private static bool ValidToken(string value) => value is { Length: 43 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    private static byte[] HashToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
    private static string Id(long value) => value.ToString(CultureInfo.InvariantCulture);
    private long Now() => _clock.GetUtcNow().ToUnixTimeMilliseconds();
    private sealed record AuthUser(ControlUser User, string PasswordHash, bool Enabled, long Version);
}
