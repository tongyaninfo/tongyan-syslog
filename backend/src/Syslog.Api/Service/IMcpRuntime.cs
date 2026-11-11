using Syslog.Api.Models;
using Syslog.Shared.Models;

namespace Syslog.Api.Service;

public interface IMcpRuntime
{
    Task<ApiResult<McpServiceView>> GetViewAsync(CancellationToken ct);
    Task<ApiResult<McpServiceView>> SetEnabledAsync(SetMcpEnabledRequest input, long actorId, string token, CancellationToken ct);
}
