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

/// <summary>MCP 管理面只接受现有管理员会话；API Key 不能签发 Key 或读取完整调用审计。</summary>
[ApiController, Authorize(Roles = "admin"), Route("api/admin/v1/system/mcp")]
public sealed class McpManagementController(IMcpStore store, IMcpRuntime runtime) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Respond(await runtime.GetViewAsync(ct));

    [HttpPut("enabled"), EnableRateLimiting("user-management")]
    public Task<IActionResult> SetEnabled(SetMcpEnabledRequest input, CancellationToken ct) =>
        WriteAsync((id, token) => runtime.SetEnabledAsync(input, id, token, ct));

    [HttpGet("permissions")]
    public IActionResult Permissions() => Ok(ApiResult<object>.Ok(McpToolCatalog.All.Select(x => new
        { x.Name, x.Module, x.Title, x.Description, x.IsWrite, x.IsDestructive })));

    [HttpGet("keys")]
    public async Task<IActionResult> Keys(CancellationToken ct) => Respond(await store.ListMcpKeysAsync(ct));

    [HttpPost("keys"), EnableRateLimiting("user-management")]
    public Task<IActionResult> Create(CreateMcpKeyRequest input, CancellationToken ct) =>
        WriteAsync((id, token) => store.CreateMcpKeyAsync(input, id, token, ct), 201);

    [HttpPost("keys/{keyId}/revoke"), EnableRateLimiting("user-management")]
    public Task<IActionResult> Revoke(string keyId, CancellationToken ct) =>
        WriteAsync((id, token) => store.RevokeMcpKeyAsync(keyId, id, token, ct));

    [HttpDelete("keys/{keyId}"), EnableRateLimiting("user-management")]
    public Task<IActionResult> Delete(string keyId, CancellationToken ct) =>
        WriteAsync((id, token) => store.DeleteMcpKeyAsync(keyId, id, token, ct));

    [HttpPost("keys/{keyId}/connection-material"), EnableRateLimiting("user-management")]
    public Task<IActionResult> Connection(string keyId, CancellationToken ct) =>
        WriteAsync((id, token) => store.GetMcpConnectionMaterialAsync(keyId, id, token, ct));

    [HttpGet("calls")]
    public async Task<IActionResult> Calls([FromQuery] McpCallQuery query, CancellationToken ct) =>
        Respond(await store.ListMcpCallsAsync(query, ct));

    [HttpGet("calls/{id}")]
    public async Task<IActionResult> Call(string id, CancellationToken ct) => Respond(await store.GetMcpCallAsync(id, ct));

    [HttpPut("audit-retention"), EnableRateLimiting("user-management")]
    public Task<IActionResult> Retention(SetMcpRetentionRequest input, CancellationToken ct) =>
        WriteAsync((id, token) => store.SetMcpRetentionAsync(input, id, token, ct));

    private async Task<IActionResult> WriteAsync<T>(Func<long, string, Task<ApiResult<T>>> write, int successStatus = 200)
    {
        if (!Request.IsHttps) return BadRequest(ApiResult<object>.Fail("https_required", "MCP 管理操作必须通过 HTTPS。"));
        if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), NumberStyles.None, CultureInfo.InvariantCulture, out var actorId))
            return Unauthorized(ApiResult<object>.Fail("authentication_required", "请重新登录。"));
        return Respond(await write(actorId, Request.Cookies[SessionAuthenticationHandler.CookieName] ?? ""), successStatus);
    }

    private IActionResult Respond<T>(ApiResult<T> result, int successStatus = 200) => StatusCode(result.Success ? successStatus : result.Code switch
    {
        "authentication_required" => 401,
        "mcp_permission_denied" => 403,
        "not_found" => 404,
        "version_conflict" or "mcp_key_inactive" or "mcp_key_revoke_required" or "mcp_key_limit" => 409,
        "mcp_migration_required" or "mcp_audit_unavailable" or "mcp_recovery_unavailable" or "service_unavailable" => 503,
        _ => 400
    }, result);
}
