using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Syslog.Api.Models;
using Syslog.Shared.Models;

namespace Syslog.Api.Service.impl;

public sealed partial class ControlStore
{
    private const int McpRequestBodyLimit = 1024 * 1024;
    private const int McpResponseBodyLimit = 4 * 1024 * 1024;
    private const int McpHeaderLimit = 256 * 1024;
    // 摘要查询明确不读取大正文；每页大小不会把完整审计载入列表。
    private const string McpCallSummaryColumns = "id,key_id,key_name,actor_id,actor_name,trace_id,rpc_id,method,tool_name,peer_ip,http_method,url,http_protocol,started_at,completed_at,duration_ms,http_status,outcome,code,request_bytes,response_bytes,request_complete,response_complete,capture_error";

    public async Task<ApiResult<string>> BeginMcpCallAsync(McpCallStart request, CancellationToken ct)
    {
        var headers = JsonSerializer.Serialize(request.RequestHeaders);
        if (request.RequestBytes < 0 || !ValidMcpRequestCapture(request)
            || Encoding.UTF8.GetByteCount(headers) > McpHeaderLimit)
            return ApiResult<string>.Fail("mcp_audit_payload_invalid", "MCP 请求捕获超出允许大小。");
        await _writeGate.WaitAsync(ct);
        try
        {
            await using var connection = await OpenAsync(false, ct);
            using var transaction = connection.BeginTransaction();
            if (!await SupportsMcpAsync(connection, transaction, ct)) return McpMigrationRequired<string>();
            var id = await ReserveIdBlockAsync(connection, transaction, 1, ct);
            var now = Now();
            await ExecuteAsync(connection, transaction,
                "INSERT INTO mcp_call_audits(id,key_id,key_name,actor_id,actor_name,trace_id,rpc_id,method,tool_name,peer_ip,http_method,url,http_protocol," +
                "request_headers_json,request_body,request_bytes,request_complete,started_at,outcome,capture_error) " +
                "VALUES($id,$key,$keyName,$actor,$actorName,$trace,$rpc,$method,$tool,$peer,$httpMethod,$url,$protocol,$headers,$body,$bytes,$complete,$now,'started',$capture);",
                ct, ("$id", id), ("$key", request.Principal?.KeyId), ("$keyName", request.Principal?.Name),
                ("$actor", request.Principal?.UserId), ("$actorName", request.Principal?.Username), ("$trace", request.TraceId),
                ("$rpc", request.RpcId), ("$method", request.Method), ("$tool", request.ToolName), ("$peer", request.PeerIp),
                ("$httpMethod", request.HttpMethod), ("$url", request.Url), ("$protocol", request.HttpProtocol),
                ("$headers", headers), ("$body", request.RequestBody), ("$bytes", request.RequestBytes),
                ("$complete", request.RequestComplete ? 1 : 0), ("$now", now), ("$capture", request.CaptureError));
            if (request.Principal is not null)
                await ExecuteAsync(connection, transaction, "UPDATE mcp_api_keys SET last_used_at=$now WHERE id=$id;", ct,
                    ("$now", now), ("$id", request.Principal.KeyId));
            transaction.Commit();
            return ApiResult<string>.Ok(Id(id));
        }
        finally { _writeGate.Release(); }
    }

    public async Task<bool> CompleteMcpCallAsync(string id, McpCallCompletion completion, CancellationToken ct)
    {
        var headers = JsonSerializer.Serialize(completion.ResponseHeaders);
        if (!TryPositiveId(id, out var callId) || completion.Outcome is not ("success" or "failed" or "rejected" or "cancelled" or "unknown")
            || completion.ResponseBytes < 0 || completion.DurationMilliseconds < 0 || completion.HttpStatus is < 100 or > 599
            || Encoding.UTF8.GetByteCount(completion.ResponseBody) > McpResponseBodyLimit || Encoding.UTF8.GetByteCount(headers) > McpHeaderLimit)
            throw new ArgumentException("MCP 响应捕获无效或超出允许大小。", nameof(completion));
        await _writeGate.WaitAsync(ct);
        try
        {
            await using var connection = await OpenAsync(false, ct);
            using var transaction = connection.BeginTransaction();
            if (!await SupportsMcpAsync(connection, transaction, ct)) return false;
            var changed = await ExecuteAsync(connection, transaction,
                "UPDATE mcp_call_audits SET response_headers_json=$headers,response_body=$body,response_bytes=$bytes,response_complete=$complete," +
                "http_status=$status,completed_at=$now,duration_ms=$duration,outcome=$outcome,code=$code," +
                "capture_error=CASE WHEN $capture IS NULL OR capture_error=$capture THEN capture_error WHEN capture_error IS NULL THEN $capture ELSE capture_error || '; ' || $capture END " +
                "WHERE id=$id AND outcome='started';", ct,
                ("$headers", headers), ("$body", completion.ResponseBody), ("$bytes", completion.ResponseBytes),
                ("$complete", completion.ResponseComplete ? 1 : 0), ("$status", completion.HttpStatus), ("$now", Now()),
                ("$duration", completion.DurationMilliseconds), ("$outcome", completion.Outcome), ("$code", completion.Code),
                ("$capture", completion.CaptureError), ("$id", callId));
            transaction.Commit();
            return changed == 1;
        }
        finally { _writeGate.Release(); }
    }

