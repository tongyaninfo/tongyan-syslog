using Syslog.Shared.Models;

namespace Syslog.Ingest.Models;

public sealed record PendingBatch(string Id, IReadOnlyList<LogEvent> Events, bool Sending);
