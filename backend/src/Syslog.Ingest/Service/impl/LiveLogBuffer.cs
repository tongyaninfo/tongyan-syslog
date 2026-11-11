using System.Globalization;
using System.Text.Json;
using Syslog.Shared.Common;
using Syslog.Shared.Models;
using Syslog.Shared.Service;

namespace Syslog.Ingest.Service.impl;

/// <summary>已登记来源的有界观察缓存。容量淘汰不影响持久流水线，重启后不回填旧暂存批次。</summary>
public sealed class LiveLogBuffer : ILiveLogBuffer
{
    private const int MaximumDevices = 10_000;
    private const int ReadChunkSize = 512;
    private const int MaximumScannedEntries = 8192;
    private const int SummaryLength = 1024;
    private const long EntryOverhead = 1024;
    private const long DeviceOverhead = 768;
    private const int EnvelopeBudget = 2048;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object _gate = new();
    private readonly SortedSet<Entry> _entries = new(Comparer<Entry>.Create((left, right) => left.Sequence.CompareTo(right.Sequence)));
    // 空来源状态也保存淘汰水位；最多一万项，包含对象、字典与字符串的预算。
    private readonly Dictionary<string, Device> _devices = new(StringComparer.Ordinal);
    private LiveLogCacheSettings _settings = new();
    private string _epoch = Guid.NewGuid().ToString("N");
    private long _sequence;
    private long _bytes;
    private long _evicted;
    private long _rejected;
    private bool _available;

    public bool Capture(LogEvent item, bool ignored)
    {
        ArgumentNullException.ThrowIfNull(item);
        lock (_gate)
        {
            if (!_available) return false;
            if (item.SourceId is not { Length: > 0 and <= 128 } || item.RawBytes is null || item.RawBytes.Length > 65_536
                || item.Message is null || item.StructuredData is null || item.Message.Length > 65_536 || item.StructuredData.Length > 65_536)
                return Reject();
            var cost = Cost(item);
            var deviceCost = DeviceOverhead + item.SourceId.Length * 2L;
            if (cost < 0 || cost + deviceCost > _settings.MaxBytes) return Reject();
            if (!_devices.TryGetValue(item.SourceId, out var device))
            {
                // 水位元数据无法无限保留；重置整个缓存世代，旧游标必须显式重新订阅。
                if (_devices.Count >= MaximumDevices) ResetEpoch();
                device = new Device(item.SourceId, deviceCost);
                _devices.Add(item.SourceId, device);
                _bytes += deviceCost;
            }
            while (device.Items.Count >= _settings.PerDeviceLimit) Evict(device.Items.First!.Value);
            while (_entries.Count > 0 && _bytes + cost > _settings.MaxBytes) Evict(_entries.Min!);
            if (_bytes + cost > _settings.MaxBytes) return Reject();
            if (_sequence == long.MaxValue)
            {
                ResetEpoch();
                device = new Device(item.SourceId, deviceCost);
                _devices.Add(item.SourceId, device);
                _bytes = deviceCost;
            }
            // 原始 byte[] 不与调用方或详情响应共享，防止外部修改改变已经缓存的证据。
            var log = item with { RawBytes = item.RawBytes.ToArray(), EventId = ignored ? 0 : item.EventId };
            var entry = new Entry(++_sequence, log, ignored, cost, device);
            entry.Node = device.Items.AddLast(entry);
            _entries.Add(entry);
            _bytes += cost;
            return true;
        }
    }

    public void SetAvailable(bool available)
    {
        lock (_gate) _available = available;
    }

