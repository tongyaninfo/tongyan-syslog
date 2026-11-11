using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Syslog.Api.Middleware;
using Xunit;

namespace Syslog.Api.Tests;

public sealed class ApiBoundaryMiddlewareTests
{
    [Theory]
    [InlineData("/api/admin/v1/system/https-certificate", true, 204)]
    [InlineData("/api/admin/v1/system/https-certificate", false, 403)]
    [InlineData("/api/admin/v1/system/listeners", true, 403)]
    public async Task CertificateUploadAloneSkipsOriginComparison(string path, bool guard, int expected)
    {
        using var services = new ServiceCollection().AddOptions().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost", 8020);
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = path;
        context.Request.Headers.Origin = "https://example.invalid";
        if (guard) context.Request.Headers["X-Syslog-Request"] = "1";
        using var response = new MemoryStream();
        context.Response.Body = response;
        var middleware = new ApiBoundaryMiddleware(next =>
        {
            next.Response.StatusCode = 204;
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(context);
        Assert.Equal(expected, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("websocket", true, "https://localhost:8020", false, false, 204)]
    [InlineData("websocket", true, "https://localhost:8020", true, false, 204)]
    [InlineData("websocket", true, null, false, false, 403)]
    [InlineData("websocket", true, "https://example.invalid", false, false, 403)]
    [InlineData("websocket", true, "https://localhost:8020/path", false, false, 403)]
    [InlineData("websocket", true, "https://localhost:8020", false, true, 403)]
    [InlineData("websocket", false, "https://localhost:8020", true, false, 403)]
    [InlineData("webtransport", true, "https://localhost:8020", true, false, 403)]
    [InlineData(null, false, "https://localhost:8020", true, false, 403)]
    public async Task Http2ConnectRequiresServerConfirmedWebSocketAndExactOrigin(
        string? protocol, bool extended, string? origin, bool guard, bool queryToken, int expected)
    {
        using var services = new ServiceCollection().AddOptions().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost", 8020);
        context.Request.Protocol = "HTTP/2";
        context.Request.Method = HttpMethods.Connect;
        context.Request.Path = "/hubs/overview";
        if (origin is not null) context.Request.Headers.Origin = origin;
        if (guard) context.Request.Headers["X-Syslog-Request"] = "1";
        if (queryToken) context.Request.QueryString = new QueryString("?access_token=synthetic");
        if (protocol is not null)
            context.Features.Set<IHttpExtendedConnectFeature>(new SyntheticExtendedConnectFeature(protocol, extended));
        using var response = new MemoryStream();
        context.Response.Body = response;
        var reached = false;
        var middleware = new ApiBoundaryMiddleware(next =>
        {
            reached = true;
            next.Response.StatusCode = 204;
            return Task.CompletedTask;
        });
        await middleware.InvokeAsync(context);
        Assert.Equal(expected, context.Response.StatusCode);
        Assert.Equal(expected == 204, reached);
    }
}

// 只模拟 Kestrel 提供的协议事实，不接受真实网络连接，也不执行流升级。
internal sealed class SyntheticExtendedConnectFeature(string protocol = "websocket", bool extended = true) : IHttpExtendedConnectFeature
{
    public bool IsExtendedConnect => extended;
    public string Protocol => protocol;
    public ValueTask<Stream> AcceptAsync() => throw new NotSupportedException("Only protocol classification is under test.");
}
