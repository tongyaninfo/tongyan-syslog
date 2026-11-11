using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Syslog.Ingest.Options;
using Syslog.Ingest.Service.impl;
using Xunit;

namespace Syslog.Ingest.Tests;

public sealed class UnregisteredDeviceBufferTests
{
    [Fact]
    public void EachAddressKeepsLatestHundredAndPreservesIndependentRawBytes()
    {
        var buffer = Create(out var clock);
        var peer = IPAddress.Parse("192.0.2.1");
        for (var index = 0; index < 101; index++)
        {
            Assert.True(buffer.TryCapture(Encoding.UTF8.GetBytes($"synthetic-{index}"), peer, "udp"));
            clock.Advance(TimeSpan.FromMilliseconds(1));
        }
        var other = IPAddress.Parse("192.0.2.2");
        byte[] bytes = [0x3c, 0xff, 0x00, 0x3e];
        Assert.True(buffer.TryCapture(bytes, other, "tls"));
        bytes[0] = 0;
        var summaries = buffer.GetLogs(peer);
        Assert.Equal(100, summaries.Count);
        Assert.Equal("synthetic-100", summaries[0].Preview);
        Assert.Equal("synthetic-1", summaries[^1].Preview);
        var summary = Assert.Single(buffer.GetLogs(other));
        var detail = buffer.GetSample(other, summary.SampleKey)!;
        Assert.Equal(new byte[] { 0x3c, 0xff, 0x00, 0x3e }, detail.RawBytes);
        detail.RawBytes[0] = 0;
        Assert.Equal(0x3c, buffer.GetSample(other, summary.SampleKey)!.RawBytes[0]);
        Assert.Null(buffer.GetSample(peer, summary.SampleKey));
        var snapshot = buffer.Snapshot();
        Assert.Equal(2, snapshot.Status.DeviceCount);
        Assert.Equal(101, snapshot.Status.SampleCount);
        Assert.Equal(102, snapshot.Status.Received);
        Assert.Equal(1, snapshot.Status.Evicted);
        Assert.Equal(101, snapshot.Items.Single(item => item.PeerAddress == peer.ToString()).Received);
    }

    [Fact]
    public void PermissionNetworksAndMessageLimitsRejectBeforeAllocatingSamples()
    {
        var settings = Settings();
        settings.MaxMessageBytes = 8;
        var buffer = new UnregisteredDeviceBuffer(Microsoft.Extensions.Options.Options.Create(settings), new TestClock());
        var peer = IPAddress.Parse("192.0.2.1");
        Assert.False(buffer.TryCapture("x"u8, IPAddress.Parse("203.0.113.1"), "udp"));
        Assert.False(buffer.TryCapture([], peer, "udp"));
        Assert.False(buffer.TryCapture("123456789"u8, peer, "udp"));
        Assert.False(buffer.TryCapture("x"u8, peer, "invalid"));
        Assert.True(buffer.TryCapture("x"u8, IPAddress.Parse("::ffff:192.0.2.1"), "udp"));
        Assert.Equal(peer.ToString(), Assert.Single(buffer.Snapshot().Items).PeerAddress);
        Assert.Equal(4, buffer.Snapshot().Status.Rejected);
        Assert.Single(buffer.GetLogs(peer));
    }

    [Fact]
    public void GlobalByteBudgetIncludesMetadataAndEvictsOldestSamples()
    {
        var settings = Settings();
        settings.UnregisteredDevices.MaxBytes = 3500;
        var buffer = new UnregisteredDeviceBuffer(Microsoft.Extensions.Options.Options.Create(settings), new TestClock());
        var peer = IPAddress.Parse("192.0.2.1");
        for (var index = 0; index < 10; index++) Assert.True(buffer.TryCapture(new byte[512], peer, "udp"));
        var snapshot = buffer.Snapshot();
        Assert.Equal(2, snapshot.Status.SampleCount);
        Assert.Equal(8, snapshot.Status.Evicted);
        Assert.Equal(3072, snapshot.Status.BufferedBytes);
        Assert.True(snapshot.Status.BufferedBytes <= snapshot.Status.MaxBytes);
        Assert.EndsWith("-10", buffer.GetLogs(peer)[0].SampleKey);
        Assert.Null(buffer.GetSample(peer, buffer.GetLogs(peer)[0].SampleKey[..^2] + "1"));
    }

