using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Syslog.Ingest.Options;
using Syslog.Shared.Models;

namespace Syslog.Ingest.Service.impl;

/// <summary>仅保留内存样本。每 IP 最近 100 条与全局字节/设备预算共同约束，重启后不恢复。</summary>
public sealed class UnregisteredDeviceBuffer : IUnregisteredDeviceBuffer
{
    public const int SamplesPerDevice = 100;
    // 元数据预算覆盖对象、链表节点、字典项与地址；小报文不能绕过全局内存限制。
    private const long DeviceCost = 1024;
    private const long SampleCost = 384;
    private const long RateCost = 256;
    private const int MaximumRateAddresses = 2048;
    private readonly object gate = new();
    private readonly Dictionary<IPAddress, DeviceState> devices = [];
    private readonly LinkedList<DeviceState> recentDevices = [];
    private readonly LinkedList<Sample> samples = [];
    private readonly Dictionary<IPAddress, AddressRateState> addressRates = [];
    private readonly string sampleEpoch = Guid.NewGuid().ToString("N");
    private readonly UnregisteredDeviceOptions options;
    private readonly IPNetwork[] networks;
    private readonly TimeProvider clock;
    private volatile bool receptionEnabled;
    private readonly int maxMessageBytes;
    private readonly RateWindow messages = new();
    private readonly RateWindow connections = new();
    private long bufferedBytes;
    private long sequence;
    private long received;
    private long evicted;
    private long rejected;
    private int activeConnections;

    public UnregisteredDeviceBuffer(IOptions<IngestOptions> settings, TimeProvider clock)
    {
        options = settings.Value.UnregisteredDevices;
        this.clock = clock;
        receptionEnabled = settings.Value.Enabled;
        maxMessageBytes = settings.Value.MaxMessageBytes;
        if (options.MaxDevices is < 1 or > 1000 || options.MaxBytes is < DeviceCost + SampleCost + RateCost + 1 or > 64 * 1024 * 1024
            || options.MessagesPerSecondPerAddress is < 1 or > 100_000 || options.MessagesPerSecond is < 1 or > 100_000
            || options.MaxConnections is < 1 or > 64 || options.MaxConnectionsPerAddress is < 1 or > 64
            || options.ConnectionsPerSecondPerAddress is < 1 or > 1000 || options.ConnectionsPerSecond is < 1 or > 1000
            || maxMessageBytes is < 1 or > 65536 || options.AllowedNetworks is null || options.AllowedNetworks.Length > 128)
            throw new ArgumentException("未登记设备暂存配置无效。");
        var parsed = new List<IPNetwork>();
        foreach (var value in options.AllowedNetworks)
        {
            if (!IPNetwork.TryParse(value, out var network)) throw new ArgumentException("未登记设备许可网段配置无效。");
            parsed.Add(network);
        }
        networks = parsed.ToArray();
    }

    public void SetReceptionEnabled(bool enabled)
    {
        // 暂停时关闭新样本和连接配额，已有样本继续供管理台读取。
        lock (gate) receptionEnabled = enabled;
    }

    public bool IsAllowed(IPAddress peer)
    {
        peer = Normalize(peer);
        return receptionEnabled && options.Enabled && networks.Any(network => network.Contains(peer));
    }

    public bool TryCapture(ReadOnlySpan<byte> bytes, IPAddress peer, string transport)
    {
        peer = Normalize(peer);
        lock (gate)
        {
            if (!IsAllowed(peer) || bytes.IsEmpty || bytes.Length > maxMessageBytes || transport is not ("udp" or "tcp" or "tls" or "relp"))
                return Reject();
            var cost = bytes.Length + SampleCost;
            if (!CanPrepare(peer, cost)) return Reject();
            var now = clock.GetUtcNow();
            if (!messages.Allow(clock, options.MessagesPerSecond)) return Reject();
            var rate = GetRate(peer);
            if (rate is null || !rate.Messages.Allow(clock, options.MessagesPerSecondPerAddress)) return Reject();
            var device = GetOrCreate(peer);
            if (device is null) return Reject();
            if (device.Samples.Count == SamplesPerDevice) RemoveSample(device.Samples.First!.Value, true);
            if (!MakeRoom(cost, device)) return Reject();
            var sample = new Sample(sampleEpoch + "-" + (++sequence).ToString(CultureInfo.InvariantCulture), now, transport, bytes.ToArray(), device);
            sample.DeviceNode = device.Samples.AddLast(sample);
            sample.GlobalNode = samples.AddLast(sample);
            bufferedBytes += cost;
            device.FirstReceivedAt ??= now;
            device.LastReceivedAt = now;
            device.Received++;
            device.Transports.Add(transport);
            received++;
            Touch(device);
            return true;
        }
    }

