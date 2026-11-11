using System.Globalization;
using MimeKit;
using Syslog.Api.Common;
using Syslog.Api.Models;
using Syslog.Shared.Models;
using Syslog.Shared.Service;

namespace Syslog.Api.Service.impl;

public sealed class NotificationService(INotificationStore store, ISourceRegistry sources, TimeProvider clock)
{
    private const int PageSize = 25;

    public async Task<ApiResult<SmtpSettingsView>> ReadSmtpAsync(CancellationToken ct) =>
        Map(await store.GetSmtpSettingsAsync(ct), ToView);

    public Task<ApiResult<SmtpSettingsView>> SaveSmtpAsync(SaveSmtpSettingsRequest input, long actorId, string token, CancellationToken ct) =>
        SaveSmtpAsync(input, OperationActor.Session(actorId, token), ct);

public async Task<ApiResult<SmtpSettingsView>> SaveSmtpAsync(SaveSmtpSettingsRequest input, OperationActor actor, CancellationToken ct)
    {
        var host = input.Host?.Trim().TrimStart('[').TrimEnd(']') ?? "";
        var username = input.Username?.Trim() ?? "";
        var senderName = input.SenderName?.Trim() ?? "";
        var prefix = input.SubjectPrefix?.Trim() ?? "";
        var password = input.Password;
        if (input.Version < 0 || !ValidText(host, 253) || Uri.CheckHostName(host) == UriHostNameType.Unknown ||
            input.Port is < 1 or > 65535 || input.Security is not ("ssl" or "starttls" or "none") ||
            !ValidText(username, 254) || !TryMailbox(input.SenderAddress, out var sender) ||
            !ValidText(senderName, 80) || !ValidText(prefix, 80) ||
            password is { Length: > 1024 } || (password is not null && password.Any(char.IsControl)) ||
            (input.Security == "none" && username.Length > 0) || (username.Length == 0 && !string.IsNullOrEmpty(password)))
            return Invalid<SmtpSettingsView>("邮件配置无效，请检查服务器地址、端口、加密方式和发件信息。明文 SMTP 仅支持无认证中继。");

        var current = await store.GetSmtpSettingsAsync(ct);
        if (current.Data is not { } previous) return Map(current, ToView);
        if (previous.Version != input.Version)
            return ApiResult<SmtpSettingsView>.Fail("version_conflict", "邮件配置已更新，请重新读取后保存。");
        var keepPassword = previous.ProtectedPassword is not null && previous.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
            && previous.Port == input.Port && previous.Security == input.Security && previous.Username == username;
        if (username.Length > 0 && string.IsNullOrEmpty(password) && !keepPassword)
            return ApiResult<SmtpSettingsView>.Fail("smtp_password_required", "请填写 SMTP 授权码。");
        var storedPassword = username.Length == 0 ? "" : string.IsNullOrEmpty(password) ? null : NotificationSecrets.Store(password);
        var settings = new StoredSmtpSettings(input.Version, host, input.Port, input.Security, username, sender,
            senderName, prefix, storedPassword);
        return Map(await store.SaveSmtpSettingsAsync(settings, actor, ct), ToView);
    }

    public async Task<ApiResult<IReadOnlyList<ReportSubscriptionView>>> ListSubscriptionsAsync(CancellationToken ct) =>
        Map(await store.ListReportSubscriptionsAsync(ct), values => (IReadOnlyList<ReportSubscriptionView>)values.Select(ToView).ToArray());

    public Task<ApiResult<ReportSubscriptionView>> SaveSubscriptionAsync(long id, SaveReportSubscriptionRequest input,
        long actorId, string token, CancellationToken ct) =>
        SaveSubscriptionAsync(id, input, OperationActor.Session(actorId, token), ct);

public async Task<ApiResult<ReportSubscriptionView>> SaveSubscriptionAsync(long id, SaveReportSubscriptionRequest input,
        OperationActor actor, CancellationToken ct)
    {
        var name = input.Name?.Trim() ?? "";
        if (input.Version < 0 || (id == 0) != (input.Version == 0) || !ValidText(name, 80) || name.Length == 0 ||
            !ReportSchedule.IsFrequency(input.Frequency) || !ReportSchedule.TryParseTime(input.SendTime, out var time) ||
            input.SourceIds is null or { Length: > 1000 } || input.Recipients is not { Length: >= 1 and <= 20 })
            return Invalid<ReportSubscriptionView>("请检查报表名称、发送时间、设备和收件地址。");

        var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var recipient in input.Recipients)
        {
            if (!TryMailbox(recipient, out var email))
                return Invalid<ReportSubscriptionView>("收件邮箱地址无效。");
            recipients.Add(email);
        }
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sourceId in input.SourceIds)
        {
            if (sourceId is null || !TryId(sourceId, out _))
                return Invalid<ReportSubscriptionView>("设备标识无效。");
            sourceIds.Add(sourceId);
        }
        if (sourceIds.Count > 0)
        {
            var available = (await sources.ListAsync(ct)).Select(source => source.Id).ToHashSet(StringComparer.Ordinal);
            if (!sourceIds.IsSubsetOf(available))
                return ApiResult<ReportSubscriptionView>.Fail("source_forbidden", "部分设备已不存在，请重新选择设备。");
        }

        var subscription = new StoredReportSubscription(id, input.Version, name, input.Enabled, input.Frequency!,
            input.SendTime!, "Asia/Shanghai", sourceIds.ToArray(), recipients.ToArray(),
            ReportSchedule.NextAfter(input.Frequency!, time, clock.GetUtcNow()));
        return Map(await store.SaveReportSubscriptionAsync(subscription, actor, ct), ToView);
    }

    public Task<ApiResult<MailDeliveryId>> EnqueueTestAsync(TestEmailRequest input, long actorId, string token, CancellationToken ct) =>
        EnqueueTestAsync(input, OperationActor.Session(actorId, token), ct);

