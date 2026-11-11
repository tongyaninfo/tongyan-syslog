using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Syslog.Api.Models;
using Syslog.Api.Service;
using Syslog.Shared.Models;

namespace Syslog.Api.Controllers;

[ApiController, Authorize(Roles = "admin"), Route("api/admin/v1/sources")]
public sealed class SourcesController(IControlStore control) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok(ApiResult<IReadOnlyList<LogSource>>.Ok(await control.ListAsync(cancellationToken)));

    [HttpPost]
    public async Task<IActionResult> Create(SourceInput input, CancellationToken cancellationToken) =>
        Result(await control.CreateSourceAsync(input, ActorId(), cancellationToken));

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, SourceInput input, CancellationToken cancellationToken) =>
        Result(await control.UpdateSourceAsync(id, input, ActorId(), cancellationToken));

    private long ActorId() => long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!, System.Globalization.CultureInfo.InvariantCulture);
    private IActionResult Result(ApiResult<LogSource> result) => StatusCode(result.Success ? 200 : result.Code switch
    { "FORBIDDEN" => 403, "NOT_FOUND" => 404, "CONFLICT" => 409, "source_filter_migration_required" => 503, _ => 400 }, result);
}
