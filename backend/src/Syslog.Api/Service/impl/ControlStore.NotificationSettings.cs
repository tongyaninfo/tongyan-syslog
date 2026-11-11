using Syslog.Api.Models;
using System.Globalization;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Syslog.Api.Service;
using Syslog.Shared.Models;

namespace Syslog.Api.Service.impl;

public sealed partial class ControlStore
{
    private const int MaxReportSubscriptions = 100;
    private const int MaxReportRecipients = 20;
    private const int MaxReportSources = 1000;
    private const string SubscriptionColumns = "id,version,name,enabled,frequency,send_time,time_zone_id,source_ids_json,recipients_json,next_send_at";

    public Task<ApiResult<StoredSmtpSettings>> GetSmtpSettingsAsync(CancellationToken cancellationToken) =>
        ReadNotificationAsync(async (connection, transaction, ct) =>
            ApiResult<StoredSmtpSettings>.Ok(await ReadSmtpSettingsAsync(connection, transaction, ct)),
            NotificationMigrationRequired<StoredSmtpSettings>(), cancellationToken);

    public Task<ApiResult<StoredSmtpSettings>> SaveSmtpSettingsAsync(StoredSmtpSettings settings, long actorId, string token,
        CancellationToken cancellationToken) =>
        SaveSmtpSettingsAsync(settings, OperationActor.Session(actorId, token), cancellationToken);

public Task<ApiResult<StoredSmtpSettings>> SaveSmtpSettingsAsync(StoredSmtpSettings settings, OperationActor actor,
        CancellationToken cancellationToken)
    {
        var actorId = actor.UserId;
        if (!ValidSmtpSettings(settings)) return Task.FromResult(InvalidNotificationInput<StoredSmtpSettings>());
        return WriteNotificationAsync(async (connection, transaction, ct) =>
        {
            if (!await IsCurrentActorAsync(connection, transaction, actor, ct, "syslog_smtp_save"))
                return NotificationAuthenticationRequired<StoredSmtpSettings>();
            var previous = await ReadSmtpSettingsAsync(connection, transaction, ct);
            if (settings.Version != previous.Version) return NotificationVersionConflict<StoredSmtpSettings>();
            // 连接目标或加密设置改变时，要求明确确认凭据，避免把旧授权码发给新服务器。
            if (previous.ProtectedPassword is not null && settings.ProtectedPassword is null &&
                (!string.Equals(previous.Host, settings.Host, StringComparison.OrdinalIgnoreCase) || previous.Port != settings.Port ||
                previous.Security != settings.Security || previous.Username != settings.Username))
                return ApiResult<StoredSmtpSettings>.Fail("smtp_password_required", "SMTP 连接设置已改变，请重新输入授权码或明确清除。");
            var password = settings.ProtectedPassword is { Length: 0 } ? null : settings.ProtectedPassword ?? previous.ProtectedPassword;
            if (password is not null && (settings.Security == "none" || settings.Username.Length == 0))
                return ApiResult<StoredSmtpSettings>.Fail("invalid_request", "使用授权码时必须设置 SMTP 用户名并启用加密连接。");
            if (settings.Version == long.MaxValue) return NotificationVersionConflict<StoredSmtpSettings>();
            var saved = settings with { Version = settings.Version + 1, ProtectedPassword = password };
            await ExecuteAsync(connection, transaction,
                """
                INSERT INTO smtp_settings(singleton,version,host,port,security,username,sender_address,sender_name,subject_prefix,protected_password,updated_by,updated_at)
                VALUES(1,$version,$host,$port,$security,$username,$address,$name,$prefix,$password,$actor,$now)
                ON CONFLICT(singleton) DO UPDATE SET version=excluded.version,host=excluded.host,port=excluded.port,security=excluded.security,
                    username=excluded.username,sender_address=excluded.sender_address,sender_name=excluded.sender_name,
                    subject_prefix=excluded.subject_prefix,protected_password=excluded.protected_password,updated_by=excluded.updated_by,updated_at=excluded.updated_at;
                """, ct, ("$version", saved.Version), ("$host", saved.Host), ("$port", saved.Port), ("$security", saved.Security),
                ("$username", saved.Username), ("$address", saved.SenderAddress), ("$name", saved.SenderName), ("$prefix", saved.SubjectPrefix),
                ("$password", password), ("$actor", actorId), ("$now", Now()));
            var auditId = await ReserveIdBlockAsync(connection, transaction, 1, ct);
            await AuditAsync(connection, transaction, auditId, actorId, "notification.smtp.configure", "smtp",
                new { version = Id(saved.Version), saved.Host, saved.Port, saved.Security, credentialUpdated = settings.ProtectedPassword is not null }, ct);
            return ApiResult<StoredSmtpSettings>.Ok(saved);
        }, NotificationMigrationRequired<StoredSmtpSettings>(), cancellationToken);
    }

