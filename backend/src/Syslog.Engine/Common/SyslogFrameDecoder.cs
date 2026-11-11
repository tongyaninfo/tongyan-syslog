namespace Syslog.Engine.Common;

public enum SyslogFrameMode { OctetCounting, Newline, Auto }
public enum SyslogFrameDecodeStatus { Complete, NeedMoreData, Invalid }

/// <summary>偏移及长度仅引用调用方缓冲区；Invalid 后必须关闭连接，不能从未知边界继续猜测。</summary>
public readonly record struct SyslogFrameDecodeResult(SyslogFrameDecodeStatus Status, int ConsumedBytes = 0,
    int PayloadOffset = 0, int PayloadLength = 0, string FailureCode = "");

/// <summary>无 I/O 的流帧解码器；调用方负责有界累积、超时、背压和消费已完成帧。</summary>
public static class SyslogFrameDecoder
{
    internal const byte TlsHandshakeRecord = 0x16;
    internal const int TlsClientHelloPrefixBytes = 6;

    public static SyslogFrameDecodeResult Decode(ReadOnlySpan<byte> buffer, SyslogFrameMode mode, int maxFrameLength = 65_536)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFrameLength);
        if (mode == SyslogFrameMode.Auto)
        {
            if (buffer.IsEmpty) return new(SyslogFrameDecodeStatus.NeedMoreData);
            if (DetectMode(buffer) is not { } detected)
                return Invalid(LooksLikeTlsClientHello(buffer) ? "unexpected_tls_handshake" : "unrecognized_frame_prefix");
            mode = detected;
        }
        return mode switch
        {
            SyslogFrameMode.OctetCounting => DecodeCounted(buffer, maxFrameLength),
            SyslogFrameMode.Newline => DecodeLine(buffer, maxFrameLength),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    /// <summary>按 RFC 6587 的首字节选择分帧方式，长度和空格仍由字节计数解码器严格校验。</summary>
    public static SyslogFrameMode? DetectMode(ReadOnlySpan<byte> buffer) => buffer.IsEmpty ? null : buffer[0] switch
    {
        (byte)'<' => SyslogFrameMode.Newline,
        >= (byte)'0' and <= (byte)'9' => SyslogFrameMode.OctetCounting,
        _ => null,
    };

    // 只识别已经缓冲的短记录头，不为未知报文继续读取或等待完整握手。
    internal static bool LooksLikeTlsClientHello(ReadOnlySpan<byte> buffer) =>
        buffer.Length >= TlsClientHelloPrefixBytes && buffer[0] == TlsHandshakeRecord && buffer[1] == 3 && buffer[2] <= 3
        && (buffer[3] > 0 || buffer[4] >= 4) && buffer[5] == 0x01;

    private static SyslogFrameDecodeResult DecodeCounted(ReadOnlySpan<byte> buffer, int maximum)
    {
        if (buffer.IsEmpty) return new(SyslogFrameDecodeStatus.NeedMoreData);
        if (buffer[0] is < (byte)'1' or > (byte)'9') return Invalid("invalid_length_prefix");
        var length = 0;
        for (var i = 0; i < buffer.Length; i++)
        {
            var current = buffer[i];
            if (current == (byte)' ')
            {
                var available = buffer.Length - i - 1;
                if (available < length) return new(SyslogFrameDecodeStatus.NeedMoreData);
                return new(SyslogFrameDecodeStatus.Complete, i + 1 + length, i + 1, length);
            }
            if (current is < (byte)'0' or > (byte)'9') return Invalid("invalid_length_prefix");
            var digit = current - (byte)'0';
            // 累积前检查，避免恶意超长十进制前缀发生整数溢出。
            if (length > (maximum - digit) / 10 || digit > maximum) return Invalid("frame_too_large");
            length = length * 10 + digit;
            if (length > maximum) return Invalid("frame_too_large");
        }
        return new(SyslogFrameDecodeStatus.NeedMoreData);
    }

    private static SyslogFrameDecodeResult DecodeLine(ReadOnlySpan<byte> buffer, int maximum)
    {
        var end = buffer.IndexOf((byte)'\n');
        if (end < 0)
        {
            // 恰好上限后的 CR 可能是 CRLF 分隔符，最多额外等待一个字节。
            if (buffer.Length > maximum && !(buffer.Length == (long)maximum + 1 && buffer[^1] == (byte)'\r'))
                return Invalid("frame_too_large");
            return new(SyslogFrameDecodeStatus.NeedMoreData);
        }
        var length = end > 0 && buffer[end - 1] == (byte)'\r' ? end - 1 : end;
        if (length == 0) return Invalid("empty_frame");
        if (length > maximum) return Invalid("frame_too_large");
        return new(SyslogFrameDecodeStatus.Complete, end + 1, 0, length);
    }

    private static SyslogFrameDecodeResult Invalid(string code) => new(SyslogFrameDecodeStatus.Invalid, FailureCode: code);
}
