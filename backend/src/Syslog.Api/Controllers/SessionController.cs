using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Syslog.Api.Common;
using Syslog.Api.Models;
using Syslog.Api.Service;
using Syslog.Shared.Models;

namespace Syslog.Api.Controllers;

[ApiController, Route("api/admin/v1/session")]
public sealed class SessionController(IControlStore control) : ControllerBase
{
    [AllowAnonymous, HttpPost("login"), EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginRequest input, CancellationToken cancellationToken)
    {
        if (!Request.IsHttps) return BadRequest(ApiResult<object>.Fail("https_required", "登录必须通过 HTTPS。"));
        var result = await control.LoginAsync(input.Username, input.Password, cancellationToken);
        if (!result.Success || result.Data is null) return Unauthorized(ApiResult<object>.Fail(result.Code, result.Message));
        Response.Cookies.Append(SessionAuthenticationHandler.CookieName, result.Data.Token, new CookieOptions
        {
            Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/", Expires = result.Data.ExpiresAt,
            IsEssential = true
        });
        // Token 仅进入安全 Cookie，JSON 响应不重复暴露会话凭据。
        return Ok(ApiResult<ControlUser>.Ok(result.Data.User));
    }

    [Authorize, HttpPost("password"), EnableRateLimiting("password-change")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest input, CancellationToken cancellationToken)
    {
        if (!Request.IsHttps) return BadRequest(ApiResult<object>.Fail("https_required", "修改密码必须通过 HTTPS。"));
        var userId = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, System.Globalization.CultureInfo.InvariantCulture);
        var token = Request.Cookies[SessionAuthenticationHandler.CookieName] ?? "";
        var result = await control.ChangePasswordAsync(userId, token, input.CurrentPassword, input.NewPassword, cancellationToken);
        if (!result.Success)
            return result.Code == "authentication_required" ? Unauthorized(result) : BadRequest(result);
        ClearSessionCookie();
        return Ok(result);
    }

    [Authorize, HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var result = await control.LogoutAsync(Request.Cookies[SessionAuthenticationHandler.CookieName] ?? "", cancellationToken);
        if (!result.Success) return StatusCode(503, result);
        ClearSessionCookie();
        return Ok(result);
    }

    [Authorize, HttpGet]
    public IActionResult Current() => Ok(ApiResult<ControlUser>.Ok(new ControlUser(
        long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, System.Globalization.CultureInfo.InvariantCulture),
        User.Identity!.Name!, User.FindFirstValue(ClaimTypes.Role)!)));

    private void ClearSessionCookie() =>
        Response.Cookies.Delete(SessionAuthenticationHandler.CookieName,
            new CookieOptions { Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/" });
}

