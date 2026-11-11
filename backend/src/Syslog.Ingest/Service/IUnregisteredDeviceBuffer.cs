using System.Net;
using Syslog.Shared.Models;

namespace Syslog.Ingest.Service;

public interface IUnregisteredDeviceBuffer
{
    void SetReceptionEnabled(bool enabled);
    UnregisteredDevicePage Snapshot();
    IReadOnlyList<UnregisteredLogSummary> GetLogs(IPAddress peer);
    UnregisteredLogDetail? GetSample(IPAddress peer, string sampleKey);
    bool IsAllowed(IPAddress peer);
    bool TryCapture(ReadOnlySpan<byte> bytes, IPAddress peer, string transport);
    bool TryReserveConnection(IPAddress peer);
    void ReleaseConnection(IPAddress peer);
    void RemoveRegistered(IReadOnlyCollection<IPAddress> peers);
}
