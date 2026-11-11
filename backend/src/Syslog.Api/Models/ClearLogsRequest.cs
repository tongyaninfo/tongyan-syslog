using System.Text.Json.Serialization;

namespace Syslog.Api.Models;

// 清空范围由路由确定，拒绝请求体夹带筛选字段。
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ClearLogsRequest(string? Confirmation);
