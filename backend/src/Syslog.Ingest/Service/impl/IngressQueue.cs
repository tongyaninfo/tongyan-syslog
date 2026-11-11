using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Syslog.Ingest.Models;
using Syslog.Ingest.Options;
using Syslog.Shared.Models;

namespace Syslog.Ingest.Service.impl;

public sealed record ReceivedMessage(byte[] Bytes, ParseContext Context, TaskCompletionSource<bool>? Persisted = null,
    byte IgnoredSeverityMask = 0);

/// <summary>条数和字节同时受限，TryWrite 失败时计入丢弃而非静默覆盖。</summary>
public sealed class IngressQueue
{
    private readonly Channel<ReceivedMessage> channel;
    private readonly IngestOptions options;
    private readonly IngestStatus status;

    public IngressQueue(IOptions<IngestOptions> options, IngestStatus status)
    {
        this.options = options.Value;
        this.status = status;
        if (this.options.QueueCapacity < 1 || this.options.QueueBytes < 1 || this.options.MaxMessageBytes < 1)
            throw new ArgumentException("接收队列配置无效");
        channel = Channel.CreateBounded<ReceivedMessage>(new BoundedChannelOptions(this.options.QueueCapacity)
        { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    }

    public bool TryEnqueue(ReceivedMessage message)
    {
        status.RecordReceive(message.Bytes.Length, message.Context.ReceivedAt, message.Context.SourceId);
        if (message.Bytes.Length == 0 || message.Bytes.Length > options.MaxMessageBytes)
            return Drop();
        // 包含每条固定元数据预算，避免零碎短消息绕开内存限制。
        long cost = message.Bytes.Length + 512L;
        if (Interlocked.Add(ref status.QueuedBytes, cost) > options.QueueBytes)
        {
            Interlocked.Add(ref status.QueuedBytes, -cost);
            return Drop();
        }
        if (channel.Writer.TryWrite(message)) return true;
        Interlocked.Add(ref status.QueuedBytes, -cost);
        return Drop();
    }

    public bool TryRead(out ReceivedMessage? message)
    {
        if (!channel.Reader.TryRead(out message)) return false;
        Interlocked.Add(ref status.QueuedBytes, -(message.Bytes.Length + 512L));
        return true;
    }

    public ValueTask<bool> WaitAsync(CancellationToken cancellationToken) => channel.Reader.WaitToReadAsync(cancellationToken);
    public int Count => channel.Reader.Count;
    private bool Drop() { Interlocked.Increment(ref status.Dropped); return false; }
}
