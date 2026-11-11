namespace Syslog.Shared.Models;

public sealed record LogSource(string Id, string Name, string PeerAddress, string TimeZoneId, int RetentionDays, bool Enabled,
    int[]? IgnoredSeverities = null);
