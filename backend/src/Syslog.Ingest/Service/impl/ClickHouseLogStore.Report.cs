using System.Text.Json;
using Syslog.Shared.Models;

namespace Syslog.Ingest.Service.impl;

public sealed partial class ClickHouseLogStore
{
    public async Task<LogReport> GetReportAsync(DateTimeOffset from, DateTimeOffset to,
        IReadOnlyCollection<string> allowedSourceIds, CancellationToken cancellationToken)
    {
        var sources = ValidateScope(allowedSourceIds);
        if (from >= to || to - from > TimeSpan.FromDays(31)
            || from.Ticks % TimeSpan.TicksPerMillisecond != 0 || to.Ticks % TimeSpan.TicksPerMillisecond != 0)
            throw Invalid("报告周期超出 31 天或时间精度无效。");

        var parameters = new Dictionary<string, string>
        {
            ["from"] = Number(from.ToUnixTimeMilliseconds()),
            ["to"] = Number(to.ToUnixTimeMilliseconds()),
            ["now"] = Number(_clock.GetUtcNow().ToUnixTimeMilliseconds())
        };
        var scope = ScopeSql(sources, parameters);
        var filters = $$"""
            {{scope}} AND received_at >= fromUnixTimestamp64Milli({from:Int64})
            AND received_at < fromUnixTimestamp64Milli({to:Int64})
            AND expires_at > fromUnixTimestamp64Milli({now:Int64})
            """;
        // 周期计数只读取时间、来源和级别。明细单独读取十条短摘要，避免对全周期读取原文列。
        var countsSql = $"""
            SELECT source_id, count() AS total, countIf(severity = 0) AS emergency,
                countIf(severity = 1) AS alert, countIf(severity = 2) AS critical, countIf(severity = 3) AS error
            FROM {_table}
            WHERE {filters}
            GROUP BY source_id
            FORMAT JSONEachRow
            """;
        var recentSql = $"""
            SELECT event_id, source_id, received_at, severity, substringUTF8(toValidUTF8(message), 1, 241) AS summary
            FROM {_table}
            WHERE {filters} AND severity IN (0, 1, 2, 3)
            ORDER BY received_at DESC, event_id DESC
            LIMIT 10
            FORMAT JSONEachRow
            """;
        // 定时报告使用现成的只读 HTTP 查询预算。缺表时报告失败，后台任务不触发初始化。
        var counts = ParseReportRows<LogReportDeviceCounts>(await SqlAsync(countsSql, parameters, cancellationToken, RequestKind.Query), sources.Length);
        var recent = ParseReportRows<LogReportEntry>(await SqlAsync(recentSql, parameters, cancellationToken, RequestKind.Query), 10);
        var permitted = sources.ToHashSet(StringComparer.Ordinal);
        if (counts.Any(row => !permitted.Contains(row.SourceId) || row.Total <= 0
                || row.Emergency < 0 || row.Alert < 0 || row.Critical < 0 || row.Error < 0
                || (decimal)row.Emergency + row.Alert + row.Critical + row.Error > row.Total)
            || counts.Select(row => row.SourceId).Distinct(StringComparer.Ordinal).Count() != counts.Count
            || recent.Any(row => !permitted.Contains(row.SourceId) || row.EventId <= 0 || row.Severity is < 0 or > 3
                || row.ReceivedAt < from || row.ReceivedAt >= to || row.Summary is null || row.Summary.Length > 482)
            || recent.Select(row => (row.EventId, row.ReceivedAt)).Distinct().Count() != recent.Count)
            throw StorageFailure();

        return new(counts, recent.OrderByDescending(row => row.ReceivedAt).ThenByDescending(row => row.EventId).ToArray());
    }

    private static List<T> ParseReportRows<T>(string text, int maximum) where T : class
    {
        try
        {
            var rows = new List<T>();
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (rows.Count >= maximum) throw StorageFailure();
                rows.Add(JsonSerializer.Deserialize<T>(line, JsonOptions) ?? throw StorageFailure());
            }
            return rows;
        }
        catch (JsonException) { throw StorageFailure(); }
    }
}
