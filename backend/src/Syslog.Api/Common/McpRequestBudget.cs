namespace Syslog.Api.Common;

/// <summary>限制 Key 与连接 IP 的请求速率；窗口缓存有上界，避免任意来源扩大内存。</summary>
public sealed class McpRequestBudget(TimeProvider clock)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (long Minute, int Count)> _windows = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _captures = new(32, 32);

    // 在读取请求体之前限制大块捕获缓冲；超额请求只保存元信息和明确的不完整标记。
    public IDisposable? TryEnterCapture() => _captures.Wait(0) ? new CaptureLease(_captures) : null;

    private sealed class CaptureLease(SemaphoreSlim gate) : IDisposable
    {
        private int _disposed;
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) gate.Release(); }
    }
    public bool TryAcquire(string ip, long? keyId)
    {
        var minute = clock.GetUtcNow().ToUnixTimeSeconds() / 60;
        lock (_gate)
        {
            if (_windows.Count >= 4096)
            {
                foreach (var entry in _windows.Where(entry => entry.Value.Minute < minute).Select(entry => entry.Key).ToArray())
                    _windows.Remove(entry);
            }
            return Take("ip:" + ip, minute, 240) && (keyId is null || Take("key:" + keyId.Value, minute, 120));
        }
    }
    private bool Take(string key, long minute, int limit)
    {
        if (!_windows.TryGetValue(key, out var window))
        {
            if (_windows.Count >= 4096) return false;
            window = (minute, 0);
        }
        if (window.Minute != minute) window = (minute, 0);
        if (window.Count >= limit) return false;
        _windows[key] = (minute, window.Count + 1);
        return true;
    }
}
