namespace Syslog.Ingest.Options;

/// <summary>ClickHouse 预算与连接配置；首次访问时检查日志库与表。</summary>
public sealed class ClickHouseOptions
{
    public const string SectionName = "ClickHouse";
    public string Endpoint { get; set; } = "http://127.0.0.1:8123/";
    public string Database { get; set; } = "syslog";
    public string Username { get; set; } = "default";
    public string Password { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 10;
    public int MaxResponseBytes { get; set; } = 16 * 1024 * 1024;
    public int MaxBatchBytes { get; set; } = 8 * 1024 * 1024;
    public int MaxBatchEvents { get; set; } = 10000;
    public long MaxMemoryUsageBytes { get; set; } = 512 * 1024 * 1024;
    public long MaxBytesToRead { get; set; } = 4L * 1024 * 1024 * 1024;
}
