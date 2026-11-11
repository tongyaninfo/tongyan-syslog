namespace Syslog.Ingest.Options;

/// <summary>未登记设备只在许可网段中暂存；总接收开关关闭时，本配置不会开启端口或采集。</summary>
public sealed class UnregisteredDeviceOptions
{
    public bool Enabled { get; set; } = true;
    public string[] AllowedNetworks { get; set; } = ["127.0.0.0/8", "::1/128", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fc00::/7"];
    public int MaxDevices { get; set; } = 1000;
    public long MaxBytes { get; set; } = 64 * 1024 * 1024;
    public int MessagesPerSecondPerAddress { get; set; } = 100;
    public int MessagesPerSecond { get; set; } = 1000;
    public int MaxConnections { get; set; } = 64;
    public int MaxConnectionsPerAddress { get; set; } = 2;
    public int ConnectionsPerSecondPerAddress { get; set; } = 5;
    public int ConnectionsPerSecond { get; set; } = 20;
}
