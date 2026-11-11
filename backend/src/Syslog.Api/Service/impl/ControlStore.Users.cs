using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Syslog.Api.Models;
using Syslog.Shared.Models;

namespace Syslog.Api.Service.impl;

public sealed partial class ControlStore
{
    private const string UserColumns = "id,username,role,enabled,created_at,session_version";

    public Task<ApiResult<ManagedUserPage>> ListUsersAsync(UserQuery query, long actorId, string token, CancellationToken cancellationToken) =>
        ListUsersAsync(query, OperationActor.Session(actorId, token), cancellationToken);

public async Task<ApiResult<ManagedUserPage>> ListUsersAsync(UserQuery query, OperationActor actor, CancellationToken cancellationToken)
    {
        var actorId = actor.UserId;
        if (query.Page < 1 || query.PageSize is < 1 or > 100 || query.Search?.Length > 64)
            return ApiResult<ManagedUserPage>.Fail("invalid_request", "账号查询参数无效。");
        await using var connection = await OpenAsync(false, cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: true);
        if (!await IsCurrentActorAsync(connection, transaction, actor, cancellationToken, "syslog_users_list"))
            return ApiResult<ManagedUserPage>.Fail("authentication_required", "管理员会话已失效。");
        var parameters = new (string Name, object? Value)[]
        {
            ("$search", (query.Search?.Trim() ?? "").ToUpperInvariant()),
            ("$enabled", query.Enabled.HasValue ? query.Enabled.Value ? 1 : 0 : null)
        };
        const string filter = " WHERE instr(normalized_username,$search)>0 AND ($enabled IS NULL OR enabled=$enabled)";
        var count = Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT count(*) FROM users" + filter, cancellationToken, parameters), CultureInfo.InvariantCulture);
        await using var command = Command(connection, transaction,
            "SELECT " + UserColumns + " FROM users" + filter + " ORDER BY id LIMIT $limit OFFSET $offset;",
            [.. parameters, ("$limit", query.PageSize), ("$offset", (long)(query.Page - 1) * query.PageSize)]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<ManagedUser>();
        while (await reader.ReadAsync(cancellationToken)) items.Add(ReadManagedUser(reader));
        // 总数与当前页共用一个读取快照，避免并发账号操作产生自相矛盾的分页信息。
        return ApiResult<ManagedUserPage>.Ok(new(items, count, query.Page, query.PageSize));
    }

    public Task<ApiResult<ManagedUser>> CreateUserAsync(CreateUserRequest input, long actorId, string token, CancellationToken cancellationToken) =>
        CreateUserAsync(input, OperationActor.Session(actorId, token), cancellationToken);

public async Task<ApiResult<ManagedUser>> CreateUserAsync(CreateUserRequest input, OperationActor actor, CancellationToken cancellationToken)
    {
        var actorId = actor.UserId;
        var username = input.Username?.Trim() ?? "";
        if (!ValidUsername(username)) return ApiResult<ManagedUser>.Fail("username_invalid", "账号格式无效。");
        if (!ValidPassword(input.Password)) return ApiResult<ManagedUser>.Fail("password_policy", "密码须为 8～1024 位且不能全为空白。");
        if (!actor.IsMcp && !ValidToken(actor.SessionToken ?? "")) return ApiResult<ManagedUser>.Fail("authentication_required", "管理员会话已失效。");
        var hash = PasswordHasher.Hash(input.Password);
        var id = await NextAsync(cancellationToken);
        var auditId = await NextAsync(cancellationToken);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(false, cancellationToken);
            using var transaction = connection.BeginTransaction();
            if (!await IsCurrentActorAsync(connection, transaction, actor, cancellationToken, "syslog_users_create"))
                return ApiResult<ManagedUser>.Fail("authentication_required", "管理员会话已失效。");
            if (await UsernameExistsAsync(connection, transaction, username, id, cancellationToken))
                return ApiResult<ManagedUser>.Fail("username_conflict", "该用户名已存在。");
            var now = Now();
            await ExecuteAsync(connection, transaction,
                "INSERT INTO users(id,username,normalized_username,password_hash,role,enabled,created_at) VALUES($id,$name,$normalized,$hash,'admin',1,$now);",
                cancellationToken, ("$id", id), ("$name", username), ("$normalized", username.ToUpperInvariant()), ("$hash", hash), ("$now", now));
            var user = new ManagedUser(id, username, "admin", true, DateTimeOffset.FromUnixTimeMilliseconds(now), 1);
            await AuditAsync(connection, transaction, auditId, actorId, "user.create", Id(id), new { username, enabled = true }, cancellationToken);
            transaction.Commit();
            return ApiResult<ManagedUser>.Ok(user);
        }
        finally { _writeGate.Release(); }
    }