    public Task<ApiResult<IReadOnlyList<StoredReportSubscription>>> ListReportSubscriptionsAsync(CancellationToken cancellationToken) =>
        ReadNotificationAsync(async (connection, transaction, ct) =>
            ApiResult<IReadOnlyList<StoredReportSubscription>>.Ok(await ReadSubscriptionsAsync(connection, transaction,
                $"SELECT {SubscriptionColumns} FROM report_subscriptions ORDER BY id;", ct)),
            NotificationMigrationRequired<IReadOnlyList<StoredReportSubscription>>(), cancellationToken);

    public Task<ApiResult<StoredReportSubscription>> SaveReportSubscriptionAsync(StoredReportSubscription subscription, long actorId,
        string token, CancellationToken cancellationToken) =>
        SaveReportSubscriptionAsync(subscription, OperationActor.Session(actorId, token), cancellationToken);

public Task<ApiResult<StoredReportSubscription>> SaveReportSubscriptionAsync(StoredReportSubscription subscription, OperationActor actor,
        CancellationToken cancellationToken)
    {
        var actorId = actor.UserId;
        if (!ValidSubscription(subscription)) return Task.FromResult(InvalidNotificationInput<StoredReportSubscription>());
        var sourcesJson = JsonSerializer.Serialize(subscription.SourceIds);
        var recipientsJson = JsonSerializer.Serialize(subscription.Recipients);
        if (recipientsJson.Length > 8192) return Task.FromResult(InvalidNotificationInput<StoredReportSubscription>());
        return WriteNotificationAsync(async (connection, transaction, ct) =>
        {
            if (!await IsCurrentActorAsync(connection, transaction, actor, ct, subscription.Id == 0 ? "syslog_report_subscription_create" : actor.Permission == "syslog_report_subscription_set_enabled" ? "syslog_report_subscription_set_enabled" : "syslog_report_subscription_update"))
                return NotificationAuthenticationRequired<StoredReportSubscription>();
            var create = subscription.Id == 0;
            if (create)
            {
                var count = Convert.ToInt64(await ScalarAsync(connection, transaction, "SELECT count(*) FROM report_subscriptions;", ct), CultureInfo.InvariantCulture);
                if (count >= MaxReportSubscriptions)
                    return ApiResult<StoredReportSubscription>.Fail("notification_subscription_limit", "报告订阅数量已达上限。");
            }
            else
            {
                var previous = (await ReadSubscriptionsAsync(connection, transaction,
                    $"SELECT {SubscriptionColumns} FROM report_subscriptions WHERE id=$id;", ct, ("$id", subscription.Id))).SingleOrDefault();
                if (previous is null) return NotificationNotFound<StoredReportSubscription>();
                if (previous.Version != subscription.Version || previous.Version == long.MaxValue) return NotificationVersionConflict<StoredReportSubscription>();
                // 独立启停只改变状态；内容编辑保留状态，不能借完整保存请求绕过另一个叶子权限。
                if (actor.IsMcp && actor.Permission == "syslog_report_subscription_set_enabled")
                    subscription = previous with { Enabled = subscription.Enabled, NextSendAt = subscription.NextSendAt };
                else if (actor.IsMcp) subscription = subscription with { Enabled = previous.Enabled };
                sourcesJson = JsonSerializer.Serialize(subscription.SourceIds);
                recipientsJson = JsonSerializer.Serialize(subscription.Recipients);
            }
            if (!await NotificationSourcesExistAsync(connection, transaction, subscription.SourceIds, ct))
                return ApiResult<StoredReportSubscription>.Fail("invalid_request", "所选设备来源不存在，请刷新后重试。");

            var firstId = await ReserveIdBlockAsync(connection, transaction, create ? 2 : 1, ct);
            var saved = subscription with { Id = create ? firstId : subscription.Id, Version = subscription.Version + 1 };
            await ExecuteAsync(connection, transaction,
                """
                INSERT INTO report_subscriptions(id,version,name,enabled,frequency,send_time,time_zone_id,source_ids_json,recipients_json,next_send_at,updated_by,updated_at)
                VALUES($id,$version,$name,$enabled,$frequency,$send,$zone,$sources,$recipients,$next,$actor,$now)
                ON CONFLICT(id) DO UPDATE SET version=excluded.version,name=excluded.name,enabled=excluded.enabled,frequency=excluded.frequency,
                    send_time=excluded.send_time,time_zone_id=excluded.time_zone_id,source_ids_json=excluded.source_ids_json,
                    recipients_json=excluded.recipients_json,next_send_at=excluded.next_send_at,updated_by=excluded.updated_by,updated_at=excluded.updated_at;
                """, ct, ("$id", saved.Id), ("$version", saved.Version), ("$name", saved.Name), ("$enabled", saved.Enabled),
                ("$frequency", saved.Frequency), ("$send", saved.SendTime), ("$zone", saved.TimeZoneId),
                ("$sources", sourcesJson), ("$recipients", recipientsJson),
                ("$next", saved.NextSendAt.ToUnixTimeMilliseconds()), ("$actor", actorId), ("$now", Now()));
            if (!create)
                await ExecuteAsync(connection, transaction,
                    "UPDATE mail_deliveries SET status='failed',failure_code='report_subscription_changed',updated_at=$now WHERE subscription_id=$id AND status='queued';",
                    ct, ("$now", Now()), ("$id", saved.Id));
            await AuditAsync(connection, transaction, create ? firstId + 1 : firstId, actorId,
                create ? "notification.report.create" : "notification.report.update", Id(saved.Id),
                new { version = Id(saved.Version), saved.Enabled, saved.Frequency, saved.SendTime, saved.SourceIds, recipientCount = saved.Recipients.Length }, ct);
            return ApiResult<StoredReportSubscription>.Ok(saved);
        }, NotificationMigrationRequired<StoredReportSubscription>(), cancellationToken);
    }

