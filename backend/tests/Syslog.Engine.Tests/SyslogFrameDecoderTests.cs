using System.Text;
using Syslog.Engine.Common;

namespace Syslog.Engine.Tests;

public sealed class SyslogFrameDecoderTests
{
    [Fact]
    public void OctetCountingWaitsForAllUtf8BytesAndConsumesOnlyOneFrame()
    {
        var payload = Encoding.UTF8.GetBytes("<14>中文\n内容");
        var prefix = Encoding.ASCII.GetBytes($"{payload.Length} ");
        var frame = prefix.Concat(payload).ToArray();
        for (var length = 0; length < frame.Length; length++)
            Assert.Equal(SyslogFrameDecodeStatus.NeedMoreData, SyslogFrameDecoder.Decode(frame.AsSpan(0, length), SyslogFrameMode.OctetCounting).Status);
        var glued = frame.Concat(Encoding.ASCII.GetBytes("4 next")).ToArray();
        var result = SyslogFrameDecoder.Decode(glued, SyslogFrameMode.OctetCounting);
        Assert.Equal(SyslogFrameDecodeStatus.Complete, result.Status);
        Assert.Equal(frame.Length, result.ConsumedBytes);
        Assert.Equal(payload, glued.AsSpan(result.PayloadOffset, result.PayloadLength).ToArray());
        var next = SyslogFrameDecoder.Decode(glued.AsSpan(result.ConsumedBytes), SyslogFrameMode.OctetCounting);
        Assert.Equal(SyslogFrameDecodeStatus.Complete, next.Status);
        Assert.Equal(4, next.PayloadLength);
    }

    [Theory]
    [InlineData("0 data", "invalid_length_prefix")]
    [InlineData("04 data", "invalid_length_prefix")]
    [InlineData("-1 data", "invalid_length_prefix")]
    [InlineData("1x data", "invalid_length_prefix")]
    [InlineData("65537 ", "frame_too_large")]
    [InlineData("999999999999999999999999999", "frame_too_large")]
    public void InvalidAndOversizedLengthPrefixesAreRejectedBeforeBufferGrowth(string frame, string code)
    {
        var result = SyslogFrameDecoder.Decode(Encoding.ASCII.GetBytes(frame), SyslogFrameMode.OctetCounting);
        Assert.Equal(SyslogFrameDecodeStatus.Invalid, result.Status);
        Assert.Equal(code, result.FailureCode);
    }

    [Theory]
    [InlineData("body\nnext\n", 5, 4)]
    [InlineData("body\r\nnext\n", 6, 4)]
    public void LineFramingRemovesOnlyConfiguredDelimiter(string frame, int consumed, int length)
    {
        var result = SyslogFrameDecoder.Decode(Encoding.ASCII.GetBytes(frame), SyslogFrameMode.Newline, 4);
        Assert.Equal(SyslogFrameDecodeStatus.Complete, result.Status);
        Assert.Equal(consumed, result.ConsumedBytes);
        Assert.Equal(length, result.PayloadLength);
    }

    [Fact]
    public void ExactLineLimitAllowsPartialCrLfButRejectsUnboundedLines()
    {
        Assert.Equal(SyslogFrameDecodeStatus.NeedMoreData, DecodeLine("body", 4).Status);
        Assert.Equal(SyslogFrameDecodeStatus.NeedMoreData, DecodeLine("body\r", 4).Status);
        Assert.Equal(SyslogFrameDecodeStatus.Complete, DecodeLine("body\r\n", 4).Status);
        Assert.Equal("frame_too_large", DecodeLine("bodyx", 4).FailureCode);
        Assert.Equal("frame_too_large", DecodeLine("body\rx", 4).FailureCode);
        Assert.Equal("empty_frame", DecodeLine("\n", 4).FailureCode);
        Assert.Equal("empty_frame", DecodeLine("\r\n", 4).FailureCode);
    }

    [Fact]
    public void SmallFrameLimitCannotOverflowOrAcceptLongPrefix()
    {
        Assert.Equal("frame_too_large", SyslogFrameDecoder.Decode("9 "u8, SyslogFrameMode.OctetCounting, 1).FailureCode);
        Assert.Equal(SyslogFrameDecodeStatus.Complete, SyslogFrameDecoder.Decode("1 x"u8, SyslogFrameMode.OctetCounting, 1).Status);
    }

    [Theory]
    [InlineData("<14>body\r\n", 0, 8)]
    [InlineData("8 <14>body", 2, 8)]
    public void AutoAcceptsBothSyslogFramings(string frame, int payloadOffset, int payloadLength)
    {
        var result = SyslogFrameDecoder.Decode(Encoding.ASCII.GetBytes(frame), SyslogFrameMode.Auto, 8);
        Assert.Equal(SyslogFrameDecodeStatus.Complete, result.Status);
        Assert.Equal(frame.Length, result.ConsumedBytes);
        Assert.Equal(payloadOffset, result.PayloadOffset);
        Assert.Equal(payloadLength, result.PayloadLength);
    }

    private static SyslogFrameDecodeResult DecodeLine(string text, int maximum) =>
        SyslogFrameDecoder.Decode(Encoding.ASCII.GetBytes(text), SyslogFrameMode.Newline, maximum);
}
