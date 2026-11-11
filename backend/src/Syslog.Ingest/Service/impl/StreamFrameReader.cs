using System.Diagnostics;
using Syslog.Engine.Common;

namespace Syslog.Ingest.Service.impl;

/// <summary>只从给定流读取，固定缓冲上限；帧超时从首字节开始，不因慢速续传无限延长。</summary>
public sealed class StreamFrameReader
{
    private readonly byte[] buffer;
    private readonly int maximum;
    private readonly TimeSpan idleTimeout;
    private readonly TimeSpan frameTimeout;
    private int buffered;
    private long frameStarted;

    public SyslogFrameMode? EffectiveMode { get; private set; }

    public StreamFrameReader(int maxFrameLength, SyslogFrameMode mode, TimeSpan idleTimeout, TimeSpan frameTimeout)
    {
        if (maxFrameLength is < 1 or > 65_536) throw new ArgumentOutOfRangeException(nameof(maxFrameLength));
        if (idleTimeout <= TimeSpan.Zero || frameTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(frameTimeout));
        maximum = maxFrameLength;
        EffectiveMode = mode == SyslogFrameMode.Auto ? null : mode;
        this.idleTimeout = idleTimeout;
        this.frameTimeout = frameTimeout;
        buffer = new byte[maxFrameLength + 12];
    }

    public async ValueTask<byte[]?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EffectiveMode ??= SyslogFrameDecoder.DetectMode(buffer.AsSpan(0, buffered));
            var decoded = SyslogFrameDecoder.Decode(buffer.AsSpan(0, buffered), EffectiveMode ?? SyslogFrameMode.Auto, maximum);
            if (decoded.Status == SyslogFrameDecodeStatus.Invalid)
                throw new FrameReadException(decoded.FailureCode, buffered);
            if (decoded.Status == SyslogFrameDecodeStatus.Complete)
            {
                var result = buffer.AsSpan(decoded.PayloadOffset, decoded.PayloadLength).ToArray();
                buffered -= decoded.ConsumedBytes;
                buffer.AsSpan(decoded.ConsumedBytes, buffered).CopyTo(buffer);
                if (buffered == 0) frameStarted = 0;
                return result;
            }
            if (buffered == buffer.Length) throw new FrameReadException("frame_too_large", buffered);
            var remaining = buffered == 0 ? idleTimeout : frameTimeout - Stopwatch.GetElapsedTime(frameStarted);
            if (remaining <= TimeSpan.Zero) throw new FrameReadException("frame_timeout", buffered);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(remaining);
            int read;
            try { read = await stream.ReadAsync(buffer.AsMemory(buffered), timeout.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new FrameReadException(buffered == 0 ? "idle_timeout" : "frame_timeout", buffered);
            }
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

/// <summary>仅携带稳定错误码和已观察字节数，不传播报文内容或底层连接信息。</summary>
public sealed class FrameReadException(string code, int observedBytes) : IOException(code)
{
    public string Code { get; } = code;
    public int ObservedBytes { get; } = observedBytes;
}
