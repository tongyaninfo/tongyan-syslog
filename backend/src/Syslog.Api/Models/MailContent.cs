namespace Syslog.Api.Models;

public sealed record MailContent(string Subject, string Html, string Text);
