using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Syslog.Ingest.Models;
using Syslog.Ingest.Options;
using Syslog.Ingest.Service;
using Syslog.Ingest.Service.impl;
using Syslog.Shared.Service;

namespace Syslog.Ingest.Workers;

/// <summary>接收开关只切换监听循环，已接收消息与进行中的暂存和写入由宿主管理。</summary>
public sealed class ReceptionRuntime : IReceptionRuntime, IHostedService
{
    private readonly IOptions<IngestOptions> settings;
    private readonly IngestStatus status;
    private readonly IngestWorker ingest;
    private readonly IUnregisteredDeviceBuffer unregistered;
    private readonly IngestOptions listenerOptions;
    private readonly ListenerWorker listener;
    private readonly SemaphoreSlim gate = new(1, 1);
    private CancellationTokenSource? listenerLifetime;
    private Task? listenerTask;
    private CancellationTokenSource? ingestLifetime;
    private Task? ingestTask;
    private bool stopping;

    public ReceptionRuntime(IOptions<IngestOptions> settings, ISourceRegistry registry, IngressQueue queue,
        IngestStatus status, IUnregisteredDeviceBuffer unregistered, IngestWorker ingest,
        IListenerCertificateProvider? certificateProvider = null)
    {
        this.settings = settings;
        this.status = status;
        this.ingest = ingest;
        this.unregistered = unregistered;
        listenerOptions = settings.Value.CopyForReception();
        listener = new ListenerWorker(Microsoft.Extensions.Options.Options.Create(listenerOptions), registry, queue, status, unregistered, certificateProvider);
    }

    public Task StartAsync(CancellationToken cancellationToken) =>
        ApplyAsync(settings.Value.Enabled, settings.Value.Listeners, cancellationToken);

    public async Task ApplyAsync(bool enabled, IReadOnlyList<ListenerOptions> listeners, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            if (stopping) throw new InvalidOperationException("接收运行时已停止");
            // 一旦开始切换，即使管理请求断开也须完成旧端口回收，再发布新的运行快照。
            await StopListenersAsync();
            listenerOptions.Enabled = enabled;
            listenerOptions.Listeners = listeners.Select(item => item.Copy()).ToList();
            unregistered.SetReceptionEnabled(enabled);
            status.ResetListeners(enabled, listenerOptions.Listeners);
            if (!enabled) return;

            if (ingestLifetime is null)
            {
                // 仅首次启用时打开暂存。切换监听端口时不取消正在写入的批次。
                ingestLifetime = new CancellationTokenSource();
                var token = ingestLifetime.Token;
                ingestTask = Task.Run(() => ingest.RunAsync(token));
            }
            listenerLifetime = new CancellationTokenSource();
            listenerTask = listener.RunAsync(listenerLifetime.Token, allowEmpty: true);
        }
        finally { gate.Release(); }
    }

    public async Task ApplyProtocolAsync(ListenerOptions configuration, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            if (stopping || listenerLifetime is null) throw new InvalidOperationException("接收未启用");
            await listener.ApplyProtocolAsync(configuration.Copy());
        }
        finally { gate.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            stopping = true;
            await StopListenersAsync();
            unregistered.SetReceptionEnabled(false);
            if (ingestLifetime is not null)
            {
                await ingestLifetime.CancelAsync();
                await ingestTask!.WaitAsync(cancellationToken);
                ingestLifetime.Dispose();
                ingestLifetime = null;
                ingestTask = null;
            }
        }
        finally { gate.Release(); }
    }

    private async Task StopListenersAsync()
    {
        if (listenerLifetime is null) return;
        try
        {
            await listenerLifetime.CancelAsync();
            await listenerTask!;
        }
        finally
        {
            listenerLifetime.Dispose();
            listenerLifetime = null;
            listenerTask = null;
        }
    }
}
