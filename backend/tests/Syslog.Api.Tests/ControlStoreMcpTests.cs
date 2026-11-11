using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Syslog.Api.Models;
using Syslog.Api.Service.impl;
using Xunit;

namespace Syslog.Api.Tests;

public sealed partial class ControlStoreTests
{
    private static CreateMcpKeyRequest ReadMcpKeyInput(DateTimeOffset? expiry = null) =>
        new("合成只读客户端", "read", ["syslog_logs_search", "syslog_mcp_identity_get"], expiry);

    [Fact]
    public async Task McpRequiresExplicitVersionFiveMigrationAndPreservesOlderFeatures()
    {
        await using var db = await TestDatabase.OpenAsync(initialize: false);
        await using var resource = typeof(ControlStore).Assembly.GetManifestResourceStream("Syslog.Api.Data.001_control.sql")!;
        using var reader = new StreamReader(resource);
        await db.ExecuteAsync(await reader.ReadToEndAsync(Ct));
        Assert.Equal("mcp_migration_required", (await db.Store.ReadMcpSettingsAsync(Ct)).Code);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Store.MigrateMcpAsync(Ct));
        await db.Store.MigrateListenersAsync(Ct);
        await db.Store.MigrateSourceFiltersAsync(Ct);
        await db.Store.MigrateNotificationsAsync(Ct);
        var admin = await db.BootstrapAsync();
        var token = (await db.Store.LoginAsync("admin", Password, Ct)).Data!.Token;
        var source = (await db.Store.CreateSourceAsync(Source with { IgnoredSeverities = [3, 6] }, admin.Id, Ct)).Data!;
        await db.Store.SaveSmtpSettingsAsync(SyntheticSmtp, admin.Id, token, Ct);
        var tablesBefore = Convert.ToInt64(await db.ScalarAsync("SELECT count(*) FROM sqlite_master WHERE type='table';"), CultureInfo.InvariantCulture);
        await db.Store.InitializeAsync(Ct);
        Assert.Equal(4L, await db.ScalarAsync("SELECT max(version) FROM schema_versions;"));
        Assert.Equal("mcp_migration_required", (await db.Store.ListMcpKeysAsync(Ct)).Code);
        await db.Store.RecoverMcpCallsAsync(Ct);
        Assert.Null(await db.Store.CleanupMcpCallsAsync(Ct));
        await db.Store.MigrateMcpAsync(Ct);
        await db.Store.MigrateMcpAsync(Ct);
        Assert.Equal(5L, await db.ScalarAsync("SELECT max(version) FROM schema_versions;"));
        Assert.Equal(tablesBefore + 3, await db.ScalarAsync("SELECT count(*) FROM sqlite_master WHERE type='table';"));
        Assert.Equal(new McpSettingsView("1", false, 30), (await db.Store.ReadMcpSettingsAsync(Ct)).Data);
        var migratedSource = Assert.Single(await db.Store.ListAsync(Ct));
        Assert.Equal(source.Id, migratedSource.Id);
        Assert.Equal(new[] { 3, 6 }, migratedSource.IgnoredSeverities!);
        Assert.Equal(SyntheticSmtp.Host, (await db.Store.GetSmtpSettingsAsync(Ct)).Data!.Host);
        Assert.True((await db.Store.ReadListenerSettingsAsync(Ct)).Success);
        Assert.False(await db.Store.IsSetupRequiredAsync(Ct));
    }

    [Fact]
    public async Task McpRejectsPartialVersionFiveSchemaWithoutRepair()
    {
        await using var db = await TestDatabase.OpenAsync(initialize: false);
        foreach (var name in new[] { "001_control", "002_listener_settings", "003_source_filters", "004_notification_settings" })
        {
            await using var resource = typeof(ControlStore).Assembly.GetManifestResourceStream("Syslog.Api.Data." + name + ".sql")!;
            using var reader = new StreamReader(resource);
            await db.ExecuteAsync(await reader.ReadToEndAsync(Ct));
        }
        await db.ExecuteAsync("INSERT INTO schema_versions(version,applied_at) VALUES(5,0);");
        await Assert.ThrowsAsync<SqliteException>(() => db.Store.MigrateMcpAsync(Ct));
        Assert.Equal(0L, await db.ScalarAsync("SELECT count(*) FROM sqlite_master WHERE name LIKE 'mcp_%';"));
    }

    [Fact]
    public async Task McpSettingsUseVersionAndRequireCurrentCookieSession()
    {
        await using var db = await TestDatabase.OpenAsync();
        var admin = await db.BootstrapAsync();
        var token = (await db.Store.LoginAsync("admin", Password, Ct)).Data!.Token;
        Assert.Equal("authentication_required", (await db.Store.SetMcpEnabledAsync(new("1", true), admin.Id, "invalid", Ct)).Code);
        var enabled = (await db.Store.SetMcpEnabledAsync(new("1", true), admin.Id, token, Ct)).Data!;
        Assert.Equal(new McpSettingsView("2", true, 30), enabled);
        Assert.Equal("version_conflict", (await db.Store.SetMcpRetentionAsync(new("1", 7), admin.Id, token, Ct)).Code);
        Assert.Equal("invalid_request", (await db.Store.SetMcpRetentionAsync(new("2", 0), admin.Id, token, Ct)).Code);
        Assert.Equal(new McpSettingsView("3", true, 7), (await db.Store.SetMcpRetentionAsync(new("2", 7), admin.Id, token, Ct)).Data);
        var reopened = new ControlStore(db.Options, db.Clock);
        Assert.Equal(new McpSettingsView("3", true, 7), (await reopened.ReadMcpSettingsAsync(Ct)).Data);
    }

    [Fact]
    public async Task McpKeyPermissionsRequireExplicitSelectionAndReadModeCannotGrantWrites()
    {
        await using var db = await TestDatabase.OpenAsync();
        var admin = await db.BootstrapAsync();
        var token = (await db.Store.LoginAsync("admin", Password, Ct)).Data!.Token;
        Assert.Equal("invalid_request", (await db.Store.CreateMcpKeyAsync(ReadMcpKeyInput() with { Permissions = [] }, admin.Id, token, Ct)).Code);
        Assert.Equal("mcp_permission_invalid", (await db.Store.CreateMcpKeyAsync(ReadMcpKeyInput() with { Permissions = ["syslog_logs_clear_all"] }, admin.Id, token, Ct)).Code);
        Assert.Equal("mcp_permission_invalid", (await db.Store.CreateMcpKeyAsync(ReadMcpKeyInput() with { Permissions = ["*"] }, admin.Id, token, Ct)).Code);
        Assert.Equal("mcp_expiry_invalid", (await db.Store.CreateMcpKeyAsync(ReadMcpKeyInput(db.Clock.GetUtcNow()), admin.Id, token, Ct)).Code);
        var created = (await db.Store.CreateMcpKeyAsync(ReadMcpKeyInput(), admin.Id, token, Ct)).Data!;
        var principal = (await db.Store.AuthenticateMcpAsync(created.Secret, Ct)).Data!;
        Assert.Equal("mcp_permission_denied", (await db.Store.ValidateMcpPermissionAsync(principal, "syslog_logs_search", Ct)).Code);
        await db.Store.SetMcpEnabledAsync(new("1", true), admin.Id, token, Ct);
        Assert.True((await db.Store.ValidateMcpPermissionAsync(principal, "syslog_logs_search", Ct)).Success);
        Assert.False((await db.Store.ValidateMcpPermissionAsync(principal, "syslog_users_list", Ct)).Success);
        Assert.False((await db.Store.ValidateMcpPermissionAsync(principal, "syslog_logs_clear_all", Ct)).Success);
        await db.ExecuteAsync("UPDATE users SET enabled=0;");
        Assert.False((await db.Store.AuthenticateMcpAsync(created.Secret, Ct)).Success);
        Assert.False((await db.Store.ValidateMcpPermissionAsync(principal, "syslog_logs_search", Ct)).Success);
    }

    [Fact]
    public async Task McpKeyCanBeCopiedAfterRestartButListsAndAdminAuditsNeverReturnItsSecret()
    {
        await using var db = await TestDatabase.OpenAsync();
        var admin = await db.BootstrapAsync();
        var token = (await db.Store.LoginAsync("admin", Password, Ct)).Data!.Token;
        var created = (await db.Store.CreateMcpKeyAsync(ReadMcpKeyInput(), admin.Id, token, Ct)).Data!;
        Assert.Null(created.Key.ExpiresAt);
        Assert.Equal(created.Secret, await db.ScalarAsync("SELECT secret FROM mcp_api_keys;"));
        var list = (await db.Store.ListMcpKeysAsync(Ct)).Data!;
        Assert.DoesNotContain(created.Secret, JsonSerializer.Serialize(list), StringComparison.Ordinal);
        var reopened = new ControlStore(db.Options, db.Clock);
        Assert.True((await reopened.AuthenticateMcpAsync(created.Secret, Ct)).Success);
        Assert.Equal(created.Secret, (await reopened.GetMcpConnectionMaterialAsync(created.Key.Id, admin.Id, token, Ct)).Data!.Secret);
        var audit = (string)(await db.ScalarAsync("SELECT group_concat(details_json) FROM audit_entries WHERE action LIKE 'mcp.%';"))!;
        Assert.DoesNotContain(created.Secret, audit, StringComparison.Ordinal);
        Assert.Equal("mcp_key_revoke_required", (await reopened.DeleteMcpKeyAsync(created.Key.Id, admin.Id, token, Ct)).Code);
    }

    [Fact]
    public async Task McpExpiryRevocationAndDeletionAreDistinctAndImmediatelyEnforced()
    {
        await using var db = await TestDatabase.OpenAsync();
        var admin = await db.BootstrapAsync();
        var token = (await db.Store.LoginAsync("admin", Password, Ct)).Data!.Token;
        var created = (await db.Store.CreateMcpKeyAsync(ReadMcpKeyInput(db.Clock.GetUtcNow().AddMinutes(1)), admin.Id, token, Ct)).Data!;
        db.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False((await db.Store.AuthenticateMcpAsync(created.Secret, Ct)).Success);
        Assert.Equal("expired", Assert.Single((await db.Store.ListMcpKeysAsync(Ct)).Data!).Status);
        Assert.Equal("mcp_key_inactive", (await db.Store.GetMcpConnectionMaterialAsync(created.Key.Id, admin.Id, token, Ct)).Code);
        Assert.Equal("mcp_key_revoke_required", (await db.Store.DeleteMcpKeyAsync(created.Key.Id, admin.Id, token, Ct)).Code);
        Assert.True((await db.Store.RevokeMcpKeyAsync(created.Key.Id, admin.Id, token, Ct)).Success);
        Assert.True((await db.Store.RevokeMcpKeyAsync(created.Key.Id, admin.Id, token, Ct)).Success);
        Assert.Equal("revoked", Assert.Single((await db.Store.ListMcpKeysAsync(Ct)).Data!).Status);
        Assert.True((await db.Store.DeleteMcpKeyAsync(created.Key.Id, admin.Id, token, Ct)).Success);
        Assert.Empty((await db.Store.ListMcpKeysAsync(Ct)).Data!);
    }

    [Fact]
    public async Task McpAdministrativeMutationRollsBackWhenReliableAuditFails()
    {
        await using var db = await TestDatabase.OpenAsync();
        var admin = await db.BootstrapAsync();
        var token = (await db.Store.LoginAsync("admin", Password, Ct)).Data!.Token;
        var created = (await db.Store.CreateMcpKeyAsync(ReadMcpKeyInput(), admin.Id, token, Ct)).Data!;
        await db.RejectAuditAsync();
        await Assert.ThrowsAsync<SqliteException>(() => db.Store.CreateMcpKeyAsync(ReadMcpKeyInput(), admin.Id, token, Ct));
        await Assert.ThrowsAsync<SqliteException>(() => db.Store.SetMcpEnabledAsync(new("1", true), admin.Id, token, Ct));
        await Assert.ThrowsAsync<SqliteException>(() => db.Store.RevokeMcpKeyAsync(created.Key.Id, admin.Id, token, Ct));
        Assert.Single((await db.Store.ListMcpKeysAsync(Ct)).Data!);
        Assert.Equal("active", Assert.Single((await db.Store.ListMcpKeysAsync(Ct)).Data!).Status);
        Assert.False((await db.Store.ReadMcpSettingsAsync(Ct)).Data!.Enabled);
    }

    [Fact]
    public async Task McpExchangeStoresExactSyntheticHeadersBodiesAndSurvivesKeyDeletion()
    {
        await using var db = await TestDatabase.OpenAsync();
        var admin = await db.BootstrapAsync();
        var token = (await db.Store.LoginAsync("admin", Password, Ct)).Data!.Token;
        var created = (await db.Store.CreateMcpKeyAsync(ReadMcpKeyInput(), admin.Id, token, Ct)).Data!;
        var principal = (await db.Store.AuthenticateMcpAsync(created.Secret, Ct)).Data!;
        var body = "{\"message\":\"<script>synthetic-only</script>\",\"password\":\"synthetic-value\"}";
        var start = McpStart(principal, body) with { RequestHeaders = new() { ["Authorization"] = ["Bearer " + created.Secret], ["X-Synthetic"] = ["one", "two"] } };
        var id = (await db.Store.BeginMcpCallAsync(start, Ct)).Data!;
        var response = "{\"success\":true,\"message\":\"完整合成原文\"}";
        Assert.True(await db.Store.CompleteMcpCallAsync(id, new(200, new() { ["X-Synthetic"] = ["response-one", "response-two"] },
            response, Encoding.UTF8.GetByteCount(response), "success", "OK", 12), Ct));
        Assert.False(await db.Store.CompleteMcpCallAsync(id, new(200, [], "different", 9, "success", "OK", 14), Ct));
        await db.Store.RevokeMcpKeyAsync(created.Key.Id, admin.Id, token, Ct);
        await db.Store.DeleteMcpKeyAsync(created.Key.Id, admin.Id, token, Ct);
        var detail = (await db.Store.GetMcpCallAsync(id, Ct)).Data!;
        Assert.Equal(body, detail.RequestBody);
        Assert.Equal(response, detail.ResponseBody);
        Assert.Equal(["Bearer " + created.Secret], detail.RequestHeaders["Authorization"]);
        Assert.Equal(["one", "two"], detail.RequestHeaders["X-Synthetic"]);
        Assert.Equal(created.Key.Name, detail.Summary.KeyName);
        Assert.Equal(created.Key.Id, detail.Summary.KeyId);
        var page = (await db.Store.ListMcpCallsAsync(new(KeyId: created.Key.Id), Ct)).Data!;
        Assert.Single(page.Items);
        Assert.DoesNotContain("synthetic-value", JsonSerializer.Serialize(page), StringComparison.Ordinal);
        Assert.DoesNotContain(created.Secret, JsonSerializer.Serialize(page), StringComparison.Ordinal);
    }

    [Fact]
    public async Task McpAuditPaginationDoesNotLoseEqualTimestampRowsAndFiltersFailures()
    {
        await using var db = await TestDatabase.OpenAsync();
        for (var i = 0; i < 3; i++)
        {
            var id = (await db.Store.BeginMcpCallAsync(McpStart(null, "{}"), Ct)).Data!;
            await db.Store.CompleteMcpCallAsync(id, new(403, [], "{}", 2, "rejected", "mcp_permission_denied", 1), Ct);
        }
        var first = (await db.Store.ListMcpCallsAsync(new(Outcome: "rejected", PageSize: 2), Ct)).Data!;
        Assert.Equal(2, first.Items.Count);
        Assert.True(first.HasMore);
        var second = (await db.Store.ListMcpCallsAsync(new(Outcome: "rejected", Cursor: first.NextCursor, PageSize: 2), Ct)).Data!;
        Assert.Single(second.Items);
        Assert.False(second.HasMore);
        Assert.DoesNotContain(second.Items[0].Id, first.Items.Select(x => x.Id));
        Assert.Empty((await db.Store.ListMcpCallsAsync(new(Outcome: "success"), Ct)).Data!.Items);
        Assert.Equal("invalid_cursor", (await db.Store.ListMcpCallsAsync(new(Cursor: "invalid!"), Ct)).Code);
    }

    [Fact]
    public async Task McpRetentionOnlyRemovesCompletedCallsAndRecoveryMarksUnknownWithoutReplay()
    {
        await using var db = await TestDatabase.OpenAsync();
        await db.BootstrapAsync();
        var finished = (await db.Store.BeginMcpCallAsync(McpStart(null, "{}"), Ct)).Data!;
        await db.Store.CompleteMcpCallAsync(finished, new(200, [], "{}", 2, "success", "OK", 1), Ct);
        var unfinished = (await db.Store.BeginMcpCallAsync(McpStart(null, "{}"), Ct)).Data!;
        db.Clock.Advance(TimeSpan.FromDays(31));
        var recent = (await db.Store.BeginMcpCallAsync(McpStart(null, "{}"), Ct)).Data!;
        await db.Store.CompleteMcpCallAsync(recent, new(200, [], "{}", 2, "success", "OK", 1), Ct);
        Assert.Equal(1, (await db.Store.CleanupMcpCallsAsync(Ct))!.Deleted);
        Assert.Equal("not_found", (await db.Store.GetMcpCallAsync(finished, Ct)).Code);
        Assert.Equal("started", (await db.Store.GetMcpCallAsync(unfinished, Ct)).Data!.Summary.Outcome);
        Assert.Equal(1L, await db.ScalarAsync("SELECT count(*) FROM audit_entries WHERE action='admin.bootstrap';"));
        await db.Store.RecoverMcpCallsAsync(Ct);
        var recovered = (await db.Store.GetMcpCallAsync(unfinished, Ct)).Data!.Summary;
        Assert.Equal("unknown", recovered.Outcome);
        Assert.Equal("mcp_call_interrupted", recovered.Code);
        Assert.False(recovered.ResponseComplete);
        Assert.Equal(0, (await db.Store.CleanupMcpCallsAsync(Ct))!.Deleted);
    }

    [Fact]
    public async Task McpAuditLimitsRejectInsteadOfSilentlyTruncating()
    {
        await using var db = await TestDatabase.OpenAsync();
        var body = new string('x', 1024 * 1024 + 1);
        Assert.Equal("mcp_audit_payload_invalid", (await db.Store.BeginMcpCallAsync(McpStart(null, body), Ct)).Code);
        Assert.Equal(0L, await db.ScalarAsync("SELECT count(*) FROM mcp_call_audits;"));
        var id = (await db.Store.BeginMcpCallAsync(McpStart(null, "captured") with { RequestComplete = false, CaptureError = "request_too_large", RequestBytes = body.Length }, Ct)).Data!;
        await Assert.ThrowsAsync<ArgumentException>(() => db.Store.CompleteMcpCallAsync(id,
            new(200, [], new string('x', 4 * 1024 * 1024 + 1), 4 * 1024 * 1024 + 1, "success", "OK", 1), Ct));
        var detail = (await db.Store.GetMcpCallAsync(id, Ct)).Data!;
        Assert.False(detail.Summary.RequestComplete);
        Assert.Equal("request_too_large", detail.Summary.CaptureError);
        Assert.Equal("started", detail.Summary.Outcome);
    }

    [Fact]
    public async Task McpAuditRetainsFullInvalidUtf8EvidenceWithinRawByteBudget()
    {
        await using var db = await TestDatabase.OpenAsync();
        var raw = Enumerable.Repeat((byte)0xff, 1024 * 1024).ToArray();
        var encoded = Convert.ToBase64String(raw);
        var result = await db.Store.BeginMcpCallAsync(McpStart(null, encoded) with
        {
            RequestBytes = raw.Length,
            CaptureError = "invalid_utf8_body_saved_as_base64"
        }, Ct);
        Assert.True(result.Success);
        Assert.Equal(encoded, (await db.Store.GetMcpCallAsync(result.Data!, Ct)).Data!.RequestBody);
        Assert.Equal("mcp_audit_payload_invalid", (await db.Store.BeginMcpCallAsync(McpStart(null, encoded), Ct)).Code);
    }

    [Fact]
    public async Task McpRecoveryRetryDoesNotMarkCurrentProcessCallsAsInterrupted()
    {
        await using var db = await TestDatabase.OpenAsync();
        var prior = (await db.Store.BeginMcpCallAsync(McpStart(null, "{}"), Ct)).Data!;
        db.Clock.Advance(TimeSpan.FromSeconds(1));
        var startup = db.Clock.GetUtcNow();
        var current = (await db.Store.BeginMcpCallAsync(McpStart(null, "{}"), Ct)).Data!;
        await db.Store.RecoverMcpCallsAsync(Ct, startup);
        Assert.Equal("unknown", (await db.Store.GetMcpCallAsync(prior, Ct)).Data!.Summary.Outcome);
        Assert.Equal("started", (await db.Store.GetMcpCallAsync(current, Ct)).Data!.Summary.Outcome);
    }

    [Fact]
    public async Task McpRuntimeRecoveryFailureOnlyBlocksMcpAndCanBeRetriedImmediately()
    {
        await using var db = await TestDatabase.OpenAsync();
        var admin = await db.BootstrapAsync();
        var token = (await db.Store.LoginAsync("admin", Password, Ct)).Data!.Token;
        await db.Store.SetMcpEnabledAsync(new("1", true), admin.Id, token, Ct);
        var runtime = new McpRuntime(db.Store);
        runtime.ReportRecovery(false);
        var faulted = (await runtime.GetViewAsync(Ct)).Data!;
        Assert.Equal("faulted", faulted.State);
        Assert.Equal("mcp_audit_recovery_failed", faulted.RecoveryError);
        Assert.Equal("mcp_recovery_unavailable", (await runtime.EnterAsync(42, Ct)).Code);
        Assert.True((await db.Store.CreateSourceAsync(Source, admin.Id, Ct)).Success);
        var recovered = await runtime.SetEnabledAsync(new("2", true), admin.Id, token, Ct);
        Assert.True(recovered.Success);
        Assert.Equal("running", recovered.Data!.State);
        Assert.Null(recovered.Data.RecoveryError);
        using var admitted = (await runtime.EnterAsync(42, Ct)).Data!;
        Assert.NotNull(admitted);
    }

    [Fact]
    public async Task McpRuntimeDoesNotClaimRunningWhenRecoveryWriteFails()
    {
        await using var db = await TestDatabase.OpenAsync();
        var admin = await db.BootstrapAsync();
        var token = (await db.Store.LoginAsync("admin", Password, Ct)).Data!.Token;
        await db.Store.BeginMcpCallAsync(McpStart(null, "{}"), Ct);
        await db.ExecuteAsync("CREATE TRIGGER reject_mcp_recovery BEFORE UPDATE ON mcp_call_audits BEGIN SELECT RAISE(ABORT,'synthetic'); END;");
        var runtime = new McpRuntime(db.Store);
        runtime.ReportRecovery(false);
        var result = await runtime.SetEnabledAsync(new("1", true), admin.Id, token, Ct);
        Assert.Equal("mcp_recovery_unavailable", result.Code);
        Assert.Equal("faulted", (await runtime.GetViewAsync(Ct)).Data!.State);
        Assert.False((await db.Store.ReadMcpSettingsAsync(Ct)).Data!.Enabled);
    }

    private static McpCallStart McpStart(McpPrincipal? principal, string body) => new(principal,
        "synthetic-trace", "1", "tools/call", "syslog_logs_search", "192.0.2.20", "POST", "https://syslog.example.test/mcp", "HTTP/1.1",
        [], body, Encoding.UTF8.GetByteCount(body));
}