    public Task<IReadOnlyList<StoredReportSubscription>> ListDueReportSubscriptionsAsync(DateTimeOffset now, int limit,
        CancellationToken cancellationToken) =>
        ReadNotificationAsync((connection, transaction, ct) => ReadSubscriptionsAsync(connection, transaction,
            $"SELECT {SubscriptionColumns} FROM report_subscriptions WHERE enabled=1 AND next_send_at<=$now ORDER BY next_send_at,id LIMIT $limit;",
            ct, ("$now", now.ToUnixTimeMilliseconds()), ("$limit", Math.Clamp(limit, 1, MaxReportSubscriptions))), [], cancellationToken);

    private static async Task<IReadOnlyList<StoredReportSubscription>> ReadSubscriptionsAsync(SqliteConnection connection,
        SqliteTransaction transaction, string sql, CancellationToken ct, params (string Name, object? Value)[] parameters)
    {
        await using var command = Command(connection, transaction, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var subscriptions = new List<StoredReportSubscription>();
        while (await reader.ReadAsync(ct)) subscriptions.Add(new(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2),
            reader.GetBoolean(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), ReadStrings(reader, 7),
            ReadStrings(reader, 8), ReadTimestamp(reader, 9)));
        return subscriptions;
    }

    private static async Task<StoredSmtpSettings> ReadSmtpSettingsAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        await using var command = Command(connection, transaction,
            "SELECT version,host,port,security,username,sender_address,sender_name,subject_prefix,protected_password FROM smtp_settings WHERE singleton=1;");
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new(reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3), reader.GetString(4), reader.GetString(5),
                reader.GetString(6), reader.GetString(7), ReadNullableString(reader, 8))
            : new(0, "", 465, "ssl", "", "", "通研 Syslog", "[通研 Syslog]", null);
    }

    private static async Task<bool> NotificationSourcesExistAsync(SqliteConnection connection, SqliteTransaction transaction, string[] sourceIds, CancellationToken ct)
    {
        if (sourceIds.Length == 0) return true;
        var count = await ScalarAsync(connection, transaction,
            "SELECT count(*) FROM sources WHERE id IN (SELECT CAST(value AS INTEGER) FROM json_each($sources));", ct,
            ("$sources", JsonSerializer.Serialize(sourceIds)));
        return Convert.ToInt64(count, CultureInfo.InvariantCulture) == sourceIds.Length;
    }

    private static bool ValidSmtpSettings(StoredSmtpSettings input) => input.Version >= 0 &&
        ValidText(input.Host, 253, false) && Uri.CheckHostName(input.Host) != UriHostNameType.Unknown && input.Port is >= 1 and <= 65535 &&
        input.Security is "ssl" or "starttls" or "none" && ValidText(input.Username, 254) && ValidMailbox(input.SenderAddress) &&
        ValidText(input.SenderName, 128) && ValidText(input.SubjectPrefix, 80) && (input.ProtectedPassword?.Length ?? 0) <= 8192;

    private static bool ValidSubscription(StoredReportSubscription input) => input.Id >= 0 && input.Version >= 0 &&
        (input.Id == 0) == (input.Version == 0) && ValidText(input.Name, 128, false) && input.Frequency is "daily" or "weekly" or "monthly" &&
        TimeOnly.TryParseExact(input.SendTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) &&
        input.TimeZoneId == "Asia/Shanghai" && input.SourceIds is { Length: <= MaxReportSources } && input.SourceIds.All(ValidSourceId) &&
        input.SourceIds.Distinct(StringComparer.Ordinal).Count() == input.SourceIds.Length &&
        input.Recipients is { Length: >= 1 and <= MaxReportRecipients } && input.Recipients.All(ValidMailbox) &&
        input.Recipients.Distinct(StringComparer.OrdinalIgnoreCase).Count() == input.Recipients.Length;

    private static bool ValidSourceId(string value) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 && Id(id) == value;
    private static bool ValidMailbox(string? value) => ValidText(value, 254, false) &&
        MailAddress.TryCreate(value, out var mailbox) && string.Equals(mailbox.Address, value, StringComparison.Ordinal);
    private static bool ValidText(string? value, int limit, bool allowEmpty = true) => value is not null &&
        value.Length <= limit && (allowEmpty || !string.IsNullOrWhiteSpace(value)) && !value.Any(char.IsControl);

    private async Task<T> ReadNotificationAsync<T>(Func<SqliteConnection, SqliteTransaction, CancellationToken, Task<T>> read,
        T migrationFallback, CancellationToken ct)
    {
        await using var connection = await OpenAsync(false, ct);
        using var transaction = connection.BeginTransaction(deferred: true);
        return await SupportsNotificationsAsync(connection, transaction, ct) ? await read(connection, transaction, ct) : migrationFallback;
    }

    private async Task<T> WriteNotificationAsync<T>(Func<SqliteConnection, SqliteTransaction, CancellationToken, Task<T>> write,
        T migrationFallback, CancellationToken ct)
    {
        await _writeGate.WaitAsync(ct);
        try
        {
            await using var connection = await OpenAsync(false, ct);
            using var transaction = connection.BeginTransaction();
            if (!await SupportsNotificationsAsync(connection, transaction, ct)) return migrationFallback;
            var result = await write(connection, transaction, ct);
            transaction.Commit();
            return result;
        }
        finally { _writeGate.Release(); }
    }

    private static string[] ReadStrings(SqliteDataReader reader, int index) =>
        JsonSerializer.Deserialize<string[]>(reader.GetString(index)) ?? throw new InvalidOperationException("通知配置的数据无效。");
    private static string? ReadNullableString(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
    private static DateTimeOffset ReadTimestamp(SqliteDataReader reader, int index) => DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(index));
    private static DateTimeOffset? ReadNullableTimestamp(SqliteDataReader reader, int index) => reader.IsDBNull(index) ? null : ReadTimestamp(reader, index);
    private static ApiResult<T> NotificationMigrationRequired<T>() => ApiResult<T>.Fail("notification_migration_required", "请先执行通知设置显式迁移。");
    private static ApiResult<T> NotificationAuthenticationRequired<T>() => ApiResult<T>.Fail("authentication_required", "管理员会话已失效。");
    private static ApiResult<T> NotificationVersionConflict<T>() => ApiResult<T>.Fail("version_conflict", "通知配置已被修改，请刷新后重试。");
    private static ApiResult<T> InvalidNotificationInput<T>() => ApiResult<T>.Fail("invalid_request", "通知设置参数无效。");
    private static ApiResult<T> NotificationNotFound<T>() => ApiResult<T>.Fail("not_found", "通知记录不存在。");
}
