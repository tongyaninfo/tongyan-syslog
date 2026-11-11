namespace Syslog.Shared.Models;

/// <summary>未登记设备的内存观察结果，不代表可信设备身份或日志已入库。</summary>
public sealed record UnregisteredDevice(string PeerAddress, DateTimeOffset FirstReceivedAt,
    DateTimeOffset LastReceivedAt, string[] Transports, long Received, int SampleCount);

/// <summary>SampleKey 为随机进程前缀与内存序号，重启失效，不能用于业务事件关联。</summary>
public sealed record UnregisteredLogSummary(string SampleKey, DateTimeOffset ReceivedAt, string Transport,
    int ByteCount, string Preview);

public sealed record UnregisteredLogDetail(string SampleKey, DateTimeOffset ReceivedAt, string Transport, byte[] RawBytes);
public sealed record UnregisteredDevicePage(IReadOnlyList<UnregisteredDevice> Items, UnregisteredBufferStatus Status);
public sealed record UnregisteredBufferStatus(bool Enabled, string[] AllowedNetworks, int DeviceCount, int SampleCount,
    long BufferedBytes, long MaxBytes, int MaxDevices, long Received, long Evicted, long Rejected);
