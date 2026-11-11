using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Syslog.Api.Service;
using Syslog.Shared.Models;

namespace Syslog.Api.Common;

public sealed class SessionAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, IControlStore control) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "SyslogSession";
    public const string CookieName = "__Host-syslog";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // 匿名初始化入口不能被浏览器旧 Cookie 触发的缺库读取阻断。
        if (Context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() is not null) return AuthenticateResult.NoResult();
        if (!Request.Cookies.TryGetValue(CookieName, out var token)) return AuthenticateResult.NoResult();
        var user = await control.ValidateSessionAsync(token, Context.RequestAborted);
        if (user is null) return AuthenticateResult.Fail("会话已失效");
        var identity = new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim(ClaimTypes.Name, user.Username), new Claim(ClaimTypes.Role, user.Role)
        ], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Response.WriteAsJsonAsync(ApiResult<object>.Fail("authentication_required", "请先登录。"), Context.RequestAborted);
    }
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Response.WriteAsJsonAsync(ApiResult<object>.Fail("forbidden", "无权执行此操作。"), Context.RequestAborted);
    }
}
