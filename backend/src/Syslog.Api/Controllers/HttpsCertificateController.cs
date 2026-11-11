using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Syslog.Api.Common;
using Syslog.Api.Models;
using Syslog.Api.Service;
using Syslog.Api.Service.impl;
using Syslog.Shared.Models;

namespace Syslog.Api.Controllers;

[ApiController, Authorize(Roles = "admin"), Route("api/admin/v1/system/https-certificate")]
public sealed class HttpsCertificateController(SystemCertificateStore store, HttpsCertificateUploadService uploads) : ControllerBase
{
    private const int MaxRequestBytes = SystemCertificateStore.MaxUploadBytes + 8 * 1024;

    [HttpGet]
    public IActionResult Get() => Request.IsHttps
        ? Ok(ApiResult<SystemCertificateView>.Ok(store.GetView()))
        : Error(400, "https_required", "证书配置必须通过 HTTPS 操作。");

    [HttpPost, EnableRateLimiting("https-certificate"), RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MemoryBufferThreshold = MaxRequestBytes, MultipartBodyLengthLimit = MaxRequestBytes, ValueCountLimit = 3)]
    public async Task<IActionResult> Upload(CancellationToken cancellationToken)
    {
        if (!Request.IsHttps) return Error(400, "https_required", "证书配置必须通过 HTTPS 操作。");
        if (!store.CanUpload) return Error(409, "https_certificate_managed_externally", "管理台证书由宿主配置管理。");
        if (Request.ContentLength > MaxRequestBytes) return TooLarge();
        if (!Request.HasFormContentType || Request.ContentType?.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase) != true)
            return Invalid();

        byte[]? bytes = null;
        try
        {
            // 证书请求单独限额，小文件全程在内存中处理，不生成框架临时上传文件。
            var form = await Request.ReadFormAsync(cancellationToken);
            // 兼容缓存中的旧版页面，旧 host 字段不参与证书选择或校验。
            if (form.Files.Count != 1 || form.Files.GetFile("file") is not { Length: > 0 } file
                || form.Keys.Any(key => key is not ("password" or "host"))
                || form["password"].Count > 1
                || Path.GetExtension(file.FileName).ToLowerInvariant() is not (".pfx" or ".p12"))
                return Invalid();
            if (file.Length > SystemCertificateStore.MaxUploadBytes) return TooLarge();
            var password = form["password"].ToString();
            if (password.Length > 1024) return Invalid();

            bytes = new byte[(int)file.Length];
            await using (var stream = file.OpenReadStream()) await stream.ReadExactlyAsync(bytes, cancellationToken);
            var actorId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
            var token = Request.Cookies[SessionAuthenticationHandler.CookieName] ?? "";
            var result = await uploads.UploadAsync(bytes, password, OperationActor.Session(actorId, token), cancellationToken);
            return StatusCode(result.Success ? 200 : result.Code switch
            {
                "authentication_required" => 401,
                "https_certificate_managed_externally" => 409,
                "https_certificate_too_large" => 413,
                "https_certificate_invalid" => 400,
                _ => 503
            }, result);
        }
        catch (CertificateValidationException error)
        {
            return error.Code switch
            {
                "certificate_size_invalid" => TooLarge(),
                "certificate_store_unavailable" => Error(503, "https_certificate_unavailable", error.Message),
                _ => Error(400, "https_certificate_invalid", error.Message)
            };
        }
        catch (BadHttpRequestException error) when (error.StatusCode == 413) { return TooLarge(); }
        catch (BadHttpRequestException) { return Invalid(); }
        catch (InvalidDataException) { return Invalid(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return Error(503, "https_certificate_unavailable", "证书保存失败，请检查服务私有目录。");
        }
        finally
        {
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private IActionResult Invalid() => Error(400, "https_certificate_invalid", "请上传有效的 PFX 或 P12 证书并检查密码。");
    private IActionResult TooLarge() => Error(413, "https_certificate_too_large", "证书文件不能超过 256 KiB。");
    private IActionResult Error(int status, string code, string message) => StatusCode(status, ApiResult<SystemCertificateView>.Fail(code, message));
}
