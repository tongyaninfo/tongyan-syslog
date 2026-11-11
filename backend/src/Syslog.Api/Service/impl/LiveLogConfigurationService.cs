using Syslog.Api.Models;
using Syslog.Shared.Models;
using Syslog.Shared.Service;

namespace Syslog.Api.Service.impl;

/// <summary>持久配置成功后才应用内存限制；旧库只停用实时缓存，不自动迁移或影响接收。</summary>
public sealed class LiveLogConfigurationService(ILiveLogSettingsStore store, ILiveLogBuffer buffer) : IHostedService
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task StartAsync(CancellationToken ct)
    {
        try { _ = await ReadAsync(ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { buffer.SetAvailable(false); }
    }
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task<ApiResult<LiveLogSettingsView>> ReadAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { return Apply(await store.ReadLiveLogSettingsAsync(ct)); }
        finally { gate.Release(); }
    }

    public async Task<ApiResult<LiveLogSettingsView>> SaveAsync(SaveLiveLogSettingsRequest input, long actorId, string token, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { return Apply(await store.SaveLiveLogSettingsAsync(input, actorId, token, ct), disableOnFailure: false); }
        finally { gate.Release(); }
    }

    private ApiResult<LiveLogSettingsView> Apply(ApiResult<SavedLiveLogSettings> result, bool disableOnFailure = true)
    {
        if (!result.Success || result.Data is null)
        {
            if (disableOnFailure) buffer.SetAvailable(false);
            return ApiResult<LiveLogSettingsView>.Fail(result.Code, result.Message);
        }
        buffer.ApplySettings(new(result.Data.PerDeviceLimit, result.Data.MaxBytes));
        buffer.SetAvailable(true);
        return ApiResult<LiveLogSettingsView>.Ok(new(result.Data, buffer.GetStats()));
    }
}
