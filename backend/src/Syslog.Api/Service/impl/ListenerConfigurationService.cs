using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Syslog.Api.Common;
using Syslog.Api.Models;
using Syslog.Ingest.Common;
using Syslog.Ingest.Models;
using Syslog.Ingest.Options;
using Syslog.Ingest.Service;
using Syslog.Shared.Models;

namespace Syslog.Api.Service.impl;

/// <summary>保存配置与运行期开关串行处理，开启接收时加载最新的已保存端点。</summary>
public sealed class ListenerConfigurationService : IListenerConfigurationService
{
    private readonly IControlStore store;
    private readonly IngestOptions options;
    private readonly IngestStatus status;
    private readonly IReceptionRuntime runtime;
    private readonly IListenerCertificateProvider? certificateProvider;
    private readonly bool forceDisabled;
    private readonly ListenerConfiguration baseline;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object snapshotGate = new();
    private ListenerConfiguration running;
    private RuntimeConfiguration published;
    private long revision = DateTime.UtcNow.Ticks;
    private bool initialized;
    private string startupFailure = "";

    public ListenerConfigurationService(IControlStore store, IOptions<IngestOptions> options,
        IngestStatus status, IConfiguration configuration, IReceptionRuntime runtime,
        IListenerCertificateProvider? certificateProvider = null)
    {
        this.store = store;
        this.options = options.Value;
        this.status = status;
        this.runtime = runtime;
        this.certificateProvider = certificateProvider;
        forceDisabled = configuration.GetValue<bool>("Ingest:ForceDisabled");
        // 兼容原有服务器 PFX 配置，管理端只看到引用标识，路径和密码始终留在进程内。
        for (var i = 0; i < this.options.Listeners.Count; i++)
        {
            var item = this.options.Listeners[i];
            if (item.Transport != "tls" || item.CertificateId.Length != 0 || string.IsNullOrWhiteSpace(item.CertificatePath)) continue;
            var id = $"legacy-tls-{i}";
            while (this.options.TlsCertificates.ContainsKey(id)) id += "x";
            this.options.TlsCertificates[id] = new TlsCertificateOptions
            { CertificatePath = item.CertificatePath, CertificatePassword = item.CertificatePassword };
            item.CertificateId = id;
        }
        var listeners = this.options.Listeners.Select(FromOptions).ToList();
        if (HasUniqueKnownProtocols(listeners))
            foreach (var item in ListenerConfiguration.Default().Listeners)
                if (!listeners.Any(existing => existing.Protocol == item.Protocol)) listeners.Add(item);
        baseline = Copy(new(this.options.Enabled, listeners));
        running = Copy(baseline);
        published = new(running, null, "", false);
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (initialized) return;
            initialized = true;
            SavedListenerSettings? savedConfiguration = null;
            try
            {
                // 新安装尚无管理员时保持接收关闭，状态探测不得创建库或执行迁移。
                if (await store.IsSetupRequiredAsync(ct))
                {
                    running = baseline with { Enabled = false };
                    Apply(running);
                    return;
                }
                var saved = await store.ReadListenerSettingsAsync(ct);
                if (!saved.Success)
                {
                    // 旧版本控制库可继续使用部署配置，网页保存须显式升级后才开放。
                    if (saved.Code != "listener_migration_required") throw new InvalidOperationException();
                }
                else if (saved.Data is { } value)
                {
                    if (value.Version != "0")
                    {
                        if (!ListenerSettingsValidation.TryNormalize(value.Configuration, out var normalized, out _))
                            throw new InvalidOperationException();
                        running = Copy(normalized);
                        savedConfiguration = value with { Configuration = running };
                    }
                    else if (HasUniqueKnownProtocols(baseline.Listeners))
                        savedConfiguration = value with { Configuration = baseline };
                }
                if (forceDisabled) running = running with { Enabled = false };
                Apply(running);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                // 配置读取故障不能悄悄退回旧端口；保留管理台供修复，数据面停止接收。
                startupFailure = "listener_configuration_unavailable";
                running = running with { Enabled = false };
                options.Enabled = false;
                status.ListenerFailureCode = startupFailure;
            }
            finally { PublishRuntime(savedConfiguration); }
        }
        finally { gate.Release(); }
    }

    public async Task<ApiResult<ListenerSettingsView>> ReadAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var result = await ReadSavedAsync(ct);
            return result.Data is { } saved
                ? ApiResult<ListenerSettingsView>.Ok(View(saved))
                : ApiResult<ListenerSettingsView>.Fail(result.Code, result.Message);
        }
        finally { gate.Release(); }
    }

    public Task<ApiResult<ListenerSettingsView>> SaveAsync(SaveListenerSettingsRequest request,
        long actorId, string token, CancellationToken ct) =>
        SaveAsync(request, OperationActor.Session(actorId, token), ct);

