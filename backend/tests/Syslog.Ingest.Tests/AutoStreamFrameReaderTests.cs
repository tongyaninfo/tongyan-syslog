using System.Text;
using Syslog.Engine.Common;
using Syslog.Engine.Service.impl;
using Syslog.Ingest.Service.impl;
using Syslog.Shared.Models;
using Xunit;

namespace Syslog.Ingest.Tests;

public sealed class AutoStreamFrameReaderTests
{
    [Fact]
    public async Task AutoReadsCoalescedCrLfAndLfFramesForTheSyslogParser()
    {
        const string first = "<14>1 2026-11-10T10:15:00Z vcenter-example vpxd 123 - - Synthetic event";
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes($"{first}\r\n<14>second\n"));
        var reader = CreateReader();
        Assert.Null(reader.EffectiveMode);

        var payload = await reader.ReadFrameAsync(stream);
        Assert.NotNull(payload);
        Assert.Equal(Encoding.UTF8.GetBytes(first), payload);
        Assert.Equal(SyslogFrameMode.Newline, reader.EffectiveMode);

        var receivedAt = new DateTimeOffset(2026, 11, 10, 10, 16, 0, TimeSpan.Zero);
        var context = new ParseContext("source-synthetic", "192.0.2.10", "tcp", receivedAt, TimeZoneInfo.Utc, receivedAt.AddDays(30));
        var parsed = new SyslogParser().Parse(payload, context);
        Assert.Equal("parsed", parsed.ParseStatus);
        Assert.Equal("vcenter-example", parsed.Hostname);
        Assert.Equal("Synthetic event", parsed.Message);
        Assert.Equal("<14>second"u8.ToArray(), await reader.ReadFrameAsync(stream));
        Assert.Null(await reader.ReadFrameAsync(stream));
    }

    [Fact]
    public async Task AutoWaitsForASplitLengthPrefixAndPreservesEmbeddedNewline()
    {
        var payload = "<14>中文\n正文"u8.ToArray();
        var bytes = Encoding.ASCII.GetBytes($"{payload.Length} ").Concat(payload).Concat("4 next"u8.ToArray()).ToArray();
        await using var stream = new SingleByteStream(bytes);
        var reader = CreateReader();

        Assert.Equal(payload, await reader.ReadFrameAsync(stream));
        Assert.Equal(SyslogFrameMode.OctetCounting, reader.EffectiveMode);
        Assert.Equal("next"u8.ToArray(), await reader.ReadFrameAsync(stream));
        Assert.Null(await reader.ReadFrameAsync(stream));
    }

    [Theory]
    [InlineData("65537 \n", "frame_too_large")]
    [InlineData("99999999999999999999", "frame_too_large")]
    [InlineData("0 \n", "invalid_length_prefix")]
    public async Task AutoRejectsMalformedOrOversizedDecimalPrefixes(string content, string expectedCode)
    {
        await using var stream = new MemoryStream(Encoding.ASCII.GetBytes(content));
        var reader = CreateReader();
        var error = await Assert.ThrowsAsync<FrameReadException>(() => reader.ReadFrameAsync(stream).AsTask());
        Assert.Equal(expectedCode, error.Code);
        Assert.Equal(SyslogFrameMode.OctetCounting, reader.EffectiveMode);
    }

    [Fact]
    public async Task AutoKeepsDetectedModeOnTheConnection()
    {
        await using var stream = new MemoryStream("1 x<14>line\n"u8.ToArray());
        var reader = CreateReader();
        Assert.Equal("x"u8.ToArray(), await reader.ReadFrameAsync(stream));
        var error = await Assert.ThrowsAsync<FrameReadException>(() => reader.ReadFrameAsync(stream).AsTask());
        Assert.Equal("invalid_length_prefix", error.Code);

        await using var nextConnection = new MemoryStream("<14>line\n"u8.ToArray());
        Assert.Equal("<14>line"u8.ToArray(), await CreateReader().ReadFrameAsync(nextConnection));
    }

    [Fact]
    public async Task AutoRejectsNonSyslogPrefixesAndIdentifiesBufferedTlsHello()
    {
        await using var unknown = new MemoryStream("GET / HTTP/1.1\r\n"u8.ToArray());
        var unknownError = await Assert.ThrowsAsync<FrameReadException>(() => CreateReader().ReadFrameAsync(unknown).AsTask());
        Assert.Equal("unrecognized_frame_prefix", unknownError.Code);

        await using var tls = new MemoryStream([0x16, 0x03, 0x01, 0, 64, 0x01]);
        var tlsError = await Assert.ThrowsAsync<FrameReadException>(() => CreateReader().ReadFrameAsync(tls).AsTask());
        Assert.Equal("unexpected_tls_handshake", tlsError.Code);
    }

    private static StreamFrameReader CreateReader() => new(65_536, SyslogFrameMode.Auto, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

    private sealed class SingleByteStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 1)], cancellationToken);
    }
}
