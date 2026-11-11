using System.Collections.Concurrent;
using System.Net;
using Syslog.Ingest.Common;
using Syslog.Ingest.Options;
using Syslog.Shared.Models;

namespace Syslog.Ingest.Models;

/// <summary>各阶段独立计数，暂存和入库不混为一个成功指标。</summary>
public sealed class IngestStatus
{
    internal const int MaxConnectionErrors = 256;
    internal const int MaxTrackedSources = 100_000;
    private readonly object sourceCountersGate = new();
    private readonly ConcurrentDictionary<string, SourceCounter> sourceCounters = new(StringComparer.Ordinal);
    private readonly object listenerGate = new();
    private readonly Dictionary<ListenerEndpoint, ListenerRuntimeSnapshot> listeners = [];
    private readonly Dictionary<(ListenerEndpoint Endpoint, string Peer), (ListenerConnectionError Error, long Order)> connectionErrors = [];
    private long connectionErrorOrder;
    private long lastReceiveUnixMilliseconds;
    private volatile string failureCode = "";
    public long Received;
    public long ReceivedBytes;
    public long Dropped;
    public long UnregisteredReceived;
    public long Persisted;
    public long Parsed;
    public long ParseFailed;
    public long ParseUnknown;
    public long Ignored;
    public long Written;
    public long WriteFailures;
    public long QueuedBytes;
    public long SpoolBytes;
    public long PendingBatches;
    public long LastWriteUnixMilliseconds;
    internal volatile bool SpoolFull;
    public string State { get; set; } = "disabled";
    public string FailureCode
    {
        get
        {
            // 暂存和写入状态由不同任务更新，暂存恢复时不能覆盖尚未恢复的写入错误。
            var code = failureCode;
            return SpoolFull && State != "failed" && code != "log_clear_outcome_unknown" ? "spool_full" : code;
        }
        set => failureCode = value;
    }
    public string ListenerState { get; set; } = "disabled";
    public string ListenerFailureCode { get; set; } = "";

    public long LastReceiveUnixMilliseconds => Interlocked.Read(ref lastReceiveUnixMilliseconds);

    internal void RecordReceive(int bytes, DateTimeOffset receivedAt, string? sourceId = null)
    {
        Interlocked.Add(ref ReceivedBytes, bytes);
        var timestamp = receivedAt.ToUnixTimeMilliseconds();
        // 并发监听可能先完成较新的报文，较早的报文不能把最近接收时间往回覆盖。
        var previous = LastReceiveUnixMilliseconds;
        while (timestamp > previous)
        {
            var observed = Interlocked.CompareExchange(ref lastReceiveUnixMilliseconds, timestamp, previous);
            if (observed == previous) break;
            previous = observed;
        }
        if (TrackSource(sourceId) is { } counter) Interlocked.Increment(ref counter.Received);
        Interlocked.Increment(ref Received);
    }

    internal void RecordWritten(IReadOnlyList<LogEvent> events, DateTimeOffset writtenAt)
    {
        // 恢复暂存的批次也从每条事件的登记 ID 归属，只有最终确认才累计一次。
        foreach (var item in events)
            if (TrackSource(item.SourceId) is { } counter) Interlocked.Increment(ref counter.Written);
        Interlocked.Exchange(ref LastWriteUnixMilliseconds, writtenAt.ToUnixTimeMilliseconds());
        Interlocked.Add(ref Written, events.Count);
    }

    internal void RecordIgnored(string sourceId)
    {
        if (TrackSource(sourceId) is { } counter) Interlocked.Increment(ref counter.Ignored);
        Interlocked.Increment(ref Ignored);
    }

    public SourceIngestCounters GetSourceCounters(string sourceId) =>
        sourceCounters.TryGetValue(sourceId, out var counter)
            ? new(Interlocked.Read(ref counter.Received), Interlocked.Read(ref counter.Written), Interlocked.Read(ref counter.Ignored))
            : default;

    private SourceCounter? TrackSource(string? sourceId)
    {
        if (string.IsNullOrEmpty(sourceId) || sourceId.Length > 128) return null;
        if (sourceCounters.TryGetValue(sourceId, out var counter)) return counter;
        // 只为登记来源和已暂存事件建项，普通查询与未知来源不能增加计数器数量。
        lock (sourceCountersGate)
        {
            if (sourceCounters.Count >= MaxTrackedSources) return sourceCounters.GetValueOrDefault(sourceId);
            return sourceCounters.GetOrAdd(sourceId, static _ => new SourceCounter());
        }
    }

    private sealed class SourceCounter
    {
        public long Received;
        public long Written;
        public long Ignored;
    }

    public IReadOnlyList<ListenerRuntimeSnapshot> GetListenerSnapshots()
    {
        lock (listenerGate) return listeners.Values.OrderBy(x => x.Protocol, StringComparer.Ordinal).ThenBy(x => x.Address, StringComparer.Ordinal).ThenBy(x => x.Port).ToArray();
    }

    public IReadOnlyList<ListenerConnectionError> GetConnectionErrors()
    {
        (ListenerConnectionError Error, long Order)[] snapshot;
        lock (listenerGate) snapshot = connectionErrors.Values.ToArray();
        return snapshot.OrderByDescending(x => x.Order).Select(x => x.Error).ToArray();
    }

    public (IReadOnlyList<ListenerRuntimeSnapshot> States, IReadOnlyList<ListenerConnectionError> ConnectionErrors) GetListenerRuntimeSnapshot()
    {
        lock (listenerGate)
            return (listeners.Values.ToArray(), connectionErrors.Values.OrderByDescending(x => x.Order).Select(x => x.Error).ToArray());
    }

