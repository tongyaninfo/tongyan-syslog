using System.Text;
using Syslog.Engine.Common;
using Syslog.Ingest.Service.impl;
using Xunit;

namespace Syslog.Ingest.Tests;

/// <summary>RELP 只使用内存双向流，验证真正的命令/回应交互，不绑定宿主端口。</summary>
public sealed class RelpSessionTests
{
    private const string Offers = "relp_version=1\nrelp_software=synthetic,1,example.invalid\ncommands=syslog";

    [Theory]
    [InlineData(1, "1")]
    [InlineData(65536, "1")]
    [InlineData(1, "0")]
    [InlineData(65536, "0")]
    public async Task OpenSyslogAndCloseHandlePartialAndCoalescedFrames(int chunkSize, string version)
    {
        var evidence = Encoding.UTF8.GetBytes("<14>合成\n报文").Concat(new byte[] { 0, 255 }).ToArray();
        await using var stream = new DuplexStream(Combine(Open(version), Frame(2, "syslog", evidence), Frame(3, "close", [])), chunkSize);
        var received = new List<byte[]>();
        await Session().RunAsync(stream, (payload, _) => { received.Add(payload.ToArray()); return Task.FromResult(true); });
        Assert.Equal(evidence, Assert.Single(received));
        var responses = Responses(stream.Output);
        Assert.Equal(new[] { 1, 2, 3 }, responses.Select(x => x.Transaction));
        Assert.Equal($"200 OK\nrelp_version={version}\ncommands=syslog", responses[0].Text);
        Assert.All(responses.Skip(1), item => Assert.Equal("200 OK", item.Text));
    }

