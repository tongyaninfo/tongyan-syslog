using System.Text;

namespace Syslog.Engine.Common;

public readonly record struct RelpFrameDecodeResult(SyslogFrameDecodeStatus Status, int Transaction = 0, string Command = "",
    int ConsumedBytes = 0, int PayloadOffset = 0, int PayloadLength = 0, string FailureCode = "");

/// <summary>RELP 字节帧解码。长度和事务号先校验再分配，报文内容不参与协议边界判断。</summary>
public static class RelpFrameDecoder
{
    public const int MaximumTransaction = 999_999_999;
    public const int MaximumHeaderBytes = 54;

    public static RelpFrameDecodeResult Decode(ReadOnlySpan<byte> buffer, int maxMessageBytes = 65_536)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMessageBytes);
        if (!buffer.IsEmpty && buffer[0] == SyslogFrameDecoder.TlsHandshakeRecord)
        {
            // TLS 握手头可能分多次读取。只等短前缀，不读取完整握手或在 RELP 端口降级接收。
            if (buffer.Length < SyslogFrameDecoder.TlsClientHelloPrefixBytes) return new(SyslogFrameDecodeStatus.NeedMoreData);
            return SyslogFrameDecoder.LooksLikeTlsClientHello(buffer) ? Invalid("relp_unexpected_tls_handshake") : Invalid();
        }
        var index = 0;
        var tx = ReadNumber(buffer, ref index, out var transaction);
        if (tx != SyslogFrameDecodeStatus.Complete) return Result(tx);
        if (buffer[index++] != (byte)' ' || transaction == 0) return Invalid();

        var commandStart = index;
        while (index < buffer.Length && buffer[index] != (byte)' ')
        {
            if (index - commandStart >= 32 || !IsLetter(buffer[index])) return Invalid();
            index++;
        }
        if (index == buffer.Length) return new(SyslogFrameDecodeStatus.NeedMoreData);
        if (index == commandStart) return Invalid();
        var command = Encoding.ASCII.GetString(buffer[commandStart..index]);
        index++;
        var lengthState = ReadNumber(buffer, ref index, out var length);
        if (lengthState != SyslogFrameDecodeStatus.Complete) return Result(lengthState);
        // 协商内容有单独小上限；syslog 原文遵守管理员配置的报文上限。
        var maximum = command == "open" ? 4096 : maxMessageBytes;
        if (length > maximum) return Invalid("relp_frame_too_large");
        if (length == 0)
        {
            if (buffer[index] != (byte)'\n') return Invalid();
            return new(SyslogFrameDecodeStatus.Complete, transaction, command, index + 1, index, 0);
        }
        if (buffer[index++] != (byte)' ') return Invalid();
        if (buffer.Length - index <= length) return new(SyslogFrameDecodeStatus.NeedMoreData);
        if (buffer[index + length] != (byte)'\n') return Invalid();
        return new(SyslogFrameDecodeStatus.Complete, transaction, command, index + length + 1, index, length);
    }

    private static SyslogFrameDecodeStatus ReadNumber(ReadOnlySpan<byte> buffer, ref int index, out int value)
    {
        value = 0;
        var start = index;
        while (index < buffer.Length && buffer[index] is >= (byte)'0' and <= (byte)'9')
        {
            if (index - start >= 9) return SyslogFrameDecodeStatus.Invalid;
            value = value * 10 + buffer[index++] - (byte)'0';
        }
        if (index == buffer.Length) return SyslogFrameDecodeStatus.NeedMoreData;
        return index == start ? SyslogFrameDecodeStatus.Invalid : SyslogFrameDecodeStatus.Complete;
    }

    private static bool IsLetter(byte value) => value is >= (byte)'a' and <= (byte)'z' or >= (byte)'A' and <= (byte)'Z';
    private static RelpFrameDecodeResult Invalid(string code = "relp_invalid_frame") => new(SyslogFrameDecodeStatus.Invalid, FailureCode: code);
    private static RelpFrameDecodeResult Result(SyslogFrameDecodeStatus state) => state == SyslogFrameDecodeStatus.Invalid ? Invalid() : new(state);
}
