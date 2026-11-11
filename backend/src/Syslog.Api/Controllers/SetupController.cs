using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Syslog.Api.Models;
using Syslog.Api.Service;
using Syslog.Shared.Models;

namespace Syslog.Api.Controllers;

[ApiController, AllowAnonymous, Route("api/admin/v1/setup")]
public sealed class SetupController(IControlStore control) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken cancellationToken)
    {
        var required = await control.IsSetupRequiredAsync(cancellationToken);
        return Ok(ApiResult<SetupStatus>.Ok(new(required, required && Request.IsHttps && IsLocalSetupRequest())));
    }

    [HttpPost("admin"), EnableRateLimiting("setup")]
    public async Task<IActionResult> Initialize(CreateUserRequest input, CancellationToken cancellationToken)
    {
        if (!Request.IsHttps) return BadRequest(ApiResult<object>.Fail("https_required", "初始化必须通过 HTTPS。"));
        if (!IsLocalSetupRequest()) return StatusCode(403, ApiResult<object>.Fail("setup_local_required", "请从服务器本机直连完成首次初始化。"));
        var result = await control.SetupAdminAsync(input.Username, input.Password, cancellationToken);
        if (result.Success) return Ok(result);
        return result.Code == "setup_already_completed" ? Conflict(result) : BadRequest(result);
    }

    private bool IsLocalSetupRequest()
    {
        var address = HttpContext.Connection.RemoteIpAddress;
        if (address?.IsIPv4MappedToIPv6 == true) address = address.MapToIPv4();
        if (address is null || !IPAddress.IsLoopback(address)) return false;
        // 不能将本机代理转发的远程访问当作安装者身份，禁止转发头并约束直连 Host。
        if (Request.Headers.Keys.Any(key => key.Equals("Forwarded", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("X-Real-IP", StringComparison.OrdinalIgnoreCase) || key.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase))) return false;
        var host = Request.Host.Host;
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(host.Trim('[', ']'), out var hostIp) && IPAddress.IsLoopback(hostIp);
    }
}
