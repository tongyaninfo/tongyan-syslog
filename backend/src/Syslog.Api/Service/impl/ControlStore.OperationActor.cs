using Microsoft.Data.Sqlite;
using Syslog.Api.Models;
using Syslog.Shared.Models;

namespace Syslog.Api.Service.impl;

public sealed partial class ControlStore
{
    public Task<ApiResult<LogSource>> CreateSourceAsync(SourceInput input, OperationActor actor, CancellationToken ct) =>
        SaveSourceAsync(null, input, actor.UserId, ct, actor);

    public Task<ApiResult<LogSource>> PatchSourceAsync(string id, SourcePatch patch, OperationActor actor, CancellationToken ct) =>
        SaveSourceAsync(id, new("", "", "", 1), actor.UserId, ct, actor, patch);

    /// <summary>业务写事务中再次检查实时身份，吊销、过期和账号停用不能被入口快照绕过。</summary>
    private Task<bool> IsCurrentActorAsync(SqliteConnection connection, SqliteTransaction transaction,
        OperationActor actor, CancellationToken ct, params string[] allowedPermissions) => actor.IsMcp
        ? allowedPermissions.Contains(actor.Permission, StringComparer.Ordinal)
            ? IsCurrentMcpActorAsync(connection, transaction, actor.UserId, actor.McpKeyId!.Value, actor.Permission ?? "", ct)
            : Task.FromResult(false)
        : IsCurrentAdminAsync(connection, transaction, actor.UserId, actor.SessionToken ?? "", ct);
}
