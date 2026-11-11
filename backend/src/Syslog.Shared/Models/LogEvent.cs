using System.Text.Json.Serialization;

namespace Syslog.Shared.Models;

/// <summary>原始证据与解析结果分开保存，入库重试必须复用事件和批次标识。</summary>
public sealed record LogEvent
{
    [JsonNumberHandling(JsonNumberHandling.WriteAsString | JsonNumberHandling.AllowReadingFromString)]
    public long EventId { get; init; }
    public string BatchId { get; init; } = "";
    public string SourceId { get; init; } = "";
    public string PeerAddress { get; init; } = "";
    public string Transport { get; init; } = "";
    public DateTimeOffset ReceivedAt { get; init; }
    public DateTimeOffset? EventTime { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public string OriginalTimestamp { get; init; } = "";
    public string TimeConfidence { get; init; } = "unknown";
    public string Hostname { get; init; } = "";
    public string AppName { get; init; } = "";
    public string ProcId { get; init; } = "";
    public string MsgId { get; init; } = "";
    public string StructuredData { get; init; } = "";
    public int? Facility { get; init; }
    public int? Severity { get; init; }
    public string Message { get; init; } = "";
    public byte[] RawBytes { get; init; } = [];
    public string ParseStatus { get; init; } = "unknown";
    public string FailureCode { get; init; } = "";
    public string ParserVersion { get; init; } = "rfc-v1";
}
