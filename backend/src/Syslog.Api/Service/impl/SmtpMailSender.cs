using System.Globalization;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Syslog.Api.Service.impl;

public sealed record SmtpSendResult(string Status, string? FailureCode = null, bool Retry = false);

public sealed class SmtpMailSender(NotificationSecrets secrets)
{
    public async Task<SmtpSendResult> SendAsync(StoredSmtpSettings settings, StoredMailDelivery delivery,
        string subject, string html, string text, CancellationToken ct)
    {
        if (settings.Host.Length == 0) return Failed("smtp_not_configured");
        if (settings.Security == "none" && settings.Username.Length > 0) return Failed("smtp_auth_requires_tls");
        string? password;
        try { password = settings.ProtectedPassword is { Length: > 0 } value ? secrets.Read(value) : null; }
        catch (Exception e) when (e is CryptographicException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { return Failed("smtp_secret_unavailable"); }
        if (settings.Username.Length > 0 && password is null) return Failed("smtp_password_required");

        var message = new MimeMessage
        {
            Date = delivery.CreatedAt,
            MessageId = "syslog-" + delivery.Id.ToString(CultureInfo.InvariantCulture) + "@notifications.syslog.invalid",
            Subject = subject,
            Body = new BodyBuilder { HtmlBody = html, TextBody = text }.ToMessageBody()
        };
        message.From.Add(new MailboxAddress(settings.SenderName, settings.SenderAddress));
        message.To.Add(MailboxAddress.Parse(delivery.Recipient));
        var security = settings.Security switch
        {
            "ssl" => SecureSocketOptions.SslOnConnect,
            "starttls" => SecureSocketOptions.StartTls,
            "none" => SecureSocketOptions.None,
            _ => throw new InvalidOperationException("未知 SMTP 加密方式。")
        };

        using var smtp = new SmtpClient { SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var sending = false;
        try
        {
            await smtp.ConnectAsync(settings.Host, settings.Port, security, timeout.Token);
            if (settings.Username.Length > 0)
                await smtp.AuthenticateAsync(settings.Username, password!, timeout.Token);
            sending = true;
            await smtp.SendAsync(message, timeout.Token);
            return new("accepted");
        }
        catch (SmtpCommandException e)
        {
            // SMTP 已明确拒绝时才按 4xx 重试，不把断线当作拒收。
            var code = (int)e.StatusCode;
            if (code is >= 400 and < 500) return Failed("smtp_temporary_rejection", retry: true);
            if (code < 400 && sending) return new("unknown", "smtp_delivery_unknown");
            return Failed(e.ErrorCode == SmtpErrorCode.RecipientNotAccepted ? "smtp_recipient_rejected" : "smtp_rejected");
        }
        catch (MailKit.Security.AuthenticationException) { return Failed("smtp_authentication_failed"); }
        catch (Exception e) when (!sending && e is (SslHandshakeException or System.Security.Authentication.AuthenticationException or NotSupportedException))
        { return Failed("smtp_tls_failed"); }
        catch (Exception e) when (e is OperationCanceledException or IOException or SocketException or SmtpProtocolException)
        {
            // DATA 已开始后，断线或超时无法确认对端是否接受，禁止自动重发。
            if (sending) return new("unknown", "smtp_delivery_unknown");
            return Failed(e is OperationCanceledException ? "smtp_timeout" : "smtp_connection_failed", retry: true);
        }
    }

    private static SmtpSendResult Failed(string code, bool retry = false) => new("failed", code, retry);
}
