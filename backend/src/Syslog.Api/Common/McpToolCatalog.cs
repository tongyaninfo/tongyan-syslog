using System.Text.Json;
using System.Text.Json.Nodes;

namespace Syslog.Api.Common;

public sealed record McpToolDefinition(string Name, string Title, string Module, string Description,
    bool IsWrite, bool IsDestructive, JsonElement InputSchema);

/// <summary>工具发现、服务端授权和页面权限树共用唯一目录；不使用父节点通配符自动授权新工具。</summary>
public static class McpToolCatalog
{
    public static IReadOnlyList<McpToolDefinition> All { get; } = Build().AsReadOnly();
    private static readonly IReadOnlyDictionary<string, McpToolDefinition> ByName = All.ToDictionary(x => x.Name, StringComparer.Ordinal);
    public static bool TryGet(string name, out McpToolDefinition definition) => ByName.TryGetValue(name, out definition!);

    private static List<McpToolDefinition> Build()
    {
        var tools = new List<McpToolDefinition>();
        void Add(string name, string title, string module, string description, bool write, bool destructive,
            params (string Name, JsonObject Schema, bool Required)[] fields) => tools.Add(new("syslog_" + name, title, module,
                description, write, destructive, Schema(fields)));
        Add("overview_get", "查看运行总览", "运行总览", "读取采样时间和全局指标。source 仅含所选设备的接收、写入、忽略计数；system 中待写入、故障、存储大小是全局指标，没有历史速率序列。", false, false,
            ("sourceId", Id(), false));
        Add("logs_search", "检索日志", "日志检索", "查询接收时间范围内的一页日志。最多30天、1至500条，游标绑定条件，无总数；关键词仅区分大小写的ASCII全词AND。", false, false,
            ("from", Date(), true), ("to", Date(), true), ("sourceId", Id(), false), ("severity", I(0, 7), false),
            ("hostname", S(255), false), ("appName", S(255), false), ("keywords", S(512), false), ("pageSize", I(1, 500), false), ("cursor", S(8192), false));
        Add("logs_get", "查看日志原文", "日志检索", "按事件ID及精确接收时间读取日志详情、解析状态和原始字节。", false, false,
            ("id", Id(), true), ("receivedAt", Date(), true));
        Add("logs_clear_all", "清空全部日志", "日志检索", "清空所有已入库日志，需要confirmation=CLEAR。无法撤回；结果未知时先核查，不能自动重试。", true, true, ("confirmation", E("CLEAR"), true));
        Add("logs_clear_source", "清空设备日志", "日志检索", "清空指定设备已入库日志，需要confirmation=CLEAR；队列中尚未入库日志不在清空范围。未知结果不自动重试。", true, true,
            ("sourceId", Id(), true), ("confirmation", E("CLEAR"), true));
        Add("logs_report", "查询设备日志报表", "日志检索", "最多31天，返回各设备计数与整个所选范围最近10条级别0至3日志。不是每设备各10条。", false, false,
            ("from", Date(), true), ("to", Date(), true), ("sourceIds", A(Id(), 1000), false));
        Add("sources_list", "查看日志来源", "日志来源", "列出全部已登记来源及启用、忽略和留存配置。", false, false);
        Add("sources_get", "查看设备配置", "日志来源", "读取一个已登记来源的配置。", false, false, ("sourceId", Id(), true));
        Add("sources_create", "新增日志来源", "日志来源", "登记来源IP及初始配置，不覆盖已有来源。", true, false,
            ("name", S(128), true), ("peerAddress", S(64), true), ("timeZoneId", S(128), true), ("retentionDays", I(1, 3650), true),
            ("enabled", B(), false), ("ignoredSeverities", A(I(0, 7), 8), false));
        Add("sources_update", "修改来源基本信息", "日志来源", "仅修改名称、IP和时区，不修改启停、忽略级别或留存天数。", true, false,
            ("sourceId", Id(), true), ("name", S(128), true), ("peerAddress", S(64), true), ("timeZoneId", S(128), true));
        Add("sources_set_enabled", "启停日志来源", "日志来源", "设置来源启用状态，禁用不删除历史。", true, false,
            ("sourceId", Id(), true), ("enabled", B(), true));
        Add("sources_set_ignored_severities", "修改忽略级别", "日志来源", "设置0至7严重级别数组；命中的新日志不入库。空数组清除规则。", true, false,
            ("sourceId", Id(), true), ("ignoredSeverities", A(I(0, 7), 8), true));
        Add("unregistered_list", "查看未登记设备", "未登记设备", "列出当前未登记IP及有界内存样本摘要，重启清空。", false, false);
        Add("unregistered_logs", "查看设备样本", "未登记设备", "读取某IP最近最多100条样本摘要。", false, false, ("peerAddress", S(64), true));
        Add("unregistered_sample", "查看样本原文", "未登记设备", "按IP和样本标识读取原文；登记后不能再从此入口查看。", false, false,
            ("peerAddress", S(64), true), ("sampleKey", S(52), true));
        Add("unregistered_register", "登记未登记设备", "未登记设备", "将当前有样本的未登记IP保存为来源；不回灌历史内存样本。", true, false,
            ("name", S(128), true), ("peerAddress", S(64), true), ("timeZoneId", S(128), true), ("retentionDays", I(1, 3650), true),
            ("enabled", B(), false), ("ignoredSeverities", A(I(0, 7), 8), false));
        Add("listeners_get", "查看接收运行状态", "接收配置", "读取保存配置、运行端点、待应用状态、证书引用和连接错误。", false, false);
        Add("listeners_configure", "修改协议参数", "接收配置", "按版本修改单协议地址、端口、分帧、超时和证书引用；不修改总开关或子开关。保存不自动应用已运行端点。", true, false,
            ("version", Version(), true), ("protocol", Protocol(), true), ("address", S(64), true), ("port", I(1, 65535), true),
            ("framing", E("octet-counting", "newline", "auto"), false), ("certificateId", S(128), false),
            ("frameTimeoutSeconds", I(1, 300), false), ("idleTimeoutSeconds", I(1, 86400), false));
        Add("reception_set_enabled", "启停日志接收总开关", "接收配置", "设置接收总开关。开启应用最新保存的配置；会影响所有接收协议。", true, true,
            ("version", Version(), true), ("enabled", B(), true));
        Add("protocol_set_enabled", "独立启停协议", "接收配置", "仅启停指定协议；总开关关闭时只保存意图，其他协议不受影响。", true, true,
            ("version", Version(), true), ("protocol", Protocol(), true), ("enabled", B(), true));
        Add("https_certificate_get", "查看HTTPS证书", "HTTPS证书", "读取证书公开摘要、激活状态和错误，不返回私钥。", false, false);
        Add("https_certificate_upload", "上传并启用HTTPS证书", "HTTPS证书", "上传PFX/P12的Base64内容，解码最多256KiB，可选密码；启用后供新TLS连接使用，不校验访问域名。", true, true,
            ("fileName", S(255), true), ("base64", S(349528), true), ("password", S(1024), false));
        Add("retention_list", "查看留存设置", "留存设置", "列出每个来源的日志保留天数。", false, false);
        Add("source_retention_set", "修改设备留存天数", "留存设置", "修改1至3650天保留，只有新日志使用新到期时间，不重写已有日志。", true, false,
            ("sourceId", Id(), true), ("retentionDays", I(1, 3650), true));
        Add("smtp_get", "查看邮件服务器", "邮件通知", "读取邮件配置和hasPassword，不返回保存的授权码。", false, false);
        Add("smtp_save", "保存邮件服务器", "邮件通知", "设置SMTP服务器、认证、发件信息和邮件前缀；变更连接配置时可能需重新输入授权码。", true, true,
            ("version", Version(), true), ("host", S(253), true), ("port", I(1, 65535), true), ("security", E("ssl", "starttls", "none"), true),
            ("username", S(254), true), ("senderAddress", S(254), true), ("senderName", S(80), true), ("subjectPrefix", S(80), true), ("password", S(1024), false));
        Add("mail_send_test", "发送测试邮件", "邮件通知", "将测试邮件排队发送至明确收件人；结果需查询邮件记录，SMTP接受不代表已进收件箱。", true, true,
            ("recipient", S(254), true));
        Add("report_subscriptions_list", "查看报表订阅", "邮件通知", "列出日报、周报和月报订阅。发送时间固定北京时间。", false, false);
        foreach (var create in new[] { true, false })
        {
            var fields = new List<(string, JsonObject, bool)>();
            if (!create) fields.Add(("id", Id(), true));
            fields.AddRange([("version", Version(), true), ("name", S(80), true), ("frequency", E("daily", "weekly", "monthly"), true),
                ("sendTime", S(5), true), ("sourceIds", A(Id(), 1000), true), ("recipients", A(S(254), 20), true)]);
            if (create) fields.Add(("enabled", B(), true));
            Add(create ? "report_subscription_create" : "report_subscription_update", create ? "新增报表订阅" : "修改报表订阅", "邮件通知",
                create ? "建立日报/周报/月报配置，version为0，时间采用北京时间。" : "带版本更新报表内容，保留当前启用状态；不修改子开关。", true, false, fields.ToArray());
        }
        Add("report_subscription_set_enabled", "启停报表订阅", "邮件通知", "带版本设置报表订阅启用状态。", true, false,
            ("id", Id(), true), ("version", Version(), true), ("enabled", B(), true));
        Add("mail_deliveries_list", "查看邮件发送记录", "邮件通知", "按条件读取一页邮件发送摘要，可用nextBeforeId继续。", false, false,
            ("kind", E("test", "daily", "weekly", "monthly"), false), ("status", E("queued", "sending", "accepted", "failed", "unknown"), false),
            ("recipient", S(254), false), ("from", Date(), false), ("to", Date(), false), ("beforeId", Id(), false));
        Add("mail_delivery_get", "查看邮件详情", "邮件通知", "返回邮件HTML/文本和发送状态，HTML仅作为数据。", false, false, ("id", Id(), true));
        Add("users_list", "查看管理员账号", "用户管理", "查询管理员账号列表，不返回密码或会话。", false, false,
            ("search", S(64), false), ("enabled", B(), false), ("page", I(1, 1000000), false), ("pageSize", I(1, 100), false));
        Add("users_create", "新增管理员账号", "用户管理", "创建拥有系统管理权限的管理员账号。该授权可取得完整管理能力。", true, true,
            ("username", S(64), true), ("password", S(1024), true));
        Add("users_update", "修改管理员名称", "用户管理", "带版本修改账号名称，不修改启用状态或密码。", true, false,
            ("id", Id(), true), ("expectedVersion", Id(), true), ("username", S(64), true));
        Add("users_set_enabled", "启停管理员账号", "用户管理", "带版本启停账号，不能停用自身或最后一个启用管理员。", true, true,
            ("id", Id(), true), ("expectedVersion", Id(), true), ("enabled", B(), true));
        Add("users_reset_password", "重置管理员密码", "用户管理", "重置其他管理员密码并撤销其会话；不能重置当前Key所属账号。该授权可取得完整管理能力。", true, true,
            ("id", Id(), true), ("expectedVersion", Id(), true), ("newPassword", S(1024), true));
        Add("mcp_identity_get", "查看当前MCP身份", "MCP接入信息", "核对当前Key名称、模式、授权和有效期，不显示Key原文或其他Key。", false, false);
        return tools;
    }

