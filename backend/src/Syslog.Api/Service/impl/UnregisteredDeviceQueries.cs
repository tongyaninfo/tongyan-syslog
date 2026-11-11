using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using Syslog.Ingest.Service;
using Syslog.Shared.Models;
using Syslog.Shared.Service;

namespace Syslog.Api.Service.impl;

/// <summary>网页与 MCP 共用未登记事实过滤，登记完成后立即隐藏旧样本。</summary>
public sealed class UnregisteredDeviceQueries(IUnregisteredDeviceBuffer buffer, ISourceRegistry sources)
{
    public async Task<ApiResult<UnregisteredDevicePage>> ListAsync(CancellationToken ct)
    {
        var registered = await RegisteredPeersAsync(ct);
        var page = buffer.Snapshot();
        var items = page.Items.Where(item => !registered.Contains(Normalize(IPAddress.Parse(item.PeerAddress)))).ToArray();
        return ApiResult<UnregisteredDevicePage>.Ok(page with
        {
            Items = items,
            Status = page.Status with { DeviceCount = items.Length, SampleCount = items.Sum(item => item.SampleCount) }
        });
    }

    public async Task<ApiResult<IReadOnlyList<UnregisteredLogSummary>>> LogsAsync(string? peerAddress, CancellationToken ct)
    {
        if (!TryParsePeer(peerAddress, out var peer)) return Invalid<IReadOnlyList<UnregisteredLogSummary>>();
        if ((await RegisteredPeersAsync(ct)).Contains(peer)) return Missing<IReadOnlyList<UnregisteredLogSummary>>();
        var logs = buffer.GetLogs(peer);
        return logs.Count == 0 ? Missing<IReadOnlyList<UnregisteredLogSummary>>() : ApiResult<IReadOnlyList<UnregisteredLogSummary>>.Ok(logs);
    }

    public async Task<ApiResult<UnregisteredLogDetail>> SampleAsync(string? peerAddress, string? sampleKey, CancellationToken ct)
    {
        if (!TryParsePeer(peerAddress, out var peer) || !IsSampleKey(sampleKey)) return Invalid<UnregisteredLogDetail>();
        if ((await RegisteredPeersAsync(ct)).Contains(peer)) return Missing<UnregisteredLogDetail>();
        var sample = buffer.GetSample(peer, sampleKey);
        return sample is null ? Missing<UnregisteredLogDetail>() : ApiResult<UnregisteredLogDetail>.Ok(sample);
    }

    private async Task<HashSet<IPAddress>> RegisteredPeersAsync(CancellationToken ct) =>
        (await sources.ListAsync(ct)).Select(source => Normalize(IPAddress.Parse(source.PeerAddress))).ToHashSet();
    private static bool TryParsePeer(string? value, [NotNullWhen(true)] out IPAddress? peer)
    {
        peer = null;
        if (value is not { Length: > 0 and <= 64 } || !IPAddress.TryParse(value, out var address)) return false;
        peer = Normalize(address); return true;
    }
    private static bool IsSampleKey([NotNullWhen(true)] string? value) =>
        value is { Length: >= 34 and <= 52 } && value[32] == '-' && value[33] != '0'
        && Guid.TryParseExact(value.AsSpan(0, 32), "N", out _)
        && long.TryParse(value.AsSpan(33), NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) && sequence > 0;
    private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    private static ApiResult<T> Invalid<T>() => ApiResult<T>.Fail("invalid_query", "请提供有效的来源 IP 和所需的样本标识。");
    private static ApiResult<T> Missing<T>() => ApiResult<T>.Fail("not_found", "暂存日志不存在或已过期。");
}
