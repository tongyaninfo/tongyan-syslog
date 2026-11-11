using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Syslog.Api.Models;
using Syslog.Ingest.Models;
using Syslog.Shared.Models;
using Syslog.Shared.Service;

namespace Syslog.Api.Service.impl;

public sealed partial class MailContentBuilder(ISourceRegistry sources, ILogStore logs, IngestStatus status, TimeProvider clock)
{
    private const int MaximumDeviceRows = 12;
    private const int MaximumSummaryLength = 240;
    private static readonly TimeZoneInfo Shanghai = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
    private static readonly Lazy<string> ReportTemplate = new(() => ReadTemplate("Report"));
    private static readonly Lazy<string> TestTemplate = new(() => ReadTemplate("TestMail"));
    private static readonly string[] Severities = ["紧急", "警报", "严重", "错误"];
    private static readonly string[] SeverityColors = ["#991b1b", "#b91c1c", "#c2410c", "#92400e"];

    public MailContent BuildTest() => new("SMTP 测试邮件", TestTemplate.Value,
        "通研 Syslog · SMTP 测试邮件\n\n这是一封由通知设置发送的测试邮件。收到此邮件说明 SMTP 发送路径可用。");

    public async Task<MailContent> BuildReportAsync(string frequency, DateTimeOffset from, DateTimeOffset to,
        IReadOnlyList<string> sourceIds, CancellationToken ct)
    {
        var title = frequency switch
        {
            "daily" => "Syslog 日报",
            "weekly" => "Syslog 周报",
            "monthly" => "Syslog 月报",
            _ => throw new ArgumentOutOfRangeException(nameof(frequency))
        };
        var registered = await sources.ListAsync(ct);
        var selected = sourceIds.Count == 0 ? registered : registered.Where(source => sourceIds.Contains(source.Id, StringComparer.Ordinal)).ToArray();
        // 计划显式选择的来源已变更时，不能退回到全部设备并发送超出授权范围的报告。
        if (sourceIds.Count != 0 && selected.Count != sourceIds.Distinct(StringComparer.Ordinal).Count())
            throw new InvalidOperationException("报告计划中的设备来源已不存在。");

        var names = selected.ToDictionary(source => source.Id, source => Limit(source.Name, 80), StringComparer.Ordinal);
        var report = selected.Count == 0 ? new LogReport([], [])
            : await logs.GetReportAsync(from, to, names.Keys, ct);
        var rates = ReadRatesAsync(ct);
        var databaseBytes = ReadDatabaseSizeAsync(ct);
        await Task.WhenAll(rates, databaseBytes);
        var period = $"{Time(from)} 至 {Time(to)}（上海时间，结束时刻不含）";
        var scope = sourceIds.Count == 0 ? $"全部已登记设备（{selected.Count} 台）"
            : selected.Count == 1 ? names[selected[0].Id] : $"所选设备（{selected.Count} 台）";
        var deviceCounts = report.Devices.ToDictionary(device => device.SourceId, StringComparer.Ordinal);
        var devices = selected.Select(source => deviceCounts.GetValueOrDefault(source.Id) ?? new(source.Id, 0, 0, 0, 0, 0))
            .OrderByDescending(HighSeverity).ThenByDescending(device => device.Total)
            .ThenBy(device => names[device.SourceId], StringComparer.Ordinal).Take(MaximumDeviceRows).ToArray();
        var entries = report.RecentEntries.Take(10).ToArray();
        var total = report.Devices.Sum(device => device.Total);
        var severityCounts = new[]
        {
            report.Devices.Sum(device => device.Emergency), report.Devices.Sum(device => device.Alert),
            report.Devices.Sum(device => device.Critical), report.Devices.Sum(device => device.Error)
        };
        var generatedAt = Time(clock.GetUtcNow());
        var runtimeRows = RuntimeRows(registered, await databaseBytes, await rates);
        var html = Placeholder().Replace(ReportTemplate.Value, match => match.Groups[1].Value switch
        {
            "TITLE" => Encode(title),
            "PERIOD" => Encode(period),
            "SCOPE" => Encode(scope),
            "TOTAL" => Number(total),
            "EMERGENCY" => Number(severityCounts[0]),
            "ALERT" => Number(severityCounts[1]),
            "CRITICAL" => Number(severityCounts[2]),
            "ERROR" => Number(severityCounts[3]),
            "LOG_ROWS" => LogRows(entries, names),
            "DEVICE_ROWS" => DeviceRows(devices, names, selected.Count - devices.Length),
            "RUNTIME_ROWS" => string.Concat(runtimeRows.Select(row => $"<tr>{Cell(row.Label)}{Cell(row.Value)}</tr>")),
            "GENERATED_AT" => Encode(generatedAt),
            _ => throw new InvalidOperationException("邮件模板占位符未知。")
        });
        var text = new StringBuilder().AppendLine(title).AppendLine(period).AppendLine(scope)
            .AppendLine($"周期已入库 {Number(total)} 条 · 紧急 {Number(severityCounts[0])} · 警报 {Number(severityCounts[1])} · 严重 {Number(severityCounts[2])} · 错误 {Number(severityCounts[3])}")
            .AppendLine().AppendLine("最近 10 条高严重级别日志");
        if (entries.Length == 0) text.AppendLine("本周期无紧急、警报、严重或错误日志。");
        foreach (var entry in entries)
            text.AppendLine($"{Time(entry.ReceivedAt)} | {names[entry.SourceId]} | {Severities[entry.Severity]} | {Limit(entry.Summary, MaximumSummaryLength)}");
        text.AppendLine().AppendLine("设备概览");
        if (devices.Length == 0) text.AppendLine("暂无已登记设备。");
        foreach (var device in devices)
            text.AppendLine($"{names[device.SourceId]} | 已入库 {Number(device.Total)} 条 | 高严重级别 {Number(HighSeverity(device))} 条");
        if (selected.Count > devices.Length) text.AppendLine($"其余 {selected.Count - devices.Length} 台设备计入周期统计。");
        text.AppendLine().AppendLine("系统运行快照 · 累计指标从本次服务启动开始");
        foreach (var row in runtimeRows) text.AppendLine($"{row.Label}  {row.Value}");
        text.AppendLine().AppendLine($"生成时间 {generatedAt}（上海时间）。周期统计以生成时仍可检索的入库日志为准。");
        var localStart = TimeZoneInfo.ConvertTime(from, Shanghai).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        // 主题范围限制字符数并清理换行、控制字符，留出配置的主题前缀空间。
        return new($"{title} · {Limit(scope, 55).Trim()} · {localStart}", html, text.ToString());
    }

