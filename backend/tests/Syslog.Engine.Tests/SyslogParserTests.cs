using System.Text;
using System.Text.Json;
using Syslog.Engine.Service.impl;
using Syslog.Shared.Models;

namespace Syslog.Engine.Tests;

public sealed class SyslogParserTests
{
    private readonly SyslogParser _parser = new();
    private static readonly TimeZoneInfo SourceZone = TimeZoneInfo.CreateCustomTimeZone("SyntheticUtcPlus8", TimeSpan.FromHours(8), "Synthetic UTC+8", "Synthetic UTC+8");

    public static IEnumerable<object[]> ProtocolCases()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", "syslog-parser-cases.json")));
        foreach (var entry in document.RootElement.EnumerateArray())
            yield return [entry.GetProperty("name").GetString()!, entry.GetRawText()];
    }

    [Theory]
    [MemberData(nameof(ProtocolCases))]
    public void ParsesSyntheticCorpus(string name, string json)
    {
        using var document = JsonDocument.Parse(json);
        var sample = document.RootElement;
        var bytes = Encoding.UTF8.GetBytes(sample.GetProperty("raw").GetString()!);
        var context = Context(sample.TryGetProperty("receivedAt", out var received) ? DateTimeOffset.Parse(received.GetString()!) : null);
        var actual = _parser.Parse(bytes, context);
        Assert.Equal(sample.GetProperty("status").GetString(), actual.ParseStatus);
        Assert.Equal(bytes, actual.RawBytes);
        Assert.Equal("source-synthetic", actual.SourceId);
        Assert.Equal(context.ReceivedAt.ToUniversalTime(), actual.ReceivedAt);
        Assert.Equal(context.ExpiresAt, actual.ExpiresAt);
        Assert.Equal(0, actual.EventId);
        Assert.Empty(actual.BatchId);
        AssertField(sample, "failureCode", actual.FailureCode);
        AssertField(sample, "hostname", actual.Hostname);
        AssertField(sample, "appName", actual.AppName);
        AssertField(sample, "procId", actual.ProcId);
        AssertField(sample, "msgId", actual.MsgId);
        AssertField(sample, "message", actual.Message);
        AssertField(sample, "structuredData", actual.StructuredData);
        AssertField(sample, "confidence", actual.TimeConfidence);
        if (sample.TryGetProperty("facility", out var facility)) Assert.Equal(facility.GetInt32(), actual.Facility);
        if (sample.TryGetProperty("severity", out var severity)) Assert.Equal(severity.GetInt32(), actual.Severity);
        if (sample.TryGetProperty("eventTime", out var time)) Assert.Equal(DateTimeOffset.Parse(time.GetString()!), actual.EventTime);
        Assert.False(string.IsNullOrWhiteSpace(name));
    }

    [Fact]
    public void InvalidUtf8PreservesOriginalBytesAndReturnsExplicitFailure()
    {
        var bytes = Encoding.ASCII.GetBytes("<14>1 - host app - - - ").Concat(new byte[] { 0xff, 0xfe }).ToArray();
        var result = _parser.Parse(bytes, Context());
        Assert.Equal("failed", result.ParseStatus);
        Assert.Equal("unsupported_message_encoding", result.FailureCode);
        Assert.Equal(bytes, result.RawBytes);
        Assert.Contains('\uFFFD', result.Message);
        bytes[0] = 0;
        Assert.Equal((byte)'<', result.RawBytes[0]);
    }

    [Fact]
    public void OversizedMessageIsNotParsedAndEvidenceIsPreserved()
    {
        var bytes = new byte[SyslogParser.MaximumMessageLength + 1];
        var result = _parser.Parse(bytes, Context());
        Assert.Equal("message_too_large", result.FailureCode);
        Assert.Equal(bytes, result.RawBytes);
    }

    [Fact]
    public void HeaderAndStructuredDataBudgetsAreEnforced()
    {
        var header = Encoding.UTF8.GetBytes($"<14>1 - {new string('a', 256)} app - - - body");
        Assert.Equal("invalid_header_field", _parser.Parse(header, Context()).FailureCode);
        var fields = string.Concat(Enumerable.Range(0, 33).Select(i => $"[e{i}@32473]"));
        var message = Encoding.UTF8.GetBytes($"<14>1 - host app - - {fields} body");
        Assert.Equal("structured_data_limit", _parser.Parse(message, Context()).FailureCode);
    }

    [Fact]
    public void ReplayingUsesFixedContextAndPreservesOriginalTime()
    {
        var bytes = Encoding.UTF8.GetBytes("<14>Dec 31 23:59:59 host-a app: replay");
        var context = Context(DateTimeOffset.Parse("2027-01-01T00:00:01+08:00"));
        var first = _parser.Parse(bytes, context);
        var second = _parser.Parse(bytes, context);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.Equal("Dec 31 23:59:59", first.OriginalTimestamp);
    }

    [Fact]
    public void DstGapDoesNotSilentlySelectAnotherYear()
    {
        var context = Context(DateTimeOffset.Parse("2026-03-08T07:30:00Z")) with { SourceTimeZone = DstZone() };
        var result = _parser.Parse(Encoding.UTF8.GetBytes("<14>Mar  8 02:30:00 host-a app: gap"), context);
        Assert.Equal("invalid_local_time", result.FailureCode);
    }

    [Fact]
    public void DstAmbiguityIsDeterministicallyFlagged()
    {
        var context = Context(DateTimeOffset.Parse("2026-11-01T06:40:00Z")) with { SourceTimeZone = DstZone() };
        var result = _parser.Parse(Encoding.UTF8.GetBytes("<14>Nov  1 01:30:00 host-a app: ambiguous"), context);
        Assert.Equal("parsed", result.ParseStatus);
        Assert.Equal("inferred_ambiguous", result.TimeConfidence);
        Assert.Equal(DateTimeOffset.Parse("2026-11-01T06:30:00Z"), result.EventTime);
    }

    [Fact]
    public void BoundedRandomPayloadsNeverThrowOrLoseEvidence()
    {
        var random = new Random(32473);
        for (var i = 0; i < 500; i++)
        {
            var bytes = new byte[random.Next(0, 2048)];
            random.NextBytes(bytes);
            if (bytes.Length > 0 && i % 2 == 0) bytes[0] = (byte)'<';
            Assert.Equal(bytes, _parser.Parse(bytes, Context()).RawBytes);
        }
    }

    private static ParseContext Context(DateTimeOffset? received = null) => new("source-synthetic", "192.0.2.10", "tcp",
        received ?? DateTimeOffset.Parse("2026-11-09T09:10:12+08:00"), SourceZone, DateTimeOffset.Parse("2027-11-09T00:00:00Z"));

    private static void AssertField(JsonElement sample, string key, string actual)
    {
        if (sample.TryGetProperty(key, out var expected)) Assert.Equal(expected.GetString(), actual);
    }

    private static TimeZoneInfo DstZone()
    {
        var start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday);
        var end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2020, 1, 1), new DateTime(2030, 12, 31), TimeSpan.FromHours(1), start, end);
        return TimeZoneInfo.CreateCustomTimeZone("SyntheticDst", TimeSpan.FromHours(-5), "Synthetic DST", "Standard", "Daylight", [rule]);
    }
}
