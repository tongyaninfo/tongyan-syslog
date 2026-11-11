using Syslog.Api.Models;
using Syslog.Shared.Models;

namespace Syslog.Api.Service.impl;

public sealed partial class ControlStore
{
    public Task<ApiResult<bool>> AuthorizeHttpsCertificateUploadAsync(long actorId, string token, CertificateSummary certificate,
        CancellationToken cancellationToken) =>
        AuthorizeHttpsCertificateUploadAsync(OperationActor.Session(actorId, token), certificate, cancellationToken);

public async Task<ApiResult<bool>> AuthorizeHttpsCertificateUploadAsync(OperationActor actor, CertificateSummary certificate,
        CancellationToken cancellationToken)
    {
        var actorId = actor.UserId;
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(false, cancellationToken);
            using var transaction = connection.BeginTransaction();
            // 会话复核和上传意图同事务提交，审计失败时不允许保存候选证书。
            if (!await IsCurrentActorAsync(connection, transaction, actor, cancellationToken, "syslog_https_certificate_upload"))
                return ApiResult<bool>.Fail("authentication_required", "管理员会话已失效。");
            var auditId = await ReserveIdBlockAsync(connection, transaction, 1, cancellationToken);
            await AuditAsync(connection, transaction, auditId, actorId, "https_certificate.upload.request", "https",
                new { certificate.Fingerprint, certificate.NotBefore, certificate.NotAfter }, cancellationToken);
            transaction.Commit();
            return ApiResult<bool>.Ok(true);
        }
        finally { _writeGate.Release(); }
    }
}