    [Fact]
    public void DeviceLimitEvictsLeastRecentlyActiveDevice()
    {
        var settings = Settings();
        settings.UnregisteredDevices.MaxDevices = 2;
        var buffer = new UnregisteredDeviceBuffer(Microsoft.Extensions.Options.Options.Create(settings), new TestClock());
        var first = IPAddress.Parse("192.0.2.1");
        var second = IPAddress.Parse("192.0.2.2");
        var third = IPAddress.Parse("192.0.2.3");
        Assert.True(buffer.TryCapture("first"u8, first, "udp"));
        Assert.True(buffer.TryCapture("second"u8, second, "tcp"));
        Assert.True(buffer.TryCapture("latest"u8, first, "tls"));
        Assert.True(buffer.TryCapture("third"u8, third, "udp"));
        Assert.Empty(buffer.GetLogs(second));
        Assert.Equal(2, buffer.Snapshot().Status.DeviceCount);
        Assert.Equal(1, buffer.Snapshot().Status.Evicted);
        Assert.Equal(new[] { "tls", "udp" }, buffer.Snapshot().Items.Single(item => item.PeerAddress == first.ToString()).Transports);
    }

    [Fact]
    public void DeviceEvictionCannotResetAddressMessageOrConnectionRate()
    {
        var settings = Settings();
        settings.UnregisteredDevices.MaxDevices = 1;
        settings.UnregisteredDevices.MessagesPerSecondPerAddress = 1;
        settings.UnregisteredDevices.ConnectionsPerSecondPerAddress = 1;
        var buffer = new UnregisteredDeviceBuffer(Microsoft.Extensions.Options.Options.Create(settings), new TestClock());
        var first = IPAddress.Parse("192.0.2.1");
        var second = IPAddress.Parse("192.0.2.2");
        Assert.True(buffer.TryCapture("first"u8, first, "udp"));
        Assert.True(buffer.TryReserveConnection(first));
        buffer.ReleaseConnection(first);
        Assert.True(buffer.TryCapture("second"u8, second, "udp"));
        Assert.False(buffer.TryCapture("first-again"u8, first, "udp"));
        Assert.False(buffer.TryReserveConnection(first));
        Assert.Single(buffer.GetLogs(second));
    }

    [Fact]
    public void UnaffordableSampleDoesNotEvictExistingEvidence()
    {
        var settings = Settings();
        settings.UnregisteredDevices.MaxBytes = 3500;
        settings.UnregisteredDevices.MaxDevices = 1;
        var buffer = new UnregisteredDeviceBuffer(Microsoft.Extensions.Options.Options.Create(settings), new TestClock());
        var first = IPAddress.Parse("192.0.2.1");
        Assert.True(buffer.TryCapture("keep"u8, first, "udp"));
        Assert.False(buffer.TryCapture(new byte[3000], IPAddress.Parse("192.0.2.2"), "udp"));
        Assert.Equal("keep", Assert.Single(buffer.GetLogs(first)).Preview);
        Assert.Equal(0, buffer.Snapshot().Status.Evicted);
    }

    [Fact]
    public void ActiveConnectionMetadataCannotCauseDestructiveFailedAdmission()
    {
        var settings = Settings();
        settings.UnregisteredDevices.MaxBytes = 3500;
        var buffer = new UnregisteredDeviceBuffer(Microsoft.Extensions.Options.Options.Create(settings), new TestClock());
        var first = IPAddress.Parse("192.0.2.1");
        var second = IPAddress.Parse("192.0.2.2");
        Assert.True(buffer.TryReserveConnection(first));
        Assert.True(buffer.TryReserveConnection(second));
        Assert.True(buffer.TryCapture("keep"u8, first, "tcp"));
        Assert.False(buffer.TryCapture(new byte[600], second, "tcp"));
        Assert.Equal("keep", Assert.Single(buffer.GetLogs(first)).Preview);
        Assert.Equal(0, buffer.Snapshot().Status.Evicted);
    }

