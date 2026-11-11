using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Syslog.Api.Common;
using Syslog.Api.Middleware;
using Xunit;

namespace Syslog.Api.Tests;

public sealed partial class McpHttpTests
{
    [Fact]
    public async Task ExhaustedRequestCaptureBudgetRejectsAndAuditsIncompleteRequest()
    {
        await using var app = await Harness.StartAsync(permissions: ["syslog_sources_create"], mode: "read_write");
        var budget = app.Services.GetRequiredService<McpRequestBudget>();
        var captures = new List<IDisposable>();
        try
        {
            for (var index = 0; index < 32; index++) captures.Add(Assert.IsAssignableFrom<IDisposable>(budget.TryEnterCapture()));
            using var response = await app.Client.SendAsync(app.Request("""
                {"jsonrpc":"2.0","id":35,"method":"tools/call","params":{"name":"syslog_sources_create","arguments":{"name":"not-captured","peerAddress":"192.0.2.204","timeZoneId":"UTC","retentionDays":30}}}
                """));
            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
            Assert.Equal("request_capture_capacity_exceeded", await ErrorCodeAsync(response));
            Assert.Empty(await app.Control.ListAsync(Ct));
            var audit = (await app.Control.GetMcpCallAsync(response.Headers.GetValues("X-Syslog-Mcp-Call-Id").Single(), Ct)).Data!;
            Assert.False(audit.Summary.RequestComplete);
            Assert.Equal("request_capture_capacity_exceeded", audit.Summary.CaptureError);
            Assert.Equal("rejected", audit.Summary.Outcome);
            Assert.Empty(audit.RequestBody);
            Assert.True(audit.Summary.RequestBytes > 0);
        }
        finally { foreach (var capture in captures) capture.Dispose(); }
        using var recovered = budget.TryEnterCapture();
        Assert.NotNull(recovered);
    }

    [Fact]
    public async Task InterruptedRequestStreamRetainsReadPrefixAndDoesNotExecute()
    {
        await using var app = await Harness.StartAsync();
        const string prefix = "{\"jsonrpc\":\"2.0\",\"id\":";
        using var originalResponse = new MemoryStream();
        using var requestBody = new PrefixThenFailureStream(Encoding.UTF8.GetBytes(prefix));
        var context = new DefaultHttpContext();
        context.RequestServices = app.Services;
        context.Request.Method = "POST";
        context.Request.Path = "/mcp";
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost");
        context.Request.Headers.Authorization = "Bearer " + app.Secret;
        context.Request.ContentLength = 100;
        context.Request.Body = requestBody;
        context.Response.Body = originalResponse;
        var executed = false;
        var middleware = new McpBoundaryMiddleware(_ => { executed = true; return Task.CompletedTask; });
        await middleware.InvokeAsync(context, app.Control, app.Runtime, app.Services.GetRequiredService<McpRequestBudget>());
        Assert.False(executed);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        var audit = (await app.Control.GetMcpCallAsync(context.Response.Headers["X-Syslog-Mcp-Call-Id"].ToString(), Ct)).Data!;
        Assert.False(audit.Summary.RequestComplete);
        Assert.Equal("request_interrupted", audit.Summary.CaptureError);
        Assert.Equal("rejected", audit.Summary.Outcome);
        Assert.Equal(prefix, audit.RequestBody);
        Assert.Equal(100, audit.Summary.RequestBytes);
        Assert.Equal(Encoding.UTF8.GetString(originalResponse.ToArray()), audit.ResponseBody);
        Assert.DoesNotContain("synthetic transport failure", audit.ResponseBody);
    }

    [Fact]
    public async Task CompletionAuditFailureReturnsUnknownWithoutRepeatingCommittedWrite()
    {
        await using var app = await Harness.StartAsync(permissions: ["syslog_sources_create"], mode: "read_write");
        // 只在隔离内存库模拟提交响应审计失败；来源写入应已完成且不可自动重放。
        await app.ExecuteAsync("CREATE TRIGGER fail_mcp_completion BEFORE UPDATE ON mcp_call_audits WHEN NEW.outcome <> 'started' BEGIN SELECT RAISE(ABORT,'synthetic response failure'); END;");
        using var request = app.Request("""
            {"jsonrpc":"2.0","id":31,"method":"tools/call","params":{"name":"syslog_sources_create","arguments":{"name":"committed","peerAddress":"192.0.2.201","timeZoneId":"UTC","retentionDays":30}}}
            """);
        using var response = await app.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("mcp_audit_result_unknown", await ErrorCodeAsync(response));
        Assert.Single(await app.Control.ListAsync(Ct));
        var callId = response.Headers.GetValues("X-Syslog-Mcp-Call-Id").Single();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(callId, body.RootElement.GetProperty("error").GetProperty("data").GetProperty("callId").GetString());
        var audit = (await app.Control.GetMcpCallAsync(callId, Ct)).Data!;
        Assert.Equal("started", audit.Summary.Outcome);
        Assert.Null(audit.ResponseBody);
    }

