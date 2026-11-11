using System.Security.Cryptography.X509Certificates;

namespace Syslog.Ingest.Models;

/// <summary>监听周期内持有服务端证书及证书链，完成连接回收后一起释放。</summary>
public sealed class ServerCertificateMaterial(X509Certificate2 certificate, X509Certificate2Collection? chain = null) : IDisposable
{
    public X509Certificate2 Certificate { get; } = certificate;
    public X509Certificate2Collection Chain { get; } = chain ?? new();

    public void Dispose()
    {
        foreach (var item in Chain)
            if (!ReferenceEquals(item, Certificate)) item.Dispose();
        Certificate.Dispose();
    }
}
