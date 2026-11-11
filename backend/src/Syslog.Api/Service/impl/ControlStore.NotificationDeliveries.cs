using Syslog.Api.Models;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Syslog.Api.Service;
using Syslog.Shared.Models;

namespace Syslog.Api.Service.impl;

public sealed partial class ControlStore
{
    private const int MaxActiveMailDeliveries = 512;
    private const string MailHeadColumns = "id,subscription_id,subscription_name,kind,status,recipient,subject_prefix,subject";
    private const string MailTailColumns = "source_ids_json,period_start,period_end,created_at,updated_at,attempts,failure_code";
    private const string MailColumns = MailHeadColumns + ",html_body,text_body," + MailTailColumns;
    private const string MailListColumns = MailHeadColumns + ",NULL,NULL," + MailTailColumns;

    public Task<ApiResult<StoredMailDelivery>> EnqueueTestEmailAsync(string recipient, long actorId, string token,
        CancellationToken cancellationToken) =>
        EnqueueTestEmailAsync(recipient, OperationActor.Session(actorId, token), cancellationToken);

public Task<ApiResult<StoredMailDelivery>> EnqueueTestEmailAsync(string recipient, OperationActor actor,
        CancellationToken cancellationToken)
    {
        var actorId = actor.UserId;
        if (!ValidMailbox(recipient)) return Task.FromResult(InvalidNotificationInput<StoredMailDelivery>());
        return WriteNotificationAsync(async (connection, transaction, ct) =>
        {
            if (!await IsCurrentActorAsync(connection, transaction, actor, ct, "syslog_mail_send_test"))
                return NotificationAuthenticationRequired<StoredMailDelivery>();
            var settings = await ReadSmtpSettingsAsync(connection, transaction, ct);
            if (settings.Version == 0)
                return ApiResult<StoredMailDelivery>.Fail("smtp_not_configured", "请先保存 SMTP 配置。");
            if (!await HasMailCapacityAsync(connection, transaction, 1, ct))
                return ApiResult<StoredMailDelivery>.Fail("notification_queue_full", "邮件队列已满，请稍后重试。");

            var id = await ReserveIdBlockAsync(connection, transaction, 2, ct);
            var delivery = new StoredMailDelivery(id, null, null, "test", "queued", recipient, settings.SubjectPrefix,
                "", null, null, [], null, null, DateTimeOffset.FromUnixTimeMilliseconds(Now()), null, 0, null);
            await InsertMailDeliveryAsync(connection, transaction, delivery, ct);
            await AuditAsync(connection, transaction, id + 1, actorId, "notification.mail.test", Id(id), new { }, ct);
            return ApiResult<StoredMailDelivery>.Ok(delivery);
        }, NotificationMigrationRequired<StoredMailDelivery>(), cancellationToken);
    }

    public Task<bool> ScheduleReportAsync(StoredReportSubscription subscription, DateTimeOffset periodStart, DateTimeOffset periodEnd,
        DateTimeOffset nextSendAt, CancellationToken cancellationToken)
    {
        var observed = _clock.GetUtcNow();
        if (periodStart >= periodEnd || periodEnd > observed || nextSendAt <= subscription.NextSendAt)
            throw new ArgumentException("报告周期或下次发送时间无效。", nameof(nextSendAt));
        return WriteNotificationAsync(async (connection, transaction, ct) =>
        {
            var current = (await ReadSubscriptionsAsync(connection, transaction,
                $"SELECT {SubscriptionColumns} FROM report_subscriptions WHERE id=$id AND version=$version AND enabled=1 AND next_send_at=$due AND next_send_at<=$now;",
                ct, ("$id", subscription.Id), ("$version", subscription.Version),
                ("$due", subscription.NextSendAt.ToUnixTimeMilliseconds()), ("$now", observed.ToUnixTimeMilliseconds()))).SingleOrDefault();
            if (current is null) return false;
            var settings = await ReadSmtpSettingsAsync(connection, transaction, ct);
            if (settings.Version == 0) return false;

            var previousRecipients = await ReadScheduledRecipientsAsync(connection, transaction, current.Id, periodStart, ct);
            var recipients = current.Recipients.Where(recipient => !previousRecipients.Contains(RecipientKey(recipient))).ToArray();
            // 无法一次提交该周期的全部新收件人时保留到期状态，避免部分排队后漏发。
            if (recipients.Length > 0)
            {
                if (!await HasMailCapacityAsync(connection, transaction, recipients.Length, ct)) return false;
                var firstId = await ReserveIdBlockAsync(connection, transaction, recipients.Length, ct);
                var now = DateTimeOffset.FromUnixTimeMilliseconds(Now());
                for (var index = 0; index < recipients.Length; index++)
                {
                    var delivery = new StoredMailDelivery(firstId + index, current.Id, current.Name, current.Frequency, "queued",
                        recipients[index], settings.SubjectPrefix, "", null, null, current.SourceIds, periodStart, periodEnd, now, null, 0, null);
                    await InsertMailDeliveryAsync(connection, transaction, delivery, ct);
                }
            }

            await ExecuteAsync(connection, transaction, "UPDATE report_subscriptions SET next_send_at=$next WHERE id=$id;", ct,
                ("$next", nextSendAt.ToUnixTimeMilliseconds()), ("$id", current.Id));
            return true;
        }, false, cancellationToken);
    }

