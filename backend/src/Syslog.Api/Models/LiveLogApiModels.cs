using Syslog.Shared.Models;

namespace Syslog.Api.Models;

public sealed record LiveLogSettingsView(SavedLiveLogSettings Settings, LiveLogCacheStats Cache);
public sealed record LiveLogQueryRequest(LiveLogFilter Filter, string? Epoch = null, string? BeforeSequence = null, int Limit = 500);
public sealed record LiveLogSubscribeRequest(string SubscriptionId, LiveLogFilter Filter, string? Epoch = null, string? AfterSequence = null);
public sealed record LiveLogIngestView(int Queued, long Dropped, bool SpoolFull, string State);
public sealed record LiveLogUpdate(string SubscriptionId, string Kind, string Epoch, string Cursor,
    IReadOnlyList<LiveLogRecord> Items, bool HasMore, string? NextBeforeSequence, string? ResetReason,
    bool BudgetLimited, LiveLogCacheStats Cache, DateTimeOffset ObservedAt, LiveLogIngestView Ingest);