    [Fact]
    public void SampleKeysCannotResolveToDifferentEvidenceAfterBufferRestart()
    {
        var first = Create(out _);
        var second = Create(out _);
        var peer = IPAddress.Parse("192.0.2.1");
        Assert.True(first.TryCapture("old"u8, peer, "udp"));
        Assert.True(second.TryCapture("new"u8, peer, "udp"));
        var oldKey = Assert.Single(first.GetLogs(peer)).SampleKey;
        var newKey = Assert.Single(second.GetLogs(peer)).SampleKey;
        Assert.NotEqual(oldKey, newKey);
        Assert.Null(second.GetSample(peer, oldKey));
    }

    [Fact]
    public void RateAddressTableHasFixedLimitAndReclaimsOnlyExpiredWindows()
    {
        var settings = Settings();
        settings.UnregisteredDevices.MaxDevices = 1;
        settings.UnregisteredDevices.MessagesPerSecond = 100_000;
        settings.UnregisteredDevices.AllowedNetworks = ["2001:db8::/64"];
        var clock = new TestClock();
        var buffer = new UnregisteredDeviceBuffer(Microsoft.Extensions.Options.Options.Create(settings), clock);
        for (var index = 1; index <= 2048; index++)
            Assert.True(buffer.TryCapture("test"u8, IPAddress.Parse($"2001:db8::{index:x}"), "udp"));
        var next = IPAddress.Parse("2001:db8::801");
        Assert.False(buffer.TryCapture("new"u8, next, "udp"));
        Assert.Equal(2048, buffer.Snapshot().Status.Received);
        Assert.Equal("2001:db8::800", Assert.Single(buffer.Snapshot().Items).PeerAddress);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(buffer.TryCapture("new"u8, next, "udp"));
        Assert.True(buffer.Snapshot().Status.BufferedBytes < 2048);
    }

    [Fact]
    public void MessageAndConnectionBudgetsResetOnlyAfterTheirWindow()
    {
        var settings = Settings();
        settings.UnregisteredDevices.MessagesPerSecond = 2;
        settings.UnregisteredDevices.MessagesPerSecondPerAddress = 1;
        settings.UnregisteredDevices.MaxConnections = 2;
        settings.UnregisteredDevices.MaxConnectionsPerAddress = 1;
        settings.UnregisteredDevices.ConnectionsPerSecondPerAddress = 1;
        settings.UnregisteredDevices.ConnectionsPerSecond = 20;
        var clock = new TestClock();
        var buffer = new UnregisteredDeviceBuffer(Microsoft.Extensions.Options.Options.Create(settings), clock);
        var first = IPAddress.Parse("192.0.2.1");
        var second = IPAddress.Parse("192.0.2.2");
        Assert.True(buffer.TryCapture("one"u8, first, "udp"));
        Assert.False(buffer.TryCapture("two"u8, first, "udp"));
        Assert.False(buffer.TryCapture("three"u8, second, "udp"));
        Assert.True(buffer.TryReserveConnection(first));
        Assert.False(buffer.TryReserveConnection(first));
        buffer.ReleaseConnection(first);
        Assert.False(buffer.TryReserveConnection(first));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(buffer.TryCapture("next"u8, first, "udp"));
        Assert.True(buffer.TryReserveConnection(first));
        Assert.True(buffer.TryReserveConnection(second));
        Assert.False(buffer.TryReserveConnection(IPAddress.Parse("192.0.2.3")));
        buffer.RemoveRegistered([first]);
        Assert.Empty(buffer.GetLogs(first));
        buffer.ReleaseConnection(first);
        buffer.ReleaseConnection(second);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(buffer.TryReserveConnection(IPAddress.Parse("192.0.2.3")));
    }

    [Fact]
    public void ZeroSampleConnectionsAreHiddenAndCannotBeEvictedWhileInUse()
    {
        var settings = Settings();
        settings.UnregisteredDevices.MaxDevices = 1;
        var buffer = new UnregisteredDeviceBuffer(Microsoft.Extensions.Options.Options.Create(settings), new TestClock());
        var first = IPAddress.Parse("192.0.2.1");
        var second = IPAddress.Parse("192.0.2.2");
        Assert.True(buffer.TryReserveConnection(first));
        Assert.Empty(buffer.Snapshot().Items);
        Assert.False(buffer.TryCapture("other"u8, second, "udp"));
        buffer.ReleaseConnection(first);
        Assert.True(buffer.TryCapture("other"u8, second, "udp"));
        Assert.Single(buffer.Snapshot().Items);
    }

