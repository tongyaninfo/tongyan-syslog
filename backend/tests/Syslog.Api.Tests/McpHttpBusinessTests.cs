using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Syslog.Api.Common;
using Syslog.Ingest.Service;
using Xunit;

namespace Syslog.Api.Tests;

public sealed partial class McpHttpTests
{
    [Fact]
    public async Task AllBusinessToolsUseExistingServicesAndPreserveTheirBoundaries()
    {
        // 全部数据为独立内存 SQLite、合成日志存储及测试证书；Harness 禁用发信 worker。
        await using var app = await Harness.StartAsync(permissions: McpToolCatalog.All.Select(tool => tool.Name).ToArray(), mode: "read_write");
        var exercised = new HashSet<string>(StringComparer.Ordinal);
        async Task<JsonElement> Call(string name, object? args = null)
        {
            exercised.Add(name);
            return await BusinessCallAsync(app, name, args ?? new { });
        }

        var identity = await Call("syslog_mcp_identity_get");
        Assert.Equal(39, identity.GetProperty("permissions").GetArrayLength());
        var overview = await Call("syslog_overview_get");
        Assert.True(overview.TryGetProperty("system", out _));
        var created = await Call("syslog_sources_create", new { name = "switch", peerAddress = "192.0.2.40", timeZoneId = "UTC", retentionDays = 365, enabled = false });
        var sourceId = created.GetProperty("id").GetString()!;
        Assert.Single((await Call("syslog_sources_list")).EnumerateArray());
        Assert.Equal("switch", (await Call("syslog_sources_get", new { sourceId })).GetProperty("name").GetString());
        await Call("syslog_sources_update", new { sourceId, name = "renamed", peerAddress = "192.0.2.41", timeZoneId = "Asia/Shanghai" });
        await Call("syslog_sources_set_enabled", new { sourceId, enabled = true });
        await Call("syslog_sources_set_ignored_severities", new { sourceId, ignoredSeverities = new[] { 6, 7 } });
        await Call("syslog_source_retention_set", new { sourceId, retentionDays = 30 });
        Assert.Equal(30, (await Call("syslog_retention_list")).EnumerateArray().Single().GetProperty("retentionDays").GetInt32());

        const string from = "2026-11-10T00:00:00Z", to = "2026-11-11T00:00:00Z";
        await Call("syslog_logs_search", new { from, to, sourceId, pageSize = 10 });
        var entry = await Call("syslog_logs_get", new { id = "9007199254740993", receivedAt = from });
        Assert.Equal(JsonValueKind.String, entry.GetProperty("eventId").ValueKind);
        var report = await Call("syslog_logs_report", new { from, to, sourceIds = new[] { sourceId } });
        Assert.Equal("9007199254740993", report.GetProperty("recentEntries").EnumerateArray().Single().GetProperty("eventId").GetString());
        await Call("syslog_logs_clear_source", new { sourceId, confirmation = "CLEAR" });
        await Call("syslog_logs_clear_all", new { confirmation = "CLEAR" });

        var buffer = app.Services.GetRequiredService<IUnregisteredDeviceBuffer>();
        buffer.SetReceptionEnabled(true);
        Assert.True(buffer.TryCapture(Encoding.UTF8.GetBytes("<34>1 2026-11-10T08:00:00Z synthetic app - - - sample"), IPAddress.Parse("192.0.2.71"), "tcp"));
        buffer.SetReceptionEnabled(false);
        Assert.Single((await Call("syslog_unregistered_list")).GetProperty("items").EnumerateArray());
        var sample = (await Call("syslog_unregistered_logs", new { peerAddress = "192.0.2.71" })).EnumerateArray().Single();
        await Call("syslog_unregistered_sample", new { peerAddress = "192.0.2.71", sampleKey = sample.GetProperty("sampleKey").GetString() });
        await Call("syslog_unregistered_register", new { name = "registered-from-sample", peerAddress = "192.0.2.71", timeZoneId = "UTC", retentionDays = 30 });
        await BusinessCallAsync(app, "syslog_unregistered_logs", new { peerAddress = "192.0.2.71" }, success: false, code: "not_found");

        var listener = await Call("syslog_listeners_get");
        var configured = await Call("syslog_listeners_configure", new
        {
            version = listener.GetProperty("saved").GetProperty("version").GetString(), protocol = "tcp", address = "127.0.0.1", port = 25140, framing = "newline"
        });
        var reception = await Call("syslog_reception_set_enabled", new { version = configured.GetProperty("saved").GetProperty("version").GetString(), enabled = false });
        var protocol = await Call("syslog_protocol_set_enabled", new
        {
            version = reception.GetProperty("saved").GetProperty("version").GetString(), protocol = "tcp", enabled = true
        });
        Assert.False(protocol.GetProperty("running").GetProperty("enabled").GetBoolean());
        Assert.True(protocol.GetProperty("saved").GetProperty("configuration").GetProperty("listeners").EnumerateArray()
            .Single(item => item.GetProperty("protocol").GetString() == "tcp").GetProperty("enabled").GetBoolean());

        await Call("syslog_https_certificate_get");
        using (var rsa = RSA.Create(2048))
        {
            var request = new CertificateRequest("CN=mcp.example.test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
            var pfx = certificate.Export(X509ContentType.Pkcs12, "synthetic-pfx-password");
            var uploaded = await Call("syslog_https_certificate_upload", new { fileName = "synthetic.pfx", base64 = Convert.ToBase64String(pfx), password = "synthetic-pfx-password" });
            Assert.Contains("mcp.example.test", uploaded.GetProperty("current").GetProperty("subject").GetString());
            CryptographicOperations.ZeroMemory(pfx);
        }

        var smtp = await Call("syslog_smtp_get");
        await Call("syslog_smtp_save", new
        {
            version = smtp.GetProperty("version").ToString(), host = "smtp.example.test", port = 25, security = "none", username = "",
            senderAddress = "sender@example.test", senderName = "Synthetic", subjectPrefix = "[test]"
        });
        var queued = await Call("syslog_mail_send_test", new { recipient = "recipient@example.test" });
        var mailId = queued.GetProperty("id").GetString();
        var delivery = await Call("syslog_mail_delivery_get", new { id = mailId });
        Assert.Equal("queued", delivery.GetProperty("status").GetString());
        Assert.Single((await Call("syslog_mail_deliveries_list")).GetProperty("items").EnumerateArray());
        Assert.Equal("queued", (await app.Control.GetMailDeliveryAsync(long.Parse(mailId!, CultureInfo.InvariantCulture), Ct)).Data!.Status);

        Assert.Empty((await Call("syslog_report_subscriptions_list")).EnumerateArray());
        var subscription = await Call("syslog_report_subscription_create", new
        {
            version = "0", name = "daily", enabled = false, frequency = "daily", sendTime = "08:00", sourceIds = new[] { sourceId }, recipients = new[] { "recipient@example.test" }
        });
        var subscriptionId = subscription.GetProperty("id").GetString();
        var updated = await Call("syslog_report_subscription_update", new
        {
            id = subscriptionId, version = subscription.GetProperty("version").ToString(), name = "weekly", frequency = "weekly", sendTime = "09:00",
            sourceIds = new[] { sourceId }, recipients = new[] { "recipient@example.test" }
        });
        Assert.False(updated.GetProperty("enabled").GetBoolean());
        var enabledSubscription = await Call("syslog_report_subscription_set_enabled", new { id = subscriptionId, version = updated.GetProperty("version").ToString(), enabled = true });
        Assert.True(enabledSubscription.GetProperty("enabled").GetBoolean());

        var users = await Call("syslog_users_list");
        Assert.Equal(1, users.GetProperty("items").GetArrayLength());
        var user = await Call("syslog_users_create", new { username = "synthetic_target", password = "synthetic-only-password" });
        var userId = user.GetProperty("id").GetString();
        var rename = await Call("syslog_users_update", new { id = userId, expectedVersion = user.GetProperty("version").GetString(), username = "renamed_target" });
        var disabled = await Call("syslog_users_set_enabled", new { id = userId, expectedVersion = rename.GetProperty("version").GetString(), enabled = false });
        Assert.False(disabled.GetProperty("enabled").GetBoolean());
        await Call("syslog_users_reset_password", new { id = userId, expectedVersion = disabled.GetProperty("version").GetString(), newPassword = "synthetic-only-new-password" });

        Assert.Equal(McpToolCatalog.All.Select(tool => tool.Name).Order(), exercised.Order());
        var calls = (await app.Control.ListMcpCallsAsync(new(PageSize: 100), Ct)).Data!;
        Assert.Equal(39, calls.Items.Where(call => call.Outcome == "success").Select(call => call.ToolName).Distinct().Count());
    }

    private static async Task<JsonElement> BusinessCallAsync(Harness app, string name, object arguments, bool success = true, string? code = null)
    {
        var body = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = Guid.NewGuid().ToString("N"), method = "tools/call", @params = new { name, arguments } });
        using var response = await app.Client.SendAsync(app.Request(body));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync();
        if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
            responseBody = responseBody.Split('\n').Where(line => line.StartsWith("data: ", StringComparison.Ordinal))
                .Select(line => line[6..].Trim()).Last(line => line.Length > 0);
        using var json = JsonDocument.Parse(responseBody);
        var result = json.RootElement.GetProperty("result");
        var structured = result.GetProperty("structuredContent");
        // 失败时只输出业务码，不把合成或真实凭据粘贴到测试错误。
        Assert.True(structured.GetProperty("success").GetBoolean() == success,
            name + ": " + structured.GetProperty("code").GetString());
        if (code is not null) Assert.Equal(code, structured.GetProperty("code").GetString());
        return structured.GetProperty("data").Clone();
    }
}
