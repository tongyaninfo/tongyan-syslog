using Syslog.Ingest.Models;
using Syslog.Shared.Models;

namespace Syslog.Ingest.Service;

/// <summary>只有确定入库后才确认批次；Sending 表示曾经发送，重启不能盲目重试。</summary>
public interface IEventSpool
{
    Task OpenAsync(CancellationToken cancellationToken);
    Task<bool> AppendAsync(IReadOnlyList<LogEvent> events, CancellationToken cancellationToken);
    Task<PendingBatch?> ReadNextAsync(CancellationToken cancellationToken);
    Task MarkSendingAsync(string batchId, CancellationToken cancellationToken);
    Task AcknowledgeAsync(string batchId, CancellationToken cancellationToken);
}
