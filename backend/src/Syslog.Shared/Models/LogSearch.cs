namespace Syslog.Shared.Models;

public sealed record LogSearch
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string? SourceId { get; init; }
    public int? Severity { get; init; }
    public string? Hostname { get; init; }
    public string? AppName { get; init; }
    public string? Keywords { get; init; }
    public int PageSize { get; init; } = 100;
    public string? Cursor { get; init; }
}

/// <summary>无精确总数的游标分页，已授权来源范围必须由服务端传入。</summary>
public sealed record LogSearchPage(IReadOnlyList<LogEvent> Items, string? NextCursor, bool HasMore);
