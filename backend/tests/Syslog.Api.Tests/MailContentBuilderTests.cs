using Syslog.Api.Service.impl;
using Syslog.Ingest.Models;
using Syslog.Shared.Models;
using Syslog.Shared.Service;
using Xunit;

namespace Syslog.Api.Tests;

public sealed class MailContentBuilderTests
{
    [Fact]
    public async Task Monthly_report_includes_only_selected_sources_and_escapes_bounded_log_summary()
    {
        var logs = new ReportLogStore();
        var runtime = new IngestStatus { ListenerState = "listening", State = "running", Received = 42, PendingBatches = 3 };
        var builder = new MailContentBuilder(new Registry(), logs, runtime, TimeProvider.System);
        var content = await builder.BuildReportAsync("monthly", logs.From, logs.From.AddDays(31), ["42"], default);

        Assert.Equal(new[] { "42" }, logs.Scope);
        Assert.Equal("Syslog 月报 · <b>测试设备</b> · 2026-10-01", content.Subject);
        Assert.Contains("&lt;b&gt;测试设备&lt;/b&gt;", content.Html);
        Assert.Contains("&lt;script&gt;synthetic&lt;/script&gt;", content.Html);
        Assert.DoesNotContain("<script>", content.Html);
        Assert.DoesNotContain(new string('x', 240), content.Html);
        Assert.Contains("…", content.Html);
        Assert.Contains("{{RUNTIME_ROWS}}", content.Html); // 动态正文中的相同字样不会再次替换。
        Assert.DoesNotContain("other-device", content.Html);
        Assert.Contains("累计接收 / 写入  42 / 0", content.Text);
        Assert.Contains("接收 / 写入 / 忽略速率  0 / 0 / 0 条/秒", content.Text);
        Assert.Contains("待写入批次  3", content.Text);
        Assert.Contains("日志库占用  2 KiB", content.Text);
        Assert.Contains("SMTP 测试邮件", builder.BuildTest().Html);
    }

    private sealed class Registry : ISourceRegistry
    {
        public Task<IReadOnlyList<LogSource>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<LogSource>>(
            [new("42", "<b>测试设备</b>\r\n\u202e", "192.0.2.10", "Asia/Shanghai", 365, true),
             new("43", "other-device", "192.0.2.11", "Asia/Shanghai", 365, true)]);
    }

    private sealed class ReportLogStore : ILogStore
    {
        public DateTimeOffset From { get; } = new(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(8));
        public string[] Scope { get; private set; } = [];

        public Task<LogReport> GetReportAsync(DateTimeOffset from, DateTimeOffset to, IReadOnlyCollection<string> scope, CancellationToken ct)
        {
            Scope = scope.ToArray();
            return Task.FromResult(new LogReport([new("42", 1, 1, 0, 0, 0)],
                [new(1, "42", from.AddHours(2), 0, "<script>synthetic</script> {{RUNTIME_ROWS}} " + new string('x', 300))]));
        }

        public Task<long?> GetDatabaseSizeAsync(CancellationToken ct) => Task.FromResult<long?>(2048);
        public Task WriteBatchAsync(IReadOnlyList<LogEvent> events, CancellationToken ct) => throw new NotSupportedException();
        public Task<LogSearchPage> SearchAsync(LogSearch query, IReadOnlyCollection<string> scope, CancellationToken ct) => throw new NotSupportedException();
        public Task<LogEvent?> GetAsync(long eventId, DateTimeOffset receivedAt, IReadOnlyCollection<string> scope, CancellationToken ct) => throw new NotSupportedException();
    }
}