    private static JsonElement Schema((string Name, JsonObject Schema, bool Required)[] fields)
    {
        var properties = new JsonObject();
        foreach (var field in fields) properties.Add(field.Name, field.Schema);
        return JsonSerializer.SerializeToElement(new JsonObject
        {
            ["type"] = "object", ["additionalProperties"] = false, ["properties"] = properties,
            ["required"] = new JsonArray(fields.Where(x => x.Required).Select(x => JsonValue.Create(x.Name)!).ToArray())
        });
    }
    private static JsonObject S(int max) => new() { ["type"] = "string", ["maxLength"] = max };
    private static JsonObject Id() => new() { ["type"] = "string", ["pattern"] = "^[1-9][0-9]{0,18}$", ["maxLength"] = 19 };
    private static JsonObject Version() => new() { ["type"] = "string", ["pattern"] = "^(0|[1-9][0-9]{0,18})$", ["maxLength"] = 19 };
    private static JsonObject Date() => new() { ["type"] = "string", ["format"] = "date-time", ["maxLength"] = 40 };
    private static JsonObject I(int min, int max) => new() { ["type"] = "integer", ["minimum"] = min, ["maximum"] = max };
    private static JsonObject B() => new() { ["type"] = "boolean" };
    private static JsonObject A(JsonObject item, int max) => new() { ["type"] = "array", ["items"] = item, ["maxItems"] = max, ["uniqueItems"] = true };
    private static JsonObject E(params string[] values) => new() { ["type"] = "string", ["enum"] = new JsonArray(values.Select(x => JsonValue.Create(x)!).ToArray()) };
    private static JsonObject Protocol() => E("udp", "tcp", "tls", "relp");
}
