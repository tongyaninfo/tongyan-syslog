using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Syslog.Ingest.Data;
using Syslog.Ingest.Options;
using Syslog.Shared.Models;
using Syslog.Shared.Service;

namespace Syslog.Ingest.Service.impl;

/// <summary>有预算的 ClickHouse HTTP 访问；写入失败交由暂存队列用同一批次重试。</summary>
public sealed partial class ClickHouseLogStore : ILogStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };
    private readonly HttpClient _client;
    private readonly ClickHouseOptions _options;
    private readonly TimeProvider _clock;
    private readonly ClickHouseStorageState _state;
    private readonly Uri _endpoint;
    private readonly string _database;
    private readonly string _table;

    public ClickHouseLogStore(HttpClient client, IOptions<ClickHouseOptions> options, TimeProvider clock, ClickHouseStorageState state)
    {
        _client = client;
        _options = options.Value;
        _clock = clock;
        _state = state;
        if (!Uri.TryCreate(_options.Endpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https") || endpoint.UserInfo.Length != 0
            || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 || endpoint.AbsolutePath != "/"
            || !Regex.IsMatch(_options.Database, @"\A[A-Za-z_][A-Za-z0-9_]{0,63}\z", RegexOptions.CultureInvariant)
            || _options.TimeoutSeconds is < 1 or > 120 || _options.MaxResponseBytes is < 1024 or > 64 * 1024 * 1024
            || _options.MaxBatchBytes is < 1024 or > 64 * 1024 * 1024 || _options.MaxBatchEvents is < 1 or > 10000
            || _options.MaxMemoryUsageBytes is < 1024 * 1024 or > 8L * 1024 * 1024 * 1024
            || _options.MaxBytesToRead is < 1024 or > 1024L * 1024 * 1024 * 1024)
        {
            throw new ArgumentException("ClickHouse 配置或资源预算无效。");
        }
        _endpoint = endpoint;
        _database = _options.Database;
        _table = $"`{_database}`.`events`";
    }

    public async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_state.SchemaReady) return;
        await _state.SchemaGate.WaitAsync(cancellationToken);
        try
        {
            if (_state.SchemaReady) return;
            var database = $"`{_database}`";
            if (!await ExistsAsync($"EXISTS DATABASE {database}", cancellationToken))
                await CommandAsync(ClickHouseSchema.CreateDatabase.Replace(ClickHouseSchema.DatabaseName, database, StringComparison.Ordinal), cancellationToken, RequestKind.Initialize);
            if (!await ExistsAsync($"EXISTS TABLE {_table}", cancellationToken))
                await CommandAsync(ClickHouseSchema.CreateTable.Replace($"{ClickHouseSchema.DatabaseName}.events", _table, StringComparison.Ordinal), cancellationToken, RequestKind.Initialize);

            await VerifyColumnsAsync(cancellationToken);
            _state.SchemaReady = true;
        }
        finally { _state.SchemaGate.Release(); }
    }

    public async Task<long?> GetDatabaseSizeAsync(CancellationToken cancellationToken)
    {
        // 看板读取元数据，不触发日志库初始化。只有存在且可见的库才返回 0 或实际字节数。
        const string sql = """
            SELECT (
                SELECT sum(bytes_on_disk) FROM system.parts
                WHERE database = {database:String} AND active = 1
            )
            FROM system.databases WHERE name = {database:String}
            FORMAT TabSeparated
            """;
        var result = (await SqlAsync(sql, new() { ["database"] = _database }, cancellationToken, RequestKind.Metadata)).Trim();
        if (result.Length == 0) return null;
        return long.TryParse(result, NumberStyles.None, CultureInfo.InvariantCulture, out var bytes) ? bytes : throw StorageFailure();
    }

    public Task ClearAsync(CancellationToken cancellationToken) => ClearCoreAsync(null, cancellationToken);

    public Task ClearSourceAsync(string sourceId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceId) || sourceId.Length > 128)
            throw new ArgumentException("日志来源标识无效。", nameof(sourceId));
        return ClearCoreAsync(sourceId, cancellationToken);
    }

    private async Task ClearCoreAsync(string? sourceId, CancellationToken cancellationToken)
    {
        await _state.MutationGate.WaitAsync(cancellationToken);
        try
        {
            if (_state.ClearOutcomeUnknown) throw ClearOutcomeUnknown();
            var cutoff = sourceId is null ? 0L : _clock.GetUtcNow().ToUnixTimeMilliseconds();
            await EnsureReadyAsync(cancellationToken);
            await VerifyColumnsAsync(cancellationToken);
            var engine = await SqlAsync("SELECT engine FROM system.tables WHERE database = {database:String} AND name = 'events' FORMAT TabSeparated",
                new() { ["database"] = _database }, cancellationToken, RequestKind.Metadata);
            // 仅清空产品预期的单节点日志表，避免将同名视图或其他引擎当作本系统的表。
            if (engine.Trim() != "MergeTree") throw SchemaIncompatible();
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (sourceId is null)
                    await CommandAsync($"TRUNCATE TABLE {_table} SYNC", cancellationToken, RequestKind.Clear);
                else
                {
                    // 固定接收时间上界，即使服务端迟到执行，也不会清掉清空后收到的日志。
                    var sql = $"DELETE FROM {_table} WHERE source_id = {{sourceId:String}} " +
                        "AND received_at < fromUnixTimestamp64Milli({cutoff:Int64}, 'UTC')";
                    var result = await SqlAsync(sql, new() { ["sourceId"] = sourceId, ["cutoff"] = Number(cutoff) },
                        cancellationToken, RequestKind.Clear, waitForLightweightDelete: true);
                    if (!string.IsNullOrWhiteSpace(result)) throw ClearOutcomeUnknown();
                }
            }
            catch (Exception error) when (error is OperationCanceledException or ClickHouseStoreException { Code: "log_clear_outcome_unknown" })
            {
                // 全表清空结果未知时继续暂存。按来源清空已限制范围及时间，不冻结其他来源写入。
                if (sourceId is null) _state.ClearOutcomeUnknown = true;
                throw ClearOutcomeUnknown();
            }
        }
        finally { _state.MutationGate.Release(); }
    }

    private async Task<bool> ExistsAsync(string sql, CancellationToken cancellationToken)
    {
        var result = await SqlAsync(sql + " FORMAT TabSeparated", [], cancellationToken, RequestKind.Metadata);
        return result.Trim() switch
        {
            "1" => true,
            "0" => false,
            _ => throw StorageFailure(),
        };
    }

    private async Task VerifyColumnsAsync(CancellationToken cancellationToken)
    {
        var text = await SqlAsync("SELECT name, type FROM system.columns WHERE database = {database:String} AND table = 'events' FORMAT JSONCompactEachRow",
            new() { ["database"] = _database }, cancellationToken, RequestKind.Metadata);
        var columns = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var column = JsonSerializer.Deserialize<string[]>(line, JsonOptions);
                if (column is not [string name, string type] || !columns.TryAdd(name, type)) throw StorageFailure();
            }
        }
        catch (JsonException) { throw StorageFailure(); }
        if (ClickHouseSchema.Columns.Any(required => columns.GetValueOrDefault(required.Key) != required.Value))
            throw SchemaIncompatible();
    }

    public Task WriteBatchAsync(IReadOnlyList<LogEvent> events, CancellationToken cancellationToken) =>
        WriteBatchAsync(events, _ => Task.CompletedTask, cancellationToken);

    public async Task WriteBatchAsync(IReadOnlyList<LogEvent> events, Func<CancellationToken, Task> beforeSend, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(beforeSend);
        if (events.Count == 0) return;
        if (events.Count > _options.MaxBatchEvents) throw Invalid("批次超过条数上限。");
        var batchId = events[0].BatchId;
        if (string.IsNullOrWhiteSpace(batchId) || batchId.Length > 128) throw Invalid("批次标识无效。");
        var ids = new HashSet<long>();
        using var buffer = new MemoryStream();
        foreach (var item in events)
        {
            if (item.BatchId != batchId || item.EventId <= 0 || !ids.Add(item.EventId)
                || string.IsNullOrWhiteSpace(item.SourceId) || item.SourceId.Length > 128
                || item.RawBytes.Length > 65536 || Encoding.UTF8.GetByteCount(item.Message) > 262144
                || new[] { item.PeerAddress, item.Transport, item.OriginalTimestamp, item.TimeConfidence, item.Hostname, item.AppName, item.ProcId, item.MsgId, item.FailureCode, item.ParserVersion }.Any(x => x.Length > 4096)
                || Encoding.UTF8.GetByteCount(item.StructuredData) > 262144
                || item.Facility is < 0 or > 23 || item.Severity is < 0 or > 7
                || item.ExpiresAt <= item.ReceivedAt)
                throw Invalid("批次事件合同无效。");
            var bytes = JsonSerializer.SerializeToUtf8Bytes(StoredRow.FromEvent(item), JsonOptions);
            if (buffer.Length + bytes.Length + 1 > _options.MaxBatchBytes) throw Invalid("批次超过字节预算。");
            buffer.Write(bytes);
            buffer.WriteByte((byte)'\n');
        }
        // token 绑定内容和批次，避免调用方误复用批次标识而静默吞掉另一批数据。
        var data = buffer.ToArray();
        var token = Convert.ToHexString(SHA256.HashData(data));
        var settings = BaseSettings();
        settings["query"] = $"INSERT INTO {_table} FORMAT JSONEachRow";
        settings["insert_deduplicate"] = "1";
        settings["insert_deduplication_token"] = token;
        settings["async_insert"] = "0";
        settings["date_time_input_format"] = "best_effort";
        using var request = new HttpRequestMessage(HttpMethod.Post, RequestUri(settings));
        request.Content = new ByteArrayContent(data);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        await _state.MutationGate.WaitAsync(cancellationToken);
        try
        {
            if (_state.ClearOutcomeUnknown) throw ClearOutcomeUnknown();
            await EnsureReadyAsync(cancellationToken);
            // 库表已就绪且已取得写锁，此时再由接收管线写入发送标记。
            await beforeSend(cancellationToken);
            var result = await SendAsync(request, cancellationToken, RequestKind.Insert);
            // wait_end_of_query 配合正文检查，不能把 HTTP 200 的尾部错误当成功。
            if (!string.IsNullOrWhiteSpace(result)) throw StorageFailure();
        }
        catch (ClickHouseStoreException)
        {
            // 重试由暂存队列调度；下一次访问重新确认库表，再使用相同内容和去重标记提交。
            _state.SchemaReady = false;
            throw;
        }
        finally { _state.MutationGate.Release(); }
    }

    public async Task<LogSearchPage> SearchAsync(LogSearch query, IReadOnlyCollection<string> allowedSourceIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var sources = ValidateScope(allowedSourceIds);
        if (query.From >= query.To || query.To - query.From > TimeSpan.FromDays(30)
            || query.From.Ticks % TimeSpan.TicksPerMillisecond != 0 || query.To.Ticks % TimeSpan.TicksPerMillisecond != 0
            || query.PageSize is < 1 or > 500 || query.Severity is < 0 or > 7
            || query.Keywords?.Length > 512 || query.Hostname?.Length > 255 || query.AppName?.Length > 255
            )
            throw Invalid("查询时间、页大小或过滤条件超出限制。");
        // splitByNonAlpha 的首版合同只接受 ASCII 词，避免中文或纯符号产生空 token 后匹配全表。
        if (!string.IsNullOrWhiteSpace(query.Keywords)
            && (query.Keywords.Any(x => x > 127) || !query.Keywords.Any(char.IsAsciiLetterOrDigit)))
            throw new ClickHouseStoreException("unsupported_search_token", "当前关键词检索支持 ASCII 字母数字词；中文与纯符号检索尚未开放。");
        if (query.SourceId is not null && !sources.Contains(query.SourceId, StringComparer.Ordinal)) throw Forbidden();
        var parameters = new Dictionary<string, string>();
        var filters = new List<string> { ScopeSql(sources, parameters), "received_at >= fromUnixTimestamp64Milli({from:Int64})", "received_at < fromUnixTimestamp64Milli({to:Int64})", "expires_at > fromUnixTimestamp64Milli({now:Int64})" };
        parameters["from"] = Number(query.From.ToUnixTimeMilliseconds());
        parameters["to"] = Number(query.To.ToUnixTimeMilliseconds());
        var queryNow = _clock.GetUtcNow();
        parameters["now"] = Number(queryNow.ToUnixTimeMilliseconds());
        AddStringFilter(filters, parameters, "source_id", "source", query.SourceId);
        AddStringFilter(filters, parameters, "hostname", "host", query.Hostname);
        AddStringFilter(filters, parameters, "app_name", "app", query.AppName);
        if (query.Severity is { } severity) { filters.Add("severity = {severity:UInt8}"); parameters["severity"] = Number(severity); }
        if (!string.IsNullOrWhiteSpace(query.Keywords))
        {
            // 此入口是区分大小写的全词 AND 查询，不伪装成任意子串匹配。
            filters.Add("hasAllTokens(message, {keywords:String})");
            parameters["keywords"] = query.Keywords;
        }
        var fingerprint = Fingerprint(query, sources);
        if (query.Cursor is not null)
        {
            var cursor = DecodeCursor(query.Cursor);
            if (cursor.Fingerprint != fingerprint || cursor.EventId <= 0 || cursor.ReceivedAt < query.From.ToUnixTimeMilliseconds() || cursor.ReceivedAt >= query.To.ToUnixTimeMilliseconds())
                throw Invalid("游标与查询条件或授权范围不匹配。");
            filters.Add("(received_at, event_id) < (fromUnixTimestamp64Milli({cursor_time:Int64}), {cursor_id:Int64})");
            parameters["cursor_time"] = Number(cursor.ReceivedAt);
            parameters["cursor_id"] = Number(cursor.EventId);
        }
        parameters["limit"] = Number(query.PageSize + 1);
        var sql = $"SELECT {Columns(false)} FROM {_table} WHERE {string.Join(" AND ", filters)} ORDER BY received_at DESC, event_id DESC LIMIT {{limit:UInt32}} FORMAT JSONEachRow";
        var rows = ParseRows(await QueryAsync(sql, parameters, cancellationToken), query.PageSize + 1);
        // 来源与过滤由服务端 SQL 强制；响应再校验权限，防止错接表/代理返回不符合同。
        if (rows.Any(x => x.EventId <= 0 || !sources.Contains(x.SourceId, StringComparer.Ordinal) || x.ReceivedAt < query.From || x.ReceivedAt >= query.To || x.ExpiresAt <= queryNow)) throw StorageFailure();
        if (rows.Select(x => (x.ReceivedAt, x.EventId)).Distinct().Count() != rows.Count)
            throw new ClickHouseStoreException("storage_reconciliation_required", "事件存在重复记录，需要核对存储状态。");
        var more = rows.Count > query.PageSize;
        var items = rows.Take(query.PageSize).ToArray();
        var next = more ? EncodeCursor(new CursorState(fingerprint, items[^1].ReceivedAt.ToUnixTimeMilliseconds(), items[^1].EventId)) : null;
        return new LogSearchPage(items, next, more);
    }

    public async Task<LogEvent?> GetAsync(long eventId, DateTimeOffset receivedAt, IReadOnlyCollection<string> allowedSourceIds, CancellationToken cancellationToken)
    {
        var sources = ValidateScope(allowedSourceIds);
        if (eventId <= 0 || receivedAt.Ticks % TimeSpan.TicksPerMillisecond != 0) throw Invalid("事件标识或接收时间无效。");
        var queryNow = _clock.GetUtcNow();
        var parameters = new Dictionary<string, string> { ["id"] = Number(eventId), ["time"] = Number(receivedAt.ToUnixTimeMilliseconds()), ["now"] = Number(queryNow.ToUnixTimeMilliseconds()) };
        var scope = ScopeSql(sources, parameters);
        var sql = $"SELECT {Columns(true)} FROM {_table} WHERE {scope} AND event_id = {{id:Int64}} AND received_at = fromUnixTimestamp64Milli({{time:Int64}}) AND expires_at > fromUnixTimestamp64Milli({{now:Int64}}) LIMIT 2 FORMAT JSONEachRow";
        var rows = ParseRows(await QueryAsync(sql, parameters, cancellationToken), 2);
        if (rows.Count > 1) throw new ClickHouseStoreException("storage_reconciliation_required", "事件存在重复记录，需要核对存储状态。");
        var item = rows.SingleOrDefault();
        if (item is not null && (item.EventId != eventId || item.ReceivedAt.ToUnixTimeMilliseconds() != receivedAt.ToUnixTimeMilliseconds() || !sources.Contains(item.SourceId, StringComparer.Ordinal) || item.ExpiresAt <= queryNow)) throw StorageFailure();
        return item;
    }

    private async Task<string> QueryAsync(string sql, Dictionary<string, string> parameters, CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken);
        try { return await SqlAsync(sql, parameters, cancellationToken, RequestKind.Query); }
        catch (ClickHouseStoreException error) when (error.Code is "storage_schema_missing" or "storage_connection_failed")
        {
            _state.SchemaReady = false;
            // 库表在服务运行中丢失时由下一次访问重新检查，不重发当前请求。
            throw;
        }
    }

    private async Task CommandAsync(string sql, CancellationToken cancellationToken, RequestKind kind)
    {
        var result = await SqlAsync(sql, [], cancellationToken, kind);
        if (!string.IsNullOrWhiteSpace(result)) throw RequestFailure(kind);
    }

    private async Task<string> SqlAsync(string sql, Dictionary<string, string> parameters, CancellationToken cancellationToken,
        RequestKind kind, bool waitForLightweightDelete = false)
    {
        // ClickHouse 从 URL 读取 HTTP 设置；multipart 只承载 SQL 和绑定参数。
        // 输出格式必须显式生效，否则查询非空时日期字符串不带时区，无法反序列化。
        var settings = BaseSettings();
        if (waitForLightweightDelete) settings["lightweight_deletes_sync"] = "1";
        if (kind is RequestKind.Query or RequestKind.Metadata) settings["readonly"] = "1";
        if (kind == RequestKind.Query)
        {
            settings["date_time_output_format"] = "iso";
            settings["output_format_json_quote_64bit_integers"] = "1";
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, RequestUri(settings));
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(sql), "query");
        foreach (var parameter in parameters) form.Add(new StringContent(parameter.Value), "param_" + parameter.Key);
        request.Content = form;
        return await SendAsync(request, cancellationToken, kind);
    }

    private Uri RequestUri(IReadOnlyDictionary<string, string> settings) => new UriBuilder(_endpoint)
    {
        Query = string.Join("&", settings.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value))),
    }.Uri;

    private Dictionary<string, string> BaseSettings() => new()
    {
        ["wait_end_of_query"] = "1",
        ["max_execution_time"] = Number(_options.TimeoutSeconds),
        ["max_memory_usage"] = Number(_options.MaxMemoryUsageBytes),
        ["max_bytes_to_read"] = Number(_options.MaxBytesToRead),
        ["max_result_bytes"] = Number(_options.MaxResponseBytes),
        ["result_overflow_mode"] = "throw",
        ["timeout_overflow_mode"] = "throw",
        ["max_threads"] = "2",
        ["send_progress_in_http_headers"] = "0",
    };

    private async Task<string> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken, RequestKind kind)
    {
        request.Headers.Add("X-ClickHouse-User", _options.Username);
        if (_options.Password.Length != 0) request.Headers.Add("X-ClickHouse-Key", _options.Password);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        try
        {
            using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var exceptionCode = response.Headers.TryGetValues("X-ClickHouse-Exception-Code", out var codes)
                ? codes.FirstOrDefault(code => code != "0") : null;
            if (!response.IsSuccessStatusCode || exceptionCode is not null)
                throw ResponseFailure(response.StatusCode, exceptionCode, kind);
            if (response.Content.Headers.ContentLength > _options.MaxResponseBytes) throw RequestFailure(kind);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var result = new MemoryStream();
            var bytes = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(bytes, timeout.Token)) != 0)
            {
                if (result.Length + count > _options.MaxResponseBytes) throw RequestFailure(kind);
                result.Write(bytes, 0, count);
            }
            return new UTF8Encoding(false, true).GetString(result.ToArray());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw TimeoutFailure(kind);
        }
        catch (HttpRequestException error) when (kind is not (RequestKind.Insert or RequestKind.Clear) && error.HttpRequestError is
            HttpRequestError.ConnectionError or HttpRequestError.NameResolutionError or HttpRequestError.SecureConnectionError)
        {
            throw new ClickHouseStoreException("storage_connection_failed", "无法连接日志存储（ClickHouse），请检查存储服务和连接配置。");
        }
        catch (HttpRequestException) { throw RequestFailure(kind); }
        catch (IOException) { throw RequestFailure(kind); }
        catch (DecoderFallbackException) { throw RequestFailure(kind); }
    }

    private static ClickHouseStoreException ResponseFailure(HttpStatusCode status, string? exceptionCode, RequestKind kind)
    {
        if (kind == RequestKind.Insert) return StorageFailure();
        // 仅识别已知状态和 ClickHouse 错误编号，响应正文可能包含 SQL、路径和凭据。
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden || exceptionCode is "497" or "516")
            return new("storage_access_denied", kind switch
            {
                RequestKind.Clear => "日志存储（ClickHouse）拒绝清空，请检查清空权限。",
                RequestKind.Initialize => "日志存储（ClickHouse）拒绝初始化，请检查缺失库表的创建权限。",
                _ => "日志存储（ClickHouse）拒绝读取，请检查存储凭据和读取权限。",
            });
        return exceptionCode switch
        {
            "60" or "81" => new("storage_schema_missing", "日志存储缺少所需的数据库或日志表，请联系管理员核对初始化状态。"),
            "359" when kind == RequestKind.Clear => new("log_clear_size_limit", "日志量超过 ClickHouse 清空保护上限，存储已拒绝清空。"),
            "159" => TimeoutFailure(kind),
            _ => RequestFailure(kind),
        };
    }

    private static ClickHouseStoreException TimeoutFailure(RequestKind kind) => kind switch
    {
        RequestKind.Query => new("storage_timeout", "日志查询超时，请缩小时间范围或增加筛选条件后重试。"),
        RequestKind.Insert => new("storage_timeout", "日志写入 ClickHouse 超时，系统将自动重试。"),
        RequestKind.Clear => ClearOutcomeUnknown(),
        _ => new("storage_timeout", "日志存储检查或初始化超时，请稍后重试。"),
    };

    private static ClickHouseStoreException RequestFailure(RequestKind kind) => kind == RequestKind.Clear ? ClearOutcomeUnknown() : StorageFailure();
    private static ClickHouseStoreException ClearOutcomeUnknown() => new("log_clear_outcome_unknown", "暂无法确认日志是否已清空，请先核对存储状态。");
    private static ClickHouseStoreException SchemaIncompatible() => new("storage_schema_incompatible", "日志表结构与当前系统不兼容，请核对已有库表定义。");
    private enum RequestKind { Query, Metadata, Initialize, Insert, Clear }

    private static List<LogEvent> ParseRows(string text, int maximum)
    {
        try
        {
            var rows = new List<LogEvent>();
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (rows.Count >= maximum) throw StorageFailure();
                var row = JsonSerializer.Deserialize<StoredRow>(line, JsonOptions) ?? throw StorageFailure();
                rows.Add(row.ToEvent());
            }
            return rows;
        }
        catch (JsonException) { throw StorageFailure(); }
        catch (FormatException) { throw StorageFailure(); }
        catch (ArgumentException) { throw StorageFailure(); }
    }

    private static string Columns(bool includeRaw) => "event_id, batch_id, source_id, peer_address, transport, received_at, event_time, expires_at, original_timestamp, time_confidence, hostname, app_name, proc_id, msg_id, structured_data, facility, severity, message, " + (includeRaw ? "raw_base64" : "'' AS raw_base64") + ", parse_status, failure_code, parser_version";
    private static string[] ValidateScope(IReadOnlyCollection<string> scope)
    {
        if (scope is null || scope.Count == 0 || scope.Count > 2000 || scope.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 128)) throw Forbidden();
        return scope.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }
    private static string ScopeSql(string[] sources, Dictionary<string, string> parameters)
    {
        for (var i = 0; i < sources.Length; i++) parameters["scope" + i] = sources[i];
        return "source_id IN (" + string.Join(",", sources.Select((_, i) => "{scope" + i + ":String}")) + ")";
    }
    private static void AddStringFilter(List<string> filters, Dictionary<string, string> parameters, string column, string name, string? value)
    {
        if (value is null) return;
        filters.Add(column + " = {" + name + ":String}");
        parameters[name] = value;
    }
    private static string Fingerprint(LogSearch query, string[] scope) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { query.From, query.To, query.SourceId, query.Severity, query.Hostname, query.AppName, query.Keywords, query.PageSize, Scope = scope })));
    private static string EncodeCursor(CursorState cursor) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static CursorState DecodeCursor(string value)
    {
        if (value.Length is 0 or > 2048) throw Invalid("游标无效。");
        try
        {
            var text = value.Replace('-', '+').Replace('_', '/');
            text = text.PadRight((text.Length + 3) / 4 * 4, '=');
            return JsonSerializer.Deserialize<CursorState>(Convert.FromBase64String(text)) ?? throw Invalid("游标无效。");
        }
        catch (Exception ex) when (ex is FormatException or JsonException) { throw Invalid("游标无效。"); }
    }
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static ClickHouseStoreException Invalid(string message) => new("invalid_query", message);
    private static ClickHouseStoreException Forbidden() => new("source_forbidden", "没有可查询的来源权限。");
    private static ClickHouseStoreException StorageFailure() => new("storage_unavailable", "存储请求失败，详细状态请由管理员核对。");
    private sealed record CursorState(string Fingerprint, long ReceivedAt, long EventId);

    private sealed record StoredRow
    {
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
        public string RawBase64 { get; init; } = "";
        public string ParseStatus { get; init; } = "unknown";
        public string FailureCode { get; init; } = "";
        public string ParserVersion { get; init; } = "";

        public static StoredRow FromEvent(LogEvent item) => new()
        {
            EventId = item.EventId, BatchId = item.BatchId, SourceId = item.SourceId, PeerAddress = item.PeerAddress,
            Transport = item.Transport, ReceivedAt = item.ReceivedAt, EventTime = item.EventTime, ExpiresAt = item.ExpiresAt,
            OriginalTimestamp = item.OriginalTimestamp, TimeConfidence = item.TimeConfidence, Hostname = item.Hostname,
            AppName = item.AppName, ProcId = item.ProcId, MsgId = item.MsgId, StructuredData = item.StructuredData,
            Facility = item.Facility, Severity = item.Severity, Message = item.Message, RawBase64 = Convert.ToBase64String(item.RawBytes),
            ParseStatus = item.ParseStatus, FailureCode = item.FailureCode, ParserVersion = item.ParserVersion,
        };
        public LogEvent ToEvent() => new()
        {
            EventId = EventId, BatchId = BatchId, SourceId = SourceId, PeerAddress = PeerAddress, Transport = Transport,
            ReceivedAt = ReceivedAt, EventTime = EventTime, ExpiresAt = ExpiresAt, OriginalTimestamp = OriginalTimestamp,
            TimeConfidence = TimeConfidence, Hostname = Hostname, AppName = AppName, ProcId = ProcId, MsgId = MsgId,
            StructuredData = StructuredData, Facility = Facility, Severity = Severity, Message = Message,
            RawBytes = Convert.FromBase64String(RawBase64), ParseStatus = ParseStatus, FailureCode = FailureCode, ParserVersion = ParserVersion,
        };
    }
}
