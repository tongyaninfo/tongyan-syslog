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

[ApiController, Authorize(Roles = "admin"), Route("api/admin/v1/users")]
public sealed class UsersController(IControlStore control) : ControllerBase
{
    private long ActorId => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, CultureInfo.InvariantCulture);
    private string SessionToken => Request.Cookies[SessionAuthenticationHandler.CookieName] ?? "";

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] UserQuery query, CancellationToken cancellationToken) =>
        Respond(await control.ListUsersAsync(query, ActorId, SessionToken, cancellationToken));

    [HttpPost, EnableRateLimiting("user-management")]
    public async Task<IActionResult> Create(CreateUserRequest input, CancellationToken cancellationToken)
    {
        if (!Request.IsHttps) return InsecureRequest();
        return Respond(await control.CreateUserAsync(input, ActorId, SessionToken, cancellationToken));
    }

    [HttpPut("{id}"), EnableRateLimiting("user-management")]
    public async Task<IActionResult> Update(string id, UpdateUserRequest input, CancellationToken cancellationToken)
    {
        if (!Request.IsHttps) return InsecureRequest();
        return Respond(await control.UpdateUserAsync(id, input, ActorId, SessionToken, cancellationToken));
    }

    [HttpPost("{id}/password"), EnableRateLimiting("user-management")]
    public async Task<IActionResult> ResetPassword(string id, ResetUserPasswordRequest input, CancellationToken cancellationToken)
    {
        if (!Request.IsHttps) return InsecureRequest();
        return Respond(await control.ResetUserPasswordAsync(id, input, ActorId, SessionToken, cancellationToken));
    }

    private BadRequestObjectResult InsecureRequest() => BadRequest(ApiResult<object>.Fail("https_required", "账号管理必须通过 HTTPS。"));
    private IActionResult Respond<T>(ApiResult<T> result) => result.Success ? Ok(result) : StatusCode(result.Code switch
    {
        "authentication_required" => 401,
        "self_disable_forbidden" or "last_admin_required" or "self_reset_forbidden" => 403,
        "user_not_found" => 404,
        "username_conflict" or "version_conflict" => 409,
        _ => 400
    }, result);
}
