using System.Security.Cryptography;
using Syslog.Api.Models;
using Syslog.Shared.Models;

namespace Syslog.Api.Service.impl;

/// <summary>网页上传和 MCP 共用证书准备、可靠授权审计与激活顺序。</summary>
public sealed class HttpsCertificateUploadService(SystemCertificateStore certificates, IControlStore control)
{
    public async Task<ApiResult<SystemCertificateView>> UploadAsync(byte[] bytes, string? password, OperationActor actor, CancellationToken ct)
    {
        if (!certificates.CanUpload) return ApiResult<SystemCertificateView>.Fail("https_certificate_managed_externally", "管理台证书由宿主配置管理。");
        if (bytes.Length is < 1 or > SystemCertificateStore.MaxUploadBytes)
            return ApiResult<SystemCertificateView>.Fail("https_certificate_too_large", "证书文件必须为 1 至 256 KiB。");
        if (password?.Length > 1024) return ApiResult<SystemCertificateView>.Fail("https_certificate_invalid", "证书密码过长。");
        try
        {
            using var candidate = certificates.PrepareUpload(bytes, password);
            var audit = await control.AuthorizeHttpsCertificateUploadAsync(actor, SystemCertificateStore.Summarize(candidate.Certificate), ct);
            if (!audit.Success) return ApiResult<SystemCertificateView>.Fail(audit.Code, audit.Message);
            ct.ThrowIfCancellationRequested();
            certificates.ActivateHttps(candidate);
            return ApiResult<SystemCertificateView>.Ok(certificates.GetView());
        }
        catch (CertificateValidationException error)
        {
            return ApiResult<SystemCertificateView>.Fail(error.Code switch
            {
                "certificate_size_invalid" => "https_certificate_too_large",
                "certificate_store_unavailable" => "https_certificate_unavailable",
                _ => "https_certificate_invalid"
            }, error.Message);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return ApiResult<SystemCertificateView>.Fail("https_certificate_unavailable", "证书保存失败，请检查服务私有目录。");
        }
    }
}
