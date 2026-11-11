using System.Text;
using Syslog.Engine.Common;
using Xunit;

namespace Syslog.Engine.Tests;

public sealed class RelpFrameDecoderTests
{
    [Fact]
    public void FrameUsesByteLengthAndPreservesEmbeddedNewlinesAndBinaryEvidence()
    {
        var payload = Encoding.UTF8.GetBytes("<14>中文\n").Concat(new byte[] { 0, 255 }).ToArray();
        var frame = Encoding.ASCII.GetBytes($"2 syslog {payload.Length} ").Concat(payload).Concat("\n3 close 0\n"u8.ToArray()).ToArray();
        var decoded = RelpFrameDecoder.Decode(frame);
        Assert.Equal(SyslogFrameDecodeStatus.Complete, decoded.Status);
        Assert.Equal(2, decoded.Transaction);
        Assert.Equal("syslog", decoded.Command);
        Assert.Equal(payload, frame.AsSpan(decoded.PayloadOffset, decoded.PayloadLength).ToArray());
        var close = RelpFrameDecoder.Decode(frame.AsSpan(decoded.ConsumedBytes));
        Assert.Equal("close", close.Command);
        Assert.Equal(0, close.PayloadLength);
    }

    [Fact]
    public void EveryPartialPrefixWaitsUntilFullFrameAndMaximumTransactionIsAccepted()
    {
        var frame = "999999999 syslog 4 data\n"u8.ToArray();
        for (var length = 0; length < frame.Length; length++)
            Assert.Equal(SyslogFrameDecodeStatus.NeedMoreData, RelpFrameDecoder.Decode(frame.AsSpan(0, length)).Status);
        Assert.Equal(RelpFrameDecoder.MaximumTransaction, RelpFrameDecoder.Decode(frame).Transaction);
    }

    [Theory]
    [InlineData("0 syslog 1 x\n")]
    [InlineData("1000000000 syslog 1 x\n")]
    [InlineData("1 ! 0\n")]
    [InlineData("1 abcdefghijklmnopqrstuvwxyzabcdefg 0\n")]
    [InlineData("1 syslog -1 x\n")]
    [InlineData("1 close 0 \n")]
    [InlineData("1 syslog 1 x!")]
    [InlineData("1 syslog 1000000000 ")]
    public void MalformedFrameIsRejectedWithoutGuessingAnotherBoundary(string content) =>
        Assert.Equal(SyslogFrameDecodeStatus.Invalid, RelpFrameDecoder.Decode(Encoding.ASCII.GetBytes(content)).Status);

    [Theory]
    [InlineData("1 syslog 11 ", 10)]
    [InlineData("1 open 4097 ", 65536)]
    public void OversizedDeclaredLengthIsRejectedBeforePayloadAllocation(string content, int maximum) =>
        Assert.Equal("relp_frame_too_large", RelpFrameDecoder.Decode(Encoding.ASCII.GetBytes(content), maximum).FailureCode);
}
