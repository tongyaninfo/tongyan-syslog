using System.Globalization;
using System.Text;
using Syslog.Engine.Common;

namespace Syslog.Ingest.Service.impl;

/// <summary>RELP 服务端会话。一次只处理一个确认，利用 TCP 背压限定在途内存；失败不发送成功 ACK。</summary>
public sealed class RelpSession(int maxMessageBytes, TimeSpan idleTimeout, TimeSpan frameTimeout)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public async Task RunAsync(Stream stream, Func<ReadOnlyMemory<byte>, CancellationToken, Task<bool>> accept,
        CancellationToken cancellationToken = default)
    {
        var reader = new RelpFrameReader(maxMessageBytes, idleTimeout, frameTimeout);
        var opened = false;
        var expectedTransaction = 1;
        while (await reader.ReadAsync(stream, cancellationToken) is { } frame)
        {
            if (frame.Transaction != expectedTransaction) throw new FrameReadException("relp_invalid_transaction", frame.Payload.Length);
            expectedTransaction = expectedTransaction == RelpFrameDecoder.MaximumTransaction ? 1 : expectedTransaction + 1;
            if (!opened)
            {
                if (frame.Command != "open" || GetOfferedVersion(frame.Payload) is not { } version)
                {
                    await RespondAsync(stream, frame.Transaction, "500 Unsupported negotiation", cancellationToken);
                    throw new FrameReadException("relp_negotiation_failed", 0);
                }
                // librelp 声明版本 0，收到高于自身版本的响应时会断开会话。
                await RespondAsync(stream, frame.Transaction, $"200 OK\nrelp_version={version}\ncommands=syslog", cancellationToken);
                opened = true;
                continue;
            }

            if (frame.Command == "close" && frame.Payload.Length == 0)
            {
                await RespondAsync(stream, frame.Transaction, "200 OK", cancellationToken);
                return;
            }
            if (frame.Command != "syslog" || frame.Payload.Length == 0)
            {
                await RespondAsync(stream, frame.Transaction, "500 Unsupported command", cancellationToken);
                throw new FrameReadException("relp_invalid_command", 0);
            }

            bool accepted;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                // 持久暂存堵塞也有期限。超时后关闭会话，由发送端保留未确认事务并决定重试。
                timeout.CancelAfter(frameTimeout);
                try { accepted = await accept(frame.Payload, timeout.Token).WaitAsync(timeout.Token); }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                { throw new FrameReadException("relp_ack_timeout", 0); }
            }
            await RespondAsync(stream, frame.Transaction, accepted ? "200 OK" : "500 Not accepted", cancellationToken);
            if (!accepted) return;
        }
    }

    private async Task RespondAsync(Stream stream, int transaction, string response, CancellationToken ct)
    {
        var payload = Encoding.ASCII.GetBytes(response);
        var header = Encoding.ASCII.GetBytes(transaction.ToString(CultureInfo.InvariantCulture) + " rsp " + payload.Length.ToString(CultureInfo.InvariantCulture) + " ");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(frameTimeout);
        try
        {
            await stream.WriteAsync(header, timeout.Token);
            await stream.WriteAsync(payload, timeout.Token);
            await stream.WriteAsync("\n"u8.ToArray(), timeout.Token);
            await stream.FlushAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new FrameReadException("relp_response_timeout", 0); }
    }

    private static string? GetOfferedVersion(byte[] bytes)
    {
        string text;
        try { text = StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException) { return null; }
        var offers = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length is 0 or > 64) return null;
        foreach (var line in lines)
        {
            var separator = line.IndexOf('=');
            if (separator is < 1 or > 32 || line.Any(character => char.IsControl(character))) return null;
            var values = line[(separator + 1)..];
            if (values.Split(',').Any(value => value.Length > 255) || !offers.TryAdd(line[..separator], values)) return null;
        }
        return offers.TryGetValue("relp_version", out var version) && version is "0" or "1"
            && offers.TryGetValue("commands", out var commands) && commands.Split(',').Contains("syslog", StringComparer.Ordinal)
            ? version : null;
    }
}