    public Task<StoredMailDelivery?> ClaimMailDeliveryAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        WriteNotificationAsync(async (connection, transaction, ct) =>
        {
            // UPDATE 和 RETURNING 在同一事务中，抢占成功后才把任务交给 SMTP 工作者。
            await using var command = Command(connection, transaction,
                $"""
                UPDATE mail_deliveries SET status='sending',attempts=attempts+1,updated_at=$now
                WHERE id=(SELECT id FROM mail_deliveries WHERE status='queued' AND available_at<=$now ORDER BY available_at,id LIMIT 1)
                RETURNING {MailColumns};
                """, ("$now", now.ToUnixTimeMilliseconds()));
            await using var reader = await command.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? ReadMailDelivery(reader) : null;
        }, null, cancellationToken);

    public Task<bool> StoreMailContentAsync(long id, string subject, string htmlBody, string textBody, CancellationToken cancellationToken)
    {
        if (id <= 0 || !ValidText(subject, 300, false) || htmlBody.Length is < 1 or > 131072 || textBody.Length is < 1 or > 65536)
            throw new ArgumentException("邮件主题或正文超过允许范围。", nameof(subject));
        return WriteNotificationAsync(async (connection, transaction, ct) =>
        {
            var changed = await ExecuteAsync(connection, transaction,
                "UPDATE mail_deliveries SET subject=$subject,html_body=$html,text_body=$text WHERE id=$id AND status='sending' AND html_body IS NULL;",
                ct, ("$subject", subject), ("$html", htmlBody), ("$text", textBody), ("$id", id));
            if (changed != 1) return false;

            // 同一订阅、周期及相同的发送快照可复用一次报告渲染；其余历史内容不可覆盖。
            await ExecuteAsync(connection, transaction,
                """
                UPDATE mail_deliveries AS sibling SET subject=$subject,html_body=$html,text_body=$text
                WHERE sibling.status='queued' AND sibling.html_body IS NULL AND EXISTS (
                    SELECT 1 FROM mail_deliveries AS current WHERE current.id=$id
                      AND current.subscription_id=sibling.subscription_id AND current.period_start=sibling.period_start
                      AND current.period_end=sibling.period_end AND current.subscription_name=sibling.subscription_name
                      AND current.kind=sibling.kind AND current.subject_prefix=sibling.subject_prefix AND current.source_ids_json=sibling.source_ids_json
                );
                """, ct, ("$subject", subject), ("$html", htmlBody), ("$text", textBody), ("$id", id));
            return true;
        }, false, cancellationToken);
    }

    public Task FinishMailDeliveryAsync(long id, string status, string? failureCode, DateTimeOffset? retryAt,
        CancellationToken cancellationToken)
    {
        if (id <= 0 || status is not ("accepted" or "failed" or "unknown" or "queued") ||
            (retryAt is not null) != (status == "queued") || failureCode is not null && !ValidText(failureCode, 128, false))
            throw new ArgumentException("邮件发送结果无效。", nameof(status));
        return WriteNotificationAsync((connection, transaction, ct) => ExecuteAsync(connection, transaction,
            "UPDATE mail_deliveries SET status=$status,failure_code=$code,available_at=coalesce($retry,available_at),updated_at=$now WHERE id=$id AND status='sending';",
            ct, ("$status", status), ("$code", failureCode), ("$retry", retryAt?.ToUnixTimeMilliseconds()), ("$now", Now()), ("$id", id)),
            0, cancellationToken);
    }

    public Task RecoverInterruptedMailDeliveriesAsync(CancellationToken cancellationToken) =>
        WriteNotificationAsync((connection, transaction, ct) => ExecuteAsync(connection, transaction,
            "UPDATE mail_deliveries SET status='unknown',failure_code='smtp_delivery_unknown',updated_at=$now WHERE status='sending';",
            ct, ("$now", Now())), 0, cancellationToken);

