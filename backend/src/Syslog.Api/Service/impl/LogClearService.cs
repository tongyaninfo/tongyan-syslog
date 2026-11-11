using Syslog.Api.Models;
using System.Globalization;
using Microsoft.Extensions.Options;
using Syslog.Ingest.Options;
using Syslog.Ingest.Service.impl;
using Syslog.Shared.Models;
using Syslog.Shared.Service;

namespace Syslog.Api.Service.impl;

/// <summary>将有审计的日志清空命令串行提交到配置的日志存储。</summary>
public sealed class LogClearService(IControlStore control, IServiceScopeFactory scopes, IOptions<ClickHouseOptions> options)
{
    private const string UnknownCode = "log_clear_outcome_unknown";
    private readonly SemaphoreSlim _clearGate = new(1, 1);

    public Task<ApiResult<bool>> ClearAsync(string? confirmation, long actorId, string token, CancellationToken cancellationToken) =>
        ClearAsync(confirmation, OperationActor.Session(actorId, token), null, cancellationToken);

    public Task<ApiResult<bool>> ClearSourceAsync(string sourceId, string? confirmation, long actorId, string token,
        CancellationToken cancellationToken) =>
        long.TryParse(sourceId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
            ? ClearAsync(confirmation, OperationActor.Session(actorId, token), id, cancellationToken)
            : Task.FromResult(ApiResult<bool>.Fail("source_not_found", "日志来源不存在。"));

    public Task<ApiResult<bool>> ClearAsync(string? confirmation, OperationActor actor, CancellationToken ct) =>
        ClearAsync(confirmation, actor, null, ct);

    public Task<ApiResult<bool>> ClearSourceAsync(string sourceId, string? confirmation, OperationActor actor, CancellationToken ct) =>
        long.TryParse(sourceId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
            ? ClearAsync(confirmation, actor, id, ct)
            : Task.FromResult(ApiResult<bool>.Fail("source_not_found", "日志来源不存在。"));

    private async Task<ApiResult<bool>> ClearAsync(string? confirmation, OperationActor actor,
        long? sourceId, CancellationToken cancellationToken)
    {
        var actorId = actor.UserId;
        if (confirmation != "CLEAR")
            return ApiResult<bool>.Fail("log_clear_confirmation_required", sourceId is null
                ? "请输入 CLEAR 确认清空全部已入库日志。"
                : "请输入 CLEAR 确认清空该来源的已入库日志。");
        if (!await _clearGate.WaitAsync(0, cancellationToken))
            return ApiResult<bool>.Fail("log_clear_in_progress", "已有清空操作正在执行，请等待结果。");
        try
        {
            using var scope = scopes.CreateScope();
            var logs = scope.ServiceProvider.GetRequiredService<ILogStore>();
            var audit = await control.BeginLogClearAsync(actor, options.Value.Database, cancellationToken, sourceId);
            if (!audit.Success) return ApiResult<bool>.Fail(audit.Code, audit.Message);

            // 意图一旦提交，浏览器断开不能中断结果审计。存储适配器有自己的超时且不会重试。
            ApiResult<bool> result;
            try
            {
                if (sourceId is { } target)
                    await logs.ClearSourceAsync(target.ToString(CultureInfo.InvariantCulture), CancellationToken.None);
                else
                    await logs.ClearAsync(CancellationToken.None);
                result = ApiResult<bool>.Ok(true);
            }
            catch (ClickHouseStoreException error)
            {
                result = ApiResult<bool>.Fail(error.Code, error.Message);
            }
            catch (Exception)
            {
                result = ApiResult<bool>.Fail(UnknownCode, "清空结果未能确认，请先查询日志核对。");
            }

            try
            {
                var outcome = result.Success ? "succeeded" : result.Code == UnknownCode ? "unknown" : "failed";
                await control.RecordLogClearResultAsync(audit.Data, actorId, outcome, result.Code, CancellationToken.None, sourceId);
            }
            catch (Exception)
            {
                // ClickHouse 与控制库独立。结果无法持久记录时只保留已提交的意图，不返回成功。
                return ApiResult<bool>.Fail(UnknownCode, "清空结果未能记录到审计，请先查询日志核对。");
            }
            return result;
        }
        finally { _clearGate.Release(); }
    }
}
