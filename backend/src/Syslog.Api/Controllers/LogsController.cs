using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Syslog.Api.Common;
using Syslog.Api.Models;
using Syslog.Api.Service.impl;
using Syslog.Shared.Models;
using Syslog.Shared.Service;

namespace Syslog.Api.Controllers;

[ApiController, Authorize(Roles = "admin"), EnableRateLimiting("search"), Route("api/admin/v1/logs")]
public sealed class LogsController(ILogStore logs, ISourceRegistry sources) : ControllerBase
{
    [HttpPost("clear"), EnableRateLimiting("user-management")]
    public Task<IActionResult> Clear(ClearLogsRequest input, [FromServices] LogClearService service, CancellationToken cancellationToken) =>
        ClearAsync(input, service, null, cancellationToken);

    [HttpPost("/api/admin/v1/sources/{sourceId}/logs/clear"), EnableRateLimiting("user-management")]
    public Task<IActionResult> ClearSource(string sourceId, ClearLogsRequest input, [FromServices] LogClearService service,
        CancellationToken cancellationToken) =>
        ClearAsync(input, service, sourceId, cancellationToken);

    private async Task<IActionResult> ClearAsync(ClearLogsRequest input, LogClearService service, string? sourceId,
        CancellationToken cancellationToken)
    {
        if (!Request.IsHttps)
            return BadRequest(ApiResult<bool>.Fail("https_required", "清空日志必须通过 HTTPS 操作。"));
        var actorId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
        var token = Request.Cookies[SessionAuthenticationHandler.CookieName] ?? "";
        var result = sourceId is null
            ? await service.ClearAsync(input.Confirmation, actorId, token, cancellationToken)
            : await service.ClearSourceAsync(sourceId, input.Confirmation, actorId, token, cancellationToken);
        return result.Success ? Ok(result) : StatusCode(result.Code switch
        {
            "log_clear_confirmation_required" => 400,
            "authentication_required" => 401,
            "source_not_found" => 404,
            "log_clear_in_progress" => 409,
            "storage_timeout" => 504,
            _ => 503
        }, result);
    }

    [HttpPost("search")]
    public async Task<IActionResult> Search(LogSearch query, CancellationToken cancellationToken)
    {
        // 首版只有管理员角色，权限范围来自管理库，绝不接受客户端自报 allowedSourceIds。
        var scope = (await sources.ListAsync(cancellationToken)).Select(x => x.Id).ToArray();
        if (scope.Length == 0) return Ok(ApiResult<LogSearchPage>.Ok(new([], null, false)));
        return Ok(ApiResult<LogSearchPage>.Ok(await logs.SearchAsync(query, scope, cancellationToken)));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Detail(string id, [FromQuery] DateTimeOffset receivedAt, CancellationToken cancellationToken)
    {
        if (!long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var eventId) || eventId <= 0 || receivedAt == default)
            return BadRequest(ApiResult<object>.Fail("invalid_query", "请提供事件标识和接收时间。"));
        var scope = (await sources.ListAsync(cancellationToken)).Select(x => x.Id).ToArray();
        var item = scope.Length == 0 ? null : await logs.GetAsync(eventId, receivedAt, scope, cancellationToken);
        return item is null ? NotFound(ApiResult<object>.Fail("not_found", "日志不存在或已过期。")) : Ok(ApiResult<LogEvent>.Ok(item));
    }
}
