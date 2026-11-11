using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Syslog.Shared.Models;
using Syslog.Shared.Service;
using Xunit;

namespace Syslog.Api.Tests;

public sealed partial class UserManagementApiTests
{
    private const string LogClearPath = "/api/admin/v1/logs/clear";

    [Fact]
    public async Task LogClearRequiresAdminSameOriginHttpsAndExactConfirmation()
    {
        var store = new ClearTestStore();
        await using var app = await Harness.StartAsync(logs: store);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client.PostAsJsonAsync(LogClearPath, new { confirmation = "CLEAR" })).StatusCode);
        await app.BootstrapAndLoginAsync();
        app.ForceNonAdmin = true;
        Assert.Equal(HttpStatusCode.Forbidden, (await app.Client.PostAsJsonAsync(LogClearPath, new { confirmation = "CLEAR" })).StatusCode);
        app.ForceNonAdmin = false;
        app.Client.DefaultRequestHeaders.Add("Origin", "https://other.invalid");
        Assert.Equal("csrf_rejected", await CodeAsync(await app.Client.PostAsJsonAsync(LogClearPath, new { confirmation = "CLEAR" })));
        app.Client.DefaultRequestHeaders.Remove("Origin");
        Assert.Equal("https_required", await CodeAsync(await app.Client.PostAsJsonAsync("http://localhost" + LogClearPath, new { confirmation = "CLEAR" })));
        Assert.Equal("log_clear_confirmation_required", await CodeAsync(await app.Client.PostAsJsonAsync(LogClearPath, new { confirmation = "clear" })));
        Assert.Equal("invalid_request", await CodeAsync(await app.Client.PostAsJsonAsync(LogClearPath, new { confirmation = "CLEAR", sourceId = "synthetic-source" })));
        Assert.Equal(0, store.ClearCalls);
        Assert.Equal(0L, await ReadClearAuditAsync(app, "SELECT count(*) FROM audit_entries WHERE action='log.clear.request';"));
    }

    [Fact]
    public async Task LogClearAuditsIntentBeforeStorageAndRejectsConcurrentClear()
    {
        var store = new ClearTestStore();
        await using var app = await Harness.StartAsync(logs: store);
        await app.BootstrapAndLoginAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.OnClear = async () =>
        {
            entered.SetResult();
            await finish.Task;
        };
        var first = app.Client.PostAsJsonAsync(LogClearPath, new { confirmation = "CLEAR" });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1L, await ReadClearAuditAsync(app, "SELECT count(*) FROM audit_entries WHERE action='log.clear.request';"));
            Assert.Equal(0L, await ReadClearAuditAsync(app, "SELECT count(*) FROM audit_entries WHERE action='log.clear.result';"));
            var duplicate = await app.Client.PostAsJsonAsync(LogClearPath, new { confirmation = "CLEAR" });
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
            Assert.Equal("log_clear_in_progress", await CodeAsync(duplicate));
        }
        finally { finish.TrySetResult(); }
        var response = await first;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<ApiResult<bool>>())!.Data);
        Assert.Equal(1, store.ClearCalls);
        Assert.Equal("succeeded", await ReadClearAuditAsync(app,
            "SELECT json_extract(details_json,'$.outcome') FROM audit_entries WHERE action='log.clear.result';"));
        Assert.Equal(1L, await ReadClearAuditAsync(app, """
            SELECT count(*) FROM audit_entries AS result JOIN audit_entries AS intent
              ON CAST(intent.id AS TEXT)=json_extract(result.details_json,'$.operationId')
              AND intent.actor_user_id=result.actor_user_id
            WHERE result.action='log.clear.result' AND intent.action='log.clear.request'
              AND intent.target_id='events' AND json_extract(intent.details_json,'$.database')='syslog';
            """));
    }

    [Theory]
    [InlineData("log.clear.request", "service_unavailable", 0)]
    [InlineData("log.clear.result", "log_clear_outcome_unknown", 1)]
    public async Task LogClearDoesNotReportSuccessWhenAuditFails(string rejectedAction, string expectedCode, int expectedCalls)
    {
        var store = new ClearTestStore();
        await using var app = await Harness.StartAsync(logs: store);
        await app.BootstrapAndLoginAsync();
        // 仅测试已声明的两个固定动作，使用独立内存库模拟审计写入失败。
        Assert.Contains(rejectedAction, new[] { "log.clear.request", "log.clear.result" });
        await using var command = app.Anchor.CreateCommand();
        command.CommandText = $"""
            CREATE TRIGGER reject_log_clear_audit BEFORE INSERT ON audit_entries
            WHEN new.action='{rejectedAction}'
            BEGIN SELECT RAISE(ABORT,'synthetic audit failure'); END;
            """;
        await command.ExecuteNonQueryAsync();
        var response = await app.Client.PostAsJsonAsync(LogClearPath, new { confirmation = "CLEAR" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(expectedCode, await CodeAsync(response));
        Assert.Equal(expectedCalls, store.ClearCalls);
        Assert.Equal((long)expectedCalls, await ReadClearAuditAsync(app, "SELECT count(*) FROM audit_entries WHERE action='log.clear.request';"));
    }

    [Fact]
    public async Task LogClearDoesNotRetryOrExposeUnexpectedStorageFailure()
    {
        var store = new ClearTestStore { OnClear = () => throw new IOException("synthetic-private-details") };
        await using var app = await Harness.StartAsync(logs: store);
        await app.BootstrapAndLoginAsync();
        var response = await app.Client.PostAsJsonAsync(LogClearPath, new { confirmation = "CLEAR" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApiResult<bool>>();
        Assert.Equal("log_clear_outcome_unknown", result!.Code);
        Assert.DoesNotContain("synthetic-private-details", result.Message);
        Assert.Equal(1, store.ClearCalls);
        Assert.Equal("unknown", await ReadClearAuditAsync(app,
            "SELECT json_extract(details_json,'$.outcome') FROM audit_entries WHERE action='log.clear.result';"));
    }

    [Fact]
    public async Task DeviceLogClearValidatesSourceAndAuditsTheScopedRequestAndResult()
    {
        var store = new ClearTestStore();
        await using var app = await Harness.StartAsync(logs: store);
        const string sourcesPath = "/api/admin/v1/sources";
        const string missingPath = sourcesPath + "/900000000000001/logs/clear";
        var confirmation = new { confirmation = "CLEAR" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.Client.PostAsJsonAsync(missingPath, confirmation)).StatusCode);
        await app.BootstrapAndLoginAsync();
        var missing = await app.Client.PostAsJsonAsync(missingPath, confirmation);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("source_not_found", await CodeAsync(missing));
        Assert.Empty(store.ClearedSources);

        var created = await app.Client.PostAsJsonAsync(sourcesPath, new
        {
            name = "synthetic-switch",
            peerAddress = "192.0.2.10",
            timeZoneId = "UTC",
            retentionDays = 30,
            enabled = false,
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var source = (await created.Content.ReadFromJsonAsync<ApiResult<LogSource>>())!.Data!;
        var path = $"{sourcesPath}/{source.Id}/logs/clear";
        Assert.Equal("log_clear_confirmation_required",
            await CodeAsync(await app.Client.PostAsJsonAsync(path, new { confirmation = "clear" })));
        Assert.Equal("invalid_request", await CodeAsync(await app.Client.PostAsJsonAsync(path,
            new { confirmation = "CLEAR", sourceId = "2" })));
        Assert.Equal(0L, await ReadClearAuditAsync(app, "SELECT count(*) FROM audit_entries WHERE action='log.clear.request';"));

        var response = await app.Client.PostAsJsonAsync(path, confirmation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<ApiResult<bool>>())!.Data);
        Assert.Equal(0, store.ClearCalls);
        Assert.Equal(source.Id, Assert.Single(store.ClearedSources));
        Assert.Equal(source.Id, await ReadClearAuditAsync(app, """
            SELECT intent.target_id FROM audit_entries AS result JOIN audit_entries AS intent
              ON CAST(intent.id AS TEXT)=json_extract(result.details_json,'$.operationId')
              AND intent.actor_user_id=result.actor_user_id
            WHERE result.action='log.clear.result' AND intent.action='log.clear.request'
              AND result.target_id=intent.target_id
              AND json_extract(intent.details_json,'$.scope')='source'
              AND json_extract(result.details_json,'$.scope')='source'
              AND json_extract(result.details_json,'$.outcome')='succeeded';
            """));
    }

    private static async Task<object?> ReadClearAuditAsync(Harness app, string sql)
    {
        await using var command = app.Anchor.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private sealed class ClearTestStore : ILogStore
    {
        public int ClearCalls { get; private set; }
        public List<string> ClearedSources { get; } = [];
        public Func<Task>? OnClear { get; set; }

        public Task ClearAsync(CancellationToken cancellationToken)
        {
            ClearCalls++;
            return OnClear?.Invoke() ?? Task.CompletedTask;
        }

        public Task ClearSourceAsync(string sourceId, CancellationToken cancellationToken)
        {
            ClearedSources.Add(sourceId);
            return OnClear?.Invoke() ?? Task.CompletedTask;
        }

        public Task WriteBatchAsync(IReadOnlyList<LogEvent> events, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<LogSearchPage> SearchAsync(LogSearch query, IReadOnlyCollection<string> allowedSourceIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<LogEvent?> GetAsync(long eventId, DateTimeOffset receivedAt, IReadOnlyCollection<string> allowedSourceIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
