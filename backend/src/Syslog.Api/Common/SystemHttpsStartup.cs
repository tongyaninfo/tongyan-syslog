using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Syslog.Api.Models;
using Syslog.Api.Service.impl;

namespace Syslog.Api.Common;

/// <summary>保留 Kestrel 的启动证书和监听配置，为后续握手应用已上传的证书。</summary>
public sealed class SystemHttpsStartup(SystemCertificateStore store, IConfiguration configuration) : IDisposable
{
    private bool _applied;
    private int _observedBootstrap;
    private readonly Lazy<CertificateStartupResult> _startup = new(() =>
    {
        var selected = store.LoadForStartup();
        var bootstrap = configuration.GetSection("Kestrel:Certificates:Default");
        // 容器的只读挂载证书由 Kestrel 按宿主配置读取。只有没有启动证书时才生成自签。
        if (selected.Certificate is not null || !string.IsNullOrWhiteSpace(bootstrap["Path"]) ||
            !string.IsNullOrWhiteSpace(bootstrap["Subject"]) ||
            configuration.GetSection("Kestrel:Certificates:Development").Exists()) return selected;
        try { return selected with { Certificate = store.LoadSelfSigned() }; }
        catch (Exception error) when (IsCertificateFailure(error))
        {
            // 私有证书不可读时，Kestrel 仍可使用部署默认值。
            return selected with { ErrorCode = selected.ErrorCode ?? "https_certificate_unavailable" };
        }
    });

    public static bool HasExternalCertificate(IConfiguration configuration) =>
        UsesHttp3(configuration["Kestrel:EndpointDefaults:Protocols"])
        || configuration.GetSection("Kestrel:Endpoints").GetChildren().Any(endpoint =>
            endpoint.GetSection("Certificate").Exists() || endpoint.GetSection("Sni").Exists() || UsesHttp3(endpoint["Protocols"]));

    public void InitializeListenerCertificates()
    {
        // 接收端的两个引用各自独立。即使管理台使用上传证书，也要保留内置自签引用。
        try { using var selfSigned = store.LoadSelfSigned(); }
        catch (Exception error) when (IsCertificateFailure(error)) { /* 不影响已有部署证书和其他协议启动。 */ }

        if (store.CanUpload) _ = _startup.Value;
        else
        {
            // 只初始化上传证书元数据。宿主提供的 HTTPS 证书继续由 Kestrel 自己读取。
            using var uploaded = store.LoadForStartup().Certificate;
        }
    }

    public void Apply(HttpsConnectionAdapterOptions options)
    {
        if (!store.CanUpload) return;
        if (_startup.Value.Certificate is { } certificate)
        {
            options.ServerCertificate = certificate.Certificate;
            options.ServerCertificateChain = certificate.Chain;
        }
        options.OnAuthenticate = (_, tls) =>
        {
            // 每次新握手取一份完整上下文，正在握手的连接可继续使用其已取到的证书。
            if (store.ActiveHttpsContext is { } current)
            {
                tls.ServerCertificate = null;
                tls.ServerCertificateSelectionCallback = null;
                tls.ServerCertificateContext = current;
                tls.AllowTlsResume = false;
            }
            else if ((tls.ServerCertificateContext?.TargetCertificate ?? tls.ServerCertificate as X509Certificate2) is { } bootstrap &&
                Interlocked.Exchange(ref _observedBootstrap, 1) == 0)
            {
                store.ObserveBootstrapCertificate(bootstrap);
            }
        };
        _applied = true;
    }

    public void MarkStarted()
    {
        if (_applied) store.MarkStarted(_startup.Value);
    }

    public void Dispose()
    {
        if (_startup.IsValueCreated) _startup.Value.Certificate?.Dispose();
    }

    private static bool IsCertificateFailure(Exception error) =>
        error is CertificateValidationException or IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException;

    private static bool UsesHttp3(string? protocols) => protocols?.Contains("Http3", StringComparison.OrdinalIgnoreCase) == true;
}