    public async Task<ApiResult<McpCallPage>> ListMcpCallsAsync(McpCallQuery query, CancellationToken ct)
    {
        if (query.PageSize is < 1 or > 100 || query.From.HasValue && query.To.HasValue && query.From >= query.To
            || query.KeyId is not null && !TryPositiveId(query.KeyId, out _)
            || query.ToolName?.Length > 128 || query.Method?.Length > 128 || query.PeerIp?.Length > 128
            || query.Outcome is not (null or "started" or "success" or "failed" or "rejected" or "cancelled" or "unknown"))
            return ApiResult<McpCallPage>.Fail("invalid_request", "调用记录查询条件无效。");
        long? beforeTime = null;
        long? beforeId = null;
        if (query.Cursor is not null)
        {
            if (!TryMcpCursor(query.Cursor, out var time, out var id))
                return ApiResult<McpCallPage>.Fail("invalid_cursor", "调用记录游标无效。");
            beforeTime = time;
            beforeId = id;
        }
        await using var connection = await OpenAsync(false, ct);
        using var transaction = connection.BeginTransaction(deferred: true);
        if (!await SupportsMcpAsync(connection, transaction, ct)) return McpMigrationRequired<McpCallPage>();
        await using var command = Command(connection, transaction,
            "SELECT " + McpCallSummaryColumns + " FROM mcp_call_audits WHERE " +
            "($from IS NULL OR started_at >= $from) AND ($to IS NULL OR started_at < $to) " +
            "AND ($key IS NULL OR key_id=$key) AND ($tool IS NULL OR tool_name=$tool) AND ($method IS NULL OR method=$method) " +
            "AND ($peer IS NULL OR peer_ip=$peer) AND ($outcome IS NULL OR outcome=$outcome) " +
            "AND ($beforeTime IS NULL OR started_at<$beforeTime OR (started_at=$beforeTime AND id<$beforeId)) " +
            "ORDER BY started_at DESC,id DESC LIMIT $limit;",
            ("$from", query.From?.ToUnixTimeMilliseconds()), ("$to", query.To?.ToUnixTimeMilliseconds()),
            ("$key", query.KeyId is null ? null : long.Parse(query.KeyId, CultureInfo.InvariantCulture)), ("$tool", query.ToolName),
            ("$method", query.Method), ("$peer", query.PeerIp), ("$outcome", query.Outcome),
            ("$beforeTime", beforeTime), ("$beforeId", beforeId), ("$limit", query.PageSize + 1));
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<McpCallSummary>();
        while (await reader.ReadAsync(ct)) rows.Add(ReadMcpCallSummary(reader));
        var more = rows.Count > query.PageSize;
        if (more) rows.RemoveAt(rows.Count - 1);
        var last = rows.LastOrDefault();
        var next = more && last is not null ? CreateMcpCursor(last.StartedAt.ToUnixTimeMilliseconds(), last.Id) : null;
        return ApiResult<McpCallPage>.Ok(new(rows, next, more));
    }

    public async Task<ApiResult<McpCallDetail>> GetMcpCallAsync(string id, CancellationToken ct)
    {
        if (!TryPositiveId(id, out var callId)) return ApiResult<McpCallDetail>.Fail("invalid_request", "调用标识无效。");
        await using var connection = await OpenAsync(false, ct);
        using var transaction = connection.BeginTransaction(deferred: true);
        if (!await SupportsMcpAsync(connection, transaction, ct)) return McpMigrationRequired<McpCallDetail>();
        await using var command = Command(connection, transaction,
            "SELECT " + McpCallSummaryColumns + ",request_headers_json,request_body,response_headers_json,response_body FROM mcp_call_audits WHERE id=$id;", ("$id", callId));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return ApiResult<McpCallDetail>.Fail("not_found", "调用记录不存在或已超过留存期限。");
        return ApiResult<McpCallDetail>.Ok(new(ReadMcpCallSummary(reader),
            JsonSerializer.Deserialize<Dictionary<string, string[]>>(reader.GetString(24))!, reader.GetString(25),
            reader.IsDBNull(26) ? new(StringComparer.OrdinalIgnoreCase) : JsonSerializer.Deserialize<Dictionary<string, string[]>>(reader.GetString(26))!,
            reader.IsDBNull(27) ? null : reader.GetString(27)));
    }

