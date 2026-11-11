namespace Syslog.Api.Models;

/// <summary>看板完整快照。来源不可读时明确返回未知，不把存储故障伪装成零来源。</summary>
public sealed record OverviewSnapshot(object Status, OverviewSourceCount? Sources, string? SourceError,
    DateTimeOffset ObservedAt, long? DatabaseSizeBytes, OverviewSourceTraffic? SourceTraffic = null);
public sealed record OverviewSourceCount(int Total, int Enabled);
public sealed record OverviewSourceTraffic(string SourceId, long Received, long Written, long Ignored);
public sealed record ListenerRuntimeUpdate(ListenerRuntimeView Runtime, DateTimeOffset ObservedAt);
