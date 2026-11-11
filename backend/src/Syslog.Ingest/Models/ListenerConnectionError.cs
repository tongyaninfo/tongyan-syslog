namespace Syslog.Ingest.Models;

/// <summary>来源 IP 在同一监听端点上最近一次未恢复的连接故障，仅含安全诊断。</summary>
public sealed record ListenerConnectionError(string Protocol, string Address, int Port, string PeerAddress,
    string FailureCode, string FailureMessage, DateTimeOffset OccurredAt);
