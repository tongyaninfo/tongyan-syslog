using Syslog.Api.Common;
using Syslog.Api.Models;
using Syslog.Api.Service;
using Syslog.Api.Service.impl;
using Syslog.Ingest.Service.impl;

namespace Syslog.Api.Workers;

/// <summary>通知发送独立于接收链路，任务先持久化，再单并发生成和发送。</summary>
public sealed class NotificationWorker(INotificationStore store, MailContentBuilder content, SmtpMailSender sender,
    TimeProvider clock, ILogger<NotificationWorker> logger) : BackgroundService
{
    private const int BatchSize = 10;
    private const int MaximumAttempts = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var recovered = false;
        var storageFailed = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!recovered)
                {
                    await store.RecoverInterruptedMailDeliveriesAsync(stoppingToken);
                    recovered = true;
                }
                var settings = (await store.GetSmtpSettingsAsync(stoppingToken)).Data;
                if (settings is { Host.Length: > 0 })
                {
                    await ScheduleAsync(stoppingToken);
                    for (var i = 0; i < BatchSize && !stoppingToken.IsCancellationRequested; i++)
                    {
                        var delivery = await store.ClaimMailDeliveryAsync(clock.GetUtcNow(), stoppingToken);
                        if (delivery is null) break;
                        await ProcessAsync(delivery, stoppingToken);
                    }
                }
                storageFailed = false;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                // 一次处理仅抢占一条。状态落库失败后在下一轮恢复，不能让记录一直显示发送中。
                recovered = false;
                if (!storageFailed) logger.LogWarning("通知任务暂不可用，请检查控制库状态。");
                storageFailed = true;
            }
            await Task.Delay(TimeSpan.FromSeconds(5), clock, stoppingToken);
        }
    }

    private async Task ScheduleAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var due = await store.ListDueReportSubscriptionsAsync(now, BatchSize, ct);
        foreach (var subscription in due)
        {
            if (!ReportSchedule.TryParseTime(subscription.SendTime, out var time) || !ReportSchedule.IsFrequency(subscription.Frequency))
                continue;
            var next = ReportSchedule.NextAfter(subscription.Frequency, time, now);
            var (from, to) = ReportSchedule.PreviousPeriod(subscription.Frequency, next);
            await store.ScheduleReportAsync(subscription, from, to, next, ct);
        }
    }

    private async Task ProcessAsync(StoredMailDelivery delivery, CancellationToken ct)
    {
        MailContent? mail = null;
        SmtpSendResult outcome;
        try
        {
            if (delivery is { HtmlBody: { } html, TextBody: { } text })
            {
                mail = new(delivery.Subject, html, text);
            }
            else
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(TimeSpan.FromMinutes(2));
                mail = delivery.Kind == "test"
                    ? content.BuildTest()
                    : await content.BuildReportAsync(delivery.Kind, delivery.PeriodStart!.Value, delivery.PeriodEnd!.Value,
                        delivery.SourceIds, deadline.Token);
                var prefix = delivery.SubjectPrefix;
                var subject = string.IsNullOrEmpty(prefix) ? mail.Subject : $"{prefix} {mail.Subject}";
                if (subject.Length > 240) subject = subject[..240];
                mail = mail with { Subject = subject };
                if (!await store.StoreMailContentAsync(delivery.Id, mail.Subject, mail.Html, mail.Text, ct))
                {
                    await FinishAsync(delivery, new("failed", "report_content_unavailable", true));
                    return;
                }
            }
        }
        catch (Exception e) when (e is ClickHouseStoreException or OperationCanceledException)
        {
            await FinishAsync(delivery, new("failed", "report_data_unavailable", true));
            return;
        }
        catch (Exception)
        {
            await FinishAsync(delivery, new("failed", "report_content_unavailable", true));
            return;
        }

        var settings = await store.GetSmtpSettingsAsync(ct);
        if (settings.Data is not { } smtp)
        {
            outcome = new("failed", settings.Code, true);
        }
        else
        {
            try { outcome = await sender.SendAsync(smtp, delivery, mail.Subject, mail.Html, mail.Text, ct); }
            catch (Exception) { outcome = new("unknown", "smtp_delivery_unknown"); }
        }
        await FinishAsync(delivery, outcome);
    }

    private async Task FinishAsync(StoredMailDelivery delivery, SmtpSendResult outcome)
    {
        var retry = outcome.Retry && delivery.Attempts < MaximumAttempts
            ? clock.GetUtcNow().AddMinutes(delivery.Attempts)
            : (DateTimeOffset?)null;
        // 停止宿主时仍尝试短时间落库，避免把明确结果丢成未知。
        using var write = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await store.FinishMailDeliveryAsync(delivery.Id, retry is not null ? "queued" : outcome.Status, outcome.FailureCode, retry, write.Token);
    }
}
