using Syslog.Ingest.Models;

namespace Syslog.Api.Models;

/// <summary>管理接口只显示公有证书信息。</summary>
public sealed record CertificateSummary(string Fingerprint, string Subject, DateTimeOffset NotBefore, DateTimeOffset NotAfter);

public sealed record SystemCertificateView(CertificateSummary? Current, CertificateSummary? Pending,
    bool RestartRequired, string? LoadError, bool CanUpload);

/// <summary>证书材料的使用者负责在宿主停止后释放。</summary>
public sealed record CertificateStartupResult(ServerCertificateMaterial? Certificate, string? ErrorCode, bool IsUploadedCandidate);

public sealed class CertificateValidationException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
