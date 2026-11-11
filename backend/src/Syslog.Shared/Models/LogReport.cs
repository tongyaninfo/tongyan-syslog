using System.Text.Json.Serialization;

namespace Syslog.Shared.Models;

public sealed record LogReportDeviceCounts(
    [property: JsonRequired] string SourceId,
    [property: JsonRequired] long Total,
    [property: JsonRequired] long Emergency,
    [property: JsonRequired] long Alert,
    [property: JsonRequired] long Critical,
    [property: JsonRequired] long Error);

public sealed record LogReportEntry(
    [property: JsonRequired] long EventId,
    [property: JsonRequired] string SourceId,
    [property: JsonRequired] DateTimeOffset ReceivedAt,
    [property: JsonRequired] int Severity,
    [property: JsonRequired] string Summary);

public sealed record LogReport(IReadOnlyList<LogReportDeviceCounts> Devices, IReadOnlyList<LogReportEntry> RecentEntries);
