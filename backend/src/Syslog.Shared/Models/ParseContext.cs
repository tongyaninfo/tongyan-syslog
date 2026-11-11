namespace Syslog.Shared.Models;

public sealed record ParseContext(string SourceId, string PeerAddress, string Transport,
    DateTimeOffset ReceivedAt, TimeZoneInfo SourceTimeZone, DateTimeOffset ExpiresAt);
