using Microsoft.Extensions.FileProviders;
using Syslog.Shared.Models;

namespace Syslog.Api.Common;

/// <summary>管理台与 API 共用来源；只为已登记页面提供 SPA 入口，API 永远不回落为 HTML。</summary>
public static class StaticUiExtensions
{
    private static readonly string[] PageRoutes =
        ["/", "/overview", "/logs", "/live-logs", "/unregistered", "/sources", "/listeners", "/https-certificate", "/notifications",
            "/retention", "/users", "/system/mcp", "/user/setup", "/user/login"];

    public static IApplicationBuilder UseSyslogStaticFiles(this IApplicationBuilder app)
    {
        var environment = app.ApplicationServices.GetRequiredService<IWebHostEnvironment>();
        // 源码运行时可能尚未构建前端；NullFileProvider 只让静态请求继续下传，不创建目录。
        var provider = environment.WebRootFileProvider ?? new NullFileProvider();
        app.UseWhen(context => !context.Request.Path.StartsWithSegments("/api") && !context.Request.Path.StartsWithSegments("/health")
            && !context.Request.Path.StartsWithSegments("/mcp"),
            branch => branch.UseStaticFiles(new StaticFileOptions { FileProvider = provider }));
        return app;
    }

    public static IEndpointRouteBuilder MapSyslogUi(this IEndpointRouteBuilder endpoints)
    {
        var environment = endpoints.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        foreach (var route in PageRoutes)
        {
            endpoints.MapMethods(route, [HttpMethods.Get, HttpMethods.Head], async context =>
            {
                var index = environment.WebRootFileProvider?.GetFileInfo("index.html");
                if (index is null || !index.Exists || index.IsDirectory)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await context.Response.WriteAsJsonAsync(ApiResult<object>.Fail("ui_not_built", "管理台资源尚未构建。"), context.RequestAborted);
                    return;
                }
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.Headers.CacheControl = "no-store";
                context.Response.ContentLength = index.Length;
                if (HttpMethods.IsHead(context.Request.Method)) return;
                await using var stream = index.CreateReadStream();
                await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
            }).AllowAnonymous();
        }

        endpoints.MapFallback("/api/{**path}", async context =>
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(ApiResult<object>.Fail("not_found", "接口不存在。"), context.RequestAborted);
        }).AllowAnonymous();
        return endpoints;
    }
}