    private (string Label, string Value)[] RuntimeRows(IReadOnlyList<LogSource> registered, long? databaseBytes, string rates) =>
    [
        ("接收监听", RuntimeState(status.ListenerState)), ("处理任务", RuntimeState(status.State)),
        ("已启用 / 已登记来源", $"{Number(registered.Count(source => source.Enabled))} / {Number(registered.Count)}"),
        ("累计接收 / 写入", $"{Number(Interlocked.Read(ref status.Received))} / {Number(Interlocked.Read(ref status.Written))}"),
        ("累计忽略 / 丢弃", $"{Number(Interlocked.Read(ref status.Ignored))} / {Number(Interlocked.Read(ref status.Dropped))}"),
        ("接收 / 写入 / 忽略速率", rates),
        ("解析成功 / 格式未识别", $"{Number(Interlocked.Read(ref status.Parsed))} / {Number(Interlocked.Read(ref status.ParseUnknown))}"),
        ("解析失败 / 写入失败", $"{Number(Interlocked.Read(ref status.ParseFailed))} / {Number(Interlocked.Read(ref status.WriteFailures))}"),
        ("待写入批次", Number(Interlocked.Read(ref status.PendingBatches))),
        ("接收队列 / 本地暂存", $"{Size(Interlocked.Read(ref status.QueuedBytes))} / {Size(Interlocked.Read(ref status.SpoolBytes))}"),
        ("日志库占用", databaseBytes is long bytes ? Size(bytes) : "暂不可用"),
        ("最近接收 / 写入", $"{Timestamp(status.LastReceiveUnixMilliseconds)} / {Timestamp(Interlocked.Read(ref status.LastWriteUnixMilliseconds))}")
    ];

    private async Task<string> ReadRatesAsync(CancellationToken ct)
    {
        var started = clock.GetTimestamp();
        var before = ReadTraffic();
        await Task.Delay(TimeSpan.FromSeconds(1), clock, ct);
        var after = ReadTraffic();
        var seconds = clock.GetElapsedTime(started).TotalSeconds;
        if (seconds <= 0 || after.Received < before.Received || after.Written < before.Written || after.Ignored < before.Ignored)
            return "暂不可用";
        return FormattableString.Invariant($"{(after.Received - before.Received) / seconds:0.##} / {(after.Written - before.Written) / seconds:0.##} / {(after.Ignored - before.Ignored) / seconds:0.##} 条/秒（{seconds:0.##} 秒采样）");
    }

