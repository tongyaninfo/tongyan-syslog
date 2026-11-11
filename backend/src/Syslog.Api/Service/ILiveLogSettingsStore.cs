using Syslog.Api.Models;
using Syslog.Shared.Models;

namespace Syslog.Api.Service;

/// <summary>实时日志只暂存在内存；控制库仅持久化缓存容量与可靠管理审计。</summary>
public interface ILiveLogSettingsStore
{
    Task<ApiResult<SavedLiveLogSettings>> ReadLiveLogSettingsAsync(CancellationToken ct);
    Task<ApiResult<SavedLiveLogSettings>> SaveLiveLogSettingsAsync(SaveLiveLogSettingsRequest input,
        long actorId, string token, CancellationToken ct);
}
