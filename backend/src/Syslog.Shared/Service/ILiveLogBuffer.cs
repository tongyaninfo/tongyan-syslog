using Syslog.Shared.Models;

namespace Syslog.Shared.Service;

/// <summary>实时观察缓存独立于持久化，写入不能等待浏览器、网络或数据库。</summary>
public interface ILiveLogBuffer
{
    bool Capture(LogEvent item, bool ignored);
    void SetAvailable(bool available);
    void ApplySettings(LiveLogCacheSettings settings);
    LiveLogCacheStats GetStats();
    ApiResult<LiveLogSnapshot> ReadSnapshot(LiveLogFilter filter, IReadOnlyCollection<string> allowedSourceIds,
        string? beforeSequence = null, int take = 100, int maxBytes = 512 * 1024);
    ApiResult<LiveLogDelta> ReadAfter(LiveLogFilter filter, IReadOnlyCollection<string> allowedSourceIds,
        string epoch, string afterSequence, int take = 200, int maxBytes = 512 * 1024);
    ApiResult<LiveLogRecord> GetDetail(string id, IReadOnlyCollection<string> allowedSourceIds);
}
