using Microsoft.AspNetCore.SignalR;
using Syslog.Api.Hubs;
using Syslog.Api.Models;
using Syslog.Api.Service;
using Syslog.Ingest.Models;
using Syslog.Shared.Models;
using Syslog.Shared.Service;

namespace Syslog.Api.Service.impl;

/// <summary>一个宿主共用采样，连接和发送均有界。每次发送前重新验证当前登录会话。</summary>
public sealed class OverviewFeed(IControlStore control, ISourceRegistry sources, IngestStatus status,
    IHubContext<OverviewHub> hub, TimeProvider clock, OverviewDatabaseSize databaseSize, IListenerConfigurationService listeners)
{
    public const int MaximumConnections = 64;
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(2);
    private readonly object _connectionsGate = new();
    private readonly Dictionary<string, Subscription> _connections = [];
    // 初始快照与周期推送使用同一闸门，避免旧快照晚于新快照送达。
    private readonly SemaphoreSlim _publishGate = new(1, 1);
    private Task<IReadOnlyList<LogSource>>? _pendingSourceRead;

    public async Task<bool> ConnectAsync(string connectionId, long userId, string token, string? sourceId, bool listenerView, Action abort, CancellationToken ct)
    {
        var subscription = new Subscription(connectionId, userId, token, sourceId, listenerView, abort);
        lock (_connectionsGate)
        {
            if (_connections.Count >= MaximumConnections || !_connections.TryAdd(connectionId, subscription)) return false;
        }
        try
        {
            await _publishGate.WaitAsync(ct);
            try
            {
                if (!await AuthorizeAsync(subscription, ct)) return false;
                if (listenerView) await PublishListenersAsync([subscription], ct);
                else await PublishOverviewAsync([subscription], ct);
                return IsConnected(subscription);
            }
            finally { _publishGate.Release(); }
        }
        catch
        {
            DisconnectAndAbort(subscription);
            return false;
        }
    }

    public void Disconnect(string connectionId)
    {
        lock (_connectionsGate) _connections.Remove(connectionId);
    }

    /// <summary>供已鉴权的 MCP 调用复用首页快照；与推送共享采样闸门，避免悬挂读取累积。</summary>
    public async Task<OverviewSnapshot> ReadSnapshotAsync(string? sourceId, CancellationToken ct)
    {
        await _publishGate.WaitAsync(ct);
        try { return (await SnapshotAsync(sourceId is null ? [] : [sourceId], ct)).ForSource(sourceId); }
        finally { _publishGate.Release(); }
    }

    public async Task PublishAsync(CancellationToken ct)
    {
        if (SnapshotConnections().Length == 0) return;
        await _publishGate.WaitAsync(ct);
        try
        {
            var subscriptions = SnapshotConnections();
            if (subscriptions.Length == 0) return;
            await Task.WhenAll(
                PublishListenersAsync(subscriptions.Where(item => item.ListenerView).ToArray(), ct),
                PublishOverviewAsync(subscriptions.Where(item => !item.ListenerView).ToArray(), ct));
        }
        finally { _publishGate.Release(); }
    }

    private async Task PublishOverviewAsync(Subscription[] subscriptions, CancellationToken ct)
    {
        if (subscriptions.Length == 0) return;
        // 看板连接共享一份来源快照，发送前复核会话在采样期间是否撤销。
        var snapshot = await SnapshotAsync(subscriptions.Select(item => item.SourceId).OfType<string>(), ct);
        await Task.WhenAll(subscriptions.Select(async subscription =>
        {
            if (await AuthorizeAsync(subscription, ct))
                await SendAsync(subscription, "OverviewUpdated", [snapshot.ForSource(subscription.SourceId)], ct);
        }));
    }

    private async Task PublishListenersAsync(Subscription[] subscriptions, CancellationToken ct)
    {
        if (subscriptions.Length == 0) return;
        var snapshot = new ListenerRuntimeUpdate(listeners.GetRuntimeSnapshot(), clock.GetUtcNow());
        await Task.WhenAll(subscriptions.Select(async subscription =>
        {
            if (await AuthorizeAsync(subscription, ct))
                await SendAsync(subscription, "ListenerRuntimeUpdated", [snapshot], ct);
        }));
    }

    private async Task<Publication> SnapshotAsync(IEnumerable<string> sourceIds, CancellationToken ct)
    {
        OverviewSourceCount? count = null;
        string? error = null;
        var requested = new HashSet<string>(sourceIds, StringComparer.Ordinal);
        var traffic = new Dictionary<string, OverviewSourceTraffic>(StringComparer.Ordinal);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(OperationTimeout);
        try
        {
            // 实现若忽略取消，也只允许一个未完成读取，不能随推送周期积累悬挂任务。
            if (_pendingSourceRead is { IsCompleted: false }) throw new TimeoutException();
            if (_pendingSourceRead is { IsFaulted: true }) _ = _pendingSourceRead.Exception;
            _pendingSourceRead = sources.ListAsync(timeout.Token);
            var items = await _pendingSourceRead.WaitAsync(timeout.Token);
            var enabled = 0;
            foreach (var item in items)
            {
                if (item.Enabled) enabled++;
                if (!requested.Contains(item.Id)) continue;
                var counters = status.GetSourceCounters(item.Id);
                traffic[item.Id] = new(item.Id, counters.Received, counters.Written, counters.Ignored);
            }
            count = new(items.Count, enabled);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { error = "service_unavailable"; }
        var shared = new OverviewSnapshot(status.Snapshot(), count, error, clock.GetUtcNow(), databaseSize.GetCachedBytes());
        return new(shared, traffic);
    }

    private async Task<bool> AuthorizeAsync(Subscription subscription, CancellationToken ct)
    {
        if (!IsConnected(subscription)) return false;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(OperationTimeout);
        try
        {
            var user = await control.ValidateSessionAsync(subscription.Token, timeout.Token).WaitAsync(timeout.Token);
            if (user is not null && user.Id == subscription.UserId && user.Role == "admin") return IsConnected(subscription);
            await SendAsync(subscription, "SessionExpired", [], ct);
        }
        catch { /* 控制库异常时同样停止推送，不泄露异常或误把未知会话当作有效。 */ }
        DisconnectAndAbort(subscription);
        return false;
    }

    private async Task SendAsync(Subscription subscription, string method, object?[] arguments, CancellationToken ct)
    {
        if (!IsConnected(subscription)) return;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(OperationTimeout);
        try
        {
            await hub.Clients.Client(subscription.ConnectionId).SendCoreAsync(method, arguments, timeout.Token).WaitAsync(timeout.Token);
        }
        catch { DisconnectAndAbort(subscription); }
    }

    private Subscription[] SnapshotConnections()
    {
        lock (_connectionsGate) return _connections.Values.ToArray();
    }

    private bool IsConnected(Subscription subscription)
    {
        lock (_connectionsGate) return _connections.TryGetValue(subscription.ConnectionId, out var existing) && ReferenceEquals(existing, subscription);
    }

    private void DisconnectAndAbort(Subscription subscription)
    {
        var removed = false;
        lock (_connectionsGate)
        {
            if (_connections.TryGetValue(subscription.ConnectionId, out var current) && ReferenceEquals(current, subscription))
                removed = _connections.Remove(subscription.ConnectionId);
        }
        if (removed) subscription.Abort();
    }

    // 登录 token 仅在活跃连接内存中保留；断开即移除，不进入消息、URL、日志或审计。
    private sealed record Subscription(string ConnectionId, long UserId, string Token, string? SourceId, bool ListenerView, Action Abort);

    private sealed record Publication(OverviewSnapshot Shared, Dictionary<string, OverviewSourceTraffic> Traffic)
    {
        public OverviewSnapshot ForSource(string? sourceId) =>
            sourceId is not null && Traffic.TryGetValue(sourceId, out var metrics) ? Shared with { SourceTraffic = metrics } : Shared;
    }
}
