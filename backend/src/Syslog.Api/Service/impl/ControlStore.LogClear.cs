using Syslog.Api.Models;
using Syslog.Shared.Models;

namespace Syslog.Api.Service.impl;

public sealed partial class ControlStore
{
    public Task<ApiResult<long>> BeginLogClearAsync(long actorId, string token, string database,
        CancellationToken cancellationToken, long? sourceId = null) =>
        BeginLogClearAsync(OperationActor.Session(actorId, token), database, cancellationToken, sourceId);

public async Task<ApiResult<long>> BeginLogClearAsync(OperationActor actor, string database,
        CancellationToken cancellationToken, long? sourceId = null)
    {
        var actorId = actor.UserId;
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(false, cancellationToken);
            using var transaction = connection.BeginTransaction();
            // 会话复核和清空意图同事务提交，审计不可用时不能触发 ClickHouse 清空。
            if (!await IsCurrentActorAsync(connection, transaction, actor, cancellationToken, sourceId is null ? "syslog_logs_clear_all" : "syslog_logs_clear_source"))
                return ApiResult<long>.Fail("authentication_required", "管理员会话已失效。");
            if (sourceId is { } id && await ScalarAsync(connection, transaction,
                "SELECT id FROM sources WHERE id=$id;", cancellationToken, ("$id", id)) is null)
                return ApiResult<long>.Fail("source_not_found", "日志来源不存在。");
            var operationId = await ReserveIdBlockAsync(connection, transaction, 1, cancellationToken);
            if (sourceId is { } target)
                await AuditAsync(connection, transaction, operationId, actorId, "log.clear.request", Id(target),
                    new { database, scope = "source", sourceId = Id(target) }, cancellationToken);
            else
                await AuditAsync(connection, transaction, operationId, actorId, "log.clear.request", "events",
                    new { database, scope = "all" }, cancellationToken);
            transaction.Commit();
            return ApiResult<long>.Ok(operationId);
        }
        finally { _writeGate.Release(); }
    }

    public async Task RecordLogClearResultAsync(long operationId, long actorId, string outcome, string code,
        CancellationToken cancellationToken, long? sourceId = null)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(false, cancellationToken);
            using var transaction = connection.BeginTransaction();
            // 操作期间会话可能失效，仍按原操作人记录已提交意图的执行结果。
            var auditId = await ReserveIdBlockAsync(connection, transaction, 1, cancellationToken);
            if (sourceId is { } target)
                await AuditAsync(connection, transaction, auditId, actorId, "log.clear.result", Id(target),
                    new { operationId = Id(operationId), outcome, code, scope = "source", sourceId = Id(target) }, cancellationToken);
            else
                await AuditAsync(connection, transaction, auditId, actorId, "log.clear.result", "events",
                    new { operationId = Id(operationId), outcome, code }, cancellationToken);
            transaction.Commit();
        }
        finally { _writeGate.Release(); }
    }
}
