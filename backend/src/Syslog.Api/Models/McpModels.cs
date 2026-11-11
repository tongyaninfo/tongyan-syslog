using System.Text.Json.Serialization;

namespace Syslog.Api.Models;

public sealed record McpSettingsView(string Version, bool Enabled, int AuditRetentionDays);
public sealed record McpServiceView(McpSettingsView Settings, string State, int ActiveCalls, int ToolCount,
    DateTimeOffset? LastCleanupAt = null, string? CleanupError = null, string? RecoveryError = null);
public sealed record SetMcpEnabledRequest(string Version, [property: JsonRequired] bool Enabled);
public sealed record SetMcpRetentionRequest(string Version, [property: JsonRequired] int AuditRetentionDays);
public sealed record CreateMcpKeyRequest(string? Name, string? AccessMode, string[]? Permissions, DateTimeOffset? ExpiresAt);

// 普通列表只暴露标识，原文仅在创建与交互式管理员主动复制时返回。
public sealed record McpKeyView(string Id, string Name, string Prefix, string AccessMode, string[] Permissions,
    string CreatedBy, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, DateTimeOffset? RevokedAt,
    DateTimeOffset? LastUsedAt, string Status);
public sealed record CreatedMcpKey(McpKeyView Key, string Secret);
public sealed record McpConnectionMaterial(string KeyId, string Name, string Secret, string AccessMode,
    string[] Permissions, DateTimeOffset? ExpiresAt);

// 仅由认证入口产生，不接收工具输入中的操作者、权限或密钥标识。
public sealed record McpPrincipal(long KeyId, long UserId, string Name, string Username, string AccessMode,
    string[] Permissions, DateTimeOffset? ExpiresAt);

public sealed record McpCallStart(McpPrincipal? Principal, string TraceId, string? RpcId, string? Method,
    string? ToolName, string PeerIp, string HttpMethod, string Url, string HttpProtocol,
    Dictionary<string, string[]> RequestHeaders, string RequestBody, long RequestBytes,
    bool RequestComplete = true, string? CaptureError = null);
public sealed record McpCallCompletion(int HttpStatus, Dictionary<string, string[]> ResponseHeaders,
    string ResponseBody, long ResponseBytes, string Outcome, string? Code, long DurationMilliseconds,
    bool ResponseComplete = true, string? CaptureError = null);
public sealed record McpCallQuery(DateTimeOffset? From = null, DateTimeOffset? To = null, string? KeyId = null,
    string? ToolName = null, string? Method = null, string? PeerIp = null, string? Outcome = null,
    string? Cursor = null, int PageSize = 30);
public sealed record McpCallSummary(string Id, string? KeyId, string? KeyName, string? ActorId,
    string? ActorName, string TraceId, string? RpcId, string? Method, string? ToolName, string PeerIp,
    string HttpMethod, string Url, string HttpProtocol, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt,
    long? DurationMilliseconds, int? HttpStatus, string Outcome, string? Code, long RequestBytes,
    long? ResponseBytes, bool RequestComplete, bool? ResponseComplete, string? CaptureError);
public sealed record McpCallDetail(McpCallSummary Summary, Dictionary<string, string[]> RequestHeaders,
    string RequestBody, Dictionary<string, string[]> ResponseHeaders, string? ResponseBody);
public sealed record McpCallPage(IReadOnlyList<McpCallSummary> Items, string? NextCursor, bool HasMore);
public sealed record McpCleanupResult(DateTimeOffset CompletedAt, int Deleted);
