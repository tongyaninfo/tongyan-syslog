using Microsoft.AspNetCore.SignalR;
using Syslog.Api.Hubs;
using Syslog.Api.Models;
using Syslog.Ingest.Models;
using Syslog.Ingest.Service.impl;
using Syslog.Shared.Common;
using Syslog.Shared.Models;
using Syslog.Shared.Service;

namespace Syslog.Api.Service.impl;

/// <summary>独立、有界的实时日志订阅；慢浏览器不会阻塞接收链或首页指标推送。</summary>
public sealed class LiveLogFeed(IControlStore control, ISourceRegistry sources, ILiveLogBuffer buffer,
    IHubContext<LiveLogsHub> hub, IngressQueue queue, IngestStatus ingest, TimeProvider clock, LiveLogConfigurationService configuration)
{
    public const int MaximumConnections = 32;
    private readonly object connectionsGate = new();
    private readonly Dictionary<string, Connection> connections = [];
    private readonly SemaphoreSlim scopeGate = new(1, 1);
    private string[]? scope;
    private DateTimeOffset scopeReadAt;
    private Task<IReadOnlyList<LogSource>>? pendingScope;

    public bool Connect(string id, long userId, string token, Action abort)
    {
        lock (connectionsGate)
            return connections.Count < MaximumConnections && connections.TryAdd(id, new(id, userId, token, abort));
    }

    public void Disconnect(string id) { lock (connectionsGate) connections.Remove(id); }

    public async Task<ApiResult<bool>> SubscribeAsync(string id, LiveLogSubscribeRequest request, CancellationToken ct)
    {
        Connection? connection;
        lock (connectionsGate) connections.TryGetValue(id, out connection);
        if (connection is null) return ApiResult<bool>.Fail("authentication_required", "登录已失效。");
        await connection.Gate.WaitAsync(ct);
        try
        {
            connection.Request = null;
            if (request is null || string.IsNullOrWhiteSpace(request.SubscriptionId) || request.SubscriptionId.Length > 64 || request.Filter is null
                || request.Epoch?.Length > 64 || request.AfterSequence?.Length > 32 || (request.Epoch is null) != (request.AfterSequence is null))
                return ApiResult<bool>.Fail("invalid_query", "实时订阅参数无效。");
            var allowed = await ScopeAsync(ct);
            var validation = LiveLogFilterValidation.Validate(request.Filter, allowed);
            if (!validation.Success) return ApiResult<bool>.Fail(validation.Code, validation.Message);
            if (!await AuthorizeAsync(connection, ct)) return ApiResult<bool>.Fail("authentication_required", "登录已失效。");
            // 首次管理员配置可能发生在宿主启动后，首次订阅重读配置即可启用，无需重启。
            if (!buffer.GetStats().Available)
            {
                var configured = await configuration.ReadAsync(ct);
                if (!configured.Success) return ApiResult<bool>.Fail(configured.Code, configured.Message);
            }
            connection.Request = request;
            connection.Epoch = request.Epoch ?? "";
            connection.Cursor = request.AfterSequence ?? "0";
            var result = await PublishOneAsync(connection, allowed, initial: true, ct);
            if (!result.Success) connection.Request = null;
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            connection.Request = null;
            Abort(connection);
            return ApiResult<bool>.Fail("service_unavailable", "实时日志服务暂不可用。");
        }
        finally { connection.Gate.Release(); }
    }

    public async Task PublishAsync(CancellationToken ct)
    {
        Connection[] items;
        lock (connectionsGate) items = connections.Values.ToArray();
        if (items.Length == 0) return;
        string[] allowed;
        try { allowed = await ScopeAsync(ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { foreach (var item in items) Abort(item); return; }
        await Task.WhenAll(items.Select(async item =>
        {
            // Subscribe 正在更新条件时跳过本周期，禁止为慢连接累积待发送任务。
            if (!await item.Gate.WaitAsync(0, ct)) return;
            try
            {
                if (item.Request is null || !Connected(item)) return;
                var result = await PublishOneAsync(item, allowed, initial: false, ct);
                if (!result.Success) Abort(item);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch { Abort(item); }
            finally { item.Gate.Release(); }
        }));
    }

    private async Task<ApiResult<bool>> PublishOneAsync(Connection connection, string[] allowed, bool initial, CancellationToken ct)
    {
        var request = connection.Request!;
        LiveLogUpdate update;
        var now = clock.GetUtcNow();
        string? reset = null;
        var fresh = initial && request.Epoch is null;
        if (!fresh)
        {
            var result = buffer.ReadAfter(request.Filter, allowed, connection.Epoch, connection.Cursor);
            if (!result.Success || result.Data is null) return ApiResult<bool>.Fail(result.Code, result.Message);
            var delta = result.Data;
            reset = delta.GapReason;
            if (reset is null)
            {
                if (delta.Items.Count == 0 && !initial && now - connection.LastSent < TimeSpan.FromSeconds(5))
                {
                    // 未匹配的条目也要推进游标，否则窄筛选会不断扫描同一段缓存。
                    connection.Cursor = delta.Cursor;
                    return ApiResult<bool>.Ok(true);
                }
                update = new(request.SubscriptionId, delta.Items.Count == 0 ? "heartbeat" : "append", delta.Epoch,
                    delta.Cursor, delta.Items, delta.HasMore, null, null, delta.BudgetLimited, delta.Stats, now, IngestView());
                return await SendUpdateAsync(connection, update, ct);
            }
        }
        var snapshotResult = buffer.ReadSnapshot(request.Filter, allowed, take: 500);
        // 分块读取期间世代切换时，不能把空快照的新水位当成已读；有界重取一次。
        if (snapshotResult.Data?.GapReason == "epoch_changed")
        {
            reset ??= "epoch_changed";
            snapshotResult = buffer.ReadSnapshot(request.Filter, allowed, take: 500);
        }
        if (!snapshotResult.Success || snapshotResult.Data is null)
            return ApiResult<bool>.Fail(snapshotResult.Code, snapshotResult.Message);
        var snapshot = snapshotResult.Data;
        update = new(request.SubscriptionId, "snapshot", snapshot.Epoch, snapshot.Watermark, snapshot.Items,
            snapshot.HasMore, snapshot.NextBeforeSequence, reset ?? snapshot.GapReason, snapshot.BudgetLimited, snapshot.Stats, now, IngestView());
        return await SendUpdateAsync(connection, update, ct);
    }

    private LiveLogIngestView IngestView() => new(queue.Count, Interlocked.Read(ref ingest.Dropped),
        ingest.FailureCode == "spool_full", ingest.State);

    private async Task<ApiResult<bool>> SendUpdateAsync(Connection connection, LiveLogUpdate update, CancellationToken ct)
    {
        if (!await AuthorizeAsync(connection, ct)) return ApiResult<bool>.Fail("authentication_required", "登录已失效。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        await hub.Clients.Client(connection.Id).SendAsync("LiveLogsUpdated", update, timeout.Token).WaitAsync(timeout.Token);
        connection.Epoch = update.Epoch;
        connection.Cursor = update.Cursor;
        connection.LastSent = update.ObservedAt;
        return ApiResult<bool>.Ok(true);
    }

    private async Task<bool> AuthorizeAsync(Connection connection, CancellationToken ct)
    {
        if (!Connected(connection)) return false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        var user = await control.ValidateSessionAsync(connection.Token, timeout.Token).WaitAsync(timeout.Token);
        if (user is { Role: "admin" } && user.Id == connection.UserId) return Connected(connection);
        try { await hub.Clients.Client(connection.Id).SendAsync("SessionExpired", timeout.Token).WaitAsync(timeout.Token); }
        finally { Abort(connection); }
        return false;
    }

    private async Task<string[]> ScopeAsync(CancellationToken ct)
    {
        await scopeGate.WaitAsync(ct);
        try
        {
            if (scope is not null && clock.GetUtcNow() - scopeReadAt < TimeSpan.FromMilliseconds(500)) return scope;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            // 实现即使不响应取消，也只能存在一个未完成的来源查询。
            if (pendingScope is { IsCompleted: false }) throw new TimeoutException();
            if (pendingScope is { IsFaulted: true }) _ = pendingScope.Exception;
            pendingScope = sources.ListAsync(timeout.Token);
            scope = (await pendingScope.WaitAsync(timeout.Token)).Select(item => item.Id).ToArray();
            scopeReadAt = clock.GetUtcNow();
            return scope;
        }
        finally { scopeGate.Release(); }
    }

    private bool Connected(Connection item)
    {
        lock (connectionsGate) return connections.TryGetValue(item.Id, out var active) && ReferenceEquals(item, active);
    }
    private void Abort(Connection item)
    {
        var removed = false;
        lock (connectionsGate)
            if (connections.TryGetValue(item.Id, out var active) && ReferenceEquals(item, active)) removed = connections.Remove(item.Id);
        if (removed) item.Abort();
    }

    // 会话凭据只驻留于活动连接内存，不进入消息、审计或 URL。
    private sealed class Connection(string id, long userId, string token, Action abort)
    {
        public string Id { get; } = id;
        public long UserId { get; } = userId;
        public string Token { get; } = token;
        public Action Abort { get; } = abort;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public LiveLogSubscribeRequest? Request { get; set; }
        public string Epoch { get; set; } = "";
        public string Cursor { get; set; } = "0";
        public DateTimeOffset LastSent { get; set; }
    }
}
