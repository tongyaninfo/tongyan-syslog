using Syslog.Api.Service.impl;

namespace Syslog.Api.Workers;

/// <summary>按一秒周期发布实时订阅的完整快照。没有订阅时不读取控制库。</summary>
public sealed class OverviewPushWorker(OverviewFeed feed, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await feed.PublishAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