    public async Task RecoverMcpCallsAsync(CancellationToken ct, DateTimeOffset? startedBefore = null)
    {
        await _writeGate.WaitAsync(ct);
        try
        {
            await using var connection = await OpenAsync(false, ct);
            using var transaction = connection.BeginTransaction();
            if (!await SupportsMcpAsync(connection, transaction, ct)) return;
            // 重启只标识未知结果，不重放可能已经提交的写操作。
            await ExecuteAsync(connection, transaction,
                "UPDATE mcp_call_audits SET completed_at=$now,duration_ms=MAX(0,$now-started_at),outcome='unknown'," +
                "code='mcp_call_interrupted',response_complete=0,capture_error=COALESCE(capture_error,'service_interrupted') " +
                "WHERE outcome='started' AND ($before IS NULL OR started_at<$before);",
                ct, ("$now", Now()), ("$before", startedBefore?.ToUnixTimeMilliseconds()));
            transaction.Commit();
        }
        finally { _writeGate.Release(); }
    }

    public async Task<McpCleanupResult?> CleanupMcpCallsAsync(CancellationToken ct)
    {
        await _writeGate.WaitAsync(ct);
        try
        {
            await using var connection = await OpenAsync(false, ct);
            using var transaction = connection.BeginTransaction();
            if (!await SupportsMcpAsync(connection, transaction, ct)) return null;
            var settings = await ReadMcpSettingsRecordAsync(connection, transaction, ct);
            var now = _clock.GetUtcNow();
            var cutoff = now.AddDays(-settings.AuditRetentionDays).ToUnixTimeMilliseconds();
            // 每批只删除 500 行并尽快释放写锁，不触碰业务操作审计和进行中的调用。
            var count = await ExecuteAsync(connection, transaction,
                "DELETE FROM mcp_call_audits WHERE id IN (SELECT id FROM mcp_call_audits WHERE completed_at IS NOT NULL AND completed_at<$cutoff ORDER BY completed_at,id LIMIT 500);",
                ct, ("$cutoff", cutoff));
            transaction.Commit();
            return new(now, count);
        }
        finally { _writeGate.Release(); }
    }

    private static McpCallSummary ReadMcpCallSummary(SqliteDataReader r) => new(
        Id(r.GetInt64(0)), r.IsDBNull(1) ? null : Id(r.GetInt64(1)), r.IsDBNull(2) ? null : r.GetString(2),
        r.IsDBNull(3) ? null : Id(r.GetInt64(3)), r.IsDBNull(4) ? null : r.GetString(4), r.GetString(5),
        r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8),
        r.GetString(9), r.GetString(10), r.GetString(11), r.GetString(12), DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(13)),
        ReadNullableTime(r, 14), r.IsDBNull(15) ? null : r.GetInt64(15), r.IsDBNull(16) ? null : r.GetInt32(16), r.GetString(17),
        r.IsDBNull(18) ? null : r.GetString(18), r.GetInt64(19), r.IsDBNull(20) ? null : r.GetInt64(20), r.GetInt64(21) != 0,
        r.IsDBNull(22) ? null : r.GetInt64(22) != 0, r.IsDBNull(23) ? null : r.GetString(23));

    private static string CreateMcpCursor(long timestamp, string id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(timestamp.ToString(CultureInfo.InvariantCulture) + ":" + id))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool ValidMcpRequestCapture(McpCallStart request)
    {
        if (request.CaptureError?.Contains("body_saved_as_base64", StringComparison.Ordinal) != true)
            return Encoding.UTF8.GetByteCount(request.RequestBody) <= McpRequestBodyLimit;
        // 非 UTF-8 证据以 Base64 保留，预算约束原始字节，不误用编码后的 4/3 长度。
        if (request.RequestBody.Length > (McpRequestBodyLimit + 2) / 3 * 4) return false;
        try { return Convert.FromBase64String(request.RequestBody).Length <= McpRequestBodyLimit; }
        catch (FormatException) { return false; }
    }

    private static bool TryMcpCursor(string value, out long timestamp, out long id)
    {
        timestamp = id = 0;
        if (value.Length is < 1 or > 100) return false;
        try
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight((padded.Length + 3) / 4 * 4, '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(padded)).Split(':');
            return parts.Length == 2 && long.TryParse(parts[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out timestamp)
                && TryPositiveId(parts[1], out id);
        }
        catch (FormatException) { return false; }
    }
}