    public Task<ApiResult<IReadOnlyList<StoredMailDelivery>>> ListMailDeliveriesAsync(string? kind, string? status, string? recipient,
        DateTimeOffset? from, DateTimeOffset? to, long? beforeId, int limit, CancellationToken cancellationToken)
    {
        if (kind is not (null or "test" or "daily" or "weekly" or "monthly") ||
            status is not (null or "queued" or "sending" or "accepted" or "failed" or "unknown") ||
            (recipient is not null && !ValidMailbox(recipient)) || from >= to || beforeId is <= 0 || limit is < 1 or > 101)
            return Task.FromResult(InvalidNotificationInput<IReadOnlyList<StoredMailDelivery>>());
        return ReadNotificationAsync(async (connection, transaction, ct) =>
        {
            // 条件只取固定 SQL 片段，具体地址仍使用参数。指定收件人时可走收件人索引。
            var recipientFilter = recipient is null ? "" : "AND recipient_key=$recipient";
            await using var command = Command(connection, transaction,
                $"""
                SELECT {MailListColumns} FROM mail_deliveries WHERE id < coalesce($before,9223372036854775807)
                    AND ($kind IS NULL OR kind=$kind) AND ($status IS NULL OR status=$status)
                    {recipientFilter}
                    AND ($from IS NULL OR created_at >= $from) AND ($to IS NULL OR created_at < $to)
                ORDER BY id DESC LIMIT $limit;
                """, ("$before", beforeId), ("$kind", kind), ("$status", status),
                ("$recipient", recipient is null ? null : RecipientKey(recipient)), ("$from", from?.ToUnixTimeMilliseconds()),
                ("$to", to?.ToUnixTimeMilliseconds()), ("$limit", limit));
            await using var reader = await command.ExecuteReaderAsync(ct);
            var deliveries = new List<StoredMailDelivery>();
            while (await reader.ReadAsync(ct)) deliveries.Add(ReadMailDelivery(reader));
            return ApiResult<IReadOnlyList<StoredMailDelivery>>.Ok(deliveries);
        }, NotificationMigrationRequired<IReadOnlyList<StoredMailDelivery>>(), cancellationToken);
    }

    public Task<ApiResult<StoredMailDelivery>> GetMailDeliveryAsync(long id, CancellationToken cancellationToken)
    {
        if (id <= 0) return Task.FromResult(InvalidNotificationInput<StoredMailDelivery>());
        return ReadNotificationAsync(async (connection, transaction, ct) =>
        {
            await using var command = Command(connection, transaction, $"SELECT {MailColumns} FROM mail_deliveries WHERE id=$id;", ("$id", id));
            await using var reader = await command.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? ApiResult<StoredMailDelivery>.Ok(ReadMailDelivery(reader)) : NotificationNotFound<StoredMailDelivery>();
        }, NotificationMigrationRequired<StoredMailDelivery>(), cancellationToken);
    }

    private static async Task<bool> HasMailCapacityAsync(SqliteConnection connection, SqliteTransaction transaction, int requested, CancellationToken ct)
    {
        var active = await ScalarAsync(connection, transaction, "SELECT count(*) FROM mail_deliveries WHERE status IN ('queued','sending');", ct);
        return Convert.ToInt64(active, CultureInfo.InvariantCulture) <= MaxActiveMailDeliveries - requested;
    }

    private static async Task<HashSet<string>> ReadScheduledRecipientsAsync(SqliteConnection connection, SqliteTransaction transaction,
        long subscriptionId, DateTimeOffset periodStart, CancellationToken ct)
    {
        await using var command = Command(connection, transaction,
            "SELECT recipient_key FROM mail_deliveries WHERE subscription_id=$subscription AND period_start=$start;",
            ("$subscription", subscriptionId), ("$start", periodStart.ToUnixTimeMilliseconds()));
        await using var reader = await command.ExecuteReaderAsync(ct);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(ct)) keys.Add(reader.GetString(0));
        return keys;
    }

    private static Task<int> InsertMailDeliveryAsync(SqliteConnection connection, SqliteTransaction transaction, StoredMailDelivery item,
        CancellationToken ct) => ExecuteAsync(connection, transaction,
        """
        INSERT INTO mail_deliveries(id,subscription_id,subscription_name,kind,status,recipient,recipient_key,subject_prefix,subject,
            source_ids_json,period_start,period_end,created_at,available_at)
        VALUES($id,$subscription,$name,$kind,'queued',$recipient,$key,$prefix,$subject,$sources,$start,$end,$now,$now);
        """, ct, ("$id", item.Id), ("$subscription", item.SubscriptionId), ("$name", item.SubscriptionName), ("$kind", item.Kind),
        ("$recipient", item.Recipient), ("$key", RecipientKey(item.Recipient)), ("$prefix", item.SubjectPrefix), ("$subject", item.Subject),
        ("$sources", JsonSerializer.Serialize(item.SourceIds)), ("$start", item.PeriodStart?.ToUnixTimeMilliseconds()),
        ("$end", item.PeriodEnd?.ToUnixTimeMilliseconds()), ("$now", item.CreatedAt.ToUnixTimeMilliseconds()));

    private static StoredMailDelivery ReadMailDelivery(SqliteDataReader reader) => new(reader.GetInt64(0),
        reader.IsDBNull(1) ? null : reader.GetInt64(1), ReadNullableString(reader, 2), reader.GetString(3), reader.GetString(4),
        reader.GetString(5), reader.GetString(6), reader.GetString(7), ReadNullableString(reader, 8), ReadNullableString(reader, 9),
        ReadStrings(reader, 10), ReadNullableTimestamp(reader, 11), ReadNullableTimestamp(reader, 12), ReadTimestamp(reader, 13),
        ReadNullableTimestamp(reader, 14), reader.GetInt32(15), ReadNullableString(reader, 16));

    private static string RecipientKey(string recipient) => recipient.ToUpperInvariant();
}
