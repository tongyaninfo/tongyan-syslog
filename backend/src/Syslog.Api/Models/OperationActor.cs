namespace Syslog.Api.Models;

/// <summary>仅由已认证的服务端入口构造，显式区分会话与 MCP 身份，不把 API Key 伪装为 Cookie。</summary>
public sealed record OperationActor
{
    private OperationActor(long userId, string? sessionToken, long? mcpKeyId, string? permission)
    {
        UserId = userId;
        SessionToken = sessionToken;
        McpKeyId = mcpKeyId;
        Permission = permission;
    }

    public long UserId { get; }
    public string? SessionToken { get; }
    public long? McpKeyId { get; }
    public string? Permission { get; }
    public bool IsMcp => McpKeyId.HasValue;
    public static OperationActor Session(long userId, string token) => new(userId, token, null, null);
    public static OperationActor Mcp(long userId, long keyId, string permission) => new(userId, null, keyId, permission);
}
