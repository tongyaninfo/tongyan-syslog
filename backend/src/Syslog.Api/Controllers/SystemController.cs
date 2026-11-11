using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Syslog.Ingest.Models;
using Syslog.Shared.Models;

namespace Syslog.Api.Controllers;

[ApiController, Route("api/admin/v1/system")]
public sealed class SystemController(IngestStatus status) : ControllerBase
{
    [Authorize(Roles = "admin"), HttpGet("status")]
    public IActionResult Status() => Ok(ApiResult<object>.Ok(status.Snapshot()));
}
