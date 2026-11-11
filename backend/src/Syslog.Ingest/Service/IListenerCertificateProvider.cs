using Syslog.Ingest.Models;

namespace Syslog.Ingest.Service;

/// <summary>宿主提供系统托管证书。接收配置只使用稳定标识，不持久化证书材料。</summary>
public interface IListenerCertificateProvider
{
    public const string SelfSignedId = "system-self-signed";
    public const string UploadedId = "system-uploaded";

    IReadOnlyList<string> GetAvailableCertificateIds();
    // 证书实例及链由调用方独立持有和释放。
    ServerCertificateMaterial? LoadCertificate(string id);

    public static bool IsManagedId(string id) => id is SelfSignedId or UploadedId;
}
