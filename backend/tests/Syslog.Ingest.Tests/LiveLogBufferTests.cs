using System.Text;
using System.Text.Json;
using Syslog.Ingest.Service.impl;
using Syslog.Shared.Common;
using Syslog.Shared.Models;
using Xunit;

namespace Syslog.Ingest.Tests;

/// <summary>只使用内存和合成记录验证容量、权限、水位与原文；不打开接收端口或日志库。</summary>
public sealed class LiveLogBufferTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-11T12:00:00Z");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void StartsUnavailableAndKeepsNewestFiveThousandPerDeviceByDefault()
    {
        var buffer = new LiveLogBuffer();
        Assert.False(buffer.Capture(Event(0), false));
        Assert.Equal("live_log_unavailable", buffer.ReadSnapshot(new(), ["source-1"]).Code);
        buffer.SetAvailable(true);
        for (var index = 1; index <= 5001; index++) Assert.True(buffer.Capture(Event(index), false));
        var stats = buffer.GetStats();
        Assert.Equal(5000, stats.Entries);
        Assert.Equal(1, stats.Evicted);
        Assert.Equal(5000, stats.PerDeviceLimit);
        Assert.Equal(512L * 1024 * 1024, stats.MaxBytes);
        var page = buffer.ReadSnapshot(new(), ["source-1"], take: 500).Data!;
        Assert.Equal("5001", page.Items[0].Sequence);
        Assert.Equal("4502", page.Items[^1].Sequence);
        Assert.True(page.HasMore);
        Assert.Equal("5001", page.Watermark);
        Assert.Equal("4502", page.NextBeforeSequence);
    }

    [Fact]
    public void HotDeviceEvictionIsVisibleEvenWhenColdDeviceKeepsOlderRecords()
    {
        var buffer = Ready(new(100));
        buffer.Capture(Event(1, "cold"), false);
        var epoch = buffer.GetStats().Epoch;
        for (var index = 0; index < 101; index++) buffer.Capture(Event(index, "hot"), false);
        Assert.Equal(101, buffer.GetStats().Entries);
        Assert.Single(buffer.ReadSnapshot(new() { SourceId = "cold" }, ["cold", "hot"]).Data!.Items);
        var gap = buffer.ReadAfter(new(), ["cold", "hot"], epoch, "1").Data!;
        Assert.Equal("cache_evicted", gap.GapReason);
        var unaffected = buffer.ReadAfter(new() { SourceId = "cold" }, ["cold", "hot"], epoch, "1").Data!;
        Assert.Null(unaffected.GapReason);
        Assert.Empty(unaffected.Items);
        Assert.Equal("102", unaffected.Cursor);
    }

    [Fact]
    public void GlobalBytesIncludeRawTextAndMetadataAndEvictBeforeDeviceCountLimit()
    {
        const long limit = 16L * 1024 * 1024;
        var buffer = Ready(new(50_000, limit));
        var payload = new byte[65_536];
        var message = new string('a', 65_536);
        for (var index = 0; index < 200; index++)
            Assert.True(buffer.Capture(Event(index) with { RawBytes = payload, Message = message }, false));
        var stats = buffer.GetStats();
        Assert.InRange(stats.Bytes, 1, limit);
        Assert.InRange(stats.Entries, 1, 199);
        Assert.Equal(200 - stats.Entries, stats.Evicted);
        Assert.True(stats.Bytes > stats.Entries * (65_536 + 65_536 * 2));
    }

    [Fact]
    public void ShrinkingSettingsEvictsImmediatelyWithoutClearingOtherDevice()
    {
        var buffer = Ready(new(300));
        buffer.Capture(Event(0, "cold"), true);
        for (var index = 0; index < 220; index++) buffer.Capture(Event(index, "hot"), false);
        var previous = buffer.GetStats();
        buffer.ApplySettings(new(100, 16L * 1024 * 1024));
        var current = buffer.GetStats();
        Assert.Equal(previous.Epoch, current.Epoch);
        Assert.Equal(101, current.Entries);
        Assert.Equal(120, current.Evicted);
        Assert.Single(buffer.ReadSnapshot(new() { SourceId = "cold" }, ["cold", "hot"]).Data!.Items);
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.ApplySettings(new(99)));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.ApplySettings(new(5000, 1)));
    }

    [Fact]
    public void SourceMetadataIsBoundedAndOverflowCreatesNewEpochWithExplicitGap()
    {
        var buffer = Ready();
        var originalEpoch = buffer.GetStats().Epoch;
        for (var index = 0; index <= 10_000; index++) buffer.Capture(Event(index, "source-" + index), false);
        var current = buffer.GetStats();
        Assert.NotEqual(originalEpoch, current.Epoch);
        Assert.Equal(1, current.Entries);
        Assert.Equal(1, current.DeviceCount);
        Assert.Equal(10_000, current.Evicted);
        Assert.Equal("epoch_changed", buffer.ReadAfter(new(), ["source-10000"], originalEpoch, "0").Data!.GapReason);
        Assert.Equal("live_log_expired", buffer.GetDetail(originalEpoch + ":1", ["source-0"]).Code);
    }

    [Fact]
    public void StrongFiltersStopAtScanBudgetAndEmptyPagesKeepAResumableCursor()
    {
        var buffer = Ready(new(50_000));
        for (var index = 1; index <= 8200; index++)
            buffer.Capture(Event(index) with { Hostname = index == 1 ? "old-match" : index == 8200 ? "new-match" : "other" }, false);
        var oldFilter = new LiveLogFilter { Hostname = "old-match" };
        var snapshot = buffer.ReadSnapshot(oldFilter, ["source-1"]).Data!;
        Assert.Empty(snapshot.Items);
        Assert.True(snapshot.HasMore);
        Assert.True(snapshot.BudgetLimited);
        Assert.Equal("8200", snapshot.Watermark);
        Assert.Equal("9", snapshot.NextBeforeSequence);
        var older = buffer.ReadSnapshot(oldFilter, ["source-1"], snapshot.NextBeforeSequence).Data!;
        Assert.Equal("1", Assert.Single(older.Items).Sequence);
        Assert.False(older.HasMore);

        var newFilter = new LiveLogFilter { Hostname = "new-match" };
        var delta = buffer.ReadAfter(newFilter, ["source-1"], snapshot.Epoch, "0").Data!;
        Assert.Empty(delta.Items);
        Assert.True(delta.HasMore);
        Assert.True(delta.BudgetLimited);
        Assert.Equal("8192", delta.Cursor);
        var remaining = buffer.ReadAfter(newFilter, ["source-1"], snapshot.Epoch, delta.Cursor).Data!;
        Assert.Equal("8200", Assert.Single(remaining.Items).Sequence);
        Assert.Equal("8200", remaining.Cursor);
        Assert.False(remaining.HasMore);
    }

    [Fact]
    public void SummaryExcludesDetailEvidenceAndBoundsHeadersWhileDetailKeepsEveryField()
    {
        var buffer = Ready();
        var original = Event(1) with
        {
            BatchId = "synthetic-batch", OriginalTimestamp = "synthetic-original-time", ProcId = "synthetic-process", MsgId = "synthetic-message",
            StructuredData = new string('数', 65_536), Hostname = new string('h', 4096), AppName = new string('a', 4096),
        };
        Assert.True(buffer.Capture(original, false));
        var summary = Assert.Single(buffer.ReadSnapshot(new(), ["source-1"]).Data!.Items);
        Assert.Empty(summary.Log.StructuredData);
        Assert.Empty(summary.Log.OriginalTimestamp);
        Assert.Empty(summary.Log.ProcId);
        Assert.Empty(summary.Log.MsgId);
        Assert.Empty(summary.Log.BatchId);
        Assert.Empty(summary.Log.RawBytes);
        Assert.Equal(255, summary.Log.Hostname.Length);
        Assert.EndsWith("…", summary.Log.Hostname);
        Assert.Equal(255, summary.Log.AppName.Length);
        Assert.EndsWith("…", summary.Log.AppName);
        var detail = buffer.GetDetail(summary.Id, ["source-1"]).Data!;
        Assert.Equal(original.StructuredData, detail.Log.StructuredData);
        Assert.Equal(original.OriginalTimestamp, detail.Log.OriginalTimestamp);
        Assert.Equal(original.ProcId, detail.Log.ProcId);
        Assert.Equal(original.MsgId, detail.Log.MsgId);
        Assert.Equal(original.BatchId, detail.Log.BatchId);
        Assert.Equal(original.Hostname, detail.Log.Hostname);
        Assert.Equal(original.AppName, detail.Log.AppName);
        Assert.Equal(original.RawBytes, detail.Log.RawBytes);
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(summary, Json).Length * 20 < JsonSerializer.SerializeToUtf8Bytes(detail, Json).Length);
        Assert.Empty(buffer.ReadSnapshot(new() { Hostname = original.Hostname[..255] }, ["source-1"]).Data!.Items);
    }

    [Fact]
    public void FiltersUseExactFieldsCaseSensitiveWholeWordsAndDisposition()
    {
        var buffer = Ready();
        buffer.Capture(Event(1) with { Hostname = "host-A", AppName = "app", Severity = 3, Message = "Error code42 disk-full" }, true);
        buffer.Capture(Event(2) with { Hostname = "host-A", AppName = "app", Severity = 3, Message = "Error coded42 disk-full" }, true);
        buffer.Capture(Event(3) with { Hostname = "host-A", AppName = "app", Severity = 3, Message = "error code42 disk-full" }, true);
        buffer.Capture(Event(4) with { Hostname = "host-A", AppName = "app", Severity = 3, Message = "Error code42 disk-full" }, false);
        var filter = new LiveLogFilter
        {
            From = Now, To = Now.AddSeconds(10), SourceId = "source-1", Hostname = "host-A", AppName = "app",
            Severity = 3, Keywords = "Error code42 full Error", Disposition = "ignored"
        };
        var result = buffer.ReadSnapshot(filter, ["source-1"]).Data!;
        Assert.Equal("1", Assert.Single(result.Items).Sequence);
        Assert.Empty(buffer.ReadSnapshot(filter with { Hostname = "host-a" }, ["source-1"]).Data!.Items);
        Assert.Empty(buffer.ReadSnapshot(filter with { AppName = "ap" }, ["source-1"]).Data!.Items);
        Assert.Equal("4", Assert.Single(buffer.ReadSnapshot(filter with { Disposition = "normal" }, ["source-1"]).Data!.Items).Sequence);
        Assert.Equal("unsupported_search_token", buffer.ReadSnapshot(filter with { Keywords = "中文" }, ["source-1"]).Code);
        Assert.Equal("unsupported_search_token", buffer.ReadSnapshot(filter with { Keywords = "!!!" }, ["source-1"]).Code);
        Assert.Equal("invalid_query", buffer.ReadSnapshot(filter with { To = Now.AddDays(31) }, ["source-1"]).Code);
        Assert.Equal("invalid_query", buffer.ReadSnapshot(filter with { Severity = 8 }, ["source-1"]).Code);
        Assert.Equal("invalid_query", buffer.ReadSnapshot(filter with { Disposition = "unknown" }, ["source-1"]).Code);
    }

    [Fact]
    public void UnauthorizedScopeCannotReadSummaryOrDetail()
    {
        var buffer = Ready();
        buffer.Capture(Event(1), false);
        var item = Assert.Single(buffer.ReadSnapshot(new(), ["source-1"]).Data!.Items);
        Assert.Empty(buffer.ReadSnapshot(new(), []).Data!.Items);
        Assert.Equal("source_forbidden", buffer.ReadSnapshot(new() { SourceId = "source-1" }, ["source-2"]).Code);
        Assert.Equal("live_log_expired", buffer.GetDetail(item.Id, ["source-2"]).Code);
        Assert.Equal("invalid_query", buffer.ReadSnapshot(new(), [new string('a', 129)]).Code);
    }

    [Fact]
    public void SummaryIsBoundedAndDetailPreservesIndependentRawBytesIncludingInvalidUtf8()
    {
        var buffer = Ready();
        byte[] raw = [0xff, 0x00, 0x80, 0x41];
        var message = new string('x', 1023) + "😀" + "<script>sample</script>";
        buffer.Capture(Event(9007199254740993) with { RawBytes = raw, Message = message, ParseStatus = "failed" }, true);
        raw[0] = 1;
        var summary = Assert.Single(buffer.ReadSnapshot(new(), ["source-1"]).Data!.Items);
        Assert.True(summary.MessageTruncated);
        Assert.Equal(1023, summary.Log.Message.Length);
        Assert.Empty(summary.Log.RawBytes);
        Assert.Equal(0, summary.Log.EventId);
        Assert.Equal("ignored", summary.Disposition);
        var full = buffer.GetDetail(summary.Id, ["source-1"]).Data!;
        Assert.Equal(message, full.Log.Message);
        Assert.Equal(new byte[] { 0xff, 0x00, 0x80, 0x41 }, full.Log.RawBytes);
        full.Log.RawBytes[0] = 2;
        Assert.Equal(0xff, buffer.GetDetail(summary.Id, ["source-1"]).Data!.Log.RawBytes[0]);
        Assert.Contains("\"eventId\":\"0\"", JsonSerializer.Serialize(summary, Json));
    }

    [Fact]
    public void PagingAndCatchUpAreOrderedWithoutDuplicatesAndNonmatchesAdvanceCursor()
    {
        var buffer = Ready();
        for (var index = 1; index <= 10; index++) buffer.Capture(Event(index), index % 2 == 0);
        var first = buffer.ReadSnapshot(new(), ["source-1"], take: 3).Data!;
        var second = buffer.ReadSnapshot(new(), ["source-1"], first.NextBeforeSequence, take: 3).Data!;
        Assert.Equal(new[] { "10", "9", "8" }, first.Items.Select(item => item.Sequence));
        Assert.Equal(new[] { "7", "6", "5" }, second.Items.Select(item => item.Sequence));
        buffer.Capture(Event(11), false);
        buffer.Capture(Event(12), true);
        var delta = buffer.ReadAfter(new(), ["source-1"], first.Epoch, first.Watermark, take: 1).Data!;
        Assert.Equal("11", Assert.Single(delta.Items).Sequence);
        Assert.True(delta.HasMore);
        var next = buffer.ReadAfter(new(), ["source-1"], delta.Epoch, delta.Cursor).Data!;
        Assert.Equal("12", Assert.Single(next.Items).Sequence);
        Assert.False(next.HasMore);
        var noMatches = buffer.ReadAfter(new() { Hostname = "not-present" }, ["source-1"], first.Epoch, "0").Data!;
        Assert.Empty(noMatches.Items);
        Assert.Equal("12", noMatches.Cursor);
    }

    [Fact]
    public void SerializedPageBudgetIncludesUnicodeEscapingAndEnvelope()
    {
        var buffer = Ready();
        for (var index = 1; index <= 50; index++) buffer.Capture(Event(index) with { Message = new string('界', 1024) }, false);
        var page = buffer.ReadSnapshot(new(), ["source-1"], take: 500, maxBytes: 32 * 1024).Data!;
        Assert.True(page.BudgetLimited);
        Assert.True(page.HasMore);
        Assert.InRange(JsonSerializer.SerializeToUtf8Bytes(page, Json).Length, 1, 32 * 1024);
        Assert.InRange(page.Items.Count, 1, 49);
        var delta = buffer.ReadAfter(new(), ["source-1"], page.Epoch, "0", maxBytes: 32 * 1024).Data!;
        Assert.True(delta.BudgetLimited);
        Assert.InRange(JsonSerializer.SerializeToUtf8Bytes(delta, Json).Length, 1, 32 * 1024);
    }

    [Fact]
    public void ParseFailureIsVisibleAndRejectedOversizeCannotDisturbRetainedRecords()
    {
        var buffer = Ready();
        Assert.True(buffer.Capture(Event(1) with { ParseStatus = "failed", FailureCode = "invalid_pri" }, false));
        Assert.False(buffer.Capture(Event(2) with { RawBytes = new byte[65_537] }, false));
        Assert.False(buffer.Capture(Event(3) with { SourceId = "" }, false));
        Assert.Equal(2, buffer.GetStats().Rejected);
        Assert.Equal("invalid_pri", Assert.Single(buffer.ReadSnapshot(new(), ["source-1"]).Data!.Items).Log.FailureCode);
        buffer.SetAvailable(false);
        Assert.False(buffer.Capture(Event(4), false));
        Assert.Equal("live_log_unavailable", buffer.ReadSnapshot(new(), ["source-1"]).Code);
        buffer.SetAvailable(true);
        Assert.Single(buffer.ReadSnapshot(new(), ["source-1"]).Data!.Items);
    }

    [Fact]
    public async Task ConcurrentCaptureAndReadsRemainBoundedAndUseValidStreamIds()
    {
        var buffer = Ready(new(100));
        var writers = Enumerable.Range(1, 4).Select(device => Task.Run(() =>
        {
            for (var index = 0; index < 1000; index++) Assert.True(buffer.Capture(Event(index, "source-" + device), index % 2 == 0));
        }));
        var reader = Task.Run(() =>
        {
            for (var index = 0; index < 100; index++)
            {
                var page = buffer.ReadSnapshot(new(), ["source-1", "source-2", "source-3", "source-4"]).Data!;
                Assert.All(page.Items, item => Assert.Equal(page.Epoch + ":" + item.Sequence, item.Id));
                Assert.Equal(page.Items.Count, page.Items.Select(item => item.Id).Distinct().Count());
            }
        });
        await Task.WhenAll(writers.Append(reader));
        Assert.Equal(400, buffer.GetStats().Entries);
        Assert.Equal(3600, buffer.GetStats().Evicted);
    }

    private static LiveLogBuffer Ready(LiveLogCacheSettings? settings = null)
    {
        var buffer = new LiveLogBuffer();
        if (settings is not null) buffer.ApplySettings(settings);
        buffer.SetAvailable(true);
        return buffer;
    }

    private static LogEvent Event(long id, string source = "source-1") => new()
    {
        EventId = id, SourceId = source, ReceivedAt = Now.AddMilliseconds(id % 1_000_000), ExpiresAt = Now.AddDays(30),
        Message = "synthetic Error message", RawBytes = Encoding.UTF8.GetBytes("<3>synthetic Error message"), ParseStatus = "parsed"
    };
}