    public bool TryReserveConnection(IPAddress peer)
    {
        peer = Normalize(peer);
        lock (gate)
        {
            if (!IsAllowed(peer) || activeConnections >= options.MaxConnections
                || !connections.Allow(clock, options.ConnectionsPerSecond)) return Reject();
            if (!CanPrepare(peer, 0)) return Reject();
            var rate = GetRate(peer);
            if (rate is null || !rate.Connections.Allow(clock, options.ConnectionsPerSecondPerAddress)) return Reject();
            var device = GetOrCreate(peer);
            if (device is null || device.Connections >= options.MaxConnectionsPerAddress) return Reject();
            device.Connections++;
            activeConnections++;
            Touch(device);
            return true;
        }
    }

    public void ReleaseConnection(IPAddress peer)
    {
        lock (gate)
        {
            if (!devices.TryGetValue(Normalize(peer), out var device) || device.Connections == 0) return;
            device.Connections--;
            activeConnections--;
        }
    }

    public void RemoveRegistered(IReadOnlyCollection<IPAddress> peers)
    {
        lock (gate)
        {
            foreach (var peer in peers)
            {
                if (!devices.TryGetValue(Normalize(peer), out var device)) continue;
                // 登记后只结束观察，不把暂存样本补写正式日志；仍在使用的连接配额必须等连接退出释放。
                while (device.Samples.First is { } first) RemoveSample(first.Value, false);
                if (device.Connections == 0) RemoveDevice(device, false);
            }
        }
    }

    public UnregisteredDevicePage Snapshot()
    {
        lock (gate)
        {
            var items = recentDevices.Reverse().Where(device => device.Samples.Count > 0).Select(device =>
                new UnregisteredDevice(device.Peer.ToString(), device.FirstReceivedAt!.Value, device.LastReceivedAt,
                    device.Transports.Order().ToArray(), device.Received, device.Samples.Count)).ToArray();
            return new(items, new(receptionEnabled && options.Enabled, options.AllowedNetworks.ToArray(), items.Length, samples.Count, bufferedBytes,
                options.MaxBytes, options.MaxDevices, received, evicted, rejected));
        }
    }

    public IReadOnlyList<UnregisteredLogSummary> GetLogs(IPAddress peer)
    {
        lock (gate)
        {
            if (!devices.TryGetValue(Normalize(peer), out var device)) return [];
            return device.Samples.Reverse().Select(sample => new UnregisteredLogSummary(sample.Key, sample.ReceivedAt,
                sample.Transport, sample.Bytes.Length, Preview(sample.Bytes))).ToArray();
        }
    }

    public UnregisteredLogDetail? GetSample(IPAddress peer, string sampleKey)
    {
        lock (gate)
        {
            if (!devices.TryGetValue(Normalize(peer), out var device)) return null;
            var sample = device.Samples.FirstOrDefault(item => item.Key == sampleKey);
            return sample is null ? null : new(sample.Key, sample.ReceivedAt, sample.Transport, sample.Bytes.ToArray());
        }
    }

    private DeviceState? GetOrCreate(IPAddress peer)
    {
        if (devices.TryGetValue(peer, out var existing)) return existing;
        if (devices.Count >= options.MaxDevices)
        {
            var oldest = recentDevices.FirstOrDefault(device => device.Connections == 0);
            if (oldest is null) return null;
            RemoveDevice(oldest, true);
        }
        if (!MakeRoom(DeviceCost, null)) return null;
        var created = new DeviceState(peer);
        created.RecentNode = recentDevices.AddLast(created);
        devices.Add(peer, created);
        bufferedBytes += DeviceCost;
        return created;
    }

    private bool MakeRoom(long extra, DeviceState? protectedDevice)
    {
        // 活跃连接元数据和本次目标不能被回收；先证明本次准入可行，再淘汰已有样本。
        var pinned = devices.Values.Count(device => device.Connections > 0) * DeviceCost;
        if (protectedDevice is not null && protectedDevice.Connections == 0) pinned += DeviceCost;
        if (pinned + addressRates.Count * RateCost + extra > options.MaxBytes) return false;
        while (bufferedBytes + extra > options.MaxBytes)
        {
            if (samples.First is { } first)
            {
                var device = first.Value.Device;
                RemoveSample(first.Value, true);
                if (device.Samples.Count == 0 && device.Connections == 0 && device != protectedDevice) RemoveDevice(device, false);
                continue;
            }
            var unused = recentDevices.FirstOrDefault(device => device != protectedDevice && device.Connections == 0);
            if (unused is null) return false;
            RemoveDevice(unused, false);
        }
        return true;
    }

