using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Syslog.Api.Common;
using Syslog.Api.Models;
using Syslog.Api.Service.impl;
using Syslog.Shared.Models;

namespace Syslog.Api.Hubs;

[Authorize(Roles = "admin")]
public sealed class LiveLogsHub(LiveLogFeed feed) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var token = Context.GetHttpContext()?.Request.Cookies[SessionAuthenticationHandler.CookieName];
        if (string.IsNullOrEmpty(token) || !long.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !feed.Connect(Context.ConnectionId, userId, token, Context.Abort))
        { Context.Abort(); return; }
        await base.OnConnectedAsync();
    }

    public Task<ApiResult<bool>> Subscribe(LiveLogSubscribeRequest input) =>
        feed.SubscribeAsync(Context.ConnectionId, input, Context.ConnectionAborted);

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        feed.Disconnect(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
