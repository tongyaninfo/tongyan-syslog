using Syslog.Api.Service;
using Syslog.Api.Service.impl;

namespace Syslog.Api.Workers;

/// <summary>仅清理 MCP 已完成调用；每批短事务，保留未知结果的关联证据直至超过留存期。</summary>
public sealed class McpRetentionWorker(IMcpStore store, McpRuntime runtime, ILogger<McpRetentionWorker> logger) : BackgroundService
{
    private readonly DateTimeOffset _recoveryCutoff = DateTimeOffset.UtcNow;
    private bool _recovered;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        runtime.ReportRecovery(false);
        try
        {
            await store.RecoverMcpCallsAsync(cancellationToken, _recoveryCutoff);
            _recovered = true;
            runtime.ReportRecovery(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { runtime.ReportCleanup(null, "mcp_audit_cleanup_failed"); }
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_recovered)
                {
                    // 重试也只恢复进程启动前的调用，不能把当前进程正在执行的调用标为中断。
                    await store.RecoverMcpCallsAsync(stoppingToken, _recoveryCutoff);
                    _recovered = true;
                    runtime.ReportRecovery(true);
                }
                for (var batch = 0; batch < 10; batch++)
                {
                    var result = await store.CleanupMcpCallsAsync(stoppingToken);
                    if (result is null) break; // 旧库不自动迁移，也不影响其他后台任务。
                    runtime.ReportCleanup(result.CompletedAt, null);
                    if (result.Deleted < 500) break;
                    await Task.Delay(TimeSpan.FromMilliseconds(25), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                // 专用审计可能含完整凭据，这里不输出异常对象或请求内容。
                runtime.ReportCleanup(null, "mcp_audit_cleanup_failed");
                logger.LogWarning("MCP 调用记录维护失败，将在下个周期重试。");
            }
            await Task.Delay(_recovered ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
