using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Syslog.Api.Common;
using Syslog.Api.Models;
using Syslog.Ingest.Service.impl;
using Syslog.Shared.Models;
using Syslog.Shared.Service;

namespace Syslog.Api.Service.impl;

/// <summary>仅负责协议参数到既有业务服务的适配。权限先检查、写事务再复核；不模拟浏览器会话。</summary>
public sealed class McpToolExecutor(IMcpStore mcp, ControlStore control, ILogStore logs, OverviewFeed overview,
    UnregisteredDeviceQueries unregistered, ListenerConfigurationService listeners, NotificationService notifications,
    SystemCertificateStore certificates, HttpsCertificateUploadService uploads, LogClearService clearing)
{
    public async Task<object> ExecuteAsync(string name, JsonElement arguments, McpPrincipal principal, CancellationToken ct)
    {
        if (!McpToolCatalog.TryGet(name, out var tool)) return Fail("mcp_tool_not_found", "工具不存在。");
        var authorization = await mcp.ValidateMcpPermissionAsync(principal, name, ct);
        if (authorization.Data is not { } current) return Fail(authorization.Code, authorization.Message);
        if (!McpArgumentValidation.IsValid(arguments, tool.InputSchema)) return Fail("invalid_request", "工具参数缺失、类型无效或包含未定义字段，请按输入合同重试。");
        var actor = OperationActor.Mcp(current.UserId, current.KeyId, name);
        try { return await ExecuteCoreAsync(name, arguments, current, actor, ct); }
        catch (ClickHouseStoreException error) { return Fail(error.Code, error.Message); }
        catch (Exception error) when (error is JsonException or FormatException or OverflowException or ArgumentException)
        { return Fail("invalid_request", "工具参数无效，请检查标识、时间范围和字段内容。"); }
    }

    private async Task<object> ExecuteCoreAsync(string name, JsonElement a, McpPrincipal principal, OperationActor actor, CancellationToken ct)
    {
        switch (name)
        {
            case "syslog_overview_get":
                var sourceId = Optional(a, "sourceId");
                if (sourceId is not null && !(await control.ListAsync(ct)).Any(item => item.Id == sourceId)) return Fail("source_not_found", "日志来源不存在。");
                var snapshot = await overview.ReadSnapshotAsync(sourceId, ct);
                return Ok(new { system = snapshot with { SourceTraffic = null }, source = snapshot.SourceTraffic });
            case "syslog_logs_search": return await SearchAsync(a, ct);
            case "syslog_logs_get":
                var scope = (await control.ListAsync(ct)).Select(item => item.Id).ToArray();
                var found = scope.Length == 0 ? null : await logs.GetAsync(Number(a, "id"), Time(a, "receivedAt"), scope, ct);
                return found is null ? Fail("not_found", "日志不存在或已过期。") : Ok(found);
            case "syslog_logs_report": return await ReportAsync(a, ct);
            case "syslog_logs_clear_all": return await clearing.ClearAsync(Text(a, "confirmation"), actor, ct);
            case "syslog_logs_clear_source": return await clearing.ClearSourceAsync(Text(a, "sourceId"), Text(a, "confirmation"), actor, ct);
            case "syslog_sources_list": return Ok(await control.ListAsync(ct));
            case "syslog_sources_get":
                var source = (await control.ListAsync(ct)).SingleOrDefault(item => item.Id == Text(a, "sourceId"));
                return source is null ? Fail("source_not_found", "日志来源不存在。") : Ok(source);
            case "syslog_sources_create": return await control.CreateSourceAsync(Source(a), actor, ct);
            case "syslog_sources_update": return await control.PatchSourceAsync(Text(a, "sourceId"), new(Text(a, "name"), Text(a, "peerAddress"), Text(a, "timeZoneId")), actor, ct);
            case "syslog_sources_set_enabled": return await control.PatchSourceAsync(Text(a, "sourceId"), new(Enabled: Bool(a, "enabled")), actor, ct);
            case "syslog_sources_set_ignored_severities": return await control.PatchSourceAsync(Text(a, "sourceId"), new(IgnoredSeverities: Ints(a, "ignoredSeverities")), actor, ct);
            case "syslog_source_retention_set": return await control.PatchSourceAsync(Text(a, "sourceId"), new(RetentionDays: Int(a, "retentionDays")), actor, ct);
            case "syslog_retention_list": return Ok((await control.ListAsync(ct)).Select(item => new { item.Id, item.Name, item.PeerAddress, item.RetentionDays }).ToArray());
            case "syslog_unregistered_list": return await unregistered.ListAsync(ct);
            case "syslog_unregistered_logs": return await unregistered.LogsAsync(Text(a, "peerAddress"), ct);
            case "syslog_unregistered_sample": return await unregistered.SampleAsync(Text(a, "peerAddress"), Text(a, "sampleKey"), ct);
            case "syslog_unregistered_register":
                var samples = await unregistered.LogsAsync(Text(a, "peerAddress"), ct);
                if (!samples.Success) return Fail(samples.Code, samples.Message);
                return await control.CreateSourceAsync(Source(a), actor, ct);
            case "syslog_listeners_get": return await listeners.ReadAsync(ct);
            case "syslog_listeners_configure": return await ConfigureListenerAsync(a, actor, ct);
            case "syslog_reception_set_enabled": return await listeners.SetReceptionAsync(Bool(a, "enabled"), actor, ct, Text(a, "version"));
            case "syslog_protocol_set_enabled": return await listeners.SetProtocolReceptionAsync(Text(a, "protocol"), new(Bool(a, "enabled"), Text(a, "version")), actor, ct);
            case "syslog_https_certificate_get": return Ok(certificates.GetView());
            case "syslog_https_certificate_upload": return await UploadAsync(a, actor, ct);
            case "syslog_smtp_get": return await notifications.ReadSmtpAsync(ct);
            case "syslog_smtp_save": return await notifications.SaveSmtpAsync(new(Number(a, "version"), Text(a, "host"), Int(a, "port"), Text(a, "security"),
                Text(a, "username"), Text(a, "senderAddress"), Text(a, "senderName"), Text(a, "subjectPrefix"), Optional(a, "password")), actor, ct);
            case "syslog_mail_send_test": return await notifications.EnqueueTestAsync(new(Text(a, "recipient")), actor, ct);
            case "syslog_report_subscriptions_list": return await notifications.ListSubscriptionsAsync(ct);
            case "syslog_report_subscription_create": return await notifications.SaveSubscriptionAsync(0, Subscription(a, Bool(a, "enabled")), actor, ct);
            case "syslog_report_subscription_update":
            case "syslog_report_subscription_set_enabled": return await UpdateSubscriptionAsync(name, a, actor, ct);
            case "syslog_mail_deliveries_list": return await notifications.ListDeliveriesAsync(Optional(a, "kind"), Optional(a, "status"), Optional(a, "recipient"),
                OptionalTime(a, "from"), OptionalTime(a, "to"), Optional(a, "beforeId"), ct);
            case "syslog_mail_delivery_get": return await notifications.GetDeliveryAsync(Number(a, "id"), ct);
            case "syslog_users_list": return await control.ListUsersAsync(new(Optional(a, "search"), OptionalBool(a, "enabled"), Int(a, "page", 1), Int(a, "pageSize", 20)), actor, ct);
            case "syslog_users_create": return await control.CreateUserAsync(new(Text(a, "username"), Text(a, "password")), actor, ct);
            case "syslog_users_update": return await control.UpdateUserAsync(Text(a, "id"), new(Text(a, "username"), true, Text(a, "expectedVersion")), actor, ct);
            case "syslog_users_set_enabled": return await control.UpdateUserAsync(Text(a, "id"), new("", Bool(a, "enabled"), Text(a, "expectedVersion")), actor, ct);
            case "syslog_users_reset_password": return await control.ResetUserPasswordAsync(Text(a, "id"), new(Text(a, "newPassword"), Text(a, "expectedVersion")), actor, ct);
            case "syslog_mcp_identity_get": return Ok(new
            {
                keyId = principal.KeyId.ToString(CultureInfo.InvariantCulture), principal.Name, principal.AccessMode,
                principal.Permissions, principal.ExpiresAt, serverName = "通研 Syslog", serverVersion = "0.1.0"
            });
            default: return Fail("mcp_tool_not_found", "工具不存在。");
        }
    }

    private async Task<object> SearchAsync(JsonElement a, CancellationToken ct)
    {
        var query = new LogSearch
        {
            From = Time(a, "from"), To = Time(a, "to"), SourceId = Optional(a, "sourceId"),
            Severity = a.TryGetProperty("severity", out var severity) ? severity.GetInt32() : null,
            Hostname = Optional(a, "hostname"), AppName = Optional(a, "appName"), Keywords = Optional(a, "keywords"),
            PageSize = Int(a, "pageSize", 100), Cursor = Optional(a, "cursor")
        };
        if (!ValidRange(query.From, query.To, 30)) return Fail("invalid_query", "查询范围须为30天以内且使用毫秒精度。");
        if (!string.IsNullOrWhiteSpace(query.Keywords) && (query.Keywords.Any(c => c > 127) || !query.Keywords.Any(char.IsAsciiLetterOrDigit)))
            return Fail("unsupported_search_token", "关键词仅支持区分大小写的 ASCII 全词 AND。");
        var scope = (await control.ListAsync(ct)).Select(item => item.Id).ToArray();
        if (query.SourceId is not null && !scope.Contains(query.SourceId, StringComparer.Ordinal)) return Fail("source_forbidden", "日志来源不存在或不可访问。");
        return Ok(scope.Length == 0 ? new LogSearchPage([], null, false) : await logs.SearchAsync(query, scope, ct));
    }

    private async Task<object> ReportAsync(JsonElement a, CancellationToken ct)
    {
        var from = Time(a, "from"); var to = Time(a, "to");
        if (!ValidRange(from, to, 31)) return Fail("invalid_query", "报表范围须为31天以内且使用毫秒精度。");
        var available = (await control.ListAsync(ct)).Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var selected = a.TryGetProperty("sourceIds", out _) ? Strings(a, "sourceIds") : [];
        if (selected.Any(id => !available.Contains(id))) return Fail("source_forbidden", "部分设备来源不存在或不可访问。");
        var scope = selected.Length == 0 ? available.ToArray() : selected;
        var report = scope.Length == 0 ? new LogReport([], []) : await logs.GetReportAsync(from, to, scope, ct);
        // 内部报表模型使用 long，MCP 输出显式转字符串，不能让 JavaScript 丢失事件 ID 精度。
        return Ok(new { report.Devices, recentEntries = report.RecentEntries.Select(item => new
            { eventId = item.EventId.ToString(CultureInfo.InvariantCulture), item.SourceId, item.ReceivedAt, item.Severity, item.Summary }).ToArray() });
    }

    private async Task<object> ConfigureListenerAsync(JsonElement a, OperationActor actor, CancellationToken ct)
    {
        var result = await listeners.ReadAsync(ct);
        if (result.Data is not { } current) return Fail(result.Code, result.Message);
        if (Text(a, "version") != current.Saved.Version) return Fail("version_conflict", "监听配置已改变，请重新读取。");
        var protocol = Text(a, "protocol");
        var previous = current.Saved.Configuration.Listeners.Single(item => item.Protocol == protocol);
        var changed = previous with
        {
            Address = Text(a, "address"), Port = Int(a, "port"), Framing = Optional(a, "framing") ?? previous.Framing,
            CertificateId = Optional(a, "certificateId") ?? previous.CertificateId,
            FrameTimeoutSeconds = Int(a, "frameTimeoutSeconds", previous.FrameTimeoutSeconds),
            IdleTimeoutSeconds = Int(a, "idleTimeoutSeconds", previous.IdleTimeoutSeconds)
        };
        return await listeners.SaveAsync(new(current.Saved.Version, current.Saved.Configuration with
            { Listeners = current.Saved.Configuration.Listeners.Select(item => item.Protocol == protocol ? changed : item).ToArray() }), actor, ct);
    }

    private async Task<object> UpdateSubscriptionAsync(string name, JsonElement a, OperationActor actor, CancellationToken ct)
    {
        var list = await notifications.ListSubscriptionsAsync(ct);
        if (list.Data is null) return Fail(list.Code, list.Message);
        var previous = list.Data.SingleOrDefault(item => item.Id == Text(a, "id"));
        if (previous is null) return Fail("not_found", "报表订阅不存在。");
        var request = name == "syslog_report_subscription_set_enabled"
            ? new SaveReportSubscriptionRequest(Number(a, "version"), previous.Name, Bool(a, "enabled"), previous.Frequency,
                previous.SendTime, previous.SourceIds, previous.Recipients)
            : Subscription(a, previous.Enabled);
        return await notifications.SaveSubscriptionAsync(Number(a, "id"), request, actor, ct);
    }

    private async Task<object> UploadAsync(JsonElement a, OperationActor actor, CancellationToken ct)
    {
        if (Path.GetExtension(Text(a, "fileName")).ToLowerInvariant() is not (".pfx" or ".p12"))
            return Fail("https_certificate_invalid", "请选择 PFX 或 P12 证书。");
        var bytes = Convert.FromBase64String(Text(a, "base64"));
        try { return await uploads.UploadAsync(bytes, Optional(a, "password"), actor, ct); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static bool ValidRange(DateTimeOffset from, DateTimeOffset to, int days) => from < to && to - from <= TimeSpan.FromDays(days)
        && from.Ticks % TimeSpan.TicksPerMillisecond == 0 && to.Ticks % TimeSpan.TicksPerMillisecond == 0;
    private static SourceInput Source(JsonElement a) => new(Text(a, "name"), Text(a, "peerAddress"), Text(a, "timeZoneId"),
        Int(a, "retentionDays"), OptionalBool(a, "enabled") ?? true, a.TryGetProperty("ignoredSeverities", out _) ? Ints(a, "ignoredSeverities") : null);
    private static SaveReportSubscriptionRequest Subscription(JsonElement a, bool enabled) => new(Number(a, "version"), Text(a, "name"), enabled,
        Text(a, "frequency"), Text(a, "sendTime"), Strings(a, "sourceIds"), Strings(a, "recipients"));
    private static string Text(JsonElement a, string name) => a.GetProperty(name).GetString()!;
    private static string? Optional(JsonElement a, string name) => a.TryGetProperty(name, out var value) ? value.GetString() : null;
    private static long Number(JsonElement a, string name) => long.Parse(Text(a, name), NumberStyles.None, CultureInfo.InvariantCulture);
    private static int Int(JsonElement a, string name, int fallback = 0) => a.TryGetProperty(name, out var value) ? value.GetInt32() : fallback;
    private static bool Bool(JsonElement a, string name) => a.GetProperty(name).GetBoolean();
    private static bool? OptionalBool(JsonElement a, string name) => a.TryGetProperty(name, out var value) ? value.GetBoolean() : null;
    private static DateTimeOffset Time(JsonElement a, string name) => DateTimeOffset.Parse(Text(a, name), CultureInfo.InvariantCulture, DateTimeStyles.None);
    private static DateTimeOffset? OptionalTime(JsonElement a, string name) => a.TryGetProperty(name, out _) ? Time(a, name) : null;
    private static string[] Strings(JsonElement a, string name) => a.GetProperty(name).EnumerateArray().Select(item => item.GetString()!).ToArray();
    private static int[] Ints(JsonElement a, string name) => a.GetProperty(name).EnumerateArray().Select(item => item.GetInt32()).ToArray();
    private static object Ok<T>(T data) => ApiResult<T>.Ok(data);
    private static object Fail(string code, string message) => ApiResult<object>.Fail(code, message);
}