public async Task<ApiResult<ListenerSettingsView>> SaveAsync(SaveListenerSettingsRequest request,
        OperationActor actor, CancellationToken ct)
    {
        if (request is null)
            return ApiResult<ListenerSettingsView>.Fail("invalid_request", "监听配置请求无效。");
        if (!ListenerSettingsValidation.TryNormalize(request.Configuration, out var normalized, out var code))
            return ApiResult<ListenerSettingsView>.Fail(code, "监听配置无效，请检查协议、地址和端口。");
        if (MissingCertificate(normalized))
            return CertificateMissing();
        await gate.WaitAsync(ct);
        try
        {
            var saved = await store.SaveListenerSettingsAsync(request with { Configuration = normalized }, actor, ct);
            if (!saved.Success || saved.Data is null)
                return ApiResult<ListenerSettingsView>.Fail(saved.Code, saved.Message);
            return ApiResult<ListenerSettingsView>.Ok(View(saved.Data));
        }
        finally { gate.Release(); }
    }

    public Task<ApiResult<ListenerSettingsView>> SetReceptionAsync(bool enabled, long actorId, string token, CancellationToken ct,
        string? expectedVersion = null) =>
        SetReceptionAsync(enabled, OperationActor.Session(actorId, token), ct, expectedVersion);

public async Task<ApiResult<ListenerSettingsView>> SetReceptionAsync(bool enabled, OperationActor actor, CancellationToken ct,
        string? expectedVersion = null)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (enabled && forceDisabled)
                return ApiResult<ListenerSettingsView>.Fail("listener_forced_disabled", "服务器已强制关闭日志接收。");
            var result = await ReadSavedAsync(ct);
            if (result.Data is not { } saved)
                return ApiResult<ListenerSettingsView>.Fail(result.Code, result.Message);
            if (expectedVersion is not null && expectedVersion != saved.Version) return VersionConflict();
            if (running.Enabled == enabled && saved.Configuration.Enabled == enabled)
                return ApiResult<ListenerSettingsView>.Ok(View(saved));

            var desired = saved.Configuration with { Enabled = enabled };
            var nextRunning = enabled ? desired : running with { Enabled = false };
            var nextListeners = ResolveListeners(nextRunning);
            var update = await store.SaveListenerSettingsAsync(new(saved.Version, desired), actor, ct, applyReception: true);
            if (!update.Success || update.Data is null)
                return ApiResult<ListenerSettingsView>.Fail(update.Code, update.Message);

            // 已提交配置与审计后继续完成切换，即使浏览器断开请求。
            try { await runtime.ApplyAsync(enabled, nextListeners, CancellationToken.None); }
            catch (InvalidOperationException)
            {
                PublishRuntime(update.Data);
                return RuntimeUnavailable();
            }
            running = Copy(nextRunning);
            startupFailure = "";
            return ApiResult<ListenerSettingsView>.Ok(View(update.Data));
        }
        finally { gate.Release(); }
    }

    public Task<ApiResult<ListenerSettingsView>> SetProtocolReceptionAsync(string protocol, SetListenerProtocolReceptionRequest input,
        long actorId, string token, CancellationToken ct) =>
        SetProtocolReceptionAsync(protocol, input, OperationActor.Session(actorId, token), ct);

