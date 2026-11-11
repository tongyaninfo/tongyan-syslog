using Syslog.Shared.Models;

namespace Syslog.Api.Models;

/// <summary>来源窄操作的字段集合；必须与已授权的具体工具一致。</summary>
public sealed record SourcePatch(string? Name = null, string? PeerAddress = null, string? TimeZoneId = null,
    bool? Enabled = null, int? RetentionDays = null, int[]? IgnoredSeverities = null)
{
    public bool TryApply(LogSource previous, string? permission, out SourceInput result)
    {
        result = new(previous.Name, previous.PeerAddress, previous.TimeZoneId, previous.RetentionDays, previous.Enabled, previous.IgnoredSeverities?.ToArray() ?? []);
        switch (permission)
        {
            case "syslog_sources_update" when Name is not null && PeerAddress is not null && TimeZoneId is not null
                && Enabled is null && RetentionDays is null && IgnoredSeverities is null:
                result = result with { Name = Name, PeerAddress = PeerAddress, TimeZoneId = TimeZoneId }; return true;
            case "syslog_sources_set_enabled" when Enabled.HasValue && Name is null && PeerAddress is null && TimeZoneId is null
                && RetentionDays is null && IgnoredSeverities is null:
                result = result with { Enabled = Enabled.Value }; return true;
            case "syslog_source_retention_set" when RetentionDays.HasValue && Name is null && PeerAddress is null && TimeZoneId is null
                && Enabled is null && IgnoredSeverities is null:
                result = result with { RetentionDays = RetentionDays.Value }; return true;
            case "syslog_sources_set_ignored_severities" when IgnoredSeverities is not null && Name is null && PeerAddress is null
                && TimeZoneId is null && Enabled is null && RetentionDays is null:
                result = result with { IgnoredSeverities = IgnoredSeverities }; return true;
            default: return false;
        }
    }
}
