using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Syslog.Api.Service.impl;
using Syslog.Shared.Models;

namespace Syslog.Api.Controllers;

[ApiController, Authorize(Roles = "admin"), EnableRateLimiting("search"), Route("api/admin/v1/unregistered-devices")]
public sealed class UnregisteredDevicesController(UnregisteredDeviceQueries queries) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Respond(await queries.ListAsync(ct));
    [HttpGet("logs")]
    public async Task<IActionResult> Logs([FromQuery] string? peerAddress, CancellationToken ct) => Respond(await queries.LogsAsync(peerAddress, ct));
    [HttpGet("sample")]
    public async Task<IActionResult> Sample([FromQuery] string? peerAddress, [FromQuery] string? sampleKey, CancellationToken ct) =>
        Respond(await queries.SampleAsync(peerAddress, sampleKey, ct));
    private IActionResult Respond<T>(ApiResult<T> result) => StatusCode(result.Success ? 200 : result.Code == "not_found" ? 404 : 400, result);
}
