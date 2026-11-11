using Syslog.Shared.Models;
using Syslog.Api.Models;

namespace Syslog.Api.Service;

// ProtectedPassword 沿用控制库历史列名，内容仅供服务端读取，可为新格式或旧版密文。
public sealed record StoredSmtpSettings(long Version, string Host, int Port, string Security, string Username,
    string SenderAddress, string SenderName, string SubjectPrefix, string? ProtectedPassword);

public sealed record StoredReportSubscription(long Id, long Version, string Name, bool Enabled, string Frequency,
    string SendTime, string TimeZoneId, string[] SourceIds, string[] Recipients, DateTimeOffset NextSendAt);

public sealed record StoredMailDelivery(long Id, long? SubscriptionId, string? SubscriptionName, string Kind, string Status,
    string Recipient, string SubjectPrefix, string Subject, string? HtmlBody, string? TextBody, string[] SourceIds,
    DateTimeOffset? PeriodStart, DateTimeOffset? PeriodEnd, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt,
    int Attempts, string? FailureCode);

public interface INotificationStore
{
    Task<ApiResult<StoredSmtpSettings>> SaveSmtpSettingsAsync(StoredSmtpSettings settings, OperationActor actor, CancellationToken ct) => actor.IsMcp
        ? Task.FromResult(ApiResult<StoredSmtpSettings>.Fail("mcp_permission_denied", "存储不支持该操作主体。"))
        : SaveSmtpSettingsAsync(settings, actor.UserId, actor.SessionToken ?? "", ct);
    Task<ApiResult<StoredReportSubscription>> SaveReportSubscriptionAsync(StoredReportSubscription subscription, OperationActor actor, CancellationToken ct) => actor.IsMcp
        ? Task.FromResult(ApiResult<StoredReportSubscription>.Fail("mcp_permission_denied", "存储不支持该操作主体。"))
        : SaveReportSubscriptionAsync(subscription, actor.UserId, actor.SessionToken ?? "", ct);
    Task<ApiResult<StoredMailDelivery>> EnqueueTestEmailAsync(string recipient, OperationActor actor, CancellationToken ct) => actor.IsMcp
        ? Task.FromResult(ApiResult<StoredMailDelivery>.Fail("mcp_permission_denied", "存储不支持该操作主体。"))
        : EnqueueTestEmailAsync(recipient, actor.UserId, actor.SessionToken ?? "", ct);
    Task<ApiResult<StoredSmtpSettings>> GetSmtpSettingsAsync(CancellationToken cancellationToken);
    Task<ApiResult<StoredSmtpSettings>> SaveSmtpSettingsAsync(StoredSmtpSettings settings, long actorId, string token,
        CancellationToken cancellationToken);
    Task<ApiResult<IReadOnlyList<StoredReportSubscription>>> ListReportSubscriptionsAsync(CancellationToken cancellationToken);
    Task<ApiResult<StoredReportSubscription>> SaveReportSubscriptionAsync(StoredReportSubscription subscription, long actorId,
        string token, CancellationToken cancellationToken);
    Task<ApiResult<StoredMailDelivery>> EnqueueTestEmailAsync(string recipient, long actorId, string token,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<StoredReportSubscription>> ListDueReportSubscriptionsAsync(DateTimeOffset now, int limit,
        CancellationToken cancellationToken);
    Task<bool> ScheduleReportAsync(StoredReportSubscription subscription, DateTimeOffset periodStart, DateTimeOffset periodEnd,
        DateTimeOffset nextSendAt, CancellationToken cancellationToken);
    Task<StoredMailDelivery?> ClaimMailDeliveryAsync(DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> StoreMailContentAsync(long id, string subject, string htmlBody, string textBody, CancellationToken cancellationToken);
    Task FinishMailDeliveryAsync(long id, string status, string? failureCode, DateTimeOffset? retryAt, CancellationToken cancellationToken);
    Task RecoverInterruptedMailDeliveriesAsync(CancellationToken cancellationToken);
    Task<ApiResult<IReadOnlyList<StoredMailDelivery>>> ListMailDeliveriesAsync(string? kind, string? status, string? recipient,
        DateTimeOffset? from, DateTimeOffset? to, long? beforeId, int limit, CancellationToken cancellationToken);
    Task<ApiResult<StoredMailDelivery>> GetMailDeliveryAsync(long id, CancellationToken cancellationToken);
}
