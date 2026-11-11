using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Syslog.Ingest.Models;
using Syslog.Ingest.Options;
using Syslog.Ingest.Service;
using Syslog.Ingest.Service.impl;
using Syslog.Shared.Models;
using Syslog.Shared.Service;

namespace Syslog.Ingest.Workers;

/// <summary>接收队列与落库解耦；存储请求失败时保留原批次并退避重试。</summary>
public sealed class IngestWorker(IngressQueue queue, ISyslogParser parser, IIdService ids, IEventSpool spool,
    ILogStore store, IOptions<IngestOptions> options, IngestStatus status, TimeProvider clock, ILiveLogBuffer? liveLogs = null) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        options.Value.Enabled ? RunAsync(stoppingToken) : Task.CompletedTask;

    internal async Task RunAsync(CancellationToken stoppingToken)
    {
        if (options.Value.BatchEvents is < 1 or > 1024 || options.Value.BatchRawBytes is < 65536 or > 512 * 1024
            || options.Value.BatchFlushMilliseconds is < 10 or > 5000)
        {
            status.State = "failed";
            status.FailureCode = "invalid_ingest_configuration";
            return;
        }
        using var stages = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        try
        {
            await spool.OpenAsync(stoppingToken);
            status.State = "running";
            var capture = CaptureAsync(stages.Token);
            var dispatch = DispatchAsync(stages.Token);
            await Task.WhenAny(capture, dispatch);
            await stages.CancelAsync();
            await Task.WhenAll(capture, dispatch);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { status.State = "stopped"; }
        catch (SpoolRecoveryException error)
        {
            status.State = "failed";
            status.FailureCode = error.Code;
            await stages.CancelAsync();
        }
        catch (Exception)
        {
            // 不把原文、内部路径或连接串写入应用日志；管理接口通过稳定错误码告知需要恢复。
            status.State = "failed";
            status.FailureCode = "ingest_recovery_required";
            await stages.CancelAsync();
        }
    }

    private async Task CaptureAsync(CancellationToken cancellationToken)
    {
        ReceivedMessage? carry = null;
        var pending = 0;
        var confirmations = new List<TaskCompletionSource<bool>>();
        try
        {
            while (carry is not null || await queue.WaitAsync(cancellationToken))
            {
                var events = new List<LogEvent>();
                var started = clock.GetTimestamp();
                var rawBytes = 0;
                var batchId = "";
                while (events.Count < options.Value.BatchEvents && rawBytes < options.Value.BatchRawBytes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var message = carry;
                    carry = null;
                    if (message is null && (!queue.TryRead(out message) || message is null))
                    {
                        if (clock.GetElapsedTime(started).TotalMilliseconds >= options.Value.BatchFlushMilliseconds) break;
                        await Task.Delay(10, cancellationToken);
                        continue;
                    }
                    if (rawBytes + message.Bytes.Length > options.Value.BatchRawBytes)
                    {
                        carry = message;
                        break;
                    }
                    pending++;
                    var confirmation = message.Persisted;
                    if (confirmation is not null) confirmations.Add(confirmation);
                    var item = parser.Parse(message.Bytes, message.Context);
                    if (item.ParseStatus == "parsed") Interlocked.Increment(ref status.Parsed);
                    else if (item.ParseStatus == "unknown") Interlocked.Increment(ref status.ParseUnknown);
                    else Interlocked.Increment(ref status.ParseFailed);

                    if (item.Severity is >= 0 and <= 7 &&
                        (message.IgnoredSeverityMask & (1 << item.Severity.Value)) != 0)
                    {
                        Observe(item, ignored: true);
                        status.RecordIgnored(message.Context.SourceId);
                        pending--;
                        if (confirmation is not null)
                        {
                            confirmations.RemoveAt(confirmations.Count - 1);
                            // 策略已处理的 RELP 报文正常确认，避免客户端重发被忽略的完整帧。
                            confirmation.TrySetResult(true);
                        }
                        // 连续收到要忽略的报文时也要按时提交批次中的其他日志。
                        if (clock.GetElapsedTime(started).TotalMilliseconds >= options.Value.BatchFlushMilliseconds) break;
                        continue;
                    }

                    var id = await ids.NextAsync(cancellationToken);
                    if (batchId.Length == 0) batchId = id.ToString("D20", System.Globalization.CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N");
                    var recorded = item with { EventId = id, BatchId = batchId };
                    Observe(recorded, ignored: false);
                    events.Add(recorded);
                    rawBytes += message.Bytes.Length;
                }
                if (events.Count == 0) continue;
                while (!await spool.AppendAsync(events, cancellationToken))
                {
                    status.SpoolFull = true;
                    await Task.Delay(1000, cancellationToken);
                }
                pending = 0;
                // 注册来源 RELP 的确认深度是文件暂存成功，不能把内存入队当成持久成功。
                foreach (var confirmation in confirmations) confirmation.TrySetResult(true);
                confirmations.Clear();
                status.SpoolFull = false;
                Interlocked.Add(ref status.Persisted, events.Count);
            }
        }
        finally
        {
            // 未进入已确认暂存的内存数据在停止/异常时可能丢失，不能计为入库。
            Interlocked.Add(ref status.Dropped, pending + (carry is null ? 0 : 1));
            foreach (var confirmation in confirmations) confirmation.TrySetResult(false);
            carry?.Persisted?.TrySetResult(false);
            while (queue.TryRead(out var message))
            {
                message?.Persisted?.TrySetResult(false);
                Interlocked.Increment(ref status.Dropped);
            }
        }
    }

    private async Task DispatchAsync(CancellationToken cancellationToken)
    {
        var retryDelay = 1000;
        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await spool.ReadNextAsync(cancellationToken);
            if (batch is null) { await Task.Delay(100, cancellationToken); continue; }
            try
            {
                // 已标记的批次可能已被 ClickHouse 接收；复用原事件、批次和内容生成相同的去重 token。
                await store.WriteBatchAsync(batch.Events,
                    token => batch.Sending ? Task.CompletedTask : spool.MarkSendingAsync(batch.Id, token),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (ClickHouseStoreException error) when (error.Code == "invalid_query")
            {
                // 客户端合同检查尚未发送HTTP，属于确定拒绝。保留暂存供修复，避免误报为数据库提交未知。
                Interlocked.Increment(ref status.WriteFailures);
                throw new SpoolRecoveryException("storage_contract_rejected");
            }
            catch (ClickHouseStoreException error)
            {
                // 就绪检查和发送失败均保留队首批次；只在收到成功响应后确认。
                Interlocked.Increment(ref status.WriteFailures);
                status.FailureCode = error.Code;
                status.State = "retrying";
                await Task.Delay(retryDelay, cancellationToken);
                retryDelay = Math.Min(retryDelay * 2, 30000);
                continue;
            }
            await spool.AcknowledgeAsync(batch.Id, cancellationToken);
            status.FailureCode = "";
            retryDelay = 1000;
            status.State = "running";
            status.RecordWritten(batch.Events, clock.GetUtcNow());
        }
    }

    private void Observe(LogEvent item, bool ignored)
    {
        // 实时观察不改变确认深度、暂存或写入结果，也不能让显示故障中断正式接收。
        // 仅在首次解析后采集；Dispatch 的退避重试绝不能再次追加同一批次。
        try { liveLogs?.Capture(item, ignored); }
        catch (Exception) { /* 观察组件异常时仍继续原持久流水线，不把实时观察失败扩大为入库失败。 */ }
    }
}
