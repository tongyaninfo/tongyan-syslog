using Syslog.Api.Common;
using Syslog.Api.Models;
using Syslog.Api.Service;
using Syslog.Shared.Models;

namespace Syslog.Api.Service.impl;

/// <summary>MCP 入口的独立开关与有界调用租约，不停止宿主或其他后台服务。</summary>
public sealed class McpRuntime(IMcpStore store) : IMcpRuntime
{
    public const int MaximumCalls = 16;
    public const int MaximumCallsPerKey = 4;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly Dictionary<long, int> _active = [];
    private int _activeCalls;
    private DateTimeOffset? _lastCleanup;
    private string? _cleanupError;
    private readonly DateTimeOffset _recoveryCutoff = DateTimeOffset.UtcNow;
    private bool _recoveryAvailable = true;

    public async Task<ApiResult<McpServiceView>> GetViewAsync(CancellationToken ct)
    {
        var settings = await store.ReadMcpSettingsAsync(ct);
        return settings.Success && settings.Data is { } value
            ? ApiResult<McpServiceView>.Ok(View(value))
            : ApiResult<McpServiceView>.Fail(settings.Code, settings.Message);
    }

    public async Task<ApiResult<McpServiceView>> SetEnabledAsync(SetMcpEnabledRequest input, long actorId, string token, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            bool needsRecovery;
            lock (_stateGate) needsRecovery = input.Enabled && !_recoveryAvailable;
            if (needsRecovery)
            {
                try
                {
                    // 首次初始化或存储恢复后，管理员可立即重试启动，不必等待后台维护周期。
                    await store.RecoverMcpCallsAsync(ct, _recoveryCutoff);
                    ReportRecovery(true);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception)
                {
                    return ApiResult<McpServiceView>.Fail("mcp_recovery_unavailable", "MCP 调用记录恢复失败，服务尚未启动。");
                }
            }
            var result = await store.SetMcpEnabledAsync(input, actorId, token, ct);
            return result.Success && result.Data is { } settings
                ? ApiResult<McpServiceView>.Ok(View(settings))
                : ApiResult<McpServiceView>.Fail(result.Code, result.Message);
        }
        finally { _gate.Release(); }
    }

    public async Task<ApiResult<IDisposable>> EnterAsync(long keyId, CancellationToken ct)
    {
        // 开关保存和新调用登记共用闸门，关闭成功后不会再登记新的调用。
        await _gate.WaitAsync(ct);
        try
        {
            var settings = await store.ReadMcpSettingsAsync(ct);
            if (!settings.Success) return ApiResult<IDisposable>.Fail(settings.Code, settings.Message);
            if (settings.Data?.Enabled != true) return ApiResult<IDisposable>.Fail("mcp_disabled", "MCP 服务已停止。");
            lock (_stateGate)
            {
                if (!_recoveryAvailable)
                    return ApiResult<IDisposable>.Fail("mcp_recovery_unavailable", "MCP 调用记录尚未完成恢复，请稍后重试或重新启动 MCP 服务。");
                _active.TryGetValue(keyId, out var count);
                if (_activeCalls >= MaximumCalls || count >= MaximumCallsPerKey)
                    return ApiResult<IDisposable>.Fail("mcp_busy", "MCP 并发调用已达上限，请稍后重试。");
                _active[keyId] = count + 1;
                _activeCalls++;
            }
            return ApiResult<IDisposable>.Ok(new Lease(this, keyId));
        }
        finally { _gate.Release(); }
    }

    public void ReportCleanup(DateTimeOffset? completedAt, string? error)
    {
        lock (_stateGate)
        {
            if (completedAt is not null) _lastCleanup = completedAt;
            _cleanupError = error;
        }
    }

    public void ReportRecovery(bool available)
    {
        lock (_stateGate) _recoveryAvailable = available;
    }

    private McpServiceView View(McpSettingsView settings)
    {
        lock (_stateGate)
            return new(settings, !_recoveryAvailable ? "faulted" : settings.Enabled ? "running" : _activeCalls == 0 ? "stopped" : "stopping",
                _activeCalls, McpToolCatalog.All.Count, _lastCleanup, _cleanupError,
                _recoveryAvailable ? null : "mcp_audit_recovery_failed");
    }

    private void Exit(long keyId)
    {
        lock (_stateGate)
        {
            if (--_active[keyId] == 0) _active.Remove(keyId);
            _activeCalls--;
        }
    }

    private sealed class Lease(McpRuntime owner, long keyId) : IDisposable
    {
        private int _disposed;
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0) owner.Exit(keyId); }
    }
}