    [Fact]
    public async Task ConcurrentCaptureAndReadsStayBoundedAndNeverExposeMutableEvidence()
    {
        var settings = Settings();
        settings.UnregisteredDevices.MessagesPerSecond = 100_000;
        settings.UnregisteredDevices.MessagesPerSecondPerAddress = 100_000;
        var buffer = new UnregisteredDeviceBuffer(Microsoft.Extensions.Options.Options.Create(settings), new TestClock());
        var peer = IPAddress.Parse("192.0.2.1");
        await Task.WhenAll(Enumerable.Range(0, 1000).Select(index => Task.Run(() =>
        {
            Assert.True(buffer.TryCapture(Encoding.UTF8.GetBytes($"sample-{index}"), peer, "udp"));
            Assert.InRange(buffer.GetLogs(peer).Count, 1, 100);
            Assert.InRange(buffer.Snapshot().Status.BufferedBytes, 1, settings.UnregisteredDevices.MaxBytes);
        })));
        var logs = buffer.GetLogs(peer);
        Assert.Equal(100, logs.Count);
        Assert.Equal(100, logs.Select(item => item.SampleKey).Distinct().Count());
        Assert.Equal(900, buffer.Snapshot().Status.Evicted);
        Assert.Equal(1000, buffer.Snapshot().Status.Received);
    }

    [Fact]
    public void PreviewIsBoundedButDetailRetainsOriginalBytes()
    {
        var buffer = Create(out _);
        var peer = IPAddress.Parse("192.0.2.1");
        var bytes = Encoding.UTF8.GetBytes("<script>alert(1)</script>" + new string('x', 4096));
        Assert.True(buffer.TryCapture(bytes, peer, "udp"));
        var summary = Assert.Single(buffer.GetLogs(peer));
        Assert.Equal(513, summary.Preview.Length);
        Assert.EndsWith("…", summary.Preview);
        Assert.Equal(bytes.Length, summary.ByteCount);
        Assert.Equal(bytes, buffer.GetSample(peer, summary.SampleKey)!.RawBytes);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void EitherDisabledSwitchStopsCaptureAndConnectionReservation(bool ingestEnabled, bool featureEnabled)
    {
        var settings = Settings();
        settings.Enabled = ingestEnabled;
        settings.UnregisteredDevices.Enabled = featureEnabled;
        var buffer = new UnregisteredDeviceBuffer(Microsoft.Extensions.Options.Options.Create(settings), new TestClock());
        var peer = IPAddress.Parse("192.0.2.1");
        Assert.False(buffer.TryCapture("test"u8, peer, "udp"));
        Assert.False(buffer.TryReserveConnection(peer));
        Assert.False(buffer.Snapshot().Status.Enabled);
        Assert.Empty(buffer.Snapshot().Items);
    }

    [Fact]
    public void InvalidNetworkOrUnboundedConfigurationFailsBeforeCapture()
    {
        var settings = Settings();
        settings.UnregisteredDevices.AllowedNetworks = ["not-a-cidr"];
        Assert.Throws<ArgumentException>(() => new UnregisteredDeviceBuffer(Microsoft.Extensions.Options.Options.Create(settings), new TestClock()));
        settings = Settings();
        settings.UnregisteredDevices.MaxBytes = 65 * 1024 * 1024;
        Assert.Throws<ArgumentException>(() => new UnregisteredDeviceBuffer(Microsoft.Extensions.Options.Options.Create(settings), new TestClock()));
    }

    private static UnregisteredDeviceBuffer Create(out TestClock clock)
    {
        clock = new TestClock();
        return new(Microsoft.Extensions.Options.Options.Create(Settings()), clock);
    }

    private static IngestOptions Settings() => new()
    {
        Enabled = true,
        UnregisteredDevices = new()
        {
            AllowedNetworks = ["192.0.2.0/24"], MessagesPerSecond = 1000, MessagesPerSecondPerAddress = 1000
        }
    };

    private sealed class TestClock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref timestamp);
        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddTicks(GetTimestamp());
        public void Advance(TimeSpan duration) => Interlocked.Add(ref timestamp, duration.Ticks);
    }
}