    [Fact]
    public async Task SyslogAckWaitsForAcceptanceAndDoesNotReadNextTransactionEarly()
    {
        await using var stream = new DuplexStream(Combine(Open(), Frame(2, "syslog", "one"u8.ToArray()), Frame(3, "syslog", "two"u8.ToArray())));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accept = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var running = Session().RunAsync(stream, (_, _) => { calls++; entered.TrySetResult(); return accept.Task; });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, calls);
        Assert.Single(Responses(stream.Output));
        accept.SetResult(true);
        await running.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, calls);
        Assert.Equal(3, Responses(stream.Output).Count);
    }

    [Fact]
    public async Task RejectedMessageGetsNegativeAckAndStopsSession()
    {
        await using var stream = new DuplexStream(Combine(Open(), Frame(2, "syslog", "one"u8.ToArray()), Frame(3, "syslog", "two"u8.ToArray())));
        var calls = 0;
        await Session().RunAsync(stream, (_, _) => { calls++; return Task.FromResult(false); });
        Assert.Equal(1, calls);
        Assert.Equal("500 Not accepted", Responses(stream.Output)[1].Text);
    }

    [Theory]
    [InlineData("relp_version=2\ncommands=syslog")]
    [InlineData("commands=syslog")]
    [InlineData("relp_version=1\ncommands=eventlog")]
    [InlineData("relp_version=1\nrelp_version=1\ncommands=syslog")]
    public async Task InvalidOffersNeverAcceptLogs(string offers)
    {
        await using var stream = new DuplexStream(Frame(1, "open", Encoding.ASCII.GetBytes(offers)));
        var error = await Assert.ThrowsAsync<FrameReadException>(() => Session().RunAsync(stream, (_, _) => throw new InvalidOperationException()));
        Assert.Equal("relp_negotiation_failed", error.Code);
        Assert.StartsWith("500 ", Assert.Single(Responses(stream.Output)).Text);
    }

    [Fact]
    public async Task SyslogBeforeOpenAndRepeatedOpenAreRejected()
    {
        await using var early = new DuplexStream(Frame(1, "syslog", "data"u8.ToArray()));
        Assert.Equal("relp_negotiation_failed", (await Assert.ThrowsAsync<FrameReadException>(() => Session().RunAsync(early, (_, _) => throw new InvalidOperationException()))).Code);
        await using var repeated = new DuplexStream(Combine(Open(), Frame(2, "open", Encoding.ASCII.GetBytes(Offers))));
        Assert.Equal("relp_invalid_command", (await Assert.ThrowsAsync<FrameReadException>(() => Session().RunAsync(repeated, (_, _) => throw new InvalidOperationException()))).Code);
    }

    [Fact]
    public async Task DuplicateTransactionIsRejectedBeforeDuplicateAcceptance()
    {
        await using var stream = new DuplexStream(Combine(Open(), Frame(1, "syslog", "data"u8.ToArray())));
        var error = await Assert.ThrowsAsync<FrameReadException>(() => Session().RunAsync(stream, (_, _) => throw new InvalidOperationException()));
        Assert.Equal("relp_invalid_transaction", error.Code);
        Assert.Single(Responses(stream.Output));
    }

    [Fact]
    public async Task AckTimeoutDoesNotEmitSuccessForPendingMessage()
    {
        await using var stream = new DuplexStream(Combine(Open(), Frame(2, "syslog", "data"u8.ToArray())));
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new RelpSession(1024, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(30));
        var error = await Assert.ThrowsAsync<FrameReadException>(() => session.RunAsync(stream, (_, _) => pending.Task));
        Assert.Equal("relp_ack_timeout", error.Code);
        Assert.Single(Responses(stream.Output));
        pending.SetResult(true);
        Assert.Single(Responses(stream.Output));
    }

    [Fact]
    public async Task CancellationIsPropagatedWithoutSuccessAck()
    {
        await using var stream = new DuplexStream(Combine(Open(), Frame(2, "syslog", "data"u8.ToArray())));
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Session().RunAsync(stream, async (_, ct) =>
        { cancellation.Cancel(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); return true; }, cancellation.Token));
        Assert.Single(Responses(stream.Output));
    }

    [Theory]
    [InlineData("1 open 12 tiny", "truncated_frame")]
    [InlineData("1 syslog 10000 ", "relp_frame_too_large")]
    public async Task ReaderRejectsTruncatedOrOversizedFrames(string content, string expected)
    {
        await using var stream = new MemoryStream(Encoding.ASCII.GetBytes(content));
        var reader = new RelpFrameReader(1024, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        Assert.Equal(expected, (await Assert.ThrowsAsync<FrameReadException>(() => reader.ReadAsync(stream).AsTask())).Code);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(65536)]
    public async Task TlsClientHelloOnRelpPortIsReportedWithoutAcceptingAnyMessage(int chunkSize)
    {
        await using var stream = new DuplexStream([0x16, 0x03, 0x01, 0x00, 0x64, 0x01], chunkSize);
        var error = await Assert.ThrowsAsync<FrameReadException>(() =>
            Session().RunAsync(stream, (_, _) => throw new InvalidOperationException()));
        Assert.Equal("relp_unexpected_tls_handshake", error.Code);
        Assert.Empty(stream.Output);
    }

    [Fact]
    public async Task SlowBytesCannotResetAbsoluteFrameTimeout()
    {
        await using var stream = new DuplexStream(Frame(1, "syslog", "data"u8.ToArray()), 1, TimeSpan.FromMilliseconds(20));
        var reader = new RelpFrameReader(1024, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(55));
        Assert.Equal("frame_timeout", (await Assert.ThrowsAsync<FrameReadException>(() => reader.ReadAsync(stream).AsTask())).Code);
    }

    private static RelpSession Session() => new(65536, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    private static byte[] Open(string version = "1") => Frame(1, "open", Encoding.ASCII.GetBytes(
        $"relp_version={version}\nrelp_software=synthetic,1,example.invalid\ncommands=syslog"));
    private static byte[] Frame(int transaction, string command, byte[] bytes) => Combine(
        Encoding.ASCII.GetBytes($"{transaction} {command} {bytes.Length}" + (bytes.Length > 0 ? " " : "")), bytes, "\n"u8.ToArray());
    private static byte[] Combine(params byte[][] values) => values.SelectMany(x => x).ToArray();
    private static List<(int Transaction, string Text)> Responses(byte[] bytes)
    {
        var responses = new List<(int, string)>();
        var offset = 0;
        while (offset < bytes.Length)
        {
            var frame = RelpFrameDecoder.Decode(bytes.AsSpan(offset));
            Assert.Equal(SyslogFrameDecodeStatus.Complete, frame.Status);
            Assert.Equal("rsp", frame.Command);
            responses.Add((frame.Transaction, Encoding.ASCII.GetString(bytes, offset + frame.PayloadOffset, frame.PayloadLength)));
            offset += frame.ConsumedBytes;
        }
        return responses;
    }

    private sealed class DuplexStream(byte[] bytes, int chunkSize = 65536, TimeSpan delay = default) : Stream
    {
        private readonly MemoryStream input = new(bytes);
        private readonly MemoryStream output = new();
        public byte[] Output => output.ToArray();
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) => input.Read(buffer, offset, Math.Min(count, chunkSize));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
            return await input.ReadAsync(buffer[..Math.Min(buffer.Length, chunkSize)], ct);
        }
        public override void Write(byte[] buffer, int offset, int count) => output.Write(buffer, offset, count);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) => output.WriteAsync(buffer, ct);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) { input.Dispose(); output.Dispose(); } base.Dispose(disposing); }
    }
}
