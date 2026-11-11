using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Syslog.Api.Models;

public sealed record SetupStatus(bool SetupRequired, bool CanInitialize);

// 管理列表不携带密码散列、会话摘要或其他凭据材料。
public sealed record ManagedUser(
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long Id,
    string Username, string Role, bool Enabled, DateTimeOffset CreatedAt,
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long Version);

public sealed record ManagedUserPage(IReadOnlyList<ManagedUser> Items, long Total, int Page, int PageSize);
public sealed record UserQuery(string? Search = null, bool? Enabled = null, int Page = 1, int PageSize = 20);
public sealed record CreateUserRequest([Required, MaxLength(64)] string Username, [Required, MaxLength(1024)] string Password);
// bool 默认值不能代表调用方明确选择停用，缺少 enabled 必须在反序列化时拒绝。
public sealed record UpdateUserRequest([Required, MaxLength(64)] string Username, [property: JsonRequired] bool Enabled,
    [Required, MaxLength(20)] string ExpectedVersion);
public sealed record ResetUserPasswordRequest([Required, MaxLength(1024)] string NewPassword,
    [Required, MaxLength(20)] string ExpectedVersion);
