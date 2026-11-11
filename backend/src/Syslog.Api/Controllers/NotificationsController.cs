using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Syslog.Api.Common;
using Syslog.Api.Models;
using Syslog.Api.Service.impl;
using Syslog.Shared.Models;

namespace Syslog.Api.Controllers;

[ApiController, Authorize(Roles = "admin"), Route("api/admin/v1/notifications")]
public sealed class NotificationsController(NotificationService notifications) : ControllerBase
{
    [HttpGet("smtp")]
    public async Task<IActionResult> GetSmtp(CancellationToken ct) => Respond(await notifications.ReadSmtpAsync(ct));

    [HttpPut("smtp"), EnableRateLimiting("user-management")]
    public Task<IActionResult> SaveSmtp(SaveSmtpSettingsRequest input, CancellationToken ct) =>
        WriteAsync((id, token) => notifications.SaveSmtpAsync(input, id, token, ct));

    [HttpPost("test"), EnableRateLimiting("notification-test")]
    public Task<IActionResult> SendTest(TestEmailRequest input, CancellationToken ct) =>
        WriteAsync((id, token) => notifications.EnqueueTestAsync(input, id, token, ct), 202);

    [HttpGet("subscriptions")]
    public async Task<IActionResult> ListSubscriptions(CancellationToken ct) =>
        Respond(await notifications.ListSubscriptionsAsync(ct));

    [HttpPost("subscriptions"), EnableRateLimiting("user-management")]
    public Task<IActionResult> CreateSubscription(SaveReportSubscriptionRequest input, CancellationToken ct) =>
        WriteAsync((id, token) => notifications.SaveSubscriptionAsync(0, input, id, token, ct), 201);

    [HttpPut("subscriptions/{subscriptionId}"), EnableRateLimiting("user-management")]
    public Task<IActionResult> UpdateSubscription(string subscriptionId, SaveReportSubscriptionRequest input, CancellationToken ct) =>
        NotificationService.TryId(subscriptionId, out var subscription)
            ? WriteAsync((id, token) => notifications.SaveSubscriptionAsync(subscription, input, id, token, ct))
            : Task.FromResult(InvalidId());

    [HttpGet("deliveries")]
    public async Task<IActionResult> ListDeliveries(string? kind, string? status, string? recipient,
        DateTimeOffset? from, DateTimeOffset? to, string? beforeId, CancellationToken ct) =>
        Respond(await notifications.ListDeliveriesAsync(kind, status, recipient, from, to, beforeId, ct));

    [HttpGet("deliveries/{id}")]
    public async Task<IActionResult> GetDelivery(string id, CancellationToken ct) =>
        NotificationService.TryId(id, out var delivery)
            ? Respond(await notifications.GetDeliveryAsync(delivery, ct))
            : InvalidId();

    private async Task<IActionResult> WriteAsync<T>(Func<long, string, Task<ApiResult<T>>> write, int successStatus = 200)
    {
        if (!Request.IsHttps) return BadRequest(ApiResult<object>.Fail("https_required", "通知配置和发送操作必须通过 HTTPS。"));
        var actorId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
        return Respond(await write(actorId, Request.Cookies[SessionAuthenticationHandler.CookieName] ?? ""), successStatus);
    }

    private IActionResult InvalidId() => BadRequest(ApiResult<object>.Fail("invalid_request", "记录标识无效。"));

    private IActionResult Respond<T>(ApiResult<T> result, int successStatus = 200) =>
        StatusCode(result.Success ? successStatus : result.Code switch
        {
            "authentication_required" => 401,
            "source_forbidden" => 403,
            "not_found" => 404,
            "version_conflict" or "smtp_not_configured" or "notification_subscription_limit" => 409,
            "notification_queue_full" => 429,
            "notification_migration_required" or "service_unavailable" => 503,
            _ => 400
        }, result);
}