public async Task<ApiResult<ListenerSettingsView>> SetProtocolReceptionAsync(string protocol, SetListenerProtocolReceptionRequest input,
        OperationActor actor, CancellationToken ct)
    {
        if (protocol is not ("udp" or "tcp" or "tls" or "relp") || input is null || string.IsNullOrEmpty(input.Version))
            return ApiResult<ListenerSettingsView>.Fail("invalid_request", "接收协议或配置版本无效。");
        await gate.WaitAsync(ct);
        try
        {
            var result = await ReadSavedAsync(ct);
            if (result.Data is not { } saved) return ApiResult<ListenerSettingsView>.Fail(result.Code, result.Message);
            if (input.Version != saved.Version) return VersionConflict();
            if (!HasUniqueKnownProtocols(running.Listeners))
                return ApiResult<ListenerSettingsView>.Fail("legacy_listener_configuration", "当前运行配置超出每协议一个监听器的网页管理范围，请先应用最新配置。");
            var existing = saved.Configuration.Listeners.Single(item => item.Protocol == protocol);
            var target = existing with { Enabled = input.Enabled };
            var current = running.Listeners.Single(item => item.Protocol == protocol);
            var next = input.Enabled ? target : current with { Enabled = false };
            var retryFailed = input.Enabled && status.GetListenerSnapshots().Any(item => item.Protocol == protocol && item.State == "failed");
            var apply = running.Enabled && (current != next || retryFailed);
            if (existing == target && !apply) return ApiResult<ListenerSettingsView>.Ok(View(saved));

            var desired = ReplaceProtocol(saved.Configuration, target);
            if (!ListenerSettingsValidation.TryNormalize(desired, out var normalized, out var code))
                return InvalidListenerConfiguration(code);
            var nextRunning = ReplaceProtocol(running, next);
            // 其他协议可能还在使用待应用配置的旧端口，需要一并检查当前端口占用关系。
            if (apply && input.Enabled && !ListenerSettingsValidation.TryNormalize(nextRunning, out _, out code))
                return InvalidListenerConfiguration(code);

            var update = await store.SaveListenerSettingsAsync(new(saved.Version, normalized), actor, ct, protocol: protocol);
            if (update.Data is null) return ApiResult<ListenerSettingsView>.Fail(update.Code, update.Message);
            if (apply)
            {
                try { await runtime.ApplyProtocolAsync(ResolveListener(next), CancellationToken.None); }
                catch (InvalidOperationException)
                {
                    PublishRuntime(update.Data);
                    return RuntimeUnavailable();
                }
                running = nextRunning;
            }
            return ApiResult<ListenerSettingsView>.Ok(View(update.Data));
        }
        finally { gate.Release(); }
    }

    private async Task<ApiResult<SavedListenerSettings>> ReadSavedAsync(CancellationToken ct)
    {
        var result = await store.ReadListenerSettingsAsync(ct);
        if (result.Data is not { Version: "0" } saved) return result;
        // 旧部署可能有多个同协议端点。网页拒绝静默丢弃多余端点。
        if (!HasUniqueKnownProtocols(baseline.Listeners))
            return ApiResult<SavedListenerSettings>.Fail("legacy_listener_configuration", "旧部署配置超出每协议一个监听器的网页管理范围，请先调整服务器配置。");
        return ApiResult<SavedListenerSettings>.Ok(saved with { Configuration = Copy(baseline) });
    }

    private ListenerSettingsView View(SavedListenerSettings saved)
    {
        PublishRuntime(saved);
        var snapshot = GetRuntimeSnapshot();
        var managedIds = certificateProvider?.GetAvailableCertificateIds() ?? [];
        return new(saved, snapshot.Running, snapshot.RestartRequired,
            options.TlsCertificates.Keys.Concat(managedIds).Distinct(StringComparer.Ordinal)
                .Where(id => AvailableCertificate(id, managedIds)).Order(StringComparer.Ordinal)
                .Select(id => new ListenerCertificateReference(id)).ToArray(), snapshot.States, snapshot.ConnectionErrors, snapshot.Revision);
    }

    private void PublishRuntime(SavedListenerSettings? saved)
    {
        var pending = saved is not null && !Equivalent(saved.Configuration, running);
        lock (snapshotGate) published = new(running, saved, startupFailure, pending);
    }

    public ListenerRuntimeView GetRuntimeSnapshot()
    {
        RuntimeConfiguration view;
        long currentRevision;
        (IReadOnlyList<ListenerRuntimeSnapshot> States, IReadOnlyList<ListenerConnectionError> ConnectionErrors) snapshots;
        // 同一闸门按捕获顺序分配版本。实时流只读内存，不等待 SQLite 或运行时切换。
        lock (snapshotGate)
        {
            view = published;
            snapshots = status.GetListenerRuntimeSnapshot();
            currentRevision = ++revision;
        }
        var states = view.Running.Listeners.Select(item =>
        {
            if (view.StartupFailure.Length != 0)
                return new ListenerRuntimeState(item.Protocol, item.Address, item.Port, "failed", view.StartupFailure,
                    ListenerFailureMessages.Describe(view.StartupFailure));
            if (forceDisabled)
                return new ListenerRuntimeState(item.Protocol, item.Address, item.Port, "disabled", "listener_forced_disabled",
                    ListenerFailureMessages.Describe("listener_forced_disabled"));
            var snapshot = snapshots.States.FirstOrDefault(value => value.Protocol == item.Protocol && value.Address == item.Address && value.Port == item.Port);
            return snapshot is null
                ? new ListenerRuntimeState(item.Protocol, item.Address, item.Port,
                    view.Running.Enabled && item.Enabled ? "starting" : "disabled", "")
                : new ListenerRuntimeState(snapshot.Protocol, snapshot.Address, snapshot.Port, snapshot.State, snapshot.FailureCode,
                    snapshot.FailureMessage);
        }).ToArray();
        return new(currentRevision.ToString(CultureInfo.InvariantCulture), view.Running, view.RestartRequired, states,
            snapshots.ConnectionErrors, view.Saved);
    }

    private bool AvailableCertificate(string id, IReadOnlyList<string>? managedIds = null)
    {
        if (id is null || !Regex.IsMatch(id, "^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$")) return false;
        if (certificateProvider is not null && IListenerCertificateProvider.IsManagedId(id))
            return (managedIds ?? certificateProvider.GetAvailableCertificateIds()).Contains(id, StringComparer.Ordinal);
        return options.TlsCertificates.TryGetValue(id, out var certificate) && !string.IsNullOrWhiteSpace(certificate.CertificatePath);
    }

    private bool MissingCertificate(ListenerConfiguration configuration) =>
        configuration.Listeners.Any(item => item.Enabled && item.Protocol == "tls" && !AvailableCertificate(item.CertificateId));

    private static ApiResult<ListenerSettingsView> CertificateMissing() =>
        ApiResult<ListenerSettingsView>.Fail("listener_certificate_missing", "请选择可用的 TLS 证书。");

    private static ApiResult<ListenerSettingsView> VersionConflict() =>
        ApiResult<ListenerSettingsView>.Fail("version_conflict", "监听配置已被修改，请刷新后重试。");

    private static ApiResult<ListenerSettingsView> RuntimeUnavailable() =>
        ApiResult<ListenerSettingsView>.Fail("listener_runtime_unavailable", "接收运行时当前无法切换，请稍后重试。");

    private static ApiResult<ListenerSettingsView> InvalidListenerConfiguration(string code) =>
        ApiResult<ListenerSettingsView>.Fail(code, "监听配置无效，请检查协议、地址和端口。");

    private static ListenerConfiguration ReplaceProtocol(ListenerConfiguration configuration, ListenerSetting listener) =>
        configuration with { Listeners = configuration.Listeners.Select(item => item.Protocol == listener.Protocol ? listener : item).ToArray() };

    private void Apply(ListenerConfiguration configuration)
    {
        options.Enabled = configuration.Enabled;
        options.Listeners = ResolveListeners(configuration);
    }

    private List<ListenerOptions> ResolveListeners(ListenerConfiguration configuration) =>
        configuration.Listeners.Select(ResolveListener).ToList();

    private ListenerOptions ResolveListener(ListenerSetting item)
    {
        TlsCertificateOptions? certificate = null;
        if (certificateProvider is null || !IListenerCertificateProvider.IsManagedId(item.CertificateId))
            options.TlsCertificates.TryGetValue(item.CertificateId, out certificate);
        return new()
        {
            Enabled = item.Enabled, Transport = item.Protocol, Address = item.Address, Port = item.Port,
            Framing = item.Framing, FrameTimeoutSeconds = item.FrameTimeoutSeconds, IdleTimeoutSeconds = item.IdleTimeoutSeconds,
            CertificateId = item.CertificateId,
            CertificatePath = item.Protocol == "tls" ? certificate?.CertificatePath ?? "" : "",
            CertificatePassword = item.Protocol == "tls" ? certificate?.CertificatePassword ?? "" : ""
        };
    }

    private static ListenerSetting FromOptions(ListenerOptions item) => new(item.Transport, item.Enabled, item.Address,
        item.Port, item.Framing, item.CertificateId, item.FrameTimeoutSeconds, item.IdleTimeoutSeconds);
    private static ListenerConfiguration Copy(ListenerConfiguration value) => value with { Listeners = value.Listeners.ToArray() };
    private static bool HasUniqueKnownProtocols(IReadOnlyList<ListenerSetting> listeners) =>
        listeners.All(item => item.Protocol is "udp" or "tcp" or "tls" or "relp")
        && listeners.Select(item => item.Protocol).Distinct(StringComparer.Ordinal).Count() == listeners.Count;
    private static bool Equivalent(ListenerConfiguration left, ListenerConfiguration right) =>
        JsonSerializer.Serialize(left with { Listeners = left.Listeners.OrderBy(item => item.Protocol, StringComparer.Ordinal).ToArray() }) ==
        JsonSerializer.Serialize(right with { Listeners = right.Listeners.OrderBy(item => item.Protocol, StringComparer.Ordinal).ToArray() });

    private sealed record RuntimeConfiguration(ListenerConfiguration Running, SavedListenerSettings? Saved, string StartupFailure, bool RestartRequired);
}
