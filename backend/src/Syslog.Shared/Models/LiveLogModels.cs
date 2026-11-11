namespace Syslog.Shared.Models;

/// <summary>实时筛选只影响视图，不能改变后台收集范围；时间均为接收时间。</summary>
public sealed record LiveLogFilter
{
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public string? SourceId { get; init; }
    public int? Severity { get; init; }
    public string? Hostname { get; init; }
    public string? AppName { get; init; }
    public string? Keywords { get; init; }
    public string Disposition { get; init; } = "all";
}

public sealed record LiveLogCacheSettings(int PerDeviceLimit = 5000, long MaxBytes = 512L * 1024 * 1024);

public sealed record LiveLogCacheStats(string Epoch, string LatestSequence, long Entries, long Bytes,
    int DeviceCount, long Evicted, long Rejected, int PerDeviceLimit, long MaxBytes, bool Available);

/// <summary>Id/Sequence 只标识本次进程的缓存；忽略日志没有持久事件 ID，其 Log.EventId 为 0。</summary>
public sealed record LiveLogRecord(string Id, string Sequence, string Disposition, LogEvent Log, bool MessageTruncated);

public sealed record LiveLogSnapshot(IReadOnlyList<LiveLogRecord> Items, string Epoch, string Watermark,
    string? NextBeforeSequence, bool HasMore, bool BudgetLimited, LiveLogCacheStats Stats, string? GapReason = null);

public sealed record LiveLogDelta(IReadOnlyList<LiveLogRecord> Items, string Epoch, string Cursor,
    bool HasMore, string? GapReason, bool BudgetLimited, LiveLogCacheStats Stats);
