using Syslog.Api.Models;
using Syslog.Shared.Models;
using Syslog.Shared.Service;

namespace Syslog.Api.Service;

public interface IControlStore : IIdService, ISourceRegistry
{
    Task<ApiResult<SavedListenerSettings>> SaveListenerSettingsAsync(SaveListenerSettingsRequest input, OperationActor actor,
        CancellationToken ct, bool applyReception = false, string? protocol = null) => actor.IsMcp
        ? Task.FromResult(ApiResult<SavedListenerSettings>.Fail("mcp_permission_denied", "存储不支持该操作主体。"))
        : SaveListenerSettingsAsync(input, actor.UserId, actor.SessionToken ?? "", ct, applyReception, protocol);
    Task<ApiResult<long>> BeginLogClearAsync(OperationActor actor, string database, CancellationToken ct, long? sourceId = null) => actor.IsMcp
        ? Task.FromResult(ApiResult<long>.Fail("mcp_permission_denied", "存储不支持该操作主体。"))
        : BeginLogClearAsync(actor.UserId, actor.SessionToken ?? "", database, ct, sourceId);
    Task<ApiResult<bool>> AuthorizeHttpsCertificateUploadAsync(OperationActor actor, CertificateSummary certificate, CancellationToken ct) => actor.IsMcp
        ? Task.FromResult(ApiResult<bool>.Fail("mcp_permission_denied", "存储不支持该操作主体。"))
        : AuthorizeHttpsCertificateUploadAsync(actor.UserId, actor.SessionToken ?? "", certificate, ct);
    Task InitializeAsync(CancellationToken cancellationToken);
    Task MigrateListenersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    Task MigrateSourceFiltersAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    Task MigrateNotificationsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    Task MigrateMcpAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    Task MigrateLiveLogsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    Task<ApiResult<SavedListenerSettings>> ReadListenerSettingsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(ApiResult<SavedListenerSettings>.Fail("listener_migration_required", "请先执行监听配置显式迁移。"));
    Task<ApiResult<SavedListenerSettings>> SaveListenerSettingsAsync(SaveListenerSettingsRequest input, long actorId, string token,
        CancellationToken cancellationToken, bool applyReception = false, string? protocol = null) =>
        Task.FromResult(ApiResult<SavedListenerSettings>.Fail("listener_migration_required", "请先执行监听配置显式迁移。"));
    Task<ApiResult<ControlUser>> BootstrapAdminAsync(string username, string password, CancellationToken cancellationToken);
    Task<bool> IsSetupRequiredAsync(CancellationToken cancellationToken);
    Task<ApiResult<ControlUser>> SetupAdminAsync(string username, string password, CancellationToken cancellationToken);
    Task<ApiResult<ManagedUserPage>> ListUsersAsync(UserQuery query, long actorId, string token, CancellationToken cancellationToken);
    Task<ApiResult<ManagedUser>> CreateUserAsync(CreateUserRequest input, long actorId, string token, CancellationToken cancellationToken);
    Task<ApiResult<ManagedUser>> UpdateUserAsync(string id, UpdateUserRequest input, long actorId, string token, CancellationToken cancellationToken);
    Task<ApiResult<bool>> ResetUserPasswordAsync(string id, ResetUserPasswordRequest input, long actorId, string token, CancellationToken cancellationToken);
    Task<ApiResult<SessionTicket>> LoginAsync(string username, string password, CancellationToken cancellationToken);
    Task<ControlUser?> ValidateSessionAsync(string token, CancellationToken cancellationToken);
    Task<ApiResult<bool>> ChangePasswordAsync(long userId, string token, string? currentPassword, string? newPassword, CancellationToken cancellationToken);
    Task<ApiResult<bool>> LogoutAsync(string token, CancellationToken cancellationToken);
    Task<ApiResult<LogSource>> CreateSourceAsync(SourceInput input, long actorId, CancellationToken cancellationToken);
    Task<ApiResult<LogSource>> UpdateSourceAsync(string sourceId, SourceInput input, long actorId, CancellationToken cancellationToken);
    Task<ApiResult<long>> BeginLogClearAsync(long actorId, string token, string database, CancellationToken cancellationToken,
        long? sourceId = null) =>
        Task.FromResult(ApiResult<long>.Fail("service_unavailable", "无法记录清空操作审计。"));
    Task RecordLogClearResultAsync(long operationId, long actorId, string outcome, string code, CancellationToken cancellationToken,
        long? sourceId = null) =>
        throw new NotSupportedException();
    Task<ApiResult<bool>> AuthorizeHttpsCertificateUploadAsync(long actorId, string token, CertificateSummary certificate, CancellationToken cancellationToken) =>
        Task.FromResult(ApiResult<bool>.Fail("service_unavailable", "无法记录 HTTPS 证书上传审计。"));
}
