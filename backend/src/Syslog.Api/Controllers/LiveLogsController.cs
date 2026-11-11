using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Syslog.Api.Common;
using Syslog.Api.Models;
using Syslog.Api.Service;
using Syslog.Api.Service.impl;
using Syslog.Shared.Models;
using Syslog.Shared.Service;

namespace Syslog.Api.Controllers;

[ApiController, Authorize(Roles = "admin"), Route("api/admin/v1/live-logs")]
public sealed class LiveLogsController(LiveLogConfigurationService settings, ILiveLogBuffer buffer,
    ISourceRegistry sources, IControlStore control) : ControllerBase
{
    [HttpGet("settings")]
    public async Task<IActionResult> Settings(CancellationToken ct) => Result(await settings.ReadAsync(ct));

    [HttpPut("settings"), EnableRateLimiting("user-management")]
    public async Task<IActionResult> Save(SaveLiveLogSettingsRequest input, CancellationToken ct)
    {
        if (!Request.IsHttps) return BadRequest(ApiResult<object>.Fail("https_required", "保存配置必须通过 HTTPS。"));
        var id = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
        return Result(await settings.SaveAsync(input, id, Token, ct));
    }

    [HttpPost("query"), EnableRateLimiting("search")]
    public async Task<IActionResult> Query(LiveLogQueryRequest input, CancellationToken ct)
    {
        if (input.Filter is null || input.Limit is < 1 or > 500)
            return BadRequest(ApiResult<object>.Fail("invalid_query", "实时日志筛选或读取条数无效。"));
        var scope = await ScopeAsync(ct);
        if (scope is null) return Unauthorized(ApiResult<object>.Fail("authentication_required", "登录已失效。"));
        if (input.Epoch is not null && input.Epoch != buffer.GetStats().Epoch)
            return Conflict(ApiResult<object>.Fail("live_logs_cache_reset", "实时缓存已重置，请重新加载。"));
        var result = buffer.ReadSnapshot(input.Filter, scope, input.BeforeSequence, input.Limit);
        if (input.Epoch is not null && result.Data is { } page && page.Epoch != input.Epoch)
            return Conflict(ApiResult<object>.Fail("live_logs_cache_reset", "实时缓存已重置，请重新加载。"));
        return Result(result);
    }

    [HttpGet("entries/{id}"), EnableRateLimiting("search")]
    public async Task<IActionResult> Detail(string id, CancellationToken ct)
    {
        var scope = await ScopeAsync(ct);
        return scope is null ? Unauthorized(ApiResult<object>.Fail("authentication_required", "登录已失效。"))
            : Result(buffer.GetDetail(id, scope));
    }

    private string Token => Request.Cookies[SessionAuthenticationHandler.CookieName] ?? "";
    private async Task<string[]?> ScopeAsync(CancellationToken ct)
    {
        var items = await sources.ListAsync(ct);
        // 查询期间账号退出或停用也不能继续返回日志正文。
        var user = await control.ValidateSessionAsync(Token, ct);
        return user is { Role: "admin" } && user.Id.ToString(CultureInfo.InvariantCulture) == User.FindFirstValue(ClaimTypes.NameIdentifier)
            ? items.Select(item => item.Id).ToArray() : null;
    }

    private IActionResult Result<T>(ApiResult<T> result) => result.Success ? Ok(result) : StatusCode(result.Code switch
    {
        "invalid_query" or "invalid_request" or "unsupported_search_token" or "invalid_live_log_settings" => 400,
        "authentication_required" => 401,
        "source_forbidden" => 403,
        "not_found" or "live_log_expired" => 404,
        "version_conflict" or "live_logs_version_conflict" or "live_logs_cache_reset" => 409,
        _ => 503
    }, result);
}
