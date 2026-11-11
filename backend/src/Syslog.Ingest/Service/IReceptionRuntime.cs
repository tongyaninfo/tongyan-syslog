using Syslog.Ingest.Options;

namespace Syslog.Ingest.Service;

public interface IReceptionRuntime
{
    Task ApplyAsync(bool enabled, IReadOnlyList<ListenerOptions> listeners, CancellationToken ct);
    Task ApplyProtocolAsync(ListenerOptions listener, CancellationToken ct);
}
