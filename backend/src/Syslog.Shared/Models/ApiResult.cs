namespace Syslog.Shared.Models;

/// <summary>业务响应保留独立 HTTP 状态，错误信息由服务端统一脱敏。</summary>
public sealed record ApiResult<T>(bool Success, string Code, string Message, T? Data)
{
    public static ApiResult<T> Ok(T data) => new(true, "OK", "成功", data);
    public static ApiResult<T> Fail(string code, string message) => new(false, code, message, default);
}
