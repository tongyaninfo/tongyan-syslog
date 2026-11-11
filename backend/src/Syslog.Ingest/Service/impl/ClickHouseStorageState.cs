namespace Syslog.Ingest.Service.impl;

/// <summary>同一宿主的 HTTP 存储客户端共享库表检查结果和变更顺序。</summary>
public sealed class ClickHouseStorageState : IDisposable
{
    internal volatile bool SchemaReady;
    internal bool ClearOutcomeUnknown;
    internal SemaphoreSlim SchemaGate { get; } = new(1, 1);
    internal SemaphoreSlim MutationGate { get; } = new(1, 1);

    public void Dispose()
    {
        SchemaGate.Dispose();
        MutationGate.Dispose();
    }
}