    public Task<ApiResult<ManagedUser>> UpdateUserAsync(string id, UpdateUserRequest input, long actorId, string token, CancellationToken cancellationToken) =>
        UpdateUserAsync(id, input, OperationActor.Session(actorId, token), cancellationToken);

public async Task<ApiResult<ManagedUser>> UpdateUserAsync(string id, UpdateUserRequest input, OperationActor actor, CancellationToken cancellationToken)
    {
        var actorId = actor.UserId;
        if (!TryPositiveId(id, out var userId)) return ApiResult<ManagedUser>.Fail("user_not_found", "账号不存在。");
        var username = input.Username?.Trim() ?? "";
        if (!TryPositiveId(input.ExpectedVersion, out var version)) return ApiResult<ManagedUser>.Fail("invalid_request", "账号版本无效。");
        var auditId = await NextAsync(cancellationToken);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(false, cancellationToken);
            using var transaction = connection.BeginTransaction();
            if (!await IsCurrentActorAsync(connection, transaction, actor, cancellationToken, "syslog_users_update", "syslog_users_set_enabled"))
                return ApiResult<ManagedUser>.Fail("authentication_required", "管理员会话已失效。");
            var previous = await ReadManagedUserAsync(connection, transaction, userId, cancellationToken);
            if (previous is null) return ApiResult<ManagedUser>.Fail("user_not_found", "账号不存在。");
            // MCP 的名称与启停分开授权，保留字段取自同一写事务，避免覆盖并发变更。
            if (actor.IsMcp && actor.Permission == "syslog_users_set_enabled") username = previous.Username;
            if (actor.IsMcp && actor.Permission == "syslog_users_update") input = input with { Enabled = previous.Enabled };
            // 旧版本允许 Unicode 名称，原名不变时仍可启停；新建及实际改名采用一致的 ASCII 规则。
            if (previous.Username != username && !ValidUsername(username)) return ApiResult<ManagedUser>.Fail("username_invalid", "账号格式无效。");
            if (previous.Version != version) return ApiResult<ManagedUser>.Fail("version_conflict", "账号已被修改，请刷新后重试。");
            if (!input.Enabled && previous.Enabled)
            {
                var enabledCount = Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT count(*) FROM users WHERE enabled=1 AND role='admin';", cancellationToken), CultureInfo.InvariantCulture);
                if (enabledCount <= 1) return ApiResult<ManagedUser>.Fail("last_admin_required", "必须保留至少一个启用的管理员。");
                if (userId == actorId) return ApiResult<ManagedUser>.Fail("self_disable_forbidden", "不能停用当前登录账号。");
            }
            if (await UsernameExistsAsync(connection, transaction, username, userId, cancellationToken))
                return ApiResult<ManagedUser>.Fail("username_conflict", "该用户名已存在。");
            if (previous.Username == username && previous.Enabled == input.Enabled) return ApiResult<ManagedUser>.Ok(previous);
            var saved = previous with { Username = username, Enabled = input.Enabled, Version = checked(previous.Version + 1) };
            await ExecuteAsync(connection, transaction,
                "UPDATE users SET username=$name,normalized_username=$normalized,enabled=$enabled,session_version=$version WHERE id=$id;",
                cancellationToken, ("$id", userId), ("$name", username), ("$normalized", username.ToUpperInvariant()), ("$enabled", input.Enabled ? 1 : 0), ("$version", saved.Version));
            await RevokeUserSessionsAsync(connection, transaction, userId, cancellationToken);
            await AuditAsync(connection, transaction, auditId, actorId, "user.update", Id(userId), new { before = previous, after = saved }, cancellationToken);
            transaction.Commit();
            return ApiResult<ManagedUser>.Ok(saved);
        }
        finally { _writeGate.Release(); }
    }

    public Task<ApiResult<bool>> ResetUserPasswordAsync(string id, ResetUserPasswordRequest input, long actorId, string token, CancellationToken cancellationToken) =>
        ResetUserPasswordAsync(id, input, OperationActor.Session(actorId, token), cancellationToken);