    [Fact]
    public async Task DuplicateRawArgumentsAreRejectedBeforeWriteAndRetainedInAudit()
    {
        await using var app = await Harness.StartAsync(permissions: ["syslog_sources_create"], mode: "read_write");
        const string body = """
            {"jsonrpc":"2.0","id":32,"method":"tools/call","params":{"name":"syslog_sources_create","arguments":{"name":"first","name":"second","peerAddress":"192.0.2.202","timeZoneId":"UTC","retentionDays":30}}}
            """;
        using var response = await app.Client.SendAsync(app.Request(body));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var rpc = ParseBoundaryResponse(await response.Content.ReadAsStringAsync(), response.Content.Headers.ContentType?.MediaType);
        Assert.True(rpc.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        Assert.Equal("invalid_request", rpc.RootElement.GetProperty("result").GetProperty("structuredContent").GetProperty("code").GetString());
        Assert.Empty(await app.Control.ListAsync(Ct));
        var audit = (await app.Control.GetMcpCallAsync(response.Headers.GetValues("X-Syslog-Mcp-Call-Id").Single(), Ct)).Data!;
        Assert.Equal(body, audit.RequestBody);
        Assert.Equal("failed", audit.Summary.Outcome);
        Assert.Equal("invalid_request", audit.Summary.Code);
    }

    [Theory]
    [InlineData("2025-11-25", "{\"jsonrpc\":\"2.0\",\"id\":33,\"method\":\"synthetic/nonexistent\"}")]
    [InlineData("2026-07-28", "{\"jsonrpc\":\"2.0\",\"id\":33,\"method\":\"synthetic/nonexistent\"}")]
    [InlineData("2025-11-25", "{broken-json")]
    public async Task JsonRpcErrorsAreAuditedAsErrorsWithExactWireResponse(string version, string body)
    {
        await using var app = await Harness.StartAsync();
        using var request = app.Request(body);
        request.Headers.Remove("MCP-Protocol-Version");
        request.Headers.Add("MCP-Protocol-Version", version);
        using var response = await app.Client.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();
        using var rpc = ParseBoundaryResponse(responseBody, response.Content.Headers.ContentType?.MediaType);
        Assert.True(rpc.RootElement.TryGetProperty("error", out _));
        var audit = (await app.Control.GetMcpCallAsync(response.Headers.GetValues("X-Syslog-Mcp-Call-Id").Single(), Ct)).Data!;
        Assert.NotEqual("success", audit.Summary.Outcome);
        Assert.Equal("mcp_protocol_error", audit.Summary.Code);
        Assert.Equal(body, audit.RequestBody);
        Assert.Equal(responseBody, audit.ResponseBody);
    }

    [Theory]
    [InlineData("syslog_sources_list", "failed")]
    [InlineData("syslog_sources_create", "unknown")]
    public async Task ResponseOverflowIsBoundedAuditedAndKeepsWriteUncertainty(string tool, string outcome)
    {
        await using var app = await Harness.StartAsync(permissions: [tool], mode: "read_write");
        using var originalResponse = new MemoryStream();
        using var requestBody = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0", id = 34, method = "tools/call", @params = new { name = tool, arguments = new { } }
        })));
        var context = new DefaultHttpContext();
        context.RequestServices = app.Services;
        context.Request.Method = "POST";
        context.Request.Path = "/mcp";
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost");
        context.Request.Headers.Authorization = "Bearer " + app.Secret;
        context.Request.Body = requestBody;
        context.Response.Body = originalResponse;
        var entered = false;
        var middleware = new McpBoundaryMiddleware(async http =>
        {
            entered = true;
            // 模拟业务已进入执行，输出超过捕获预算；不接触真实日志存储或接收端口。
            await http.Response.Body.WriteAsync(new byte[McpHttpProtocol.MaximumResponseBytes + 1], Ct);
        });
        await middleware.InvokeAsync(context, app.Control, app.Runtime, app.Services.GetRequiredService<McpRequestBudget>());
        Assert.True(entered);
        Assert.Equal(503, context.Response.StatusCode);
        Assert.InRange(originalResponse.Length, 1, 4096);
        var audit = (await app.Control.GetMcpCallAsync(context.Response.Headers["X-Syslog-Mcp-Call-Id"].ToString(), Ct)).Data!;
        Assert.Equal(outcome, audit.Summary.Outcome);
        Assert.False(audit.Summary.ResponseComplete);
        Assert.Equal("response_too_large", audit.Summary.CaptureError);
        Assert.Equal(Encoding.UTF8.GetString(originalResponse.ToArray()), audit.ResponseBody);
        Assert.Equal(0, (await app.Runtime.GetViewAsync(Ct)).Data!.ActiveCalls);
    }

    [Fact]
    public async Task ToolNotificationCannotPerformUnacknowledgedWrite()
    {
        await using var app = await Harness.StartAsync(permissions: ["syslog_sources_create"], mode: "read_write");
        using var request = app.Request("""
            {"jsonrpc":"2.0","method":"tools/call","params":{"name":"syslog_sources_create","arguments":{"name":"notification","peerAddress":"192.0.2.203","timeZoneId":"UTC","retentionDays":30}}}
            """);
        using var response = await app.Client.SendAsync(request);
        Assert.Empty(await app.Control.ListAsync(Ct));
    }

    private static JsonDocument ParseBoundaryResponse(string body, string? mediaType) => JsonDocument.Parse(
        mediaType == "text/event-stream" ? body.Split('\n').Last(line => line.StartsWith("data: ", StringComparison.Ordinal))[6..] : body);

    // 合成部分读取失败，不创建网络连接，也不依赖宿主或客户端中断时序。
    private sealed class PrefixThenFailureStream(byte[] prefix) : MemoryStream(prefix, writable: false)
    {
        private bool _returnedPrefix;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_returnedPrefix) return ValueTask.FromException<int>(new IOException("synthetic transport failure"));
            _returnedPrefix = true;
            return base.ReadAsync(buffer, cancellationToken);
        }
    }
}
