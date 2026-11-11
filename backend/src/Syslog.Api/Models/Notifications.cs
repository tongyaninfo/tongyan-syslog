namespace Syslog.Api.Models;

public sealed record SaveSmtpSettingsRequest(long Version, string? Host, int Port, string? Security,
    string? Username, string? SenderAddress, string? SenderName, string? SubjectPrefix, string? Password);

public sealed record SmtpSettingsView(long Version, string Host, int Port, string Security,
    string Username, string SenderAddress, string SenderName, string SubjectPrefix, bool HasPassword);

public sealed record SaveReportSubscriptionRequest(long Version, string? Name, bool Enabled, string? Frequency,
    string? SendTime, string?[]? SourceIds, string?[]? Recipients);

public sealed record ReportSubscriptionView(string Id, long Version, string Name, bool Enabled, string Frequency,
    string SendTime, string[] SourceIds, string[] Recipients, DateTimeOffset? NextSendAt);

public sealed record TestEmailRequest(string? Recipient);
public sealed record MailDeliveryId(string Id);
public sealed record MailDeliveryPage(IReadOnlyList<MailDeliveryView> Items, string? NextBeforeId);
public sealed record MailDeliveryView(string Id, string Kind, string Status, string Recipient, string Subject,
    DateTimeOffset? PeriodStart, DateTimeOffset? PeriodEnd, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt,
    int Attempts, string? FailureCode, string? SubscriptionName, string? HtmlBody = null, string? TextBody = null);
