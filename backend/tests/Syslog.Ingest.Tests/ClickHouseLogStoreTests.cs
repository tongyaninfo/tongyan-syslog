using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Syslog.Ingest.Data;
using Syslog.Ingest.Options;
using Syslog.Ingest.Service.impl;
using Syslog.Shared.Models;
using Xunit;

namespace Syslog.Ingest.Tests;

public sealed class ClickHouseLogStoreTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-10T10:00:00Z");
    private static LogSearch Query => new() { From = Now.AddDays(-1), To = Now, PageSize = 2 };

    [Fact]
    public async Task Malformed_protocol_fields_preserve_raw_without_poisoning_storage_contract()
    {
        var raw = Encoding.UTF8.GetBytes("<34>1 " + new string('A', 5000) + " host app proc msg - text");
        var parsed = new Syslog.Engine.Service.impl.SyslogParser().Parse(raw,
            new ParseContext("source-1", "192.0.2.10", "udp", Now, TimeZoneInfo.Utc, Now.AddDays(365))) with { EventId = 1, BatchId = "synthetic-batch" };
        Assert.Equal("failed", parsed.ParseStatus);
        Assert.Equal("invalid_timestamp", parsed.FailureCode);
        var handler = Respond("");
        await Create(handler).WriteBatchAsync([parsed], default);
        using var row = JsonDocument.Parse(handler.Requests[0].Body.Trim());
        Assert.Equal(raw, Convert.FromBase64String(row.RootElement.GetProperty("raw_base64").GetString()!));
    }

    [Fact]
    public async Task Submillisecond_filter_is_rejected_instead_of_silently_rounding()
    {
        var handler = Respond("");
        var store = Create(handler);
        await Assert.ThrowsAsync<ClickHouseStoreException>(() => store.SearchAsync(Query with { From = Query.From.AddTicks(1) }, ["source-1"], default));
        await Assert.ThrowsAsync<ClickHouseStoreException>(() => store.GetAsync(1, Now.AddTicks(1), ["source-1"], default));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Write_preserves_raw_bytes_and_never_retries_unknown_commit()
    {
        var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("private database internal-secret") }));
        var store = Create(handler);
        var error = await Assert.ThrowsAsync<ClickHouseStoreException>(() => store.WriteBatchAsync([Event(1) with { RawBytes = [0xff, 0x00, 0xc3] }], default));
        Assert.Equal("storage_unavailable", error.Code);
        Assert.DoesNotContain("internal-secret", error.ToString());
        Assert.Single(handler.Requests);
        using var row = JsonDocument.Parse(handler.Requests[0].Body.Trim());
        Assert.Equal(new byte[] { 0xff, 0x00, 0xc3 }, Convert.FromBase64String(row.RootElement.GetProperty("raw_base64").GetString()!));
        Assert.Contains("wait_end_of_query=1", handler.Requests[0].Uri.Query);
        Assert.Contains("insert_deduplication_token=", handler.Requests[0].Uri.Query);
        Assert.DoesNotContain("internal-secret", handler.Requests[0].Uri.ToString());
    }

    [Fact]
    public async Task Write_rejects_http_200_error_body()
    {
        var handler = Respond("Code: 241. DB::Exception: secret");
        var error = await Assert.ThrowsAsync<ClickHouseStoreException>(() => Create(handler).WriteBatchAsync([Event(1)], default));
        Assert.Equal("storage_unavailable", error.Code);
        Assert.DoesNotContain("secret", error.ToString());
    }

    [Fact]
    public async Task Search_parameterizes_injection_and_forces_scope_and_expiry()
    {
        var handler = Respond("");
        var query = Query with { Keywords = "x' OR 1=1 --", Hostname = "host'", SourceId = "source-1" };
        await Create(handler).SearchAsync(query, ["source-1"], default);
        var sql = handler.Requests[0].Fields["query"];
        Assert.DoesNotContain(query.Keywords, sql);
        Assert.Contains("hasAllTokens(message, {keywords:String})", sql);
        Assert.Contains("source_id IN ({scope0:String})", sql);
        Assert.Contains("expires_at >", sql);
        Assert.Equal(query.Keywords, handler.Requests[0].Fields["param_keywords"]);
        Assert.Equal("source-1", handler.Requests[0].Fields["param_scope0"]);
        var settings = handler.Requests[0].Uri.Query;
        Assert.Contains("wait_end_of_query=1", settings);
        Assert.Contains("readonly=1", settings);
        Assert.Contains("date_time_output_format=iso", settings);
        Assert.Contains("output_format_json_quote_64bit_integers=1", settings);
        Assert.DoesNotContain("param_keywords", settings);
        Assert.DoesNotContain("date_time_output_format", handler.Requests[0].Fields.Keys);
    }

    [Fact]
    public async Task Invalid_scope_time_and_page_do_not_send_network_requests()
    {
        var handler = Respond("");
        var store = Create(handler);
        Assert.Equal("source_forbidden", (await Assert.ThrowsAsync<ClickHouseStoreException>(() => store.SearchAsync(Query, [], default))).Code);
        await Assert.ThrowsAsync<ClickHouseStoreException>(() => store.SearchAsync(Query with { From = Now.AddDays(-31) }, ["source-1"], default));
        await Assert.ThrowsAsync<ClickHouseStoreException>(() => store.SearchAsync(Query with { PageSize = 501 }, ["source-1"], default));
        await Assert.ThrowsAsync<ClickHouseStoreException>(() => store.SearchAsync(Query with { SourceId = "other" }, ["source-1"], default));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Report_reads_scoped_period_counts_and_recent_severity_without_schema_writes()
    {
        var handler = new Handler(async (request, ct) =>
        {
            var sql = await SqlAsync(request, ct);
            var rows = sql.Contains("GROUP BY source_id", StringComparison.Ordinal)
                ? """{"source_id":"source-1","total":"4","emergency":"1","alert":"0","critical":"0","error":"2"}"""
                : """{"event_id":"1","source_id":"source-1","received_at":"2026-11-10T09:00:00Z","severity":0,"summary":"synthetic sample"}""";
            return Response(rows);
        });
        using var state = new ClickHouseStorageState();
        var store = Create(handler, state: state);
        var result = await store.GetReportAsync(Now.AddDays(-31), Now, ["source-1"], default);
        Assert.Equal(new LogReportDeviceCounts("source-1", 4, 1, 0, 0, 2), Assert.Single(result.Devices));
        Assert.Equal("synthetic sample", Assert.Single(result.RecentEntries).Summary);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Contains("readonly=1", request.Uri.Query);
            Assert.Contains("expires_at >", request.Fields["query"]);
            Assert.Equal("source-1", request.Fields["param_scope0"]);
            Assert.DoesNotContain("raw_base64", request.Fields["query"]);
        });
        Assert.Contains("severity IN (0, 1, 2, 3)", handler.Requests[1].Fields["query"]);
        Assert.Contains("LIMIT 10", handler.Requests[1].Fields["query"]);
        Assert.False(state.SchemaReady);
    }

    [Theory]
    [InlineData("12345\n", 12345L)]
    [InlineData("0\n", 0L)]
    [InlineData("", null)]
    public async Task Database_size_is_a_read_only_metadata_probe_even_before_initialization(string response, long? expected)
    {
        using var state = new ClickHouseStorageState();
        var handler = Respond(response);
        var store = Create(handler, new() { Database = "custom_logs" }, state);
        Assert.Equal(expected, await store.GetDatabaseSizeAsync(default));
        var request = Assert.Single(handler.Requests);
        Assert.Contains("readonly=1", request.Uri.Query);
        Assert.Contains("sum(bytes_on_disk) FROM system.parts", request.Fields["query"]);
        Assert.Contains("active = 1", request.Fields["query"]);
        Assert.Equal("custom_logs", request.Fields["param_database"]);
        Assert.False(state.SchemaReady);
    }

    [Fact]
    public async Task Cursor_binds_permissions_and_filters_and_uses_keyset()
    {
        var handler = Respond(Row(3) + Row(2) + Row(1));
        var store = Create(handler);
        var first = await store.SearchAsync(Query, ["source-1"], default);
        Assert.True(first.HasMore);
        Assert.Equal(2, first.Items.Count);
        Assert.NotNull(first.NextCursor);
        await Assert.ThrowsAsync<ClickHouseStoreException>(() => store.SearchAsync(Query with { Cursor = first.NextCursor, Keywords = "different" }, ["source-1"], default));
        await Assert.ThrowsAsync<ClickHouseStoreException>(() => store.SearchAsync(Query with { Cursor = first.NextCursor }, ["source-1", "source-2"], default));
        Assert.Single(handler.Requests);
        await store.SearchAsync(Query with { Cursor = first.NextCursor }, ["source-1"], default);
        Assert.Contains("(received_at, event_id) <", handler.Requests[1].Fields["query"]);
        Assert.Equal("2", handler.Requests[1].Fields["param_cursor_id"]);
    }

    [Fact]
    public async Task Detail_decodes_original_bytes_and_requires_exact_identity()
    {
        var handler = Respond(Row(1, raw: "/wDD"));
        var result = await Create(handler).GetAsync(1, Now.AddHours(-1), ["source-1"], default);
        Assert.NotNull(result);
        Assert.Equal(new byte[] { 0xff, 0, 0xc3 }, result.RawBytes);
        Assert.Contains("received_at = fromUnixTimestamp64Milli", handler.Requests[0].Fields["query"]);
        await Assert.ThrowsAsync<ClickHouseStoreException>(() => Create(Respond(Row(1, source: "other"))).GetAsync(1, Now.AddHours(-1), ["source-1"], default));
    }

    [Fact]
    public async Task Response_budget_malformed_tail_and_duplicates_fail_closed()
    {
        await Assert.ThrowsAsync<ClickHouseStoreException>(() => Create(Respond(new string('x', 2048)), new() { MaxResponseBytes = 1024 }).SearchAsync(Query, ["source-1"], default));
        var error = await Assert.ThrowsAsync<ClickHouseStoreException>(() => Create(Respond(Row(1) + "Code: 241 secret")).SearchAsync(Query, ["source-1"], default));
        Assert.DoesNotContain("secret", error.ToString());
        var duplicate = await Assert.ThrowsAsync<ClickHouseStoreException>(() => Create(Respond(Row(1) + Row(1))).GetAsync(1, Now.AddHours(-1), ["source-1"], default));
        Assert.Equal("storage_reconciliation_required", duplicate.Code);
    }

    [Fact]
    public async Task Transport_exceptions_are_sanitized_and_caller_cancellation_propagates()
    {
        var handler = new Handler((_, _) => throw new HttpRequestException(
            HttpRequestError.ConnectionError, "https://private.example/?secret=hidden"));
        var error = await Assert.ThrowsAsync<ClickHouseStoreException>(() => Create(handler).SearchAsync(Query, ["source-1"], default));
        Assert.Equal("storage_connection_failed", error.Code);
        Assert.Contains("ClickHouse", error.Message);
        Assert.DoesNotContain("private.example", error.ToString());
        var writeError = await Assert.ThrowsAsync<ClickHouseStoreException>(() => Create(handler).WriteBatchAsync([Event(1)], default));
        Assert.Equal("storage_unavailable", writeError.Code);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(Respond("")).SearchAsync(Query, ["source-1"], cancelled.Token));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, null, "storage_access_denied")]
    [InlineData(HttpStatusCode.InternalServerError, "497", "storage_access_denied")]
    [InlineData(HttpStatusCode.InternalServerError, "516", "storage_access_denied")]
    [InlineData(HttpStatusCode.NotFound, "60", "storage_schema_missing")]
    [InlineData(HttpStatusCode.NotFound, "81", "storage_schema_missing")]
    [InlineData(HttpStatusCode.OK, "159", "storage_timeout")]
    [InlineData(HttpStatusCode.InternalServerError, "synthetic-secret", "storage_unavailable")]
    public async Task Query_errors_use_stable_diagnostics_without_returning_storage_response(
        HttpStatusCode status, string? exceptionCode, string expected)
    {
        var handler = new Handler((_, _) =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent("synthetic-secret") };
            if (exceptionCode is not null) response.Headers.Add("X-ClickHouse-Exception-Code", exceptionCode);
            return Task.FromResult(response);
        });
        var error = await Assert.ThrowsAsync<ClickHouseStoreException>(() => Create(handler).SearchAsync(Query, ["source-1"], default));
        Assert.Equal(expected, error.Code);
        Assert.DoesNotContain("synthetic-secret", error.ToString());
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Batch_contract_and_byte_budget_are_checked_before_send()
    {
        var handler = Respond("");
        var store = Create(handler, new() { MaxBatchBytes = 1024 });
        await Assert.ThrowsAsync<ClickHouseStoreException>(() => store.WriteBatchAsync([Event(1), Event(1)], default));
        await Assert.ThrowsAsync<ClickHouseStoreException>(() => store.WriteBatchAsync([Event(1) with { Message = new string('x', 2048) }], default));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Retrying_identical_batch_uses_identical_token_but_does_not_retry_automatically()
    {
        var handler = Respond("");
        var store = Create(handler);
        await store.WriteBatchAsync([Event(1)], default);
        await store.WriteBatchAsync([Event(1)], default);
        Assert.Equal(handler.Requests[0].Uri, handler.Requests[1].Uri);
        await store.WriteBatchAsync([Event(1) with { Message = "changed" }], default);
        Assert.NotEqual(handler.Requests[0].Uri, handler.Requests[2].Uri);
    }

    [Fact]
    public async Task Timeout_is_bounded_and_does_not_expose_transport_details()
    {
        var handler = new Handler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var error = await Assert.ThrowsAsync<ClickHouseStoreException>(() => Create(handler, new() { TimeoutSeconds = 1 }).SearchAsync(Query, ["source-1"], default));
        Assert.Equal("storage_timeout", error.Code);
        Assert.Contains("缩小时间范围", error.Message);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Search_rejects_duplicate_rows_and_punctuation_only_keywords()
    {
        var duplicate = await Assert.ThrowsAsync<ClickHouseStoreException>(() => Create(Respond(Row(1) + Row(1))).SearchAsync(Query, ["source-1"], default));
        Assert.Equal("storage_reconciliation_required", duplicate.Code);
        var handler = Respond("");
        var error = await Assert.ThrowsAsync<ClickHouseStoreException>(() => Create(handler).SearchAsync(Query with { Keywords = "---" }, ["source-1"], default));
        Assert.Equal("unsupported_search_token", error.Code);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("交换机")]
    [InlineData("error 失败")]
    [InlineData("échec")]
    public async Task Non_ascii_keywords_fail_before_http_instead_of_matching_everything(string keywords)
    {
        var handler = Respond("");
        var error = await Assert.ThrowsAsync<ClickHouseStoreException>(() => Create(handler).SearchAsync(Query with { Keywords = keywords }, ["source-1"], default));
        Assert.Equal("unsupported_search_token", error.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task First_connection_creates_only_missing_objects_once_across_http_clients()
    {
        using var state = new ClickHouseStorageState();
        var handler = new Handler(async (request, ct) =>
        {
            var sql = await SqlAsync(request, ct);
            return sql switch
            {
                "EXISTS DATABASE `tenant_logs` FORMAT TabSeparated" or "EXISTS TABLE `tenant_logs`.`events` FORMAT TabSeparated" => Response("0\n"),
                _ when sql.StartsWith("CREATE ", StringComparison.Ordinal) => Response(""),
                _ when sql.Contains("FROM system.columns", StringComparison.Ordinal) => Response(ExpectedColumns()),
                _ => throw new InvalidOperationException("Unexpected SQL"),
            };
        });
        var options = new ClickHouseOptions { Database = "tenant_logs", MaxResponseBytes = 1024 };
        var store = Create(handler, options, state);
        var second = Create(handler, options, state);
        await Task.WhenAll(store.EnsureReadyAsync(default), second.EnsureReadyAsync(default));

        Assert.Collection(handler.Requests.Select(x => x.Fields["query"]),
            sql => Assert.StartsWith("EXISTS DATABASE `tenant_logs`", sql),
            sql => Assert.Equal("CREATE DATABASE IF NOT EXISTS `tenant_logs`", sql),
            sql => Assert.StartsWith("EXISTS TABLE `tenant_logs`.`events`", sql),
            sql => Assert.StartsWith("CREATE TABLE IF NOT EXISTS `tenant_logs`.`events`", sql),
            sql => Assert.Contains("FROM system.columns", sql));
        Assert.All(handler.Requests, request => Assert.DoesNotContain("database", request.Fields.Keys));
        Assert.Contains("readonly=1", handler.Requests[0].Uri.Query);
        Assert.DoesNotContain("readonly", handler.Requests[1].Uri.Query);
        Assert.DoesNotContain("readonly", handler.Requests[3].Uri.Query);
        Assert.Contains("TYPE text", handler.Requests[3].Fields["query"]);
        Assert.Equal("tenant_logs", handler.Requests[^1].Fields["param_database"]);
    }

    [Fact]
    public async Task Existing_incompatible_table_is_reported_without_ddl()
    {
        using var state = new ClickHouseStorageState();
        var handler = new Handler(async (request, ct) => (await SqlAsync(request, ct)) switch
        {
            var sql when sql.StartsWith("EXISTS ", StringComparison.Ordinal) => Response("1\n"),
            var sql when sql.Contains("FROM system.columns", StringComparison.Ordinal) => Response("[\"event_id\",\"String\"]\n"),
            _ => throw new InvalidOperationException("Unexpected SQL"),
        });
        var error = await Assert.ThrowsAsync<ClickHouseStoreException>(() => Create(handler, state: state).SearchAsync(Query, ["source-1"], default));
        Assert.Equal("storage_schema_incompatible", error.Code);
        Assert.Equal(3, handler.Requests.Count);
        Assert.DoesNotContain(handler.Requests, x => x.Fields["query"].StartsWith("CREATE ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unknown_clear_outcome_blocks_later_inserts_before_sending_marker()
    {
        using var state = new ClickHouseStorageState { SchemaReady = true };
        var handler = new Handler(async (request, ct) =>
        {
            var sql = await SqlAsync(request, ct);
            if (sql.Contains("FROM system.columns", StringComparison.Ordinal)) return Response(ExpectedColumns());
            if (sql.Contains("FROM system.tables", StringComparison.Ordinal)) return Response("MergeTree\n");
            Assert.Equal("TRUNCATE TABLE `syslog`.`events` SYNC", sql);
            throw new HttpRequestException(HttpRequestError.ResponseEnded, "synthetic private diagnostic");
        });
        var store = Create(handler, state: state);
        var error = await Assert.ThrowsAsync<ClickHouseStoreException>(() => store.ClearAsync(default));
        Assert.Equal("log_clear_outcome_unknown", error.Code);
        Assert.DoesNotContain("private diagnostic", error.Message);
        Assert.DoesNotContain("readonly", handler.Requests[^1].Uri.Query);
        var markerCalls = 0;
        var otherClient = Create(handler, state: state);
        error = await Assert.ThrowsAsync<ClickHouseStoreException>(() => otherClient.WriteBatchAsync([Event(1)], _ =>
        {
            markerCalls++;
            return Task.CompletedTask;
        }, default));
        Assert.Equal("log_clear_outcome_unknown", error.Code);
        Assert.Equal(0, markerCalls);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Device_clear_binds_source_and_cutoff_and_unknown_outcome_allows_later_inserts()
    {
        using var state = new ClickHouseStorageState { SchemaReady = true };
        var handler = new Handler(async (request, ct) =>
        {
            if (request.Content is ByteArrayContent) return Response("");
            var sql = await SqlAsync(request, ct);
            if (sql.Contains("FROM system.columns", StringComparison.Ordinal)) return Response(ExpectedColumns());
            if (sql.Contains("FROM system.tables", StringComparison.Ordinal)) return Response("MergeTree\n");
            Assert.Equal("DELETE FROM `syslog`.`events` WHERE source_id = {sourceId:String} " +
                "AND received_at < fromUnixTimestamp64Milli({cutoff:Int64}, 'UTC')", sql);
            throw new HttpRequestException(HttpRequestError.ResponseEnded, "synthetic private diagnostic");
        });
        var store = Create(handler, state: state);
        var error = await Assert.ThrowsAsync<ClickHouseStoreException>(() => store.ClearSourceAsync("9007199254740993", default));
        Assert.Equal("log_clear_outcome_unknown", error.Code);
        Assert.Equal("9007199254740993", handler.Requests[^1].Fields["param_sourceId"]);
        Assert.Equal(Now.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            handler.Requests[^1].Fields["param_cutoff"]);
        Assert.Contains("lightweight_deletes_sync=1", handler.Requests[^1].Uri.Query);

        await Create(handler, state: state).WriteBatchAsync([Event(1) with { SourceId = "other-device" }], default);
        Assert.Contains("INSERT", Uri.UnescapeDataString(handler.Requests[^1].Uri.Query));
    }

    [Fact]
    public async Task Clear_waits_for_in_flight_insert_and_size_rejection_leaves_writes_available()
    {
        using var state = new ClickHouseStorageState { SchemaReady = true };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var insertStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseInsert = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Handler(async (request, ct) =>
        {
            if (request.Content is ByteArrayContent)
            {
                insertStarted.TrySetResult();
                await releaseInsert.Task.WaitAsync(ct);
                return Response("");
            }
            var sql = await SqlAsync(request, ct);
            if (sql.Contains("FROM system.columns", StringComparison.Ordinal)) return Response(ExpectedColumns());
            if (sql.Contains("FROM system.tables", StringComparison.Ordinal)) return Response("MergeTree\n");
            Assert.Equal("TRUNCATE TABLE `syslog`.`events` SYNC", sql);
            var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
            response.Headers.Add("X-ClickHouse-Exception-Code", "359");
            return response;
        });
        var ingest = Create(handler, state: state);
        var management = Create(handler, state: state);
        var writing = ingest.WriteBatchAsync([Event(1)], timeout.Token);
        await insertStarted.Task.WaitAsync(timeout.Token);
        var clearing = management.ClearAsync(timeout.Token);
        Assert.False(clearing.IsCompleted);
        Assert.Single(handler.Requests);
        releaseInsert.SetResult();
        await writing;
        Assert.Equal("log_clear_size_limit", (await Assert.ThrowsAsync<ClickHouseStoreException>(() => clearing)).Code);
        await ingest.WriteBatchAsync([Event(2)], timeout.Token);
        Assert.Equal(5, handler.Requests.Count);
        Assert.Contains("wait_end_of_query=1", handler.Requests[3].Uri.Query);
    }

    private static ClickHouseLogStore Create(Handler handler, ClickHouseOptions? options = null, ClickHouseStorageState? state = null) =>
        new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(options ?? new ClickHouseOptions()), new FixedClock(), state ?? new ClickHouseStorageState { SchemaReady = true });
    private static Task<string> SqlAsync(HttpRequestMessage request, CancellationToken ct) =>
        ((MultipartFormDataContent)request.Content!).Single(field => field.Headers.ContentDisposition?.Name?.Trim('"') == "query").ReadAsStringAsync(ct);
    private static string ExpectedColumns() => string.Join('\n', ClickHouseSchema.Columns.Select(pair => JsonSerializer.Serialize(new[] { pair.Key, pair.Value })));
    private static HttpResponseMessage Response(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) };
    private static Handler Respond(string body) => new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") }));
    private static LogEvent Event(long id) => new() { EventId = id, BatchId = "batch-1", SourceId = "source-1", ReceivedAt = Now.AddHours(-1), ExpiresAt = Now.AddYears(1), RawBytes = [1], Message = "synthetic test" };
    private static string Row(long id, string source = "source-1", string raw = "") => JsonSerializer.Serialize(new { event_id = id.ToString(), batch_id = "batch-1", source_id = source, received_at = Now.AddHours(-1), expires_at = Now.AddYears(1), raw_base64 = raw, message = "test" }) + "\n";
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    {
        public List<Captured> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fields = new Dictionary<string, string>();
            if (request.Content is MultipartFormDataContent multipart)
                foreach (var item in multipart) fields[item.Headers.ContentDisposition!.Name!.Trim('"')] = await item.ReadAsStringAsync(cancellationToken);
            Requests.Add(new Captured(request.RequestUri!, await request.Content!.ReadAsStringAsync(cancellationToken), fields));
            return await action(request, cancellationToken);
        }
    }
    private sealed record Captured(Uri Uri, string Body, Dictionary<string, string> Fields);
}
