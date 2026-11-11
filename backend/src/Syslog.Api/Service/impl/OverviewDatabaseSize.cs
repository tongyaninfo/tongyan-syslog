using Syslog.Shared.Service;

namespace Syslog.Api.Service.impl;

/// <summary>看板共享日志库占用缓存，刷新不占用实时推送的等待预算。</summary>
public sealed class OverviewDatabaseSize(ILogStore store, TimeProvider clock, IHostApplicationLifetime lifetime)
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(2);
    private readonly object _gate = new();
    private long? _refreshedAt;
    private long? _bytes;
    private bool _refreshing;

    public long? GetCachedBytes()
    {
        lock (_gate)
        {
            if (!_refreshing && !lifetime.ApplicationStopping.IsCancellationRequested
                && (_refreshedAt is not long timestamp || clock.GetElapsedTime(timestamp) >= RefreshInterval))
            {
                _refreshing = true;
                _ = Task.Run(RefreshAsync);
            }
            return _bytes;
        }
    }

    private async Task RefreshAsync()
    {
        long? bytes = null;
        try
        {
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
            budget.CancelAfter(QueryTimeout);
            bytes = await store.GetDatabaseSizeAsync(budget.Token);
        }
        catch (Exception)
        {
            // 存储故障视为未知，缓存空值以限制重试频率。
        }
        finally
        {
            lock (_gate)
            {
                _bytes = bytes;
                _refreshedAt = clock.GetTimestamp();
                _refreshing = false;
            }
        }
    }
}
