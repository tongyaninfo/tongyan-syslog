using Microsoft.AspNetCore.Http.Features;
using Syslog.Ingest.Service.impl;
using Syslog.Shared.Models;

namespace Syslog.Api.Middleware;

/// <summary>统一错误和同源写入边界；不把存储异常、内部路径或日志正文回传给调用者。</summary>
public sealed class ApiBoundaryMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        // MCP 使用独立 Bearer、完整审计和 JSON-RPC 错误，不能被管理 API 的错误封装替换。
        if (context.Request.Path.StartsWithSegments("/mcp")) { await next(context); return; }
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        if (context.Request.Path.StartsWithSegments("/hubs/overview") || context.Request.Path.StartsWithSegments("/hubs/live-logs"))
        {
            if (!context.Request.IsHttps)
            {
                await FailAsync(context, 400, "https_required", "实时看板必须通过 HTTPS。");
                return;
            }
            // Cookie 是唯一登录凭据，WebSocket 不使用 URL token，也不接受跨站 Cookie 订阅。
            var origin = context.Request.Headers.Origin.ToString();
            var sameOrigin = Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                && uri.UserInfo.Length == 0 && uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0
                && uri.GetLeftPart(UriPartial.Authority).Equals($"{context.Request.Scheme}://{context.Request.Host}", StringComparison.OrdinalIgnoreCase);
            // HTTP/2 WebSocket 使用扩展 CONNECT；浏览器握手不能携带自定义写入头。
            // 只认服务器确认的 websocket 扩展协议，普通 CONNECT 不能借此绕过请求边界。
            var isConnect = HttpMethods.IsConnect(context.Request.Method);
            var isWebSocketConnect = isConnect && context.Features.Get<IHttpExtendedConnectFeature>()
                is { IsExtendedConnect: true, Protocol: "websocket" };
            if (context.Request.Query.ContainsKey("access_token") ||
                isConnect && !isWebSocketConnect ||
                (HttpMethods.IsGet(context.Request.Method) || isWebSocketConnect ? !sameOrigin :
                    context.Request.Headers["X-Syslog-Request"] != "1" || origin.Length != 0 && !sameOrigin))
            {
                await FailAsync(context, 403, "csrf_rejected", "实时连接来源校验失败。");
                return;
            }
        }
        if (context.Request.Path.StartsWithSegments("/api") && !HttpMethods.IsGet(context.Request.Method)
            && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method))
        {
            var origin = context.Request.Headers.Origin.ToString();
            var certificateUpload = HttpMethods.IsPost(context.Request.Method)
                && string.Equals(context.Request.Path.Value?.TrimEnd('/'), "/api/admin/v1/system/https-certificate", StringComparison.OrdinalIgnoreCase);
            // 上传允许不匹配的 Origin，仍要求自定义头。未启用 CORS，跨站表单无法加头。
            if (context.Request.Headers["X-Syslog-Request"] != "1" ||
                !certificateUpload && origin.Length != 0 && (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                    || uri.GetLeftPart(UriPartial.Authority) != $"{context.Request.Scheme}://{context.Request.Host}"))
            {
                await FailAsync(context, 403, "csrf_rejected", "请求来源校验失败。");
                return;
            }
        }
        try { await next(context); }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        catch (ClickHouseStoreException error)
        {
            var status = error.Code switch
            {
                "invalid_query" or "unsupported_search_token" => 400,
                "source_forbidden" => 403,
                "storage_timeout" => 504,
                _ => 503
            };
            await FailAsync(context, status, error.Code, error.Message);
        }
        catch (BadHttpRequestException) { await FailAsync(context, 400, "invalid_request", "请求格式无效。"); }
        catch (Exception) { await FailAsync(context, 503, "service_unavailable", "服务暂不可用，请检查运行状态。"); }
    }

    private static Task FailAsync(HttpContext context, int status, string code, string message)
    {
        if (context.Response.HasStarted) { context.Abort(); return Task.CompletedTask; }
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(ApiResult<object>.Fail(code, message), context.RequestAborted);
    }
}