    public void ApplySettings(LiveLogCacheSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!LiveLogFilterValidation.ValidSettings(settings)) throw new ArgumentOutOfRangeException(nameof(settings), "实时缓存配置超出限制。");
        lock (_gate)
        {
            _settings = settings;
            foreach (var device in _devices.Values)
                while (device.Items.Count > settings.PerDeviceLimit) Evict(device.Items.First!.Value);
            while (_entries.Count > 0 && _bytes > settings.MaxBytes) Evict(_entries.Min!);
            // 元数据上限的预算低于最小配置字节数，因此正常缩容不需要丢弃淘汰水位。
        }
    }

    public LiveLogCacheStats GetStats()
    {
        lock (_gate) return Stats();
    }

    public ApiResult<LiveLogSnapshot> ReadSnapshot(LiveLogFilter filter, IReadOnlyCollection<string> allowedSourceIds,
        string? beforeSequence = null, int take = 100, int maxBytes = 512 * 1024)
    {
        var valid = LiveLogFilterValidation.Validate(filter, allowedSourceIds);
        if (!valid.Success) return ApiResult<LiveLogSnapshot>.Fail(valid.Code, valid.Message);
        if (take is < 1 or > 500 || maxBytes is < 4096 or > 512 * 1024
            || beforeSequence is not null && !TrySequence(beforeSequence, out _)) return Invalid<LiveLogSnapshot>();
        var scope = Scope(filter, allowedSourceIds);
        var tokens = LiveLogFilterValidation.Tokens(filter.Keywords);
        LiveLogCacheStats stats;
        long upper;
        long evictedBefore;
        lock (_gate)
        {
            if (!_available) return Unavailable<LiveLogSnapshot>();
            stats = Stats();
            upper = beforeSequence is null ? _sequence : Math.Min(_sequence, ParseSequence(beforeSequence) - 1);
            evictedBefore = EvictionWatermark(scope);
        }
        var items = new List<LiveLogRecord>();
        var remaining = maxBytes - EnvelopeBudget;
        long? lastVisited = null;
        var more = false;
        var budgetLimited = false;
        var scanned = 0;
        while (upper > 0)
        {
            Entry[] chunk;
            lock (_gate)
            {
                if (_epoch != stats.Epoch) return ChangedSnapshot();
                if (!_available) return Unavailable<LiveLogSnapshot>();
                chunk = Chunk(0, upper, descending: true);
            }
            if (chunk.Length == 0) break;
            foreach (var entry in chunk)
            {
                // 强过滤也必须有扫描上限；允许空页携带下一游标继续读取，避免一次遍历全部缓存。
                if (scanned >= MaximumScannedEntries) { more = true; budgetLimited = true; break; }
                scanned++;
                if (Matches(entry, filter, scope, tokens))
                {
                    var item = Summary(entry, stats.Epoch);
                    var cost = JsonSerializer.SerializeToUtf8Bytes(item, Json).Length + 1;
                    if (items.Count >= take || cost > remaining)
                    {
                        if (items.Count == 0 && cost > remaining) return TooLarge<LiveLogSnapshot>();
                        more = true;
                        budgetLimited = cost > remaining;
                        break;
                    }
                    items.Add(item);
                    remaining -= cost;
                }
                lastVisited = entry.Sequence;
            }
            if (more) break;
            upper = chunk[^1].Sequence - 1;
        }
        lock (_gate)
        {
            if (_epoch != stats.Epoch) return ChangedSnapshot();
            if (!_available) return Unavailable<LiveLogSnapshot>();
            var gap = EvictionWatermark(scope) > evictedBefore ? "cache_evicted_during_read" : null;
            return ApiResult<LiveLogSnapshot>.Ok(new(items, stats.Epoch, stats.LatestSequence,
                more && lastVisited is { } last ? Number(last) : null, more, budgetLimited, stats, gap));
        }
    }

    public ApiResult<LiveLogDelta> ReadAfter(LiveLogFilter filter, IReadOnlyCollection<string> allowedSourceIds,
        string epoch, string afterSequence, int take = 200, int maxBytes = 512 * 1024)
    {
        var valid = LiveLogFilterValidation.Validate(filter, allowedSourceIds);
        if (!valid.Success) return ApiResult<LiveLogDelta>.Fail(valid.Code, valid.Message);
        if (!TrySequence(afterSequence, out var after) || epoch is not { Length: 32 } || take is < 1 or > 200
            || maxBytes is < 4096 or > 512 * 1024) return Invalid<LiveLogDelta>();
        var scope = Scope(filter, allowedSourceIds);
        var tokens = LiveLogFilterValidation.Tokens(filter.Keywords);
        LiveLogCacheStats stats;
        long upper;
        lock (_gate)
        {
            if (!_available) return Unavailable<LiveLogDelta>();
            if (_epoch != epoch) return Gap("epoch_changed");
            if (after > _sequence) return Invalid<LiveLogDelta>();
            if (EvictionWatermark(scope) > after) return Gap("cache_evicted");
            stats = Stats();
            upper = _sequence;
        }
        var items = new List<LiveLogRecord>();
        var cursor = after;
        var remaining = maxBytes - EnvelopeBudget;
        var more = false;
        var budgetLimited = false;
        var scanned = 0;
        while (cursor < upper)
        {
            Entry[] chunk;
            lock (_gate)
            {
                if (_epoch != epoch) return Gap("epoch_changed");
                if (!_available) return Unavailable<LiveLogDelta>();
                if (EvictionWatermark(scope) > cursor) return Gap("cache_evicted");
                chunk = Chunk(cursor + 1, upper, descending: false);
            }
            if (chunk.Length == 0) { cursor = upper; break; }
            foreach (var entry in chunk)
            {
                if (scanned >= MaximumScannedEntries) { more = true; budgetLimited = true; break; }
                scanned++;
                if (Matches(entry, filter, scope, tokens))
                {
                    var item = Summary(entry, epoch);
                    var cost = JsonSerializer.SerializeToUtf8Bytes(item, Json).Length + 1;
                    if (items.Count >= take || cost > remaining)
                    {
                        if (items.Count == 0 && cost > remaining) return TooLarge<LiveLogDelta>();
                        more = true;
                        budgetLimited = cost > remaining;
                        break;
                    }
                    items.Add(item);
                    remaining -= cost;
                }
                cursor = entry.Sequence;
            }
            if (more) break;
        }
        lock (_gate)
        {
            if (_epoch != epoch) return Gap("epoch_changed");
            if (!_available) return Unavailable<LiveLogDelta>();
            if (EvictionWatermark(scope) > cursor) return Gap("cache_evicted");
            return ApiResult<LiveLogDelta>.Ok(new(items, epoch, Number(cursor), more, null, budgetLimited, stats));
        }
    }

    public ApiResult<LiveLogRecord> GetDetail(string id, IReadOnlyCollection<string> allowedSourceIds)
    {
        if (!LiveLogFilterValidation.ValidScope(allowedSourceIds) || id is not { Length: >= 34 and <= 53 } || id[32] != ':'
            || !TrySequence(id[33..], out var sequence)) return Invalid<LiveLogRecord>();
        lock (_gate)
        {
            if (!_available) return Unavailable<LiveLogRecord>();
            if (id[..32] != _epoch || !_entries.TryGetValue(Entry.Bound(sequence), out var entry)
                || !allowedSourceIds.Contains(entry.Log.SourceId, StringComparer.Ordinal))
                return ApiResult<LiveLogRecord>.Fail("live_log_expired", "该日志已不在实时缓存中，请重新读取列表。");
            return ApiResult<LiveLogRecord>.Ok(new(id, Number(sequence), entry.Ignored ? "ignored" : "normal",
                entry.Log with { RawBytes = entry.Log.RawBytes.ToArray() }, false));
        }
    }

    private Entry[] Chunk(long minimum, long maximum, bool descending)
    {
        if (minimum > maximum || _entries.Count == 0) return [];
        var view = _entries.GetViewBetween(Entry.Bound(minimum), Entry.Bound(maximum));
        return (descending ? view.Reverse() : view).Take(ReadChunkSize).ToArray();
    }

    private static HashSet<string> Scope(LiveLogFilter filter, IReadOnlyCollection<string> allowed) =>
        new(filter.SourceId is null ? allowed : [filter.SourceId], StringComparer.Ordinal);

    private static bool Matches(Entry entry, LiveLogFilter filter, HashSet<string> scope, string[] tokens) =>
        scope.Contains(entry.Log.SourceId)
        && (filter.From is null || entry.Log.ReceivedAt >= filter.From)
        && (filter.To is null || entry.Log.ReceivedAt < filter.To)
        && (filter.Severity is null || entry.Log.Severity == filter.Severity)
        && (filter.Hostname is null || entry.Log.Hostname == filter.Hostname)
        && (filter.AppName is null || entry.Log.AppName == filter.AppName)
        && (filter.Disposition == "all" || (filter.Disposition == "ignored") == entry.Ignored)
        && LiveLogFilterValidation.MatchesKeywords(entry.Log.Message, tokens);

    private static LiveLogRecord Summary(Entry entry, string epoch)
    {
        var length = Math.Min(entry.Log.Message.Length, SummaryLength);
        if (length > 0 && length < entry.Log.Message.Length && char.IsHighSurrogate(entry.Log.Message[length - 1])) length--;
        var truncated = length != entry.Log.Message.Length;
        return new(epoch + ":" + Number(entry.Sequence), Number(entry.Sequence), entry.Ignored ? "ignored" : "normal",
            entry.Log with
            {
                // 详情证据按需获取；每个浏览器可能保留五千条摘要，不能随摘要重复传送大字段。
                RawBytes = [], StructuredData = "", OriginalTimestamp = "", ProcId = "", MsgId = "", BatchId = "",
                Hostname = HeaderSummary(entry.Log.Hostname), AppName = HeaderSummary(entry.Log.AppName),
                Message = truncated ? entry.Log.Message[..length] : entry.Log.Message,
            }, truncated);
    }

    private static string HeaderSummary(string value)
    {
        if (value.Length <= 255) return value;
        var length = 254;
        if (char.IsHighSurrogate(value[length - 1])) length--;
        return value[..length] + "…";
    }

    private long EvictionWatermark(HashSet<string> scope)
    {
        long result = 0;
        foreach (var source in scope)
            if (_devices.TryGetValue(source, out var device)) result = Math.Max(result, device.EvictedThrough);
        return result;
    }

    private void Evict(Entry entry)
    {
        _entries.Remove(entry);
        entry.Device.Items.Remove(entry.Node!);
        entry.Device.EvictedThrough = Math.Max(entry.Device.EvictedThrough, entry.Sequence);
        _bytes -= entry.Cost;
        _evicted++;
    }

    private void ResetEpoch()
    {
        _evicted += _entries.Count;
        _entries.Clear();
        _devices.Clear();
        _bytes = 0;
        _sequence = 0;
        _epoch = Guid.NewGuid().ToString("N");
    }

    private bool Reject() { _rejected++; return false; }
    private LiveLogCacheStats Stats() => new(_epoch, Number(_sequence), _entries.Count, _bytes,
        _devices.Values.Count(device => device.Items.Count > 0), _evicted, _rejected, _settings.PerDeviceLimit, _settings.MaxBytes, _available);
    private ApiResult<LiveLogDelta> Gap(string reason)
    {
        var stats = Stats();
        return ApiResult<LiveLogDelta>.Ok(new([], stats.Epoch, stats.LatestSequence, false, reason, false, stats));
    }
    private ApiResult<LiveLogSnapshot> ChangedSnapshot()
    {
        var stats = Stats();
        // 读取途中换代时未返回新世代内容；游标必须归零，后续增量才能补回新世代已有日志。
        return ApiResult<LiveLogSnapshot>.Ok(new([], stats.Epoch, "0", null, false, false, stats, "epoch_changed"));
    }
    private static long Cost(LogEvent item)
    {
        var fields = new[] { item.SourceId, item.PeerAddress, item.Transport, item.BatchId, item.OriginalTimestamp, item.TimeConfidence,
            item.Hostname, item.AppName, item.ProcId, item.MsgId, item.StructuredData, item.Message, item.ParseStatus, item.FailureCode, item.ParserVersion };
        if (fields.Any(field => field is null || field.Length > 65_536)) return -1;
        return EntryOverhead + item.RawBytes.Length + fields.Sum(field => field.Length * 2L);
    }
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static long ParseSequence(string value) => long.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture);
    private static bool TrySequence(string value, out long sequence)
    {
        sequence = 0;
        return value is { Length: > 0 and <= 19 } && (value.Length == 1 || value[0] != '0')
            && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out sequence) && sequence >= 0;
    }
    private static ApiResult<T> Invalid<T>() => ApiResult<T>.Fail("invalid_query", "实时日志参数、游标或读取预算无效。");
    private static ApiResult<T> Unavailable<T>() => ApiResult<T>.Fail("live_log_unavailable", "实时日志缓存尚未就绪，请检查配置状态。");
    private static ApiResult<T> TooLarge<T>() => ApiResult<T>.Fail("live_log_response_too_large", "单条摘要超过本次读取预算，请使用更大的读取预算。");

    private sealed class Device(string sourceId, long cost)
    {
        public string SourceId { get; } = sourceId;
        public long Cost { get; } = cost;
        public LinkedList<Entry> Items { get; } = [];
        public long EvictedThrough;
    }
    private sealed class Entry(long sequence, LogEvent log, bool ignored, long cost, Device device)
    {
        public long Sequence { get; } = sequence;
        public LogEvent Log { get; } = log;
        public bool Ignored { get; } = ignored;
        public long Cost { get; } = cost;
        public Device Device { get; } = device;
        public LinkedListNode<Entry>? Node;
        public static Entry Bound(long sequence) => new(sequence, null!, false, 0, null!);
    }
}