public async Task<ApiResult<bool>> ResetUserPasswordAsync(string id, ResetUserPasswordRequest input, OperationActor actor, CancellationToken cancellationToken)
    {
        var actorId = actor.UserId;
        if (!TryPositiveId(id, out var userId)) return ApiResult<bool>.Fail("user_not_found", "账号不存在。");
        if (!TryPositiveId(input.ExpectedVersion, out var version)) return ApiResult<bool>.Fail("invalid_request", "账号版本无效。");
        if (!ValidPassword(input.NewPassword)) return ApiResult<bool>.Fail("password_policy", "密码须为 8～1024 位且不能全为空白。");
        if (!actor.IsMcp && !ValidToken(actor.SessionToken ?? "")) return ApiResult<bool>.Fail("authentication_required", "管理员会话已失效。");
        var hash = PasswordHasher.Hash(input.NewPassword);
        var auditId = await NextAsync(cancellationToken);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(false, cancellationToken);
            using var transaction = connection.BeginTransaction();
            if (!await IsCurrentActorAsync(connection, transaction, actor, cancellationToken, "syslog_users_reset_password"))
                return ApiResult<bool>.Fail("authentication_required", "管理员会话已失效。");
            if (userId == actorId) return ApiResult<bool>.Fail("self_reset_forbidden", "请通过修改密码验证当前密码后操作。");
            var previous = await ReadManagedUserAsync(connection, transaction, userId, cancellationToken);
            if (previous is null) return ApiResult<bool>.Fail("user_not_found", "账号不存在。");
            if (previous.Version != version) return ApiResult<bool>.Fail("version_conflict", "账号已被修改，请刷新后重试。");
            await ExecuteAsync(connection, transaction,
                "UPDATE users SET password_hash=$hash,session_version=$version WHERE id=$id;", cancellationToken,
                ("$hash", hash), ("$version", checked(previous.Version + 1)), ("$id", userId));
            await RevokeUserSessionsAsync(connection, transaction, userId, cancellationToken);
            await AuditAsync(connection, transaction, auditId, actorId, "user.password_reset", Id(userId), new { }, cancellationToken);
            transaction.Commit();
            return ApiResult<bool>.Ok(true);
        }
        finally { _writeGate.Release(); }
    }

    private async Task<bool> IsCurrentAdminAsync(SqliteConnection connection, SqliteTransaction transaction, long actorId, string token, CancellationToken ct)
    {
        if (!ValidToken(token)) return false;
        var result = await ScalarAsync(connection, transaction,
            "SELECT count(*) FROM sessions s JOIN users u ON u.id=s.user_id WHERE u.id=$id AND u.role='admin' AND u.enabled=1 AND s.token_hash=$hash AND s.revoked=0 AND s.expires_at>$now AND s.user_version=u.session_version;",
            ct, ("$id", actorId), ("$hash", HashToken(token)), ("$now", Now()));
        return Convert.ToInt64(result, CultureInfo.InvariantCulture) == 1;
    }

    private static async Task<bool> UsernameExistsAsync(SqliteConnection connection, SqliteTransaction transaction, string username, long exceptId, CancellationToken ct) =>
        Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT count(*) FROM users WHERE normalized_username=$name AND id<>$id;", ct,
            ("$name", username.ToUpperInvariant()), ("$id", exceptId)), CultureInfo.InvariantCulture) != 0;

    private static async Task<ManagedUser?> ReadManagedUserAsync(SqliteConnection connection, SqliteTransaction transaction, long id, CancellationToken ct)
    {
        await using var command = Command(connection, transaction, "SELECT " + UserColumns + " FROM users WHERE id=$id;", ("$id", id));
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadManagedUser(reader) : null;
    }

    private static Task<int> RevokeUserSessionsAsync(SqliteConnection connection, SqliteTransaction transaction, long id, CancellationToken ct) =>
        ExecuteAsync(connection, transaction, "UPDATE sessions SET revoked=1 WHERE user_id=$id AND revoked=0;", ct, ("$id", id));

    private static ManagedUser ReadManagedUser(SqliteDataReader reader) => new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
        reader.GetBoolean(3), DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(4)), reader.GetInt64(5));
    private static bool TryPositiveId(string? value, out long id) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
    // 全部新密码入口共用规则；沿用 .NET 字符串的 UTF-16 长度，不裁剪用户密码。
    private static bool ValidPassword([NotNullWhen(true)] string? password) => !string.IsNullOrWhiteSpace(password) && password.Length is >= 8 and <= 1024;
}
