using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Syslog.Api.Common;
using Syslog.Api.Service.impl;

namespace Syslog.Api.Hubs;

/// <summary>只向当前认证管理员推送看板与监听状态。</summary>
[Authorize(Roles = "admin")]
public sealed class OverviewHub(OverviewFeed feed) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var request = Context.GetHttpContext()?.Request;
        var token = request?.Cookies[SessionAuthenticationHandler.CookieName];
        var listenerView = false;
        if (request is not null && request.Query.TryGetValue("view", out var views))
        {
            if (views.Count != 1 || views[0] != "listeners")
            {
                Context.Abort();
                return;
            }
            listenerView = true;
        }
        string? sourceId = null;
        if (request is not null && request.Query.TryGetValue("sourceId", out var sourceIds))
        {
            if (listenerView || sourceIds.Count != 1 || !long.TryParse(sourceIds[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
            {
                Context.Abort();
                return;
            }
            sourceId = id.ToString(CultureInfo.InvariantCulture);
        }
        if (string.IsNullOrEmpty(token) || !long.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier),
            NumberStyles.None, CultureInfo.InvariantCulture, out var userId) ||
            !await feed.ConnectAsync(Context.ConnectionId, userId, token, sourceId, listenerView, Context.Abort, Context.ConnectionAborted))
        {
            Context.Abort();
            return;
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        feed.Disconnect(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