public async Task<ApiResult<MailDeliveryId>> EnqueueTestAsync(TestEmailRequest input, OperationActor actor, CancellationToken ct)
    {
        if (!TryMailbox(input.Recipient, out var recipient)) return Invalid<MailDeliveryId>("收件邮箱地址无效。");
        return Map(await store.EnqueueTestEmailAsync(recipient, actor, ct), value => new MailDeliveryId(Id(value.Id)));
    }

    public async Task<ApiResult<MailDeliveryPage>> ListDeliveriesAsync(string? kind, string? status, string? recipient,
        DateTimeOffset? from, DateTimeOffset? to, string? beforeId, CancellationToken ct)
    {
        long id = 0;
        string? address = null;
        if ((kind is not null && kind != "test" && !ReportSchedule.IsFrequency(kind)) ||
            status is not (null or "queued" or "sending" or "accepted" or "failed" or "unknown") ||
            (recipient is not null && !TryMailbox(recipient, out address)) || from >= to ||
            (beforeId is not null && !TryId(beforeId, out id)))
            return Invalid<MailDeliveryPage>("发送记录筛选条件无效。");
        var result = await store.ListMailDeliveriesAsync(kind, status, address, from, to, id == 0 ? null : id, PageSize + 1, ct);
        return Map(result, rows => new MailDeliveryPage(rows.Take(PageSize).Select(row => ToView(row, false)).ToArray(),
            rows.Count > PageSize ? Id(rows[PageSize - 1].Id) : null));
    }

    public async Task<ApiResult<MailDeliveryView>> GetDeliveryAsync(long id, CancellationToken ct) =>
        Map(await store.GetMailDeliveryAsync(id, ct), row => ToView(row, true));

    private static SmtpSettingsView ToView(StoredSmtpSettings value) => new(value.Version, value.Host, value.Port,
        value.Security, value.Username, value.SenderAddress, value.SenderName, value.SubjectPrefix, !string.IsNullOrEmpty(value.ProtectedPassword));

    private static ReportSubscriptionView ToView(StoredReportSubscription value) => new(Id(value.Id), value.Version,
        value.Name, value.Enabled, value.Frequency, value.SendTime, value.SourceIds, value.Recipients,
        value.Enabled ? value.NextSendAt : null);

    private static MailDeliveryView ToView(StoredMailDelivery value, bool includeBody) => new(Id(value.Id), value.Kind, value.Status,
        value.Recipient, value.Subject, value.PeriodStart, value.PeriodEnd, value.CreatedAt, value.UpdatedAt,
        value.Attempts, value.FailureCode, value.SubscriptionName, includeBody ? value.HtmlBody : null, includeBody ? value.TextBody : null);

    public static bool TryId(string value, out long id) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
    private static string Id(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static bool ValidText(string value, int maximum) => value.Length <= maximum && !value.Any(char.IsControl);

    private static bool TryMailbox(string? value, out string address)
    {
        address = value?.Trim() ?? "";
        if (!ValidText(address, 254) || !MailboxAddress.TryParse(address, out var parsed) ||
            !address.Equals(parsed.Address, StringComparison.Ordinal) || address.LastIndexOf('@') is <= 0)
            return false;
        var domainStart = address.LastIndexOf('@') + 1;
        if (domainStart == address.Length) return false;
        address = address[..domainStart] + address[domainStart..].ToLowerInvariant();
        return true;
    }

    private static ApiResult<T> Invalid<T>(string message) => ApiResult<T>.Fail("invalid_request", message);

    private static ApiResult<TOutput> Map<TInput, TOutput>(ApiResult<TInput> result, Func<TInput, TOutput> convert) =>
        result.Success && result.Data is { } data
            ? ApiResult<TOutput>.Ok(convert(data))
            : ApiResult<TOutput>.Fail(result.Code, result.Message);
}
