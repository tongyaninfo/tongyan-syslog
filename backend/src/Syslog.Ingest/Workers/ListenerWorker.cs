using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Syslog.Engine.Common;
using Syslog.Ingest.Common;
using Syslog.Ingest.Models;
using Syslog.Ingest.Options;
using Syslog.Ingest.Service;
using Syslog.Ingest.Service.impl;
using Syslog.Shared.Models;
using Syslog.Shared.Service;

[assembly: InternalsVisibleTo("Syslog.Ingest.Tests")]

namespace Syslog.Ingest.Workers;

/// <summary>配置显式启用后才访问端口和证书。来源快照刷新失败时立即拒绝新报文，旧连接也关闭。</summary>
public sealed class ListenerWorker(IOptions<IngestOptions> settings, ISourceRegistry registry,
    IngressQueue queue, IngestStatus status, IUnregisteredDeviceBuffer unregistered,
    IListenerCertificateProvider? certificateProvider = null) : BackgroundService
{
    private readonly IngestOptions options = settings.Value;
    private SourceSnapshot sources = new(false, []);
    private readonly object sourceGate = new();
    private TaskCompletionSource sourcesReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentDictionary<long, ActiveConnection> connections = new();
    private readonly ConcurrentDictionary<ListenerOptions, byte> boundListeners = new();
    private readonly object endpointGate = new();
    private readonly Dictionary<ListenerOptions, EndpointRun> endpointRuns = [];
    private CancellationTokenSource? receptionLifetime;
    private readonly Dictionary<IPAddress, int> addressConnections = [];
    private readonly object connectionGate = new();
    private long nextConnection;
    private int activeConnections;
    private Task<IReadOnlyList<LogSource>>? pendingRegistryRead;
    private readonly SemaphoreSlim tlsHandshakes = new(64, 64);
    private readonly SemaphoreSlim unregisteredTlsHandshakes = new(8, 8);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        status.ResetListeners(options.Enabled, options.Listeners);
        return RunAsync(stoppingToken);
    }

    internal async Task RunAsync(CancellationToken stoppingToken, bool allowEmpty = false)
    {
        if (!options.Enabled || !allowEmpty && !options.Listeners.Any(x => x.Enabled)) { status.ListenerState = "disabled"; return; }
        if (!ValidOptions())
        {
            foreach (var listener in options.Listeners.Where(x => x.Enabled)) SetState(listener, "failed", "invalid_listener_configuration");
            return;
        }
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        lock (endpointGate)
        {
            receptionLifetime = lifetime;
            foreach (var listener in options.Listeners.Where(x => x.Enabled)) StartEndpoint(listener, lifetime.Token);
        }
        try
        {
            // 先登记宿主生命周期，再异步刷新授权来源，后续协议开关可以立即提交。
            await Task.Yield();
            while (!await RefreshSourcesAsync(lifetime.Token))
                await Task.Delay(TimeSpan.FromSeconds(options.SourceRefreshSeconds), lifetime.Token);
            await RefreshLoopAsync(lifetime.Token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        finally
        {
            lifetime.Cancel();
            InvalidateSources();
            EndpointRun[] stopped;
            lock (endpointGate)
            {
                receptionLifetime = null;
                stopped = endpointRuns.Values.ToArray();
                endpointRuns.Clear();
            }
            await Task.WhenAll(stopped.Select(StopEndpointAsync));
        }
    }

    internal async Task ApplyProtocolAsync(ListenerOptions listener)
    {
        EndpointRun? previousRun;
        ListenerOptions previous;
        lock (endpointGate)
        {
            if (receptionLifetime is null) throw new InvalidOperationException("接收未启用");
            previous = options.Listeners.Single(item => item.Transport == listener.Transport);
            endpointRuns.TryGetValue(previous, out previousRun);
        }
        // 等目标端点的 socket、连接和证书全部释放后再登记新的端点，其他端点继续处理。
        if (previousRun is not null) await StopEndpointAsync(previousRun);
        lock (endpointGate)
        {
            if (receptionLifetime is null) throw new InvalidOperationException("接收未启用");
            endpointRuns.Remove(previous);
            options.Listeners = options.Listeners.Select(item => ReferenceEquals(item, previous) ? listener : item).ToList();
            status.ResetProtocol(listener);
            if (!listener.Enabled) return;
            if (ValidOptions()) StartEndpoint(listener, receptionLifetime.Token);
            else SetState(listener, "failed", "invalid_listener_configuration");
        }
    }

    private void StartEndpoint(ListenerOptions listener, CancellationToken receptionToken)
    {
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(receptionToken);
        endpointRuns.Add(listener, new(lifetime, Task.Run(() => RunListenerAsync(listener, lifetime.Token))));
    }

    private static async Task StopEndpointAsync(EndpointRun run)
    {
        using (run.Lifetime)
        {
            await run.Lifetime.CancelAsync();
            await run.Task;
        }
    }

    private async Task RunListenerAsync(ListenerOptions listener, CancellationToken cancellationToken)
    {
        ServerCertificateMaterial? material = null;
        try
        {
            Task ready;
            lock (sourceGate) ready = sourcesReady.Task;
            await ready.WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var address = IPAddress.Parse(listener.Address);
            using var socket = new Socket(address.AddressFamily, listener.Transport == "udp" ? SocketType.Dgram : SocketType.Stream,
                listener.Transport == "udp" ? ProtocolType.Udp : ProtocolType.Tcp);
            if (address.AddressFamily == AddressFamily.InterNetworkV6) socket.DualMode = false;
            // 证书必须在绑定端口之前校验；TLS 失败绝不转为明文监听。
            SslStreamCertificateContext? certificate = null;
            if (listener.Transport == "tls")
            {
                material = LoadCertificate(listener);
                // 同一个监听器复用证书链，不在握手时下载缺失的中间证书。
                certificate = SslStreamCertificateContext.Create(material.Certificate, material.Chain, offline: true);
            }
            socket.Bind(new IPEndPoint(address, listener.Port));
            if (listener.Transport != "udp") socket.Listen(Math.Min(options.MaxConnections, 512));
            lock (endpointGate)
            {
                boundListeners.TryAdd(listener, 0);
                if (Volatile.Read(ref sources).Available) SetState(listener, "listening");
                else SetState(listener, "failed", "source_registry_unavailable");
            }
            if (listener.Transport == "udp") await ReceiveDatagramsAsync(socket, cancellationToken);
            else
            {
                await AcceptConnectionsAsync(socket, listener, certificate, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            // 一个协议的端口或证书失败不撤销其他已经成功的监听器。
            var code = listener.Transport == "tls" && exception is not SocketException && !boundListeners.ContainsKey(listener)
                ? "tls_listener_start_failed" : "listener_io_failed";
            SetState(listener, "failed", code, GetSocketError(exception));
        }
        finally
        {
            lock (endpointGate)
            {
                boundListeners.TryRemove(listener, out _);
                if (cancellationToken.IsCancellationRequested) SetState(listener, "stopped");
            }
            var active = connections.Where(entry => ReferenceEquals(entry.Value.Listener, listener)).Select(entry => entry.Value).ToArray();
            foreach (var connection in active) connection.Socket.Dispose();
            // 握手任务可能仍在使用证书，待当前端点的连接退出后再释放。
            try { await Task.WhenAll(active.Select(connection => connection.Task)); }
            finally { material?.Dispose(); }
        }
    }

    private async Task ReceiveDatagramsAsync(Socket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[options.MaxMessageBytes + 1];
        EndPoint remote = new IPEndPoint(socket.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any, 0);
        while (!cancellationToken.IsCancellationRequested)
        {
            SocketReceiveMessageFromResult received;
            try { received = await socket.ReceiveMessageFromAsync(buffer.AsMemory(), SocketFlags.None, remote, cancellationToken); }
            catch (SocketException exception) when (exception.SocketErrorCode == SocketError.MessageSize)
            {
                // 平台可能只报告截断而不给原始长度；只能计已知下限，不能伪造完整字节数。
                Drop(options.MaxMessageBytes + 1);
                continue;
            }
            var peer = Normalize(((IPEndPoint)received.RemoteEndPoint).Address);
            if (received.SocketFlags.HasFlag(SocketFlags.Truncated) || received.ReceivedBytes > options.MaxMessageBytes)
            { DropFromPeer(received.ReceivedBytes, peer); continue; }
            AcceptMessage(buffer.AsSpan(0, received.ReceivedBytes), peer, "udp");
        }
    }

    private async Task AcceptConnectionsAsync(Socket listenerSocket, ListenerOptions listener, SslStreamCertificateContext? certificate,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var socket = await listenerSocket.AcceptAsync(cancellationToken);
            IPAddress peer;
            try
            {
                if (socket.RemoteEndPoint is not IPEndPoint endpoint) { socket.Dispose(); continue; }
                peer = Normalize(endpoint.Address);
            }
            catch (Exception exception) when (exception is SocketException or ObjectDisposedException)
            {
                // 对端可能在取地址前断开，此时只有新接入的连接需要回收。
                socket.Dispose();
                continue;
            }
            long id;
            bool unregisteredConnection;
            lock (sourceGate)
            {
                if (!TryReservePeerConnection(peer, out unregisteredConnection)) { socket.Dispose(); continue; }
                id = Interlocked.Increment(ref nextConnection);
                var task = HandleConnectionAsync(socket, peer, listener, certificate, unregisteredConnection, cancellationToken);
                connections[id] = new(listener, socket, unregisteredConnection, task);
                _ = task.ContinueWith(_ => connections.TryRemove(id, out var completed), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
    }

    private async Task HandleConnectionAsync(Socket socket, IPAddress peer, ListenerOptions listener, SslStreamCertificateContext? certificate,
        bool unregisteredConnection, CancellationToken cancellationToken)
    {
        // 即使套接字已有可同步读取的数据，也要先让接收循环继续处理其他客户端。
        await Task.Yield();
        try
        {
            await ProcessConnectionAsync(() => new NetworkStream(socket, ownsSocket: false), peer, listener,
                certificate, unregisteredConnection, cancellationToken);
        }
        finally
        {
            socket.Dispose();
            ReleasePeerConnection(peer, unregisteredConnection);
        }
    }

    internal async Task ProcessConnectionAsync(Func<Stream> openNetwork, IPAddress peer, ListenerOptions listener,
        SslStreamCertificateContext? certificate, bool unregisteredConnection, CancellationToken cancellationToken)
    {
        peer = Normalize(peer);
        StreamFrameReader? reader = null;
        try
        {
            using var network = openNetwork();
            using var ssl = certificate is not null ? new SslStream(network, leaveInnerStreamOpen: true) : null;
            Stream stream = network;
            if (ssl is not null)
            {
                using var handshake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                handshake.CancelAfter(TimeSpan.FromSeconds(listener.FrameTimeoutSeconds));
                // 未登记来源最多占用 8 个握手槽，避免占满正式来源共享的 64 个槽。
                if (unregisteredConnection) await unregisteredTlsHandshakes.WaitAsync(handshake.Token);
                try
                {
                    await tlsHandshakes.WaitAsync(handshake.Token);
                    try
                    {
                        await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                        {
                            ServerCertificateContext = certificate,
                            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                            ClientCertificateRequired = false,
                            AllowRenegotiation = false
                        }, handshake.Token);
                    }
                    finally { tlsHandshakes.Release(); }
                }
                finally { if (unregisteredConnection) unregisteredTlsHandshakes.Release(); }
                stream = ssl;
            }
            if (listener.Transport == "relp")
            {
                var session = new RelpSession(options.MaxMessageBytes, TimeSpan.FromSeconds(listener.IdleTimeoutSeconds),
                    TimeSpan.FromSeconds(listener.FrameTimeoutSeconds));
                await session.RunAsync(stream, async (bytes, token) =>
                {
                    if (!await AcceptRelpMessageAsync(bytes, peer, unregisteredConnection, token)) return false;
                    status.ClearConnectionFailure(listener, peer);
                    return true;
                }, cancellationToken);
                return;
            }
            var mode = listener.Framing switch
            {
                "auto" => SyslogFrameMode.Auto,
                "newline" => SyslogFrameMode.Newline,
                _ => SyslogFrameMode.OctetCounting,
            };
            reader = new StreamFrameReader(options.MaxMessageBytes, mode, TimeSpan.FromSeconds(listener.IdleTimeoutSeconds),
                TimeSpan.FromSeconds(listener.FrameTimeoutSeconds));
            while (!cancellationToken.IsCancellationRequested)
            {
                var bytes = await reader.ReadFrameAsync(stream, cancellationToken);
                if (bytes is null) return;
                // 当前队列为有界 TryWrite；满时关闭连接，发送方必须按自身能力恢复，不能宣称无丢失。
                if (!AcceptMessage(bytes, peer, listener.Transport, unregisteredConnection)) return;
                status.ClearConnectionFailure(listener, peer);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (FrameReadException exception)
        {
            if (exception.Code != "idle_timeout" && exception.ObservedBytes > 0) DropFromPeer(exception.ObservedBytes, peer);
            if (exception.Code != "idle_timeout") ReportFailure(exception.Code);
        }
        catch (OperationCanceledException) { ReportFailure("tls_handshake_timeout"); }
        catch (AuthenticationException) { ReportFailure("tls_handshake_failed"); }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
            ReportFailure("connection_io_failed", GetSocketError(exception));
        }
        catch (Exception)
        {
            // 单连接的非预期故障必须可见；任务在 finally 释放配额，不能成为未观察异常。
            ReportFailure("connection_processing_failed");
        }

        void ReportFailure(string code, SocketError? socketError = null)
        {
            var snapshot = Volatile.Read(ref sources);
            if (cancellationToken.IsCancellationRequested || !snapshot.Available) return;
            var registered = snapshot.Entries.TryGetValue(peer, out var source);
            // 管理员停用或重新登记来源而关闭的旧连接属于预期回收。
            if (ShouldReconnect(unregisteredConnection, registered, source?.Source.Enabled ?? false, unregistered.IsAllowed(peer))) return;
            var framing = reader?.EffectiveMode switch
            {
                SyslogFrameMode.Newline => "newline",
                SyslogFrameMode.OctetCounting => "octet-counting",
                _ => listener.Transport is "tcp" or "tls" ? listener.Framing : "",
            };
            status.RecordConnectionFailure(listener, peer, code, ListenerFailureMessages.Describe(code, framing, socketError));
        }
    }

    private bool Enqueue(byte[] bytes, IPAddress peer, string transport, SourceState source, TaskCompletionSource<bool>? persisted)
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        return queue.TryEnqueue(new ReceivedMessage(bytes, new ParseContext(source.Source.Id, peer.ToString(), transport,
            now, source.TimeZone, now.AddDays(source.Source.RetentionDays)), persisted, source.IgnoredSeverityMask));
    }

    internal async Task<bool> AcceptRelpMessageAsync(ReadOnlyMemory<byte> bytes, IPAddress peer, bool? unregisteredConnection, CancellationToken ct)
    {
        var persisted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!AcceptMessage(bytes.Span, peer, "relp", unregisteredConnection, persisted)) return false;
        return await persisted.Task.WaitAsync(ct);
    }

    internal bool AcceptMessage(ReadOnlySpan<byte> bytes, IPAddress peer, string transport, bool? unregisteredConnection = null,
        TaskCompletionSource<bool>? persisted = null)
    {
        peer = Normalize(peer);
        lock (sourceGate)
        {
            var snapshot = Volatile.Read(ref sources);
            var registered = snapshot.Entries.TryGetValue(peer, out var source);
            if (bytes.IsEmpty || bytes.Length > options.MaxMessageBytes || !snapshot.Available || status.State == "failed")
            { Drop(bytes.Length, source?.Source.Id); return false; }
            if (unregisteredConnection is { } lease && ShouldReconnect(lease, registered, source?.Source.Enabled ?? false, unregistered.IsAllowed(peer)))
            { Drop(bytes.Length, source?.Source.Id); return false; }
            if (registered)
            {
                // 已登记但停用的来源不能重新变成未登记设备，从而绕过管理员停用操作。
                if (!source!.Source.Enabled || !source.Rate.Allow(options.MessagesPerSecondPerSource))
                { Drop(bytes.Length, source.Source.Id); return false; }
                return Enqueue(bytes.ToArray(), peer, transport, source, persisted);
            }
            if (!unregistered.TryCapture(bytes, peer, transport)) { Drop(bytes.Length); return false; }
            status.RecordReceive(bytes.Length, DateTimeOffset.UtcNow);
            Interlocked.Increment(ref status.UnregisteredReceived);
            // 未登记来源按产品约定只有内存观察样本；其 RELP ACK 仅表示已暂存到该有界缓存。
            persisted?.TrySetResult(true);
            return true;
        }
    }

    internal bool TryReservePeerConnection(IPAddress peer, out bool unregisteredConnection)
    {
        peer = Normalize(peer);
        unregisteredConnection = false;
        lock (sourceGate)
        {
            var snapshot = Volatile.Read(ref sources);
            if (!snapshot.Available || status.State == "failed") return false;
            if (snapshot.Entries.TryGetValue(peer, out var source))
            {
                if (!source.Source.Enabled || !source.ConnectionRate.Allow(10)) return false;
            }
            else
            {
                // 先预留宿主总配额，宿主饱和时不能为最终被拒绝的连接驱逐观察样本。
                if (!ReserveConnection(peer)) return false;
                if (!unregistered.TryReserveConnection(peer)) { ReleasePeerConnection(peer, false); return false; }
                unregisteredConnection = true;
                return true;
            }
            return ReserveConnection(peer);
        }
    }

    internal void ReleasePeerConnection(IPAddress peer, bool unregisteredConnection)
    {
        peer = Normalize(peer);
        lock (connectionGate)
        {
            activeConnections--;
            if (--addressConnections[peer] == 0) addressConnections.Remove(peer);
        }
        if (unregisteredConnection) unregistered.ReleaseConnection(peer);
    }

    private bool ReserveConnection(IPAddress peer)
    {
        lock (connectionGate)
        {
            addressConnections.TryGetValue(peer, out var current);
            if (activeConnections >= options.MaxConnections || current >= options.MaxConnectionsPerAddress) return false;
            activeConnections++;
            addressConnections[peer] = current + 1;
            return true;
        }
    }

    private async Task RefreshLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.SourceRefreshSeconds));
        while (await timer.WaitForNextTickAsync(cancellationToken)) await RefreshSourcesAsync(cancellationToken);
    }

    internal async Task<bool> RefreshSourcesAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 刷新请求自身也有上界，数据库挂起不能无限延长旧授权的有效时间。
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.SourceRefreshSeconds));
            // 若实现未遵守取消，保持最多一个未完成调用，不按周期无限创建悬挂任务。
            if (pendingRegistryRead is { IsCompleted: false }) throw new TimeoutException();
            if (pendingRegistryRead is { IsFaulted: true }) _ = pendingRegistryRead.Exception;
            pendingRegistryRead = registry.ListAsync(timeout.Token);
            var entries = await pendingRegistryRead.WaitAsync(timeout.Token);
            if (entries.Count > IngestStatus.MaxTrackedSources) throw new InvalidDataException();
            var previous = Volatile.Read(ref sources);
            var snapshot = new Dictionary<IPAddress, SourceState>();
            foreach (var source in entries)
            {
                if (!IPAddress.TryParse(source.PeerAddress, out var address) || source.RetentionDays is < 1 or > 36_500)
                    throw new InvalidDataException();
                address = Normalize(address);
                var zone = TimeZoneInfo.FindSystemTimeZoneById(source.TimeZoneId);
                var rate = previous.Entries.TryGetValue(address, out var existing) && existing.Source.Id == source.Id ? existing.Rate : new RateWindow();
                var connectionRate = existing is not null && existing.Source.Id == source.Id ? existing.ConnectionRate : new RateWindow();
                // 用每次刷新时的配置快照给报文分配策略，排队期间的配置变动不改变这条报文。
                byte ignoredSeverityMask = 0;
                foreach (var severity in source.IgnoredSeverities ?? [])
                    if (severity is >= 0 and <= 7) ignoredSeverityMask |= (byte)(1 << severity);
                if (!snapshot.TryAdd(address, new SourceState(source, zone, rate, connectionRate, ignoredSeverityMask)))
                    throw new InvalidDataException();
            }
            lock (sourceGate)
            {
                // 快照切换与暂存移出使用同一闸门，避免登记成功后并发接收又把样本放回观察列表。
                Volatile.Write(ref sources, new(true, snapshot));
                unregistered.RemoveRegistered(snapshot.Keys);
                sourcesReady.TrySetResult();
            }
            foreach (var connection in connections.Values)
            {
                var socket = connection.Socket;
                try
                {
                    if (socket.RemoteEndPoint is IPEndPoint endpoint)
                    {
                        var peer = Normalize(endpoint.Address);
                        var registered = snapshot.TryGetValue(peer, out var source);
                        if (ShouldReconnect(connection.Unregistered, registered, source?.Source.Enabled ?? false, unregistered.IsAllowed(peer)))
                            socket.Dispose();
                    }
                }
                catch (ObjectDisposedException) { /* 并发退出的连接已经完成撤权。 */ }
                catch (SocketException) { socket.Dispose(); }
            }
            lock (endpointGate)
            {
                if (status.ListenerFailureCode == "source_registry_unavailable")
                {
                    status.ListenerFailureCode = "";
                    status.ListenerState = "listening";
                }
                foreach (var listener in options.Listeners.Where(x => x.Enabled))
                {
                    var endpoint = status.GetListenerSnapshots().FirstOrDefault(x => x.Protocol == listener.Transport && x.Address == listener.Address && x.Port == listener.Port);
                    if (endpoint?.FailureCode == "source_registry_unavailable") SetState(listener, boundListeners.ContainsKey(listener) ? "listening" : "starting");
                }
            }
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            InvalidateSources();
            foreach (var connection in connections.Values) connection.Socket.Dispose();
            lock (endpointGate)
            {
                if (options.Listeners.Any(item => item.Enabled) || receptionLifetime is null) Failure("source_registry_unavailable");
                foreach (var listener in options.Listeners.Where(x => x.Enabled))
                {
                    var endpoint = status.GetListenerSnapshots().FirstOrDefault(x => x.Protocol == listener.Transport && x.Address == listener.Address && x.Port == listener.Port);
                    if (endpoint?.State is "starting" or "listening" || endpoint?.FailureCode == "source_registry_unavailable")
                        SetState(listener, "failed", "source_registry_unavailable");
                }
            }
            return false;
        }
    }

    private void InvalidateSources()
    {
        lock (sourceGate)
        {
            Volatile.Write(ref sources, new(false, []));
            if (sourcesReady.Task.IsCompleted) sourcesReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    /// <summary>来源分类变化时关闭旧连接，让重连重新领取对应配额，不能沿用旧租约绕过未知连接上限。</summary>
    internal static bool ShouldReconnect(bool wasUnregistered, bool isRegistered, bool isEnabled, bool unknownAllowed) =>
        wasUnregistered == isRegistered || (isRegistered ? !isEnabled : !unknownAllowed);

    private bool ValidOptions() => options.MaxMessageBytes is > 0 and <= 65_536 && options.MaxConnections is > 0 and <= 100_000
        && options.MaxConnectionsPerAddress > 0 && options.MessagesPerSecondPerSource > 0 && options.SourceRefreshSeconds is >= 1 and <= 60
        && options.Listeners.Count is <= 32 && options.Listeners.Where(x => x.Enabled).All(listener => IPAddress.TryParse(listener.Address, out _)
            && listener.Port is >= 1 and <= 65535 && listener.Transport is "udp" or "tcp" or "tls" or "relp"
            && listener.Framing is "auto" or "newline" or "octet-counting" && listener.FrameTimeoutSeconds is >= 1 and <= 300
            && listener.IdleTimeoutSeconds is >= 1 and <= 86400);

    private ServerCertificateMaterial LoadCertificate(ListenerOptions listener)
    {
        var material = certificateProvider is not null && IListenerCertificateProvider.IsManagedId(listener.CertificateId)
            ? certificateProvider.LoadCertificate(listener.CertificateId) ?? throw new AuthenticationException("invalid_server_certificate")
            : new ServerCertificateMaterial(X509CertificateLoader.LoadPkcs12FromFile(listener.CertificatePath, listener.CertificatePassword,
                X509KeyStorageFlags.EphemeralKeySet));
        var certificate = material.Certificate;
        if (!certificate.HasPrivateKey || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow || certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow)
        { material.Dispose(); throw new AuthenticationException("invalid_server_certificate"); }
        return material;
    }

    private void DropFromPeer(int bytes, IPAddress peer)
    {
        var snapshot = Volatile.Read(ref sources);
        Drop(bytes, snapshot.Entries.GetValueOrDefault(peer)?.Source.Id);
    }

    private void Drop(int bytes, string? sourceId = null)
    {
        status.RecordReceive(bytes, DateTimeOffset.UtcNow, sourceId);
        Interlocked.Increment(ref status.Dropped);
    }

    private void Failure(string code) { status.ListenerState = "failed"; status.ListenerFailureCode = code; }
    private void SetState(ListenerOptions listener, string state, string code = "", SocketError? socketError = null) =>
        status.SetListenerState(listener.Transport, listener.Address, listener.Port, state, code,
            ListenerFailureMessages.Describe(code, socketError: socketError));
    // NetworkStream 通常将 SocketException 包装在 IOException 中，仅提取枚举。
    private static SocketError? GetSocketError(Exception exception) =>
        (exception as SocketException ?? exception.InnerException as SocketException)?.SocketErrorCode;
    private sealed record ActiveConnection(ListenerOptions Listener, Socket Socket, bool Unregistered, Task Task);
    private sealed record EndpointRun(CancellationTokenSource Lifetime, Task Task);
    private sealed record SourceState(LogSource Source, TimeZoneInfo TimeZone, RateWindow Rate, RateWindow ConnectionRate,
        byte IgnoredSeverityMask);
    private sealed record SourceSnapshot(bool Available, Dictionary<IPAddress, SourceState> Entries);

    private sealed class RateWindow
    {
        private readonly object gate = new();
        private long windowStarted = Stopwatch.GetTimestamp();
        private int count;
        public bool Allow(int maximum)
        {
            lock (gate)
            {
                if (Stopwatch.GetElapsedTime(windowStarted) >= TimeSpan.FromSeconds(1)) { windowStarted = Stopwatch.GetTimestamp(); count = 0; }
                if (count >= maximum) return false;
                count++;
                return true;
            }
        }
    }
}
