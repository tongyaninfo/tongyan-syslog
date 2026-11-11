using Microsoft.Extensions.Hosting;
using Syslog.Api.Service.impl;
using Syslog.Shared.Models;
using Syslog.Shared.Service;
using Xunit;

namespace Syslog.Api.Tests;

public sealed class OverviewDatabaseSizeTests
{
    [Fact]
    public async Task RefreshDoesNotBlockReadersAndFailuresAreCached()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var store = new ControlledStore();
        var clock = new TestClock();
        var size = new OverviewDatabaseSize(store, clock, new TestLifetime());

        Assert.Null(await Task.Run(size.GetCachedBytes).WaitAsync(timeout.Token));
        await store.FirstStarted.Task.WaitAsync(timeout.Token);
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Null(size.GetCachedBytes());
        Assert.Equal(1, store.Calls);

        store.FirstResult.SetResult(8192);
        while (size.GetCachedBytes() is null)
            await Task.Delay(10, timeout.Token);
        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(8192L, size.GetCachedBytes());
        Assert.Equal(1, store.Calls);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(8192L, size.GetCachedBytes());
        await store.SecondStarted.Task.WaitAsync(timeout.Token);
        store.SecondResult.SetException(new InvalidOperationException("synthetic storage failure"));
        while (size.GetCachedBytes() is not null)
            await Task.Delay(10, timeout.Token);
        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Null(size.GetCachedBytes());
        Assert.Equal(2, store.Calls);
    }

    private sealed class TestClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
        public void Advance(TimeSpan elapsed) => Interlocked.Add(ref _timestamp, elapsed.Ticks);
    }

    private sealed class TestLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() => throw new NotSupportedException();
    }

    private sealed class ControlledStore : ILogStore
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<long?> FirstResult { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<long?> SecondResult { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<long?> GetDatabaseSizeAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                FirstStarted.SetResult();
                return FirstResult.Task.WaitAsync(cancellationToken);
            }
            SecondStarted.TrySetResult();
            return SecondResult.Task.WaitAsync(cancellationToken);
        }

        public Task WriteBatchAsync(IReadOnlyList<LogEvent> events, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<LogSearchPage> SearchAsync(LogSearch query, IReadOnlyCollection<string> allowedSourceIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<LogEvent?> GetAsync(long eventId, DateTimeOffset receivedAt, IReadOnlyCollection<string> allowedSourceIds, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
