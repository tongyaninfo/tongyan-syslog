using System.Net;
using OptionsFactory = Microsoft.Extensions.Options.Options;
using Syslog.Engine.Service.impl;
using Syslog.Ingest.Models;
using Syslog.Ingest.Options;
using Syslog.Ingest.Service;
using Syslog.Ingest.Service.impl;
using Syslog.Ingest.Workers;
using Syslog.Shared.Models;
using Syslog.Shared.Service;
using Xunit;

namespace Syslog.Ingest.Tests;

/// <summary>仅内存替身验证管线；绝不调用文件暂存、真实数据库或监听端口。</summary>
public sealed partial class PipelineTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-10T12:00:00Z");

    [Fact]
    public void Last_receive_time_follows_counted_messages_and_does_not_move_backwards()
    {
        var context = Create(new IngestOptions { QueueCapacity = 1 });
        Assert.Equal(0, context.Status.LastReceiveUnixMilliseconds);
        var first = Message(1);
        Assert.True(context.Queue.TryEnqueue(first));
        var older = first with { Context = first.Context with { ReceivedAt = first.Context.ReceivedAt.AddMilliseconds(-1) } };
        Assert.False(context.Queue.TryEnqueue(older));
        Assert.Equal(2, context.Status.Received);
        Assert.Equal(1, context.Status.Dropped);
        Assert.Equal(new SourceIngestCounters(2, 0), context.Status.GetSourceCounters("source-1"));
        Assert.Equal(first.Context.ReceivedAt.ToUnixTimeMilliseconds(), context.Status.LastReceiveUnixMilliseconds);
        Assert.True(context.Queue.TryRead(out _));
        var newer = first with { Context = first.Context with { ReceivedAt = first.Context.ReceivedAt.AddMilliseconds(1) } };
        Assert.True(context.Queue.TryEnqueue(newer));
        Assert.Equal(newer.Context.ReceivedAt.ToUnixTimeMilliseconds(), context.Status.LastReceiveUnixMilliseconds);
    }

    [Fact]
    public async Task Disabled_worker_performs_no_spool_or_storage_io()
    {
        var context = Create(new IngestOptions { Enabled = false });
        using var worker = context.Worker;
        await worker.StartAsync(default);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, context.Spool.OpenCalls);
        Assert.Empty(context.Store.Writes);
        Assert.Equal("disabled", context.Status.State);
        await Stop(worker);
    }

    [Fact]
    public async Task ReceptionToggleReplacesEndpointsAndKeepsPendingWritesAndSamples()
    {
        var options = new IngestOptions
        {
            BatchEvents = 1,
            Listeners = [new() { Transport = "tcp", Port = 15514 }],
            UnregisteredDevices = new() { AllowedNetworks = ["192.0.2.0/24"] }
        };
        var context = Create(options);
        using var worker = context.Worker;
        var settings = OptionsFactory.Create(options);
        var registry = new PendingSourceRegistry();
        var unregistered = new UnregisteredDeviceBuffer(settings, new FixedClock());
        var runtime = new ReceptionRuntime(settings, registry, context.Queue, context.Status, unregistered, worker);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var ct = timeout.Token;
        try
        {
            await runtime.StartAsync(ct);
            Assert.Equal(0, context.Spool.OpenCalls);
            Assert.Equal("disabled", Assert.Single(context.Status.GetListenerSnapshots()).State);

            await runtime.ApplyAsync(true, options.Listeners, ct);
            // 故意挂起来源读取，验证切换期间不会实际绑定套接字，也不会累计悬挂的读取。
            await registry.Entered.Task.WaitAsync(ct);
            Assert.Equal("starting", Assert.Single(context.Status.GetListenerSnapshots()).State);
            var peer = IPAddress.Parse("192.0.2.42");
            Assert.True(unregistered.TryCapture("synthetic"u8, peer, "tcp"));
            context.Spool.AppendGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.True(context.Queue.TryEnqueue(Message(12)));
            await context.Spool.AppendStarted.Task.WaitAsync(ct);

            await runtime.ApplyAsync(false, options.Listeners, ct);
            Assert.Equal("disabled", Assert.Single(context.Status.GetListenerSnapshots()).State);
            Assert.False(unregistered.Snapshot().Status.Enabled);
            Assert.False(unregistered.TryCapture("paused"u8, peer, "tcp"));
            context.Spool.AppendGate.SetResult(true);
            await Until(() => Interlocked.Read(ref context.Status.Written) == 1);
            Assert.Single(unregistered.GetLogs(peer));

            await runtime.ApplyAsync(true, [new() { Transport = "relp", Port = 12514 }], ct);
            var snapshot = Assert.Single(context.Status.GetListenerSnapshots());
            Assert.Equal("relp", snapshot.Protocol);
            Assert.Equal(12514, snapshot.Port);
            Assert.True(unregistered.Snapshot().Status.Enabled);
            await Until(() => context.Status.ListenerFailureCode == "source_registry_unavailable");
            Assert.Equal(1, registry.Calls);
            Assert.Equal(1, context.Spool.OpenCalls);
            Assert.False(options.Enabled);
            Assert.Equal(15514, Assert.Single(options.Listeners).Port);
        }
        finally
        {
            await runtime.StopAsync(CancellationToken.None);
            registry.Complete();
        }
    }

    [Fact]
    public async Task Capture_assigns_unique_event_ids_and_one_batch_id_before_dispatch()
    {
        var context = Create(new IngestOptions { Enabled = true, BatchEvents = 2, BatchFlushMilliseconds = 10 });
        Assert.True(context.Queue.TryEnqueue(Message(12)));
        Assert.True(context.Queue.TryEnqueue(Message(24, "source-2")));
        using var worker = context.Worker;
        try
        {
            await worker.StartAsync(default);
            await Until(() => Interlocked.Read(ref context.Status.Written) == 2);
            var batch = Assert.Single(context.Store.Writes);
            Assert.Equal(2, batch.Count);
            Assert.Equal(2, batch.Select(x => x.EventId).Distinct().Count());
            Assert.All(batch, x => Assert.True(x.EventId > 0));
            Assert.Single(batch.Select(x => x.BatchId).Distinct());
            Assert.Matches("^[0-9]{20}-[a-f0-9]{32}$", batch[0].BatchId);
            Assert.Equal(2, context.Status.Received);
            Assert.Equal(2, context.Status.Parsed);
            Assert.Equal(2, context.Status.Persisted);
            Assert.Equal(new SourceIngestCounters(1, 1), context.Status.GetSourceCounters("source-1"));
            Assert.Equal(new SourceIngestCounters(1, 1), context.Status.GetSourceCounters("source-2"));
            Assert.Equal(default(SourceIngestCounters), context.Status.GetSourceCounters("source-3"));
            Assert.Equal(1, context.Spool.Acknowledged);
            Assert.Equal(Now.ToUnixTimeMilliseconds(), context.Status.LastWriteUnixMilliseconds);
        }
        finally { await Stop(worker); }
    }

    [Fact]
    public async Task Unknown_write_result_retries_the_same_batch_before_acknowledging_it()
    {
        var context = Create(new IngestOptions { Enabled = true, BatchEvents = 1, BatchFlushMilliseconds = 10 });
        context.Store.FailWrites = true;
        Assert.True(context.Queue.TryEnqueue(Message(10)));
        using var worker = context.Worker;
        try
        {
            await worker.StartAsync(default);
            await Until(() => context.Status.State == "retrying");
            var original = Assert.Single(Assert.Single(context.Store.Writes));
            Assert.True(Assert.Single(context.Spool.Pending).Sending);
            Assert.Equal(0, context.Spool.Acknowledged);
            Assert.Equal(1, context.Status.WriteFailures);
            Assert.Equal(0, context.Status.Written);
            Assert.Equal(new SourceIngestCounters(1, 0), context.Status.GetSourceCounters("source-1"));
            Assert.Equal("storage_timeout", context.Status.FailureCode);

            context.Store.FailWrites = false;
            await Until(() => Interlocked.Read(ref context.Status.Written) == 1);
            Assert.Equal(2, context.Store.Writes.Count);
            var retry = Assert.Single(context.Store.Writes[1]);
            Assert.Equal(original.EventId, retry.EventId);
            Assert.Equal(original.BatchId, retry.BatchId);
            Assert.Equal(original.RawBytes, retry.RawBytes);
            Assert.Equal(1, context.Spool.MarkSendingCalls);
            Assert.Equal(1, context.Spool.Acknowledged);
            Assert.Empty(context.Spool.Pending);
            Assert.Equal("running", context.Status.State);
            Assert.Equal("", context.Status.FailureCode);
            Assert.Equal(new SourceIngestCounters(1, 1), context.Status.GetSourceCounters("source-1"));
        }
        finally { await Stop(worker); }
    }

    [Fact]
    public async Task Failed_storage_readiness_retries_without_marking_a_batch_sending()
    {
        var context = Create(new IngestOptions { Enabled = true, BatchEvents = 1, BatchFlushMilliseconds = 10 });
        context.Store.FailReadiness = true;
        Assert.True(context.Queue.TryEnqueue(Message(10)));
        using var worker = context.Worker;
        try
        {
            await worker.StartAsync(default);
            await Until(() => context.Status.FailureCode == "storage_connection_failed");
            Assert.Empty(context.Store.Writes);
            Assert.False(Assert.Single(context.Spool.Pending).Sending);
            Assert.Equal(0, context.Spool.MarkSendingCalls);

            context.Store.FailReadiness = false;
            await Until(() => Interlocked.Read(ref context.Status.Written) == 1);
            Assert.Equal(1, context.Spool.MarkSendingCalls);
            Assert.Equal("", context.Status.FailureCode);
        }
        finally { await Stop(worker); }
    }

    [Fact]
    public async Task Existing_sending_batch_on_start_is_resent_before_newer_batches()
    {
        var context = Create(new IngestOptions { Enabled = true, BatchFlushMilliseconds = 10 });
        var batchId = "00000000000000000001-" + new string('a', 32);
        var nextBatchId = "00000000000000000002-" + new string('b', 32);
        context.Spool.Seed(new PendingBatch(batchId, [new LogEvent { EventId = 1, BatchId = batchId }], true));
        context.Spool.Seed(new PendingBatch(nextBatchId, [new LogEvent { EventId = 2, BatchId = nextBatchId }], false));
        using var worker = context.Worker;
        try
        {
            await worker.StartAsync(default);
            await Until(() => Interlocked.Read(ref context.Status.Written) == 2);
            Assert.Equal([1L, 2L], context.Store.Writes.Select(batch => Assert.Single(batch).EventId));
            Assert.Equal([batchId, nextBatchId], context.Store.Writes.Select(batch => Assert.Single(batch).BatchId));
            Assert.Equal(1, context.Spool.MarkSendingCalls);
            Assert.Equal(2, context.Spool.Acknowledged);
            Assert.Empty(context.Spool.Pending);
        }
        finally { await Stop(worker); }
    }

    [Fact]
    public async Task Invalid_stored_batch_stops_with_error_and_keeps_the_batch()
    {
        var context = Create(new IngestOptions { Enabled = true });
        var batchId = "00000000000000000001-" + new string('a', 32);
        context.Spool.Seed(new PendingBatch(batchId, [new LogEvent { EventId = 1, BatchId = batchId }], true));
        context.Store.RejectWrites = true;
        using var worker = context.Worker;
        try
        {
            await worker.StartAsync(default);
            await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("failed", context.Status.State);
            Assert.Equal("storage_contract_rejected", context.Status.FailureCode);
            Assert.Equal(1, context.Status.WriteFailures);
            Assert.Equal(0, context.Status.Written);
            Assert.Empty(context.Store.Writes);
            Assert.Equal(0, context.Spool.MarkSendingCalls);
            Assert.Equal(0, context.Spool.Acknowledged);
            Assert.True(Assert.Single(context.Spool.Pending).Sending);
        }
        finally { await Stop(worker); }
    }

    [Fact]
    public async Task Cancellation_stops_waiting_worker_without_marking_failure()
    {
        var context = Create(new IngestOptions { Enabled = true });
        using var worker = context.Worker;
        await worker.StartAsync(default);
        await Until(() => context.Status.State == "running");
        await Stop(worker);
        Assert.Equal("stopped", context.Status.State);
        Assert.Empty(context.Store.Writes);
        Assert.Equal("", context.Status.FailureCode);
        Assert.True(worker.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Cancellation_interrupts_storage_backoff_and_keeps_unacknowledged_batch()
    {
        var context = Create(new IngestOptions { Enabled = true, BatchEvents = 1, BatchFlushMilliseconds = 10 });
        context.Store.FailWrites = true;
        Assert.True(context.Queue.TryEnqueue(Message(10)));
        using var worker = context.Worker;
        try
        {
            await worker.StartAsync(default);
            await Until(() => context.Status.State == "retrying");
        }
        finally { await Stop(worker); }
        Assert.Equal("stopped", context.Status.State);
        Assert.Equal(0, context.Spool.Acknowledged);
        Assert.True(Assert.Single(context.Spool.Pending).Sending);
        Assert.True(worker.ExecuteTask!.IsCompletedSuccessfully);
    }

    [Fact]
    public void Spool_pressure_does_not_overwrite_the_current_storage_error()
    {
        var status = new IngestStatus { State = "retrying", FailureCode = "storage_timeout" };
        status.SpoolFull = true;
        Assert.Equal("spool_full", status.FailureCode);
        status.SpoolFull = false;
        Assert.Equal("storage_timeout", status.FailureCode);
        status.SpoolFull = true;
        status.FailureCode = "log_clear_outcome_unknown";
        Assert.Equal("log_clear_outcome_unknown", status.FailureCode);
    }

    [Fact]
    public void Queue_count_limit_rejects_new_messages_and_releases_byte_reservation()
    {
        var status = new IngestStatus();
        var queue = new IngressQueue(OptionsFactory.Create(new IngestOptions { QueueCapacity = 1, QueueBytes = 10000 }), status);
        Assert.True(queue.TryEnqueue(Message(10)));
        Assert.False(queue.TryEnqueue(Message(20)));
        Assert.Equal(2, status.Received);
        Assert.Equal(30, status.ReceivedBytes);
        Assert.Equal(1, status.Dropped);
        Assert.Equal(522, status.QueuedBytes);
        Assert.True(queue.TryRead(out var item));
        Assert.Equal(10, item!.Bytes.Length);
        Assert.Equal(0, status.QueuedBytes);
        Assert.False(queue.TryRead(out _));
    }

    [Fact]
    public void Queue_byte_limit_includes_fixed_metadata_and_exact_boundary()
    {
        var status = new IngestStatus();
        var queue = new IngressQueue(OptionsFactory.Create(new IngestOptions { QueueCapacity = 10, QueueBytes = 1026 }), status);
        Assert.True(queue.TryEnqueue(Message(1)));
        Assert.True(queue.TryEnqueue(Message(1)));
        Assert.Equal(1026, status.QueuedBytes);
        Assert.False(queue.TryEnqueue(Message(1)));
        Assert.Equal(1026, status.QueuedBytes);
        Assert.True(queue.TryRead(out _));
        Assert.Equal(513, status.QueuedBytes);
        Assert.True(queue.TryEnqueue(Message(1)));
        Assert.Equal(1, status.Dropped);
    }

    [Fact]
    public void Queue_rejects_empty_and_oversized_messages_but_accepts_exact_maximum()
    {
        var status = new IngestStatus();
        var queue = new IngressQueue(OptionsFactory.Create(new IngestOptions { MaxMessageBytes = 10 }), status);
        Assert.False(queue.TryEnqueue(Message(0)));
        Assert.False(queue.TryEnqueue(Message(11)));
        Assert.True(queue.TryEnqueue(Message(10)));
        Assert.Equal(3, status.Received);
        Assert.Equal(21, status.ReceivedBytes);
        Assert.Equal(2, status.Dropped);
        Assert.Equal(522, status.QueuedBytes);
    }

    [Fact]
    public async Task Batch_raw_byte_budget_does_not_consume_an_oversized_next_message()
    {
        var context = Create(new IngestOptions { Enabled = true, BatchEvents = 2, BatchRawBytes = 65536, BatchFlushMilliseconds = 10 });
        Assert.True(context.Queue.TryEnqueue(Message(40000)));
        Assert.True(context.Queue.TryEnqueue(Message(40000)));
        using var worker = context.Worker;
        try
        {
            await worker.StartAsync(default);
            await Until(() => Interlocked.Read(ref context.Status.Written) == 2);
            Assert.Equal(2, context.Store.Writes.Count);
            Assert.All(context.Store.Writes, batch => Assert.InRange(batch.Sum(x => x.RawBytes.Length), 1, 65536));
            Assert.Equal(2, context.Store.Writes.SelectMany(x => x).Select(x => x.EventId).Distinct().Count());
        }
        finally { await Stop(worker); }
    }

    [Fact]
    public async Task RelpConfirmationIsCompletedOnlyAfterSpoolAppendSucceeds()
    {
        var context = Create(new IngestOptions { Enabled = true, BatchEvents = 1, BatchFlushMilliseconds = 10 });
        context.Spool.AppendGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(context.Queue.TryEnqueue(Message(12) with { Persisted = confirmation }));
        using var worker = context.Worker;
        try
        {
            await worker.StartAsync(default);
            await context.Spool.AppendStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(confirmation.Task.IsCompleted);
            Assert.Equal(0, context.Status.Persisted);
            context.Spool.AppendGate.SetResult(true);
            Assert.True(await confirmation.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally { await Stop(worker); }
    }

    [Fact]
    public async Task IgnoredSeverityIsAcknowledgedWithoutAllocatingAnIdOrWritingAndIsScopedToSource()
    {
        var context = Create(new IngestOptions { Enabled = true, BatchEvents = 2, BatchFlushMilliseconds = 50 }, new SyslogParser());
        context.Spool.AppendGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var ignoredConfirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var normalConfirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        const byte ignoredSeverities = (1 << 3) | (1 << 7);
        var policy = Message(1) with { IgnoredSeverityMask = ignoredSeverities };
        Assert.True(context.Queue.TryEnqueue(policy with
        {
            Bytes = "<15>1 - synthetic app - - - ignored debug"u8.ToArray(),
            Persisted = ignoredConfirmation,
        }));
        Assert.True(context.Queue.TryEnqueue(policy with { Bytes = "<11>1 - synthetic app - - - ignored error"u8.ToArray() }));
        Assert.True(context.Queue.TryEnqueue(policy with { Bytes = "message without priority"u8.ToArray() }));
        Assert.True(context.Queue.TryEnqueue(Message(1, "source-2") with
        {
            Bytes = "<15>1 - synthetic app - - - retained debug"u8.ToArray(),
            Persisted = normalConfirmation,
        }));

        using var worker = context.Worker;
        try
        {
            await worker.StartAsync(default);
            await context.Spool.AppendStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(await ignoredConfirmation.Task.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.False(normalConfirmation.Task.IsCompleted);
            Assert.Equal(2, context.Status.Ignored);
            Assert.Equal(0, context.Status.Dropped);
            Assert.Equal(0, context.Status.Persisted);
            Assert.Equal(new SourceIngestCounters(3, 0, 2), context.Status.GetSourceCounters("source-1"));

            context.Spool.AppendGate.SetResult(true);
            Assert.True(await normalConfirmation.Task.WaitAsync(TimeSpan.FromSeconds(2)));
            await Until(() => Interlocked.Read(ref context.Status.Written) == 2);
            var batch = Assert.Single(context.Store.Writes);
            Assert.Equal(new long[] { 1, 2 }, batch.Select(item => item.EventId));
            Assert.Null(batch[0].Severity);
            Assert.Equal(7, batch[1].Severity);
            Assert.Equal(3, context.Status.Parsed);
            Assert.Equal(1, context.Status.ParseUnknown);
            Assert.Equal(new SourceIngestCounters(3, 1, 2), context.Status.GetSourceCounters("source-1"));
            Assert.Equal(new SourceIngestCounters(1, 1, 0), context.Status.GetSourceCounters("source-2"));
        }
        finally { await Stop(worker); }
    }

    [Fact]
    public async Task FailedSpoolAppendRejectsRelpConfirmationAndRetainsFailureMetrics()
    {
        var context = Create(new IngestOptions { Enabled = true, BatchEvents = 1, BatchFlushMilliseconds = 10 });
        context.Spool.AppendGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(context.Queue.TryEnqueue(Message(12) with { Persisted = confirmation }));
        using var worker = context.Worker;
        try
        {
            await worker.StartAsync(default);
            await context.Spool.AppendStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            context.Spool.AppendGate.SetException(new IOException("synthetic_spool_failure"));
            Assert.False(await confirmation.Task.WaitAsync(TimeSpan.FromSeconds(2)));
            await Until(() => context.Status.State == "failed");
            Assert.Equal(0, context.Status.Persisted);
            Assert.Equal(1, context.Status.Dropped);
            Assert.Empty(context.Store.Writes);
        }
        finally { await Stop(worker); }
    }

    private static PipelineContext Create(IngestOptions options, ISyslogParser? parser = null, ILiveLogBuffer? liveLogs = null)
    {
        var status = new IngestStatus();
        var queue = new IngressQueue(OptionsFactory.Create(options), status);
        var spool = new MemorySpool();
        var store = new MemoryStore();
        var worker = new IngestWorker(queue, parser ?? new MemoryParser(), new SequenceIds(), spool, store, OptionsFactory.Create(options), status, new FixedClock(), liveLogs);
        return new PipelineContext(queue, spool, store, status, worker);
    }
    private static ReceivedMessage Message(int size, string sourceId = "source-1") =>
        new(new byte[size], new ParseContext(sourceId, "192.0.2.1", "udp", Now, TimeZoneInfo.Utc, Now.AddYears(1)));
    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }
    private static async Task Stop(IngestWorker worker)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await worker.StopAsync(timeout.Token);
    }
    private sealed record PipelineContext(IngressQueue Queue, MemorySpool Spool, MemoryStore Store, IngestStatus Status, IngestWorker Worker);
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class PendingSourceRegistry : ISourceRegistry
    {
        private readonly TaskCompletionSource<IReadOnlyList<LogSource>> pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;

        public Task<IReadOnlyList<LogSource>> ListAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            Entered.TrySetResult();
            return pending.Task;
        }

        public void Complete() => pending.TrySetResult([]);
    }

    private sealed class SequenceIds : IIdService
    {
        private long value;
        public Task<long> NextAsync(CancellationToken cancellationToken) { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Interlocked.Increment(ref value)); }
    }
    private sealed class MemoryParser : ISyslogParser
    {
        public LogEvent Parse(ReadOnlyMemory<byte> message, ParseContext context) => new() { SourceId = context.SourceId, ReceivedAt = context.ReceivedAt, ExpiresAt = context.ExpiresAt, RawBytes = message.ToArray(), ParseStatus = "parsed" };
    }
    private sealed class MemoryStore : ILogStore
    {
        private readonly object sync = new();
        private readonly List<IReadOnlyList<LogEvent>> writes = [];
        public volatile bool FailWrites;
        public volatile bool FailReadiness;
        public bool RejectWrites { get; set; }
        public IReadOnlyList<IReadOnlyList<LogEvent>> Writes { get { lock (sync) return writes.ToArray(); } }
        public Task EnsureReadyAsync(CancellationToken cancellationToken) => FailReadiness
            ? Task.FromException(new ClickHouseStoreException("storage_connection_failed", "模拟存储未就绪")) : Task.CompletedTask;
        public async Task WriteBatchAsync(IReadOnlyList<LogEvent> events, Func<CancellationToken, Task> beforeSend,
            CancellationToken cancellationToken)
        {
            if (RejectWrites) throw new ClickHouseStoreException("invalid_query", "模拟批次合同无效");
            await EnsureReadyAsync(cancellationToken);
            await beforeSend(cancellationToken);
            await WriteBatchAsync(events, cancellationToken);
        }
        public Task WriteBatchAsync(IReadOnlyList<LogEvent> events, CancellationToken cancellationToken)
        {
            lock (sync) writes.Add(events.ToArray());
            return FailWrites ? Task.FromException(new ClickHouseStoreException("storage_timeout", "模拟未知提交")) : Task.CompletedTask;
        }
        public Task<LogSearchPage> SearchAsync(LogSearch query, IReadOnlyCollection<string> allowedSourceIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<LogEvent?> GetAsync(long eventId, DateTimeOffset receivedAt, IReadOnlyCollection<string> allowedSourceIds, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class MemorySpool : IEventSpool
    {
        private readonly object sync = new();
        private readonly List<PendingBatch> pending = [];
        public int OpenCalls;
        public int MarkSendingCalls;
        public int Acknowledged;
        public TaskCompletionSource<bool>? AppendGate { get; set; }
        public TaskCompletionSource AppendStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<PendingBatch> Pending { get { lock (sync) return pending.ToArray(); } }
        public void Seed(PendingBatch batch) { lock (sync) pending.Add(batch); }
        public Task OpenAsync(CancellationToken cancellationToken) { Interlocked.Increment(ref OpenCalls); return Task.CompletedTask; }
        public async Task<bool> AppendAsync(IReadOnlyList<LogEvent> events, CancellationToken cancellationToken)
        {
            AppendStarted.TrySetResult();
            if (AppendGate is not null && !await AppendGate.Task.WaitAsync(cancellationToken)) return false;
            lock (sync) pending.Add(new PendingBatch(events[0].BatchId, events.ToArray(), false));
            return true;
        }
        public Task<PendingBatch?> ReadNextAsync(CancellationToken cancellationToken) { lock (sync) return Task.FromResult(pending.FirstOrDefault()); }
        public Task MarkSendingAsync(string batchId, CancellationToken cancellationToken)
        {
            lock (sync)
            {
                var index = pending.FindIndex(x => x.Id == batchId);
                if (pending[index].Sending) throw new InvalidOperationException("批次已标记发送");
                pending[index] = pending[index] with { Sending = true };
                Interlocked.Increment(ref MarkSendingCalls);
            }
            return Task.CompletedTask;
        }
        public Task AcknowledgeAsync(string batchId, CancellationToken cancellationToken)
        {
            // 只改变内存替身，绝不调用 FileEventSpool 或触碰文件。
            lock (sync) pending.RemoveAll(x => x.Id == batchId);
            Interlocked.Increment(ref Acknowledged);
            return Task.CompletedTask;
        }
    }
}
