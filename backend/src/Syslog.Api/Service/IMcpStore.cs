using Syslog.Api.Models;
using Syslog.Shared.Models;

namespace Syslog.Api.Service;

public interface IMcpStore
{
    Task<ApiResult<McpSettingsView>> ReadMcpSettingsAsync(CancellationToken ct);
    Task<ApiResult<McpSettingsView>> SetMcpEnabledAsync(SetMcpEnabledRequest input, long actorId, string token, CancellationToken ct);
    Task<ApiResult<McpSettingsView>> SetMcpRetentionAsync(SetMcpRetentionRequest input, long actorId, string token, CancellationToken ct);
    Task<ApiResult<IReadOnlyList<McpKeyView>>> ListMcpKeysAsync(CancellationToken ct);
    Task<ApiResult<CreatedMcpKey>> CreateMcpKeyAsync(CreateMcpKeyRequest input, long actorId, string token, CancellationToken ct);
    Task<ApiResult<bool>> RevokeMcpKeyAsync(string keyId, long actorId, string token, CancellationToken ct);
    Task<ApiResult<bool>> DeleteMcpKeyAsync(string keyId, long actorId, string token, CancellationToken ct);
    Task<ApiResult<McpConnectionMaterial>> GetMcpConnectionMaterialAsync(string keyId, long actorId, string token, CancellationToken ct);
    Task<ApiResult<McpPrincipal>> AuthenticateMcpAsync(string secret, CancellationToken ct);
    Task<ApiResult<McpPrincipal>> ValidateMcpPermissionAsync(McpPrincipal principal, string permission, CancellationToken ct);
    Task<ApiResult<string>> BeginMcpCallAsync(McpCallStart request, CancellationToken ct);
    Task<bool> CompleteMcpCallAsync(string id, McpCallCompletion completion, CancellationToken ct);
    Task<ApiResult<McpCallPage>> ListMcpCallsAsync(McpCallQuery query, CancellationToken ct);
    Task<ApiResult<McpCallDetail>> GetMcpCallAsync(string id, CancellationToken ct);
    Task RecoverMcpCallsAsync(CancellationToken ct, DateTimeOffset? startedBefore = null);
    Task<McpCleanupResult?> CleanupMcpCallsAsync(CancellationToken ct);
}
