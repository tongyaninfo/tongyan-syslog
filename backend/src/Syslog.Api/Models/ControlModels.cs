using System.Text.Json.Serialization;

namespace Syslog.Api.Models;

// 首版仅提供管理员角色；不将尚未实现的来源级授权标为可用。
public sealed record ControlUser(
    [property: JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)] long Id,
    string Username,
    string Role);

public sealed record SessionTicket(string Token, DateTimeOffset ExpiresAt, ControlUser User);

public sealed record SourceInput(string Name, string PeerAddress, string TimeZoneId, int RetentionDays, bool Enabled = true,
    int[]? IgnoredSeverities = null);
