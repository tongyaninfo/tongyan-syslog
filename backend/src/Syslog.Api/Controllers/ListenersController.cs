using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Syslog.Api.Common;
using Syslog.Api.Models;
using Syslog.Api.Service;
using Syslog.Shared.Models;

namespace Syslog.Api.Controllers;

[ApiController, Authorize(Roles = "admin"), Route("api/admin/v1/system/listeners")]
public sealed class ListenersController(IListenerConfigurationService configuration) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Read(CancellationToken cancellationToken) =>
        Respond(await configuration.ReadAsync(cancellationToken));

    [HttpPut, EnableRateLimiting("user-management")]
    public async Task<IActionResult> Save(SaveListenerSettingsRequest input, CancellationToken cancellationToken)
    {
        if (!Request.IsHttps) return BadRequest(ApiResult<object>.Fail("https_required", "监听配置必须通过 HTTPS 保存。"));
        var actorId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
        var token = Request.Cookies[SessionAuthenticationHandler.CookieName] ?? "";
        var result = await configuration.SaveAsync(input, actorId, token, cancellationToken);
        return Respond(result);
    }

    [HttpPut("reception"), EnableRateLimiting("user-management")]
    public async Task<IActionResult> SetReception(SetListenerReceptionRequest input, CancellationToken cancellationToken)
    {
        if (!Request.IsHttps) return BadRequest(ApiResult<object>.Fail("https_required", "接收开关必须通过 HTTPS 操作。"));
        var actorId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
        var token = Request.Cookies[SessionAuthenticationHandler.CookieName] ?? "";
        return Respond(await configuration.SetReceptionAsync(input.Enabled, actorId, token, cancellationToken, input.Version));
    }

    [HttpPut("reception/{protocol}"), EnableRateLimiting("user-management")]
    public async Task<IActionResult> SetProtocolReception(string protocol, SetListenerProtocolReceptionRequest input, CancellationToken cancellationToken)
    {
        if (!Request.IsHttps) return BadRequest(ApiResult<object>.Fail("https_required", "接收开关必须通过 HTTPS 操作。"));
        var actorId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
        var token = Request.Cookies[SessionAuthenticationHandler.CookieName] ?? "";
        return Respond(await configuration.SetProtocolReceptionAsync(protocol, input, actorId, token, cancellationToken));
    }

    private IActionResult Respond(ApiResult<ListenerSettingsView> result) => result.Success ? Ok(result) : StatusCode(result.Code switch
        {
            "authentication_required" => 401,
            "version_conflict" => 409,
            "listener_migration_required" or "listener_runtime_unavailable" or "service_unavailable" => 503,
            _ => 400
        }, result);
}