    private (long Received, long Written, long Ignored) ReadTraffic() =>
        (Interlocked.Read(ref status.Received), Interlocked.Read(ref status.Written), Interlocked.Read(ref status.Ignored));

    private async Task<long?> ReadDatabaseSizeAsync(CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(2));
        try { return await logs.GetDatabaseSizeAsync(budget.Token); }
        catch (Exception) when (!ct.IsCancellationRequested) { return null; }
    }

    private static string LogRows(LogReportEntry[] entries, IReadOnlyDictionary<string, string> names)
    {
        if (entries.Length == 0) return EmptyRow("本周期无紧急、警报、严重或错误日志。");
        var html = new StringBuilder();
        foreach (var entry in entries)
        {
            var severity = entry.Severity;
            html.Append($"""
                <tr><td style="padding:13px 0;border-bottom:1px solid #e2e8f0;word-break:break-word;">
                <strong style="color:{SeverityColors[severity]};">{Severities[severity]}</strong>
                <strong style="color:#0f172a;">&nbsp;{Encode(names[entry.SourceId])}</strong>
                <span style="font-size:12px;color:#475569;">&nbsp;{Encode(Time(entry.ReceivedAt))}</span><br>
                <span style="color:#334155;">{Encode(Limit(entry.Summary, MaximumSummaryLength))}</span>
                </td></tr>
                """);
        }
        return html.ToString();
    }

    private static string DeviceRows(LogReportDeviceCounts[] devices, IReadOnlyDictionary<string, string> names, int hidden)
    {
        if (devices.Length == 0) return EmptyRow("暂无已登记设备。", 3);
        var html = new StringBuilder();
        foreach (var device in devices)
            html.Append($"<tr>{Cell(names[device.SourceId])}{Cell(Number(device.Total))}{Cell(Number(HighSeverity(device)))}</tr>");
        if (hidden > 0) html.Append(EmptyRow($"其余 {Number(hidden)} 台设备计入周期统计。", 3));
        return html.ToString();
    }

    private static string Cell(string value) => $"<td style=\"padding:10px 8px;border-bottom:1px solid #e2e8f0;word-break:break-word;\">{Encode(value)}</td>";
    private static string EmptyRow(string value, int columns = 1) => $"<tr><td colspan=\"{columns}\" style=\"padding:13px 0;color:#475569;\">{Encode(value)}</td></tr>";
    private static long HighSeverity(LogReportDeviceCounts device) => device.Emergency + device.Alert + device.Critical + device.Error;
    private static string Encode(string value) => WebUtility.HtmlEncode(value);
    private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
    private static string Time(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, Shanghai).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    private static string Timestamp(long value) => value > 0 ? Time(DateTimeOffset.FromUnixTimeMilliseconds(value)) : "暂无";
    private static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{Number(bytes)} B",
        < 1024L * 1024 => FormattableString.Invariant($"{bytes / 1024d:0.##} KiB"),
        < 1024L * 1024 * 1024 => FormattableString.Invariant($"{bytes / (1024d * 1024):0.##} MiB"),
        _ => FormattableString.Invariant($"{bytes / (1024d * 1024 * 1024):0.##} GiB")
    };
    private static string RuntimeState(string state) => state switch
    {
        "starting" => "启动中", "listening" => "监听中", "running" => "运行中", "failed" => "异常", "retrying" => "等待重试",
        "stopped" => "已停止", "disabled" => "未启用", _ => "未知"
    };

    private static string Limit(string value, int length)
    {
        var text = new StringBuilder();
        var remaining = length;
        // 不拆分 Unicode 字符，也不保留报文中的换行或控制字符。
        foreach (var rune in value.EnumerateRunes())
        {
            if (remaining-- == 0) return text.Append('…').ToString();
            if (Rune.IsControl(rune) || Rune.IsWhiteSpace(rune) || Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format) text.Append(' ');
            else text.Append(rune.ToString());
        }
        return text.Length == 0 ? "—" : text.ToString();
    }

    private static string ReadTemplate(string name)
    {
        using var stream = typeof(MailContentBuilder).Assembly.GetManifestResourceStream($"Syslog.Api.Resources.Email.{name}.html")
            ?? throw new InvalidOperationException("邮件模板资源缺失。");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"\{\{([A-Z_]+)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();
}
