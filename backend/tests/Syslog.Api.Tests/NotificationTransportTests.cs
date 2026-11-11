using System.Net;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using MimeKit;
using Syslog.Api.Common;
using Syslog.Api.Service;
using Syslog.Api.Service.impl;
using Xunit;

namespace Syslog.Api.Tests;

public sealed class NotificationTransportTests
{
    [Theory]
    [InlineData("  synthetic-smtp-code  ")]
    [InlineData("plain:synthetic-smtp-code")]
    [InlineData("CfDJ8synthetic-smtp-code")]
    public void StoredSmtpCodePreservesOriginalValueWithoutCreatingLegacyKeyDirectory(string sample)
    {
        var root = NewNotificationFixture();
        var environment = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = root }).Environment;
        var missingDirectory = Path.Combine(root, "unavailable", "legacy-keys");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        configuration["Notifications:KeyDirectory"] = missingDirectory;
        var secrets = new NotificationSecrets(environment, configuration);

        var stored = NotificationSecrets.Store(sample);
        Assert.Equal("plain:" + sample, stored);
        Assert.Equal(sample, secrets.Read(stored));
        Assert.False(Directory.Exists(Path.GetDirectoryName(missingDirectory)!));
    }

    [Fact]
    public void StoredSmtpCodeReadsLegacyPayloadWithoutGeneratingKeys()
    {
        var root = NewNotificationFixture();
        var environment = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = root }).Environment;
        var legacyDirectory = Path.Combine(root, "run-data", "notification-keys");
        var keyDirectory = CreatePrivateKeyDirectory(legacyDirectory);
        const string sample = "synthetic-smtp-authorization-code";
        // 生成旧版本实际写入 SQLite 的密文，确保兼容读取使用同一应用标识和用途。
        var legacy = DataProtectionProvider.Create(keyDirectory, options => options.SetApplicationName("Tongyan.Syslog.Notifications"))
            .CreateProtector("smtp-authorization-code", "v1");
        var protectedValue = legacy.Protect(sample);
        var originalKey = Assert.Single(Directory.GetFiles(legacyDirectory));
        Assert.Equal(sample, new NotificationSecrets(environment).Read(protectedValue));
        Assert.Equal(originalKey, Assert.Single(Directory.GetFiles(legacyDirectory)));

        var missingDirectory = Path.Combine(root, "missing-legacy-keys");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        configuration["Notifications:KeyDirectory"] = missingDirectory;
        var configured = new NotificationSecrets(environment, configuration);
        Assert.ThrowsAny<IOException>(() => configured.Read(protectedValue));
        Assert.False(Directory.Exists(missingDirectory));

        configuration["Notifications:KeyDirectory"] = Path.Combine("run-data", "notification-keys");
        Assert.Equal(sample, configured.Read(protectedValue));
    }

    private static string NewNotificationFixture() => Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory,
        "TestResults", "notification-secrets-" + Guid.NewGuid().ToString("N"))).FullName;

    private static DirectoryInfo CreatePrivateKeyDirectory(string path)
    {
        if (!OperatingSystem.IsWindows())
            return Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        using var identity = WindowsIdentity.GetCurrent();
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new(identity.User!, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        var directory = new DirectoryInfo(path);
        directory.Create(security);
        return directory;
    }

    [Theory]
    [InlineData("daily", "2026-11-11T01:00:00Z", "2026-11-12T01:00:00Z", "2026-11-09T16:00:00Z", "2026-11-10T16:00:00Z")]
    [InlineData("weekly", "2026-11-09T01:00:00Z", "2026-11-16T01:00:00Z", "2026-11-01T16:00:00Z", "2026-11-08T16:00:00Z")]
    [InlineData("monthly", "2026-11-01T02:00:00Z", "2026-12-01T01:00:00Z", "2026-09-30T16:00:00Z", "2026-10-31T16:00:00Z")]
    public void CalendarReportsUseTheLastCompletedShanghaiPeriod(string frequency, string now, string next, string from, string to)
    {
        var scheduled = ReportSchedule.NextAfter(frequency, new TimeOnly(9, 0), DateTimeOffset.Parse(now));
        Assert.Equal(DateTimeOffset.Parse(next), scheduled);
        Assert.Equal((DateTimeOffset.Parse(from), DateTimeOffset.Parse(to)), ReportSchedule.PreviousPeriod(frequency, scheduled));
    }

    [Theory]
    [InlineData("none", "accept", "accepted", null, false)]
    [InlineData("none", "transient", "failed", "smtp_temporary_rejection", true)]
    [InlineData("none", "drop-after-data", "unknown", "smtp_delivery_unknown", false)]
    [InlineData("starttls", "no-starttls", "failed", "smtp_tls_failed", false)]
    public async Task SmtpSubmissionReportsAcceptanceRejectionAndAmbiguousDisconnect(string security, string behavior,
        string status, string? failure, bool retry)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var remote = RunRelayAsync(listener, behavior, deadline.Token);
        var settings = new StoredSmtpSettings(1, "127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, security, "",
            "sender@example.test", "通研 Syslog", "[示例]", null);
        var delivery = new StoredMailDelivery(1, null, null, "test", "sending", "recipient@example.test", "[示例]", "",
            null, null, [], null, null, DateTimeOffset.UtcNow, null, 1, null);
        // 此内网中继不使用认证，测试不会创建密钥或使用真实邮箱。
        var sender = new SmtpMailSender(new NotificationSecrets(WebApplication.CreateBuilder().Environment));
        var result = await sender.SendAsync(settings, delivery, "[示例] SMTP 测试邮件", "<p>示例正文</p>", "示例正文", deadline.Token);
        Assert.Equal(new SmtpSendResult(status, failure, retry), result);
        var message = await remote;
        if (behavior is "accept" or "drop-after-data")
        {
            Assert.NotNull(message);
            Assert.Equal("[示例] SMTP 测试邮件", message.Subject);
            Assert.Contains("示例正文", message.HtmlBody);
            Assert.Contains("示例正文", message.TextBody);
        }
        else
        {
            Assert.Null(message);
        }
    }

    private static async Task<MimeMessage?> RunRelayAsync(TcpListener listener, string behavior, CancellationToken ct)
    {
        using var client = await listener.AcceptTcpClientAsync(ct);
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
        await writer.WriteLineAsync("220 relay.example.test ESMTP".AsMemory(), ct);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.StartsWith("EHLO ", StringComparison.Ordinal))
                await writer.WriteLineAsync("250 relay.example.test".AsMemory(), ct);
            else if (line.StartsWith("MAIL FROM:", StringComparison.OrdinalIgnoreCase))
            {
                Assert.NotEqual("no-starttls", behavior);
                await writer.WriteLineAsync("250 Sender accepted".AsMemory(), ct);
            }
            else if (line.StartsWith("RCPT TO:", StringComparison.OrdinalIgnoreCase))
            {
                if (behavior == "transient")
                {
                    await writer.WriteLineAsync("450 Try again".AsMemory(), ct);
                    continue;
                }
                await writer.WriteLineAsync("250 Recipient accepted".AsMemory(), ct);
            }
            else if (line == "DATA")
            {
                await writer.WriteLineAsync("354 End with dot".AsMemory(), ct);
                var payload = new StringBuilder();
                while (await reader.ReadLineAsync(ct) is { } data && data != ".") payload.Append(data).Append("\r\n");
                if (behavior == "accept") await writer.WriteLineAsync("250 Queued".AsMemory(), ct);
                using var mime = new MemoryStream(Encoding.UTF8.GetBytes(payload.ToString()));
                return await MimeMessage.LoadAsync(mime, ct);
            }
            else
            {
                await writer.WriteLineAsync("250 OK".AsMemory(), ct);
            }
        }
        return null;
    }
}
