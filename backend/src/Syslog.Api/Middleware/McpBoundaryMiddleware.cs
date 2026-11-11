using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Syslog.Api.Common;
using Syslog.Api.Models;
using Syslog.Api.Service;
using Syslog.Api.Service.impl;
using Syslog.Shared.Models;

namespace Syslog.Api.Middleware;

/// <summary>每个 MCP HTTP 交换独立鉴权并完整审计；审计写入失败时不执行工具。</summary>
public sealed class McpBoundaryMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IMcpStore store, McpRuntime runtime, McpRequestBudget budget)
    {
        if (!context.Request.Path.StartsWithSegments("/mcp")) { await next(context); return; }
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.XContentTypeOptions = "nosniff";
        var watch = Stopwatch.StartNew();
        using var captureLease = budget.TryEnterCapture();
        var requestBody = context.Request.Body;
        var responseFeature = context.Features.Get<IHttpResponseBodyFeature>()!;
        var originalResponse = context.Response.Body;
        using var capturedResponse = new BoundedStream(McpHttpProtocol.MaximumResponseBytes);
        context.Response.Body = capturedResponse;
        IDisposable? active = null;
        string? auditId = null;
        JsonElement? rpcId = null;
        string? method = null;
        string? toolName = null;
        string? captureError = null;
        var requestComplete = true;
        var responseComplete = true;
        var outcome = "rejected";
        string? resultCode = null;
        byte[] bytes = [];
        using var requestDeadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        requestDeadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = McpHttpProtocol.MaximumRequestBytes;
            using var capturedRequest = new MemoryStream();
            if (captureLease is null)
            {
                requestComplete = false;
                captureError = "request_capture_capacity_exceeded";
            }
            else try
            {
                var chunk = new byte[8192];
                while (capturedRequest.Length <= McpHttpProtocol.MaximumRequestBytes)
                {
                    var count = await requestBody.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length,
                        McpHttpProtocol.MaximumRequestBytes + 1L - capturedRequest.Length)), requestDeadline.Token);
                    if (count == 0) break;
                    capturedRequest.Write(chunk, 0, count);
                }
            }
            catch (BadHttpRequestException error) when (error.StatusCode == 413)
            { requestComplete = false; captureError = "request_too_large"; }
            catch (OperationCanceledException)
            { requestComplete = false; captureError = "request_interrupted"; }
            catch (Exception error) when (error is IOException or BadHttpRequestException)
            { requestComplete = false; captureError = "request_interrupted"; }

            // 断线/超时仍保留已经读到的前缀，不能把部分捕获误标为完整空正文。
            bytes = capturedRequest.ToArray();
            if (bytes.Length > McpHttpProtocol.MaximumRequestBytes)
            {
                bytes = bytes[..McpHttpProtocol.MaximumRequestBytes];
                requestComplete = false;
                captureError = "request_too_large";
            }

            string bodyText;
            try { bodyText = new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException)
            {
                // 非 UTF-8 报文拒绝执行，保留原始字节的 Base64 并显式标记编码。
                bodyText = Convert.ToBase64String(bytes);
                captureError = captureError is null ? "invalid_utf8_body_saved_as_base64" : captureError + "; invalid_utf8_body_saved_as_base64";
            }
            if (captureError is null)
            {
                try
                {
                    using var json = JsonDocument.Parse(bytes);
                    if (json.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        if (json.RootElement.TryGetProperty("id", out var id) && id.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.Null)
                            rpcId = id.Clone();
                        if (json.RootElement.TryGetProperty("method", out var methodValue) && methodValue.ValueKind == JsonValueKind.String)
                            method = methodValue.GetString();
                        if (method == "tools/call" && json.RootElement.TryGetProperty("params", out var parameters)
                            && parameters.ValueKind == JsonValueKind.Object)
                        {
                            if (parameters.TryGetProperty("name", out var tool) && tool.ValueKind == JsonValueKind.String) toolName = tool.GetString();
                            // 保留原始对象，不能让 SDK 转 dictionary 后吞掉重复属性而绕过严格参数校验。
                            if (parameters.TryGetProperty("arguments", out var arguments)) context.Items[McpHttpProtocol.ArgumentsKey] = arguments.Clone();
                        }
                    }
                }
                catch (JsonException) { /* 具体 JSON-RPC 错误交由 SDK 处理；审计仍保留原文。 */ }
            }

            var authentication = await AuthenticateAsync(context, store);
            var principal = authentication.Data;
            var peerIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            // 取 RawTarget 而不重建查询字符串，避免审计改变参数顺序、编码及重复值。
            var rawTarget = context.Features.Get<IHttpRequestFeature>()?.RawTarget;
            var url = context.Request.Scheme + "://" + context.Request.Host +
                (string.IsNullOrEmpty(rawTarget) ? context.Request.PathBase + context.Request.Path + context.Request.QueryString : rawTarget);
            ApiResult<string> started;
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                started = await store.BeginMcpCallAsync(new McpCallStart(principal, context.TraceIdentifier, rpcId?.GetRawText(),
                    method, toolName, peerIp, context.Request.Method, url, context.Request.Protocol,
                    Headers(context.Request.Headers), bodyText, context.Request.ContentLength ?? bytes.Length,
                    requestComplete, captureError), timeout.Token);
            if (!started.Success || started.Data is null)
            {
                resultCode = started.Code == "mcp_migration_required" ? started.Code : "mcp_audit_unavailable";
                await RejectAsync(context, 503, resultCode, "MCP 审计存储不可用或尚未升级，未执行操作。", rpcId);
            }
            else
            {
                auditId = started.Data;
                context.Response.Headers["X-Syslog-Mcp-Call-Id"] = auditId;
                if (!budget.TryAcquire(peerIp, principal?.KeyId))
                    await Reject(429, "mcp_rate_limited", "MCP 请求过于频繁，请稍后重试。");
                else if (!requestComplete || captureError is not null)
                    await Reject(captureError == "request_too_large" ? 413 : captureLease is null ? 429 : 400,
                        captureError ?? "invalid_request", "MCP 请求未完整读取或编码无效。");
                else if (context.Request.Query.Keys.Any(key => key.Equals("access_token", StringComparison.OrdinalIgnoreCase)
                    || key.Equals("api_key", StringComparison.OrdinalIgnoreCase) || key.Equals("apikey", StringComparison.OrdinalIgnoreCase)))
                    await Reject(400, "mcp_header_required", "请通过 Authorization 请求头传入 API Key。");
                else if (!ValidOrigin(context))
                    await Reject(403, "mcp_origin_rejected", "MCP 请求来源无效。");
                else if (!authentication.Success || principal is null)
                {
                    context.Response.Headers.WWWAuthenticate = "Bearer realm=\"tongyan-syslog\"";
                    await Reject(401, authentication.Code, authentication.Message);
                }
                else if (!string.Equals(context.Request.Path.Value?.TrimEnd('/'), "/mcp", StringComparison.OrdinalIgnoreCase))
                    await Reject(404, "mcp_endpoint_not_found", "MCP 接口不存在。");
                else
                {
                    var admission = await runtime.EnterAsync(principal.KeyId, context.RequestAborted);
                    if (!admission.Success || admission.Data is null)
                        await Reject(admission.Code == "mcp_busy" ? 429 : 503, admission.Code, admission.Message);
                    else
                    {
                        active = admission.Data;
                        context.Items[McpHttpProtocol.PrincipalKey] = principal;
                        using var body = new MemoryStream(bytes, writable: false);
                        context.Request.Body = body;
                        outcome = "success";
                        try
                        {
                            await next(context);
                            await context.Response.BodyWriter.FlushAsync(CancellationToken.None);
                            outcome = context.Items[McpHttpProtocol.OutcomeKey] as string ?? (context.Response.StatusCode >= 400 ? "rejected" : "success");
                            resultCode = context.Items[McpHttpProtocol.ResultCodeKey] as string ?? (context.Response.StatusCode >= 400 ? "mcp_protocol_error" : "OK");
                            // JSON-RPC 协议错误可能使用 HTTP 200，审计不能仅依据 HTTP 状态判成功。
                            if (outcome == "success" && HasProtocolError(capturedResponse))
                            { outcome = "failed"; resultCode = "mcp_protocol_error"; }
                        }
                        catch (OperationCanceledException)
                        {
                            outcome = IsWrite(toolName) ? "unknown" : "cancelled";
                            await Reject(503, IsWrite(toolName) ? "mcp_operation_unknown" : "mcp_cancelled", "调用已中断，请核对调用记录；写操作不要自动重试。");
                        }
                        catch (ResponseBudgetException)
                        {
                            responseComplete = false;
                            captureError = "response_too_large";
                            outcome = IsWrite(toolName) ? "unknown" : "failed";
                            await Reject(503, "mcp_response_too_large", "MCP 响应超过单次预算，请缩小查询范围。");
                        }
                        catch (Exception)
                        {
                            outcome = IsWrite(toolName) ? "unknown" : "failed";
                            await Reject(503, IsWrite(toolName) ? "mcp_operation_unknown" : "service_unavailable", "调用未完成，请核对调用记录。");
                        }
                    }
                }
            }

            context.Response.ContentLength = capturedResponse.Length;
            if (auditId is not null)
            {
                bool completed;
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    completed = await store.CompleteMcpCallAsync(auditId, new McpCallCompletion(context.Response.StatusCode,
                        Headers(context.Response.Headers), Encoding.UTF8.GetString(capturedResponse.ToArray()), capturedResponse.Length,
                        outcome, resultCode, watch.ElapsedMilliseconds, responseComplete, captureError), timeout.Token);
                }
                catch { completed = false; }
                if (!completed)
                {
                    await Reject(503, "mcp_audit_result_unknown", "调用结果未能写入审计。请凭调用 ID 核对状态，不要自动重试写操作。");
                    context.Response.ContentLength = capturedResponse.Length;
                }
            }
        }
        catch (Exception)
        {
            // 不泄露数据库异常、工具参数、Bearer 或宿主路径。
            await RejectAsync(context, 503, "mcp_audit_unavailable", "MCP 审计服务不可用，请检查运行状态。", rpcId, capturedResponse, auditId);
            context.Response.ContentLength = capturedResponse.Length;
        }
        finally
        {
            context.Request.Body = requestBody;
            context.Features.Set(responseFeature);
            try
            {
                capturedResponse.Position = 0;
                await capturedResponse.CopyToAsync(originalResponse, context.RequestAborted);
            }
            catch (OperationCanceledException) { /* 应用响应已记录，客户端断开不回滚已发生的动作。 */ }
            finally { active?.Dispose(); }
        }

        async Task Reject(int status, string code, string message)
        {
            resultCode = code;
            await RejectAsync(context, status, code, message, rpcId, capturedResponse, auditId);
        }
    }

    private static bool IsWrite(string? toolName) => toolName is not null && McpToolCatalog.All.Any(tool => tool.Name == toolName && tool.IsWrite);

    private static bool HasProtocolError(MemoryStream response)
    {
        if (response.Length == 0) return false;
        try
        {
            using var json = JsonDocument.Parse(response.GetBuffer().AsMemory(0, (int)response.Length));
            return json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("error", out _);
        }
        catch (JsonException)
        {
            // 旧协议可返回一次性 SSE；只检查 SDK 生成的 data 帧，不从日志内容猜测错误。
            foreach (var line in Encoding.UTF8.GetString(response.GetBuffer(), 0, (int)response.Length).Split('\n'))
            {
                if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
                try
                {
                    using var data = JsonDocument.Parse(line[6..]);
                    if (data.RootElement.ValueKind == JsonValueKind.Object && data.RootElement.TryGetProperty("error", out _)) return true;
                }
                catch (JsonException) { }
            }
            return false;
        }
    }

    private static async Task<ApiResult<McpPrincipal>> AuthenticateAsync(HttpContext context, IMcpStore store)
    {
        var headers = context.Request.Headers.Authorization;
        if (headers.Count != 1 || headers[0] is not { } header || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            || header.Length > 512 || header[7..].Trim() is not { Length: > 0 } secret)
            return ApiResult<McpPrincipal>.Fail("mcp_authentication_required", "请提供有效的 MCP API Key。");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        return await store.AuthenticateMcpAsync(secret, timeout.Token);
    }

    private static bool ValidOrigin(HttpContext context)
    {
        var values = context.Request.Headers.Origin;
        if (values.Count == 0) return true;
        return values.Count == 1 && Uri.TryCreate(values[0], UriKind.Absolute, out var uri)
            && uri.UserInfo.Length == 0 && uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0
            && uri.GetLeftPart(UriPartial.Authority).Equals(context.Request.Scheme + "://" + context.Request.Host, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string[]> Headers(IHeaderDictionary headers) =>
        headers.ToDictionary(pair => pair.Key, pair => pair.Value.Select(value => value ?? "").ToArray(), StringComparer.OrdinalIgnoreCase);

    private static async Task RejectAsync(HttpContext context, int status, string code, string message, JsonElement? id,
        MemoryStream? buffer = null, string? callId = null)
    {
        if (buffer is not null) { buffer.SetLength(0); buffer.Position = 0; }
        context.Response.StatusCode = status;
        context.Response.ContentLength = null;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.Remove("Transfer-Encoding");
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            jsonrpc = "2.0", id, error = new { code = -32000, message, data = new { code, callId } }
        }, McpHttpProtocol.Json);
        await context.Response.Body.WriteAsync(payload, CancellationToken.None);
    }

    private sealed class ResponseBudgetException : IOException;

    private sealed class BoundedStream(int maximum) : MemoryStream
    {
        private void Check(int size) { if (Position + size > maximum) throw new ResponseBudgetException(); }
        public override void Write(byte[] buffer, int offset, int count) { Check(count); base.Write(buffer, offset, count); }
        public override void Write(ReadOnlySpan<byte> buffer) { Check(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte value) { Check(1); base.WriteByte(value); }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Write(buffer, offset, count); return Task.CompletedTask; }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); Write(buffer.Span); return ValueTask.CompletedTask; }
    }
}
