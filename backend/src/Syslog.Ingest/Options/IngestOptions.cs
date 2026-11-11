namespace Syslog.Ingest.Options;

public sealed class IngestOptions
{
    public bool Enabled { get; set; }
    public string SpoolDirectory { get; set; } = "run-data/spool";
    public int QueueCapacity { get; set; } = 4096;
    public long QueueBytes { get; set; } = 64 * 1024 * 1024;
    public long SpoolBytes { get; set; } = 1024L * 1024 * 1024;
    public int BatchEvents { get; set; } = 256;
    public int BatchRawBytes { get; set; } = 512 * 1024;
    public int BatchFlushMilliseconds { get; set; } = 500;
    public int MaxMessageBytes { get; set; } = 65536;
    public int MaxConnections { get; set; } = 2000;
    public int MaxConnectionsPerAddress { get; set; } = 4;
    public int MessagesPerSecondPerSource { get; set; } = 5000;
    public int SourceRefreshSeconds { get; set; } = 10;
    public List<ListenerOptions> Listeners { get; set; } = [];
    // 证书材料只由服务器受控配置提供；管理库仅保存 CertificateId 引用。
    public Dictionary<string, TlsCertificateOptions> TlsCertificates { get; set; } = new(StringComparer.Ordinal);
    public UnregisteredDeviceOptions UnregisteredDevices { get; set; } = new();

    internal IngestOptions CopyForReception() => (IngestOptions)MemberwiseClone();
}

public sealed class ListenerOptions
{
    public bool Enabled { get; set; } = true;
    public string Address { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 5514;
    public string Transport { get; set; } = "udp";
    public string Framing { get; set; } = "octet-counting";
    public int FrameTimeoutSeconds { get; set; } = 30;
    public int IdleTimeoutSeconds { get; set; } = 1800;
    public string CertificatePath { get; set; } = "";
    public string CertificatePassword { get; set; } = "";
    public string CertificateId { get; set; } = "";

    internal ListenerOptions Copy() => (ListenerOptions)MemberwiseClone();
}

public sealed class TlsCertificateOptions
{
    public string CertificatePath { get; set; } = "";
    public string CertificatePassword { get; set; } = "";
}
