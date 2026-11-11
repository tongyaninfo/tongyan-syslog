using System.Diagnostics;
using Syslog.Engine.Common;
using Syslog.Ingest.Models;

namespace Syslog.Ingest.Service.impl;

/// <summary>RELP 固定大小缓冲，帧期限从首字节开始计算；既支持半包也支持一个读取中的多帧。</summary>
public sealed class RelpFrameReader
{
    private readonly byte[] buffer;
    private readonly int maximum;
    private readonly TimeSpan idleTimeout;
    private readonly TimeSpan frameTimeout;
    private int buffered;
    private long frameStarted;

    public RelpFrameReader(int maxMessageBytes, TimeSpan idleTimeout, TimeSpan frameTimeout)
    {
        if (maxMessageBytes is < 1 or > 65_536) throw new ArgumentOutOfRangeException(nameof(maxMessageBytes));
        if (idleTimeout <= TimeSpan.Zero || frameTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(frameTimeout));
        maximum = maxMessageBytes;
        this.idleTimeout = idleTimeout;
        this.frameTimeout = frameTimeout;
        buffer = new byte[Math.Max(4096, maximum) + RelpFrameDecoder.MaximumHeaderBytes];
    }

    public async ValueTask<RelpFrame?> ReadAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var decoded = RelpFrameDecoder.Decode(buffer.AsSpan(0, buffered), maximum);
            if (decoded.Status == SyslogFrameDecodeStatus.Invalid) throw new FrameReadException(decoded.FailureCode, buffered);
            if (decoded.Status == SyslogFrameDecodeStatus.Complete)
            {
                var frame = new RelpFrame(decoded.Transaction, decoded.Command, buffer.AsSpan(decoded.PayloadOffset, decoded.PayloadLength).ToArray());
                buffered -= decoded.ConsumedBytes;
                buffer.AsSpan(decoded.ConsumedBytes, buffered).CopyTo(buffer);
                // 已完整收到的后续帧不因上一条持久化等待被错误判超时。
                frameStarted = buffered == 0 ? 0 : Stopwatch.GetTimestamp();
                return frame;
            }
            if (buffered == buffer.Length) throw new FrameReadException("relp_frame_too_large", buffered);
            var remaining = buffered == 0 ? idleTimeout : frameTimeout - Stopwatch.GetElapsedTime(frameStarted);
            if (remaining <= TimeSpan.Zero) throw new FrameReadException("frame_timeout", buffered);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(remaining);
            int read;
            try { read = await stream.ReadAsync(buffer.AsMemory(buffered), timeout.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { throw new FrameReadException(buffered == 0 ? "idle_timeout" : "frame_timeout", buffered); }
            if (read == 0)
            {
                if (buffered > 0) throw new FrameReadException("truncated_frame", buffered);
                return null;
            }
            if (buffered == 0) frameStarted = Stopwatch.GetTimestamp();
            buffered += read;
        }
    }
}
