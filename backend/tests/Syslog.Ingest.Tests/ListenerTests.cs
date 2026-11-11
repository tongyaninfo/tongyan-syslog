using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Options;
using Syslog.Engine.Common;
using Syslog.Ingest.Common;
using Syslog.Ingest.Models;
using Syslog.Ingest.Options;
using Syslog.Ingest.Service;
using Syslog.Ingest.Service.impl;
using Syslog.Ingest.Workers;
using Syslog.Shared.Models;
using Syslog.Shared.Service;
using Xunit;

namespace Syslog.Ingest.Tests;

public sealed class ListenerTests
{
    [Fact]
    public async Task DisabledWorkerNeverReadsRegistryCertificateOrBindsConfiguredEndpoint()
    {
        var registry = new FakeRegistry { Fail = true };
        var settings = new IngestOptions { Enabled = false, Listeners = [new() { Transport = "tls", CertificatePath = "must-not-be-read.pfx", Address = "invalid-address" }] };
        using var worker = Create(settings, registry, out _, out var status);
        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);
        Assert.Equal(0, registry.Calls);
        Assert.Equal("disabled", status.ListenerState);
    }

    [Fact]
    public async Task SourceWhitelistNormalizesMappedAddressesAndCopiesEvidence()
    {
        var registry = new FakeRegistry();
        using var worker = Create(new(), registry, out var queue, out var status);
        Assert.True(await worker.RefreshSourcesAsync(CancellationToken.None));
        var bytes = Encoding.UTF8.GetBytes("<14>synthetic");
        Assert.True(worker.AcceptMessage(bytes, IPAddress.Parse("::ffff:192.0.2.10"), "tcp"));
        bytes[0] = 0;
        Assert.True(queue.TryRead(out var received));
        Assert.Equal((byte)'<', received!.Bytes[0]);
        Assert.Equal("192.0.2.10", received.Context.PeerAddress);
        Assert.Equal("synthetic", received.Context.SourceId);
        Assert.Equal(TimeSpan.FromDays(365), received.Context.ExpiresAt - received.Context.ReceivedAt);
        Assert.Equal(0, received.Context.ReceivedAt.Ticks % TimeSpan.TicksPerMillisecond);
        Assert.Equal(1, status.Received);
        Assert.Equal(0, status.Dropped);
        Assert.Equal(new SourceIngestCounters(1, 0), status.GetSourceCounters("synthetic"));
    }

    [Fact]
    public async Task QueuedMessagesKeepTheSourceSeverityPolicyAtReception()
    {
        var registry = new FakeRegistry();
        registry.Sources = [registry.Sources[0] with { IgnoredSeverities = [0, 7] }];
        using var worker = Create(new(), registry, out var queue, out _);
        var peer = IPAddress.Parse("192.0.2.10");
        Assert.True(await worker.RefreshSourcesAsync(default));
        Assert.True(worker.AcceptMessage("first"u8, peer, "udp"));

        registry.Sources = [registry.Sources[0] with { IgnoredSeverities = [3] }];
        Assert.True(await worker.RefreshSourcesAsync(default));
        Assert.True(worker.AcceptMessage("second"u8, peer, "udp"));

        Assert.True(queue.TryRead(out var first));
        Assert.Equal((byte)0b1000_0001, first!.IgnoredSeverityMask);
        Assert.True(queue.TryRead(out var second));
        Assert.Equal((byte)0b0000_1000, second!.IgnoredSeverityMask);
    }

    [Fact]
    public async Task UnknownDisabledAndRevokedSourcesAreRejectedAndCounted()
    {
        var registry = new FakeRegistry();
        using var worker = Create(new(), registry, out var queue, out var status);
        await worker.RefreshSourcesAsync(CancellationToken.None);
        Assert.False(worker.AcceptMessage("test"u8, IPAddress.Parse("192.0.2.11"), "udp"));
        registry.Sources = [registry.Sources[0] with { Enabled = false }];
        await worker.RefreshSourcesAsync(CancellationToken.None);
        Assert.False(worker.AcceptMessage("test"u8, IPAddress.Parse("192.0.2.10"), "udp"));
        Assert.False(queue.TryRead(out _));
        Assert.Equal(2, status.Received);
        Assert.Equal(8, status.ReceivedBytes);
        Assert.Equal(2, status.Dropped);
        Assert.Equal(new SourceIngestCounters(1, 0), status.GetSourceCounters("synthetic"));
        Assert.Equal(default(SourceIngestCounters), status.GetSourceCounters("unobserved"));
    }

    [Fact]
    public async Task RegistryFailureFailsClosedAndSuccessfulRefreshRestoresSnapshot()
    {
        var registry = new FakeRegistry();
        using var worker = Create(new(), registry, out _, out var status);
        await worker.RefreshSourcesAsync(CancellationToken.None);
        registry.Fail = true;
        Assert.False(await worker.RefreshSourcesAsync(CancellationToken.None));
        Assert.False(worker.AcceptMessage("test"u8, IPAddress.Parse("192.0.2.10"), "udp"));
        Assert.Equal("source_registry_unavailable", status.ListenerFailureCode);
        registry.Fail = false;
        Assert.True(await worker.RefreshSourcesAsync(CancellationToken.None));
        Assert.True(worker.AcceptMessage("test"u8, IPAddress.Parse("192.0.2.10"), "udp"));
        Assert.Equal("", status.ListenerFailureCode);
    }

    [Fact]
    public async Task RateBudgetSurvivesRefreshAndQueueFailuresAreNotDoubleCounted()
    {
        var registry = new FakeRegistry();
        var settings = new IngestOptions { MessagesPerSecondPerSource = 1, QueueCapacity = 1 };
        using var worker = Create(settings, registry, out _, out var status);
        await worker.RefreshSourcesAsync(CancellationToken.None);
        Assert.True(worker.AcceptMessage("first"u8, IPAddress.Parse("192.0.2.10"), "udp"));
        await worker.RefreshSourcesAsync(CancellationToken.None);
        Assert.False(worker.AcceptMessage("second"u8, IPAddress.Parse("192.0.2.10"), "udp"));
        Assert.Equal(2, status.Received);
        Assert.Equal(1, status.Dropped);
        Assert.Equal(new SourceIngestCounters(2, 0), status.GetSourceCounters("synthetic"));
    }

    [Fact]
    public async Task StorageFailureStopsAcceptanceWithoutOverwritingStorageStatus()
    {
        var registry = new FakeRegistry();
        using var worker = Create(new(), registry, out _, out var status);
        await worker.RefreshSourcesAsync(CancellationToken.None);
        status.State = "failed";
        status.FailureCode = "storage_failed";
        Assert.False(worker.AcceptMessage("test"u8, IPAddress.Parse("192.0.2.10"), "tcp"));
        Assert.Equal("storage_failed", status.FailureCode);
        Assert.Equal(1, status.Dropped);
    }

    [Fact]
    public async Task DuplicateNormalizedSourcesInvalidateWholeSnapshot()
    {
        var registry = new FakeRegistry();
        registry.Sources = [registry.Sources[0], registry.Sources[0] with { Id = "duplicate", PeerAddress = "::ffff:192.0.2.10" }];
        using var worker = Create(new(), registry, out _, out var status);
        Assert.False(await worker.RefreshSourcesAsync(CancellationToken.None));
        Assert.False(worker.AcceptMessage("test"u8, IPAddress.Parse("192.0.2.10"), "udp"));
        Assert.Equal("source_registry_unavailable", status.ListenerFailureCode);
    }

    [Theory]
    [InlineData("udp")]
    [InlineData("tcp")]
    [InlineData("tls")]
    [InlineData("relp")]
    public async Task UnregisteredSourceUsesMemoryAndPromotionStartsOnlyFutureFormalEvents(string transport)
    {
        var registry = new FakeRegistry();
        using var worker = CreateWithDiscovery(registry, out var queue, out var status, out var buffer);
        var peer = IPAddress.Parse("192.0.2.11");
        Assert.False(worker.AcceptMessage("before-snapshot"u8, peer, transport));
        await worker.RefreshSourcesAsync(CancellationToken.None);
        Assert.True(worker.AcceptMessage("observed"u8, peer, transport));
        Assert.Single(buffer.GetLogs(peer));
        Assert.False(queue.TryRead(out _));
        Assert.Equal(1, status.UnregisteredReceived);
        Assert.Equal(0, status.Parsed);
        Assert.Equal(0, status.Persisted);
        Assert.Equal(0, status.Written);
        registry.Sources = [new("registered", "Synthetic", peer.ToString(), "UTC", 365, true)];
        await worker.RefreshSourcesAsync(CancellationToken.None);
        Assert.Empty(buffer.GetLogs(peer));
        Assert.True(worker.AcceptMessage("after"u8, peer, transport));
        Assert.True(queue.TryRead(out var formal));
        Assert.Equal("registered", formal!.Context.SourceId);
        Assert.Equal("after"u8.ToArray(), formal.Bytes);
        Assert.False(queue.TryRead(out _));
        Assert.Equal(1, status.UnregisteredReceived);
    }

    [Fact]
    public async Task DisabledSourcesAndFailedRegistryNeverFallBackToDiscovery()
    {
        var registry = new FakeRegistry();
        registry.Sources = [registry.Sources[0] with { Enabled = false }];
        using var worker = CreateWithDiscovery(registry, out _, out var status, out var buffer);
        await worker.RefreshSourcesAsync(CancellationToken.None);
        Assert.False(worker.AcceptMessage("disabled"u8, IPAddress.Parse("192.0.2.10"), "udp"));
        Assert.False(worker.TryReservePeerConnection(IPAddress.Parse("192.0.2.10"), out _));
        registry.Fail = true;
        Assert.False(await worker.RefreshSourcesAsync(CancellationToken.None));
        Assert.False(worker.AcceptMessage("unknown"u8, IPAddress.Parse("192.0.2.11"), "udp"));
        Assert.False(worker.TryReservePeerConnection(IPAddress.Parse("192.0.2.11"), out _));
        Assert.Empty(buffer.Snapshot().Items);
        Assert.Equal(2, status.Dropped);
        Assert.Equal(0, status.UnregisteredReceived);
    }

    [Fact]
    public async Task UnknownConnectionQuotaDoesNotConsumeRegisteredSourceQuotaAndSurvivesRefresh()
    {
        var registry = new FakeRegistry();
        using var worker = CreateWithDiscovery(registry, out _, out _, out var buffer);
        await worker.RefreshSourcesAsync(CancellationToken.None);
        var unknown = IPAddress.Parse("192.0.2.11");
        var known = IPAddress.Parse("192.0.2.10");
        Assert.True(worker.TryReservePeerConnection(unknown, out var unknownLease));
        Assert.True(unknownLease);
        Assert.False(worker.TryReservePeerConnection(IPAddress.Parse("192.0.2.12"), out _));
        Assert.True(worker.TryReservePeerConnection(known, out var knownLease));
        Assert.False(knownLease);
        await worker.RefreshSourcesAsync(CancellationToken.None);
        Assert.True(worker.AcceptMessage("still-observing"u8, unknown, "tcp"));
        Assert.Single(buffer.GetLogs(unknown));
        worker.ReleasePeerConnection(unknown, unknownLease);
        worker.ReleasePeerConnection(known, knownLease);
        Assert.True(worker.TryReservePeerConnection(IPAddress.Parse("192.0.2.12"), out var replacement));
        worker.ReleasePeerConnection(IPAddress.Parse("192.0.2.12"), replacement);
    }

    [Theory]
    [InlineData(true, true, true, true, true)]
    [InlineData(false, false, false, true, true)]
    [InlineData(false, true, false, true, true)]
    [InlineData(true, false, false, false, true)]
    [InlineData(true, false, false, true, false)]
    [InlineData(false, true, true, false, false)]
    public void ConnectionClassificationChangesRequireFreshQuota(bool wasUnknown, bool registered, bool enabled, bool allowed, bool expected)
    {
        Assert.Equal(expected, ListenerWorker.ShouldReconnect(wasUnknown, registered, enabled, allowed));
    }

    [Fact]
    public async Task ExistingConnectionCannotChangeClassificationWithoutReconnecting()
    {
        var registry = new FakeRegistry();
        using var worker = CreateWithDiscovery(registry, out var queue, out _, out var buffer);
        var peer = IPAddress.Parse("192.0.2.10");
        await worker.RefreshSourcesAsync(CancellationToken.None);
        Assert.True(worker.TryReservePeerConnection(peer, out var oldLease));
        Assert.False(oldLease);
        registry.Sources = [];
        await worker.RefreshSourcesAsync(CancellationToken.None);
        Assert.False(worker.AcceptMessage("old-known-lease"u8, peer, "tcp", oldLease));
        Assert.Empty(buffer.Snapshot().Items);
        worker.ReleasePeerConnection(peer, oldLease);
        Assert.True(worker.TryReservePeerConnection(peer, out var newLease));
        Assert.True(worker.AcceptMessage("discovered"u8, peer, "tcp", newLease));
        registry.Sources = [new("registered", "Synthetic", peer.ToString(), "UTC", 365, true)];
        await worker.RefreshSourcesAsync(CancellationToken.None);
        Assert.False(worker.AcceptMessage("old-unknown-lease"u8, peer, "tcp", newLease));
        Assert.False(queue.TryRead(out _));
        worker.ReleasePeerConnection(peer, newLease);
    }

    [Fact]
    public async Task StreamReaderHandlesTinyReadsAndCoalescedUtf8Frames()
    {
        var payload = Encoding.UTF8.GetBytes("<14>中文");
        var combined = Encoding.ASCII.GetBytes($"{payload.Length} ").Concat(payload).Concat("4 next"u8.ToArray()).ToArray();
        await using var stream = new ChunkedStream(combined, 1);
        var reader = Reader(SyslogFrameMode.OctetCounting);
        Assert.Equal(payload, await reader.ReadFrameAsync(stream));
        Assert.Equal("next"u8.ToArray(), await reader.ReadFrameAsync(stream));
        Assert.Null(await reader.ReadFrameAsync(stream));
    }

    [Fact]
    public async Task LineReaderDoesNotReuseReturnedPayloadBuffer()
    {
        await using var stream = new MemoryStream("first\r\nsecond\n"u8.ToArray());
        var reader = Reader(SyslogFrameMode.Newline);
        var first = await reader.ReadFrameAsync(stream);
        Assert.Equal("second"u8.ToArray(), await reader.ReadFrameAsync(stream));
        Assert.Equal("first"u8.ToArray(), first);
    }

    [Theory]
    [InlineData("10 tiny", "truncated_frame")]
    [InlineData("9999999999999999 ", "frame_too_large")]
    [InlineData("0 ", "invalid_length_prefix")]
    public async Task InvalidStreamsProduceStableErrorCodes(string content, string expected)
    {
        await using var stream = new MemoryStream(Encoding.ASCII.GetBytes(content));
        var error = await Assert.ThrowsAsync<FrameReadException>(() => Reader(SyslogFrameMode.OctetCounting).ReadFrameAsync(stream).AsTask());
        Assert.Equal(expected, error.Code);
    }

    [Fact]
    public async Task AbsoluteFrameTimeoutIsNotResetBySlowBytes()
    {
        await using var stream = new ChunkedStream("9 slow-data"u8.ToArray(), 1, TimeSpan.FromMilliseconds(20));
        var reader = new StreamFrameReader(100, SyslogFrameMode.OctetCounting, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(75));
        var error = await Assert.ThrowsAsync<FrameReadException>(() => reader.ReadFrameAsync(stream).AsTask());
        Assert.Equal("frame_timeout", error.Code);
    }

    [Fact]
    public async Task IdleTimeoutAndCallerCancellationAreDistinguished()
    {
        await using var idle = new ChunkedStream("1 x"u8.ToArray(), 1, TimeSpan.FromSeconds(10));
        var reader = new StreamFrameReader(100, SyslogFrameMode.OctetCounting, TimeSpan.FromMilliseconds(30), TimeSpan.FromSeconds(1));
        var error = await Assert.ThrowsAsync<FrameReadException>(() => reader.ReadFrameAsync(idle).AsTask());
        Assert.Equal("idle_timeout", error.Code);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadFrameAsync(idle, cancellation.Token).AsTask());
    }

    [Fact]
    public async Task DisabledEndpointsDoNotReadRegistryOrCertificateEvenWhenGlobalReceptionEnabled()
    {
        var registry = new FakeRegistry { Fail = true };
        var options = new IngestOptions { Enabled = true, Listeners = [new() { Enabled = false, Transport = "tls", Address = "invalid", CertificatePath = "must-not-read.pfx" }] };
        using var worker = Create(options, registry, out _, out var status);
        await worker.StartAsync(default);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(0, registry.Calls);
        Assert.Equal("disabled", Assert.Single(status.GetListenerSnapshots()).State);
        await worker.StopAsync(default);
    }

    [Fact]
    public async Task MissingManagedCertificateFailsTlsBeforeBinding()
    {
        var provider = new MissingCertificateProvider();
        var settings = new IngestOptions
        {
            Enabled = true,
            Listeners = [new() { Transport = "tls", Address = "127.0.0.1", Port = 16514, CertificateId = IListenerCertificateProvider.UploadedId }]
        };
        using var worker = Create(settings, new(), out _, out var status, provider);
        await worker.StartAsync(default);
        try
        {
            Assert.Equal(IListenerCertificateProvider.UploadedId, await provider.Requested.Task.WaitAsync(TimeSpan.FromSeconds(2)));
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            while (Assert.Single(status.GetListenerSnapshots()).State == "starting")
                await Task.Delay(5, budget.Token);
            var listener = Assert.Single(status.GetListenerSnapshots());
            Assert.Equal("failed", listener.State);
            Assert.Equal("tls_listener_start_failed", listener.FailureCode);
        }
        finally { await worker.StopAsync(default); }
    }

    [Fact]
    public async Task SwitchingTlsPreservesAnEstablishedTcpConnectionAndReleasesOnlyTheTlsPort()
    {
        using var tcpReservation = new TcpListener(IPAddress.Loopback, 0);
        using var tlsReservation = new TcpListener(IPAddress.Loopback, 0);
        tcpReservation.Start();
        tlsReservation.Start();
        var tcpPort = ((IPEndPoint)tcpReservation.LocalEndpoint).Port;
        var tlsPort = ((IPEndPoint)tlsReservation.LocalEndpoint).Port;
        tcpReservation.Stop();
        tlsReservation.Stop();
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=synthetic-listener", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        var registry = new FakeRegistry { Sources = [new("synthetic", "Synthetic device", "127.0.0.1", "UTC", 365, true)] };
        var tcp = new ListenerOptions { Transport = "tcp", Address = "127.0.0.1", Port = tcpPort };
        var tls = new ListenerOptions { Enabled = false, Transport = "tls", Address = "127.0.0.1", Port = tlsPort,
            CertificateId = IListenerCertificateProvider.SelfSignedId };
        using var worker = Create(new() { Enabled = true, Listeners = [tcp, tls] }, registry, out var queue, out var status,
            new TestCertificateProvider(certificate));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ct = timeout.Token;
        await worker.StartAsync(ct);
        try
        {
            await WaitForState("tcp", "listening");
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(IPAddress.Loopback, tcpPort, ct);
            await tcpClient.GetStream().WriteAsync("5 first"u8.ToArray(), ct);
            await AssertNextFrame("tcp", "first");

            var enabledTls = tls.Copy();
            enabledTls.Enabled = true;
            var missingTls = enabledTls.Copy();
            missingTls.CertificateId = IListenerCertificateProvider.UploadedId;
            await worker.ApplyProtocolAsync(missingTls);
            await WaitForState("tls", "failed");
            Assert.Equal("tls_listener_start_failed", status.GetListenerSnapshots().Single(item => item.Protocol == "tls").FailureCode);
            await tcpClient.GetStream().WriteAsync("7 healthy"u8.ToArray(), ct);
            await AssertNextFrame("tcp", "healthy");

            await worker.ApplyProtocolAsync(enabledTls);
            await WaitForState("tls", "listening");
            using var tlsClient = new TcpClient();
            await tlsClient.ConnectAsync(IPAddress.Loopback, tlsPort, ct);
            using var stream = new SslStream(tlsClient.GetStream(), false,
                (_, presented, _, _) => presented?.GetCertHashString() == certificate.Thumbprint);
            await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "synthetic-listener" }, ct);
            await stream.WriteAsync("3 tls"u8.ToArray(), ct);
            await AssertNextFrame("tls", "tls");

            await worker.ApplyProtocolAsync(tls);
            await WaitForState("tls", "disabled");
            var shutdown = await Record.ExceptionAsync(async () => Assert.Equal(0, await stream.ReadAsync(new byte[1], ct)));
            Assert.True(shutdown is null or IOException);
            using var failedConnection = new TcpClient();
            await Assert.ThrowsAsync<SocketException>(() => failedConnection.ConnectAsync(IPAddress.Loopback, tlsPort, ct).AsTask());
            await worker.ApplyProtocolAsync(enabledTls);
            await WaitForState("tls", "listening");
            await tcpClient.GetStream().WriteAsync("6 second"u8.ToArray(), ct);
            await AssertNextFrame("tcp", "second");
            await WaitForState("tcp", "listening");
        }
        finally { await worker.StopAsync(CancellationToken.None); }

        async Task WaitForState(string protocol, string expected)
        {
            // 后台启动可能尚未发布首个状态；在同一超时预算内等待它出现，不把调度先后当成失败。
            while (status.GetListenerSnapshots().SingleOrDefault(item => item.Protocol == protocol)?.State != expected)
                await Task.Delay(10, ct);
        }

        async Task AssertNextFrame(string transport, string expected)
        {
            ReceivedMessage? message;
            while (!queue.TryRead(out message)) await Task.Delay(10, ct);
            Assert.Equal(transport, message!.Context.Transport);
            Assert.Equal(expected, Encoding.ASCII.GetString(message.Bytes));
        }
    }

    [Fact]
    public async Task RegistryRecoveryDoesNotClearIndependentEndpointFailure()
    {
        var registry = new FakeRegistry { Fail = true };
        var options = new IngestOptions
        {
            Enabled = true,
            Listeners =
            [
                new() { Transport = "udp", Address = "127.0.0.1", Port = 5514 },
                new() { Transport = "tls", Address = "127.0.0.1", Port = 6514 }
            ]
        };
        using var worker = Create(options, registry, out _, out var status);
        status.SetListenerState("udp", "127.0.0.1", 5514, "starting");
        status.SetListenerState("tls", "127.0.0.1", 6514, "failed", "tls_listener_start_failed");
        Assert.False(await worker.RefreshSourcesAsync(default));
        Assert.Equal("source_registry_unavailable", status.GetListenerSnapshots().Single(x => x.Protocol == "udp").FailureCode);
        Assert.Equal("tls_listener_start_failed", status.GetListenerSnapshots().Single(x => x.Protocol == "tls").FailureCode);
        registry.Fail = false;
        Assert.True(await worker.RefreshSourcesAsync(default));
        Assert.Equal("starting", status.GetListenerSnapshots().Single(x => x.Protocol == "udp").State);
        Assert.Equal("tls_listener_start_failed", status.GetListenerSnapshots().Single(x => x.Protocol == "tls").FailureCode);
        Assert.Equal("failed", status.ListenerState);
    }

    [Fact]
    public void EndpointSnapshotRetainsIndependentStatesAndIsDetachedFromFutureUpdates()
    {
        var status = new IngestStatus();
        status.SetListenerState("udp", "127.0.0.1", 5514, "listening");
        status.SetListenerState("tls", "127.0.0.1", 6514, "failed", "tls_listener_start_failed");
        var before = status.GetListenerSnapshots();
        Assert.Equal("listening", before.Single(x => x.Protocol == "udp").State);
        Assert.Equal("failed", status.ListenerState);
        status.SetListenerState("tls", "127.0.0.1", 6514, "disabled");
        Assert.Equal("failed", before.Single(x => x.Protocol == "tls").State);
        Assert.Equal("listening", status.ListenerState);
        Assert.Equal("", status.ListenerFailureCode);
    }

    [Fact]
    public void ConnectionFailureProvidesSocketReasonAndCannotOverwriteStoppedEndpoint()
    {
        var status = new IngestStatus();
        var endpoint = new ListenerOptions { Transport = "tcp", Address = "127.0.0.1", Port = 5514 };
        var peer = IPAddress.Parse("::ffff:192.0.2.10");
        status.SetListenerState(endpoint.Transport, endpoint.Address, endpoint.Port, "listening");
        status.RecordConnectionFailure(endpoint, peer, "connection_io_failed",
            ListenerFailureMessages.Describe("connection_io_failed", socketError: SocketError.ConnectionReset));

        var active = Assert.Single(status.GetListenerSnapshots());
        Assert.Equal("listening", active.State);
        Assert.Empty(active.FailureCode);
        Assert.Empty(active.FailureMessage);
        Assert.Empty(status.ListenerFailureCode);
        var error = Assert.Single(status.GetConnectionErrors());
        Assert.Equal("192.0.2.10", error.PeerAddress);
        Assert.Equal("connection_io_failed", error.FailureCode);
        Assert.Contains("ConnectionReset", error.FailureMessage);
        Assert.Equal(TimeSpan.Zero, error.OccurredAt.Offset);

        status.SetListenerState(endpoint.Transport, endpoint.Address, endpoint.Port, "stopped");
        status.RecordConnectionFailure(endpoint, peer, "connection_io_failed", "迟到的连接错误");
        var stopped = Assert.Single(status.GetListenerSnapshots());
        Assert.Equal("stopped", stopped.State);
        Assert.Empty(stopped.FailureCode);
        Assert.Empty(stopped.FailureMessage);
        Assert.Empty(status.GetConnectionErrors());
    }

    [Fact]
    public async Task InvalidTcpFrameLeavesOtherPeersAvailableAndClearsOnlyOnSuccessfulReception()
    {
        var registry = new FakeRegistry();
        registry.Sources = [registry.Sources[0], registry.Sources[0] with { Id = "second", PeerAddress = "192.0.2.11" }];
        var endpoint = new ListenerOptions { Transport = "tcp", Address = "127.0.0.1", Port = 5514, Framing = "octet-counting" };
        var anotherEndpoint = new ListenerOptions { Transport = "tcp", Address = "127.0.0.1", Port = 5515 };
        using var worker = Create(new() { QueueCapacity = 1 }, registry, out var queue, out var status);
        Assert.True(await worker.RefreshSourcesAsync(default));
        status.SetListenerState("tcp", "127.0.0.1", 5514, "listening");
        status.SetListenerState("tcp", "127.0.0.1", 5515, "listening");
        var brokenPeer = IPAddress.Parse("192.0.2.10");
        var healthyPeer = IPAddress.Parse("192.0.2.11");
        status.RecordConnectionFailure(anotherEndpoint, brokenPeer, "connection_io_failed", "另一监听端点的诊断");

        // 未加长度前缀的 TCP 数据在严格模式下只影响该客户端。
        await worker.ProcessConnectionAsync(() => new MemoryStream("<14>legacy\r\n"u8.ToArray()), brokenPeer, endpoint, null, false, default);
        Assert.Equal("invalid_length_prefix", Assert.Single(status.GetConnectionErrors(), item => item.Port == 5514).FailureCode);
        Assert.All(status.GetListenerSnapshots(), item => Assert.Equal("listening", item.State));
        Assert.Empty(status.ListenerFailureCode);

        await worker.ProcessConnectionAsync(() => new MemoryStream("11 <14>healthy"u8.ToArray()), healthyPeer, endpoint, null, false, default);
        Assert.Equal(2, status.GetConnectionErrors().Count);
        await worker.ProcessConnectionAsync(() => new MemoryStream("8 <14>next"u8.ToArray()), brokenPeer, endpoint, null, false, default);
        Assert.Equal(2, status.GetConnectionErrors().Count); // 队列满，不能把被丢弃的消息算作恢复。
        Assert.True(queue.TryRead(out var other));
        Assert.Equal("192.0.2.11", other!.Context.PeerAddress);

        await worker.ProcessConnectionAsync(() => new MemoryStream("8 <14>next"u8.ToArray()), IPAddress.Parse("::ffff:192.0.2.10"), endpoint, null, false, default);
        Assert.True(queue.TryRead(out var recovered));
        Assert.Equal("192.0.2.10", recovered!.Context.PeerAddress);
        Assert.Equal(5515, Assert.Single(status.GetConnectionErrors()).Port);
    }

    [Fact]
    public void ConnectionErrorHistoryEvictsOldestAndKeepsOnlyLatestErrorForEachPeer()
    {
        var status = new IngestStatus();
        var endpoint = new ListenerOptions { Transport = "tcp", Address = "127.0.0.1", Port = 5514 };
        status.SetListenerState("tcp", "127.0.0.1", 5514, "listening");
        for (var i = 0; i < IngestStatus.MaxConnectionErrors; i++)
            status.RecordConnectionFailure(endpoint, IPAddress.Parse($"2001:db8::{i:x}"), "invalid_length_prefix", "错误前缀");
        status.RecordConnectionFailure(endpoint, IPAddress.Parse("2001:db8::"), "frame_timeout", "帧超时");
        status.RecordConnectionFailure(endpoint, IPAddress.Parse("2001:db8:1::1"), "frame_timeout", "帧超时");
        var errors = status.GetConnectionErrors();
        Assert.Equal(IngestStatus.MaxConnectionErrors, errors.Count);
        Assert.Equal("2001:db8:1::1", errors[0].PeerAddress);
        Assert.Equal("frame_timeout", Assert.Single(errors, item => item.PeerAddress == "2001:db8::").FailureCode);
        Assert.DoesNotContain(errors, item => item.PeerAddress == "2001:db8::1");
    }

    [Fact]
    public async Task RegisteredRelpWaitsForSpoolConfirmationButUnknownRelpUsesMemoryImmediately()
    {
        var registry = new FakeRegistry();
        using var worker = CreateWithDiscovery(registry, out var queue, out _, out var buffer);
        await worker.RefreshSourcesAsync(default);
        var known = worker.AcceptRelpMessageAsync("registered"u8.ToArray(), IPAddress.Parse("192.0.2.10"), false, default);
        Assert.False(known.IsCompleted);
        Assert.True(queue.TryRead(out var item));
        Assert.Equal("relp", item!.Context.Transport);
        item.Persisted!.SetResult(true);
        Assert.True(await known);
        Assert.True(await worker.AcceptRelpMessageAsync("unknown"u8.ToArray(), IPAddress.Parse("192.0.2.11"), true, default));
        Assert.Equal("relp", Assert.Single(buffer.GetLogs(IPAddress.Parse("192.0.2.11"))).Transport);
        Assert.False(queue.TryRead(out _));
    }

    [Fact]
    public async Task RejectedRelpDoesNotWaitOrReportSuccess()
    {
        var registry = new FakeRegistry();
        using var worker = Create(new() { QueueCapacity = 1 }, registry, out _, out _);
        await worker.RefreshSourcesAsync(default);
        Assert.True(worker.AcceptMessage("occupy"u8, IPAddress.Parse("192.0.2.10"), "tcp"));
        Assert.False(await worker.AcceptRelpMessageAsync("rejected"u8.ToArray(), IPAddress.Parse("192.0.2.10"), false, default));
    }

    private static StreamFrameReader Reader(SyslogFrameMode mode) => new(1024, mode, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

    private static ListenerWorker Create(IngestOptions settings, FakeRegistry registry, out IngressQueue queue, out IngestStatus status,
        IListenerCertificateProvider? certificateProvider = null)
    {
        status = new();
        queue = new(Microsoft.Extensions.Options.Options.Create(settings), status);
        return new(Microsoft.Extensions.Options.Options.Create(settings), registry, queue, status,
            new UnregisteredDeviceBuffer(Microsoft.Extensions.Options.Options.Create(settings), TimeProvider.System), certificateProvider);
    }

    private static ListenerWorker CreateWithDiscovery(FakeRegistry registry, out IngressQueue queue, out IngestStatus status,
        out UnregisteredDeviceBuffer buffer)
    {
        var settings = Microsoft.Extensions.Options.Options.Create(new IngestOptions
        {
            Enabled = true, UnregisteredDevices = new() { AllowedNetworks = ["192.0.2.0/24"], MaxConnections = 1 }
        });
        status = new();
        queue = new(settings, status);
        buffer = new(settings, TimeProvider.System);
        return new(settings, registry, queue, status, buffer);
    }

    private sealed class FakeRegistry : ISourceRegistry
    {
        public int Calls { get; private set; }
        public bool Fail { get; set; }
        public IReadOnlyList<LogSource> Sources { get; set; } = [new("synthetic", "Synthetic device", "192.0.2.10", "UTC", 365, true)];
        public Task<IReadOnlyList<LogSource>> ListAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Fail ? Task.FromException<IReadOnlyList<LogSource>>(new IOException("synthetic_failure")) : Task.FromResult(Sources);
        }
    }

    private sealed class TestCertificateProvider(X509Certificate2 certificate) : IListenerCertificateProvider
    {
        public IReadOnlyList<string> GetAvailableCertificateIds() => [IListenerCertificateProvider.SelfSignedId];
        public ServerCertificateMaterial? LoadCertificate(string id) => id == IListenerCertificateProvider.SelfSignedId
            ? new(new X509Certificate2(certificate)) : null;
    }

    private sealed class MissingCertificateProvider : IListenerCertificateProvider
    {
        public TaskCompletionSource<string> Requested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<string> GetAvailableCertificateIds() => [];

        public ServerCertificateMaterial? LoadCertificate(string id)
        {
            Requested.TrySetResult(id);
            return null;
        }
    }

    private sealed class ChunkedStream(byte[] bytes, int chunkSize, TimeSpan delay = default) : MemoryStream(bytes)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);
            return await base.ReadAsync(buffer[..Math.Min(buffer.Length, chunkSize)], cancellationToken);
        }
    }
}
