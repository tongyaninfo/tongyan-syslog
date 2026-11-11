using Syslog.Engine.Service.impl;
using Syslog.Ingest.Models;
using Syslog.Ingest.Options;
using Syslog.Ingest.Service.impl;
using Syslog.Shared.Models;
using Syslog.Shared.Service;
using Xunit;

namespace Syslog.Ingest.Tests;

public sealed partial class PipelineTests
{
    [Fact]
    public async Task LiveLogsIncludeIgnoredWithoutIdsAndNormalBeforeDurabilityConfirmation()
    {
        var liveLogs = ReadyLiveBuffer();
        var context = Create(new IngestOptions { Enabled = true, BatchEvents = 1, BatchFlushMilliseconds = 10 }, new SyslogParser(), liveLogs);
        context.Spool.AppendGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var ignoredConfirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var normalConfirmation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(context.Queue.TryEnqueue(Message(1) with
        {
            Bytes = "<15>1 - synthetic app - - - ignored debug"u8.ToArray(),
            IgnoredSeverityMask = 1 << 7,
            Persisted = ignoredConfirmation,
        }));
        Assert.True(context.Queue.TryEnqueue(Message(1) with
        {
            Bytes = "<11>1 - synthetic app - - - normal error"u8.ToArray(),
            Persisted = normalConfirmation,
        }));
        using var worker = context.Worker;
        try
        {
            await worker.StartAsync(default);
            await context.Spool.AppendStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(await ignoredConfirmation.Task.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.False(normalConfirmation.Task.IsCompleted);
            var rows = liveLogs.ReadSnapshot(new(), ["source-1"]).Data!.Items;
            Assert.Equal(2, rows.Count);
            Assert.Equal("normal", rows[0].Disposition);
            Assert.Equal(1, rows[0].Log.EventId);
            Assert.Equal("ignored", rows[1].Disposition);
            Assert.Equal(0, rows[1].Log.EventId);
            Assert.Equal(0, context.Status.Persisted);
            Assert.Empty(context.Store.Writes);

            context.Spool.AppendGate.SetResult(true);
            await Until(() => Interlocked.Read(ref context.Status.Written) == 1);
            Assert.Equal(2, liveLogs.GetStats().Entries);
            Assert.Equal(1, Assert.Single(Assert.Single(context.Store.Writes)).EventId);
        }
        finally { await Stop(worker); }
    }

    [Fact]
    public async Task StorageRetriesDoNotAddDuplicateLiveLogs()
    {
        var liveLogs = ReadyLiveBuffer();
        var context = Create(new IngestOptions { Enabled = true, BatchEvents = 1, BatchFlushMilliseconds = 10 }, liveLogs: liveLogs);
        context.Store.FailWrites = true;
        Assert.True(context.Queue.TryEnqueue(Message(12)));
        using var worker = context.Worker;
        try
        {
            await worker.StartAsync(default);
            await Until(() => context.Status.State == "retrying");
            var original = Assert.Single(liveLogs.ReadSnapshot(new(), ["source-1"]).Data!.Items);
            context.Store.FailWrites = false;
            await Until(() => Interlocked.Read(ref context.Status.Written) == 1);
            Assert.Equal(2, context.Store.Writes.Count);
            Assert.Equal(original.Id, Assert.Single(liveLogs.ReadSnapshot(new(), ["source-1"]).Data!.Items).Id);
            Assert.Equal("1", liveLogs.GetStats().LatestSequence);
        }
        finally { await Stop(worker); }
    }

    [Fact]
    public async Task RecoveredSpoolDoesNotRepopulateTheLiveObservationCache()
    {
        var liveLogs = ReadyLiveBuffer();
        var context = Create(new IngestOptions { Enabled = true, BatchFlushMilliseconds = 10 }, liveLogs: liveLogs);
        var batchId = "00000000000000000001-" + new string('c', 32);
        context.Spool.Seed(new PendingBatch(batchId, [new LogEvent { EventId = 1, SourceId = "source-1", BatchId = batchId }], true));
        using var worker = context.Worker;
        try
        {
            await worker.StartAsync(default);
            await Until(() => Interlocked.Read(ref context.Status.Written) == 1);
            Assert.Equal(0, liveLogs.GetStats().Entries);
            Assert.Equal("0", liveLogs.GetStats().LatestSequence);
        }
        finally { await Stop(worker); }
    }

    [Fact]
    public async Task ObservationFailureCannotStopNormalPersistence()
    {
        var context = Create(new IngestOptions { Enabled = true, BatchEvents = 1, BatchFlushMilliseconds = 10 }, liveLogs: new FailingLiveBuffer());
        Assert.True(context.Queue.TryEnqueue(Message(12)));
        using var worker = context.Worker;
        try
        {
            await worker.StartAsync(default);
            await Until(() => Interlocked.Read(ref context.Status.Written) == 1);
            Assert.Equal(1, context.Spool.Acknowledged);
            Assert.Equal("running", context.Status.State);
        }
        finally { await Stop(worker); }
    }

    [Fact]
    public async Task UnparseableMessageRetainsOriginalBytesInLiveDetail()
    {
        var liveLogs = ReadyLiveBuffer();
        var context = Create(new IngestOptions { Enabled = true, BatchEvents = 1, BatchFlushMilliseconds = 10 }, new SyslogParser(), liveLogs);
        var raw = new byte[] { 0xff, 0x00, 0x80, 0x41 };
        Assert.True(context.Queue.TryEnqueue(Message(1) with { Bytes = raw }));
        using var worker = context.Worker;
        try
        {
            await worker.StartAsync(default);
            await Until(() => Interlocked.Read(ref context.Status.Written) == 1);
            var summary = Assert.Single(liveLogs.ReadSnapshot(new(), ["source-1"]).Data!.Items);
            Assert.NotEqual("parsed", summary.Log.ParseStatus);
            Assert.Equal("normal", summary.Disposition);
            Assert.Equal(raw, liveLogs.GetDetail(summary.Id, ["source-1"]).Data!.Log.RawBytes);
        }
        finally { await Stop(worker); }
    }

    private static LiveLogBuffer ReadyLiveBuffer()
    {
        var buffer = new LiveLogBuffer();
        buffer.SetAvailable(true);
        return buffer;
    }

    private sealed class FailingLiveBuffer : ILiveLogBuffer
    {
        public bool Capture(LogEvent item, bool ignored) => throw new InvalidOperationException("synthetic_observation_failure");
        public void SetAvailable(bool available) => throw new NotSupportedException();
        public void ApplySettings(LiveLogCacheSettings settings) => throw new NotSupportedException();
        public LiveLogCacheStats GetStats() => throw new NotSupportedException();
        public ApiResult<LiveLogSnapshot> ReadSnapshot(LiveLogFilter filter, IReadOnlyCollection<string> allowedSourceIds,
            string? beforeSequence = null, int take = 100, int maxBytes = 512 * 1024) => throw new NotSupportedException();
        public ApiResult<LiveLogDelta> ReadAfter(LiveLogFilter filter, IReadOnlyCollection<string> allowedSourceIds,
            string epoch, string afterSequence, int take = 200, int maxBytes = 512 * 1024) => throw new NotSupportedException();
        public ApiResult<LiveLogRecord> GetDetail(string id, IReadOnlyCollection<string> allowedSourceIds) => throw new NotSupportedException();
    }
}