    internal void ResetProtocol(ListenerOptions listener)
    {
        lock (listenerGate)
        {
            foreach (var key in listeners.Keys.Where(key => key.Protocol == listener.Transport).ToArray()) listeners.Remove(key);
            foreach (var key in connectionErrors.Keys.Where(key => key.Endpoint.Protocol == listener.Transport).ToArray()) connectionErrors.Remove(key);
            var state = listener.Enabled ? "starting" : "disabled";
            listeners[ListenerEndpoint.From(listener)] = new(listener.Transport, listener.Address, listener.Port, state, "", "");
            UpdateListenerSummary();
        }
    }

    internal void ResetListeners(bool enabled, IReadOnlyList<ListenerOptions> configuration)
    {
        lock (listenerGate)
        {
            listeners.Clear();
            connectionErrors.Clear();
            foreach (var listener in configuration)
            {
                var state = enabled && listener.Enabled ? "starting" : "disabled";
                listeners[ListenerEndpoint.From(listener)] =
                    new(listener.Transport, listener.Address, listener.Port, state, "", "");
            }
            ListenerState = listeners.Values.Any(x => x.State == "starting") ? "starting" : "disabled";
            ListenerFailureCode = "";
        }
    }

    public void SetListenerState(string protocol, string address, int port, string state, string failureCode = "", string? failureMessage = null)
    {
        lock (listenerGate)
        {
            var key = new ListenerEndpoint(protocol, address, port);
            listeners[key] = new(protocol, address, port, state, failureCode,
                failureMessage ?? ListenerFailureMessages.Describe(failureCode));
            if (state is "stopped" or "disabled")
                foreach (var entry in connectionErrors.Keys.Where(x => x.Endpoint == key).ToArray())
                    connectionErrors.Remove(entry);
            UpdateListenerSummary();
        }
    }

    internal void RecordConnectionFailure(ListenerOptions listener, IPAddress peer, string code, string message)
    {
        lock (listenerGate)
        {
            var endpoint = ListenerEndpoint.From(listener);
            // 旧连接的迟到异常不能为已停止或故障中的监听器新增记录。
            if (!listeners.TryGetValue(endpoint, out var state) || state.State != "listening") return;
            var peerAddress = FormatPeer(peer);
            var key = (endpoint, peerAddress);
            if (connectionErrors.Count == MaxConnectionErrors && !connectionErrors.ContainsKey(key))
                connectionErrors.Remove(connectionErrors.MinBy(x => x.Value.Order).Key);
            var error = new ListenerConnectionError(endpoint.Protocol, endpoint.Address, endpoint.Port, peerAddress,
                code, message, DateTimeOffset.UtcNow);
            connectionErrors[key] = (error, ++connectionErrorOrder);
        }
    }

    internal void ClearConnectionFailure(ListenerOptions listener, IPAddress peer)
    {
        lock (listenerGate)
            connectionErrors.Remove((ListenerEndpoint.From(listener), FormatPeer(peer)));
    }

    private static string FormatPeer(IPAddress peer) => (peer.IsIPv4MappedToIPv6 ? peer.MapToIPv4() : peer).ToString();

    private readonly record struct ListenerEndpoint(string Protocol, string Address, int Port)
    {
        public static ListenerEndpoint From(ListenerOptions listener) => new(listener.Transport, listener.Address, listener.Port);
    }

    private void UpdateListenerSummary()
    {
        var values = listeners.Values;
        ListenerState = values.Any(x => x.State == "failed") ? "failed"
            : values.Any(x => x.State == "listening") ? "listening"
            : values.Any(x => x.State == "starting") ? "starting"
            : values.Any(x => x.State == "stopped") ? "stopped" : "disabled";
        ListenerFailureCode = values.FirstOrDefault(x => x.FailureCode.Length > 0)?.FailureCode ?? "";
    }

    public object Snapshot() => new
    {
        State, FailureCode, ListenerState, ListenerFailureCode, Received = Interlocked.Read(ref Received),
        ReceivedBytes = Interlocked.Read(ref ReceivedBytes), Dropped = Interlocked.Read(ref Dropped),
        UnregisteredReceived = Interlocked.Read(ref UnregisteredReceived),
        Persisted = Interlocked.Read(ref Persisted), Parsed = Interlocked.Read(ref Parsed),
        ParseFailed = Interlocked.Read(ref ParseFailed), ParseUnknown = Interlocked.Read(ref ParseUnknown), Written = Interlocked.Read(ref Written),
        Ignored = Interlocked.Read(ref Ignored),
        WriteFailures = Interlocked.Read(ref WriteFailures), QueuedBytes = Interlocked.Read(ref QueuedBytes),
        SpoolBytes = Interlocked.Read(ref SpoolBytes), PendingBatches = Interlocked.Read(ref PendingBatches),
        LastReceiveUnixMilliseconds,
        LastWriteUnixMilliseconds = Interlocked.Read(ref LastWriteUnixMilliseconds)
    };
}

/// <summary>单个登记来源在当前进程的累计接收、确认入库和策略忽略计数。</summary>
public readonly record struct SourceIngestCounters(long Received, long Written, long Ignored = 0);

/// <summary>仅公开监听端点及安全诊断，不包含证书路径、密码或底层异常。</summary>
public sealed record ListenerRuntimeSnapshot(string Protocol, string Address, int Port, string State, string FailureCode, string FailureMessage);