    private bool CanPrepare(IPAddress peer, long sampleCost)
    {
        PruneExpiredRates();
        devices.TryGetValue(peer, out var device);
        if (device is null && devices.Count >= options.MaxDevices && devices.Values.All(item => item.Connections > 0)) return false;
        var newRate = !addressRates.ContainsKey(peer);
        if (newRate && addressRates.Count >= MaximumRateAddresses) return false;
        var pinned = devices.Values.Count(item => item.Connections > 0) * DeviceCost;
        if (device is null || device.Connections == 0) pinned += DeviceCost;
        return pinned + (addressRates.Count + (newRate ? 1 : 0)) * RateCost + sampleCost <= options.MaxBytes;
    }

    private AddressRateState? GetRate(IPAddress peer)
    {
        if (!addressRates.TryGetValue(peer, out var rate))
        {
            devices.TryGetValue(peer, out var protectedDevice);
            if (addressRates.Count >= MaximumRateAddresses || !MakeRoom(RateCost, protectedDevice)) return null;
            rate = new AddressRateState();
            addressRates.Add(peer, rate);
            bufferedBytes += RateCost;
        }
        rate.LastUsed = clock.GetTimestamp();
        return rate;
    }

    private void PruneExpiredRates()
    {
        // 配额状态与设备样本分离。驱逐设备不会重置当前秒的配额；只回收整个窗口已过期的地址。
        var now = clock.GetTimestamp();
        foreach (var peer in addressRates.Where(item => clock.GetElapsedTime(item.Value.LastUsed, now) >= TimeSpan.FromSeconds(1))
                     .Select(item => item.Key).ToArray())
        {
            addressRates.Remove(peer);
            bufferedBytes -= RateCost;
        }
    }

    private void RemoveSample(Sample sample, bool countEviction)
    {
        sample.Device.Samples.Remove(sample.DeviceNode!);
        samples.Remove(sample.GlobalNode!);
        bufferedBytes -= sample.Bytes.Length + SampleCost;
        if (countEviction) evicted++;
    }

    private void RemoveDevice(DeviceState device, bool countEviction)
    {
        while (device.Samples.First is { } first) RemoveSample(first.Value, countEviction);
        recentDevices.Remove(device.RecentNode!);
        devices.Remove(device.Peer);
        bufferedBytes -= DeviceCost;
    }

    private void Touch(DeviceState device)
    {
        recentDevices.Remove(device.RecentNode!);
        recentDevices.AddLast(device.RecentNode!);
    }

    private bool Reject() { rejected++; return false; }
    private static string Preview(byte[] bytes) => Encoding.UTF8.GetString(bytes.AsSpan(0, Math.Min(bytes.Length, 512))) + (bytes.Length > 512 ? "…" : "");
    private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    private sealed class DeviceState(IPAddress peer)
    {
        public IPAddress Peer { get; } = peer;
        public LinkedList<Sample> Samples { get; } = [];
        public LinkedListNode<DeviceState>? RecentNode { get; set; }
        public HashSet<string> Transports { get; } = [];
        public DateTimeOffset? FirstReceivedAt { get; set; }
        public DateTimeOffset LastReceivedAt { get; set; }
        public long Received { get; set; }
        public int Connections { get; set; }
    }

    private sealed class AddressRateState
    {
        public long LastUsed { get; set; }
        public RateWindow Messages { get; } = new();
        public RateWindow Connections { get; } = new();
    }

    private sealed class Sample(string key, DateTimeOffset receivedAt, string transport, byte[] bytes, DeviceState device)
    {
        public string Key { get; } = key;
        public DateTimeOffset ReceivedAt { get; } = receivedAt;
        public string Transport { get; } = transport;
        public byte[] Bytes { get; } = bytes;
        public DeviceState Device { get; } = device;
        public LinkedListNode<Sample>? DeviceNode { get; set; }
        public LinkedListNode<Sample>? GlobalNode { get; set; }
    }

    // 所有调用均持有 gate；使用单调时钟避免系统时间调整重置配额。
    private sealed class RateWindow
    {
        private long? started;
        private int count;
        public bool Allow(TimeProvider clock, int maximum)
        {
            var now = clock.GetTimestamp();
            if (started is null || clock.GetElapsedTime(started.Value, now) >= TimeSpan.FromSeconds(1)) { started = now; count = 0; }
            if (count >= maximum) return false;
            count++;
            return true;
        }
    }
}
