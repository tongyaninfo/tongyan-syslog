using System.Globalization;
using System.Text.Json;
using Syslog.Api.Common;
using Syslog.Api.Models;
using Syslog.Api.Service;
using Xunit;

namespace Syslog.Api.Tests;

public sealed partial class ControlStoreTests
{
    [Fact]
    public void McpArgumentsRejectUnknownWriteFieldsDuplicateNamesAndUnzonedDates()
    {
        Assert.True(McpToolCatalog.TryGet("syslog_sources_update", out var update));
        Assert.True(McpArgumentValidation.IsValid(JsonSerializer.SerializeToElement(new
            { sourceId = "1", name = "switch", peerAddress = "192.0.2.10", timeZoneId = "UTC" }), update.InputSchema));
        Assert.False(McpArgumentValidation.IsValid(JsonSerializer.SerializeToElement(new
            { sourceId = "1", name = "switch", peerAddress = "192.0.2.10", timeZoneId = "UTC", enabled = true }), update.InputSchema));
        using var duplicate = JsonDocument.Parse("{\"sourceId\":\"1\",\"name\":\"one\",\"name\":\"two\",\"peerAddress\":\"192.0.2.10\",\"timeZoneId\":\"UTC\"}");
        Assert.False(McpArgumentValidation.IsValid(duplicate.RootElement, update.InputSchema));
        Assert.True(McpToolCatalog.TryGet("syslog_logs_search", out var search));
        Assert.False(McpArgumentValidation.IsValid(JsonSerializer.SerializeToElement(new
            { from = "2026-11-11T00:00:00", to = "2026-11-12T00:00:00Z" }), search.InputSchema));
        Assert.True(McpToolCatalog.TryGet("syslog_sources_set_ignored_severities", out var ignore));
        Assert.False(McpArgumentValidation.IsValid(JsonSerializer.SerializeToElement(new { sourceId = "1", ignoredSeverities = new[] { 3, 3 } }), ignore.InputSchema));
        Assert.False(McpArgumentValidation.IsValid(JsonSerializer.SerializeToElement(new { sourceId = "1", ignoredSeverities = new[] { 8 } }), ignore.InputSchema));
    }

    [Fact]
    public async Task McpSourcePatchPreservesUnrelatedFieldsAndRevocationIsCheckedInTransaction()
    {
        await using var database = await TestDatabase.OpenAsync();
        var admin = await database.BootstrapAsync();
        var source = (await database.Store.CreateSourceAsync(new("original", "192.0.2.10", "UTC", 365, false, [4]), admin.Id, Ct)).Data!;
        var (actor, key, token) = await NewMcpOperationAsync(database, "syslog_source_retention_set");
        var result = await database.Store.PatchSourceAsync(source.Id, new(RetentionDays: 7), actor, Ct);
        Assert.True(result.Success);
        Assert.Equal((source.Id, source.Name, source.PeerAddress, source.TimeZoneId, source.Enabled, 7),
            (result.Data!.Id, result.Data.Name, result.Data.PeerAddress, result.Data.TimeZoneId, result.Data.Enabled, result.Data.RetentionDays));
        Assert.Equal(source.IgnoredSeverities, result.Data.IgnoredSeverities);
        Assert.False((await database.Store.PatchSourceAsync(source.Id, new(RetentionDays: 9, Enabled: true), actor, Ct)).Success);
        var forgedAction = OperationActor.Mcp(actor.UserId, actor.McpKeyId!.Value, "syslog_sources_set_enabled");
        Assert.False((await database.Store.PatchSourceAsync(source.Id, new(Enabled: true), forgedAction, Ct)).Success);
        Assert.True((await database.Store.RevokeMcpKeyAsync(key.Key.Id, actor.UserId, token, Ct)).Success);
        Assert.False((await database.Store.PatchSourceAsync(source.Id, new(RetentionDays: 12), actor, Ct)).Success);
        Assert.Equal(7, Assert.Single(await database.Store.ListAsync(Ct)).RetentionDays);
        Assert.False(Assert.Single(await database.Store.ListAsync(Ct)).Enabled);
    }

    [Fact]
    public async Task McpUserNameAndEnabledOperationsCannotChangeEachOthersFields()
    {
        await using var database = await TestDatabase.OpenAsync();
        var admin = await database.BootstrapAsync();
        var (stateActor, _, token) = await NewMcpOperationAsync(database, "syslog_users_set_enabled");
        var target = (await database.Store.CreateUserAsync(new("target", Password), admin.Id, token, Ct)).Data!;
        var disabled = await database.Store.UpdateUserAsync(target.Id.ToString(CultureInfo.InvariantCulture),
            new("attempted_rename", false, target.Version.ToString(CultureInfo.InvariantCulture)), stateActor, Ct);
        Assert.True(disabled.Success);
        Assert.Equal("target", disabled.Data!.Username);
        Assert.False(disabled.Data.Enabled);
        var (nameActor, _, _) = await NewMcpOperationAsync(database, "syslog_users_update");
        var renamed = await database.Store.UpdateUserAsync(target.Id.ToString(CultureInfo.InvariantCulture),
            new("renamed", true, disabled.Data.Version.ToString(CultureInfo.InvariantCulture)), nameActor, Ct);
        Assert.True(renamed.Success);
        Assert.Equal("renamed", renamed.Data!.Username);
        Assert.False(renamed.Data.Enabled);
        Assert.False((await database.Store.UpdateUserAsync(admin.Id.ToString(CultureInfo.InvariantCulture),
            new("ignored", false, "1"), stateActor, Ct)).Success);
    }

    [Fact]
    public async Task McpListenerConfigurationCannotChangeMasterOrChildSwitches()
    {
        await using var database = await TestDatabase.OpenAsync();
        await database.BootstrapAsync();
        var (actor, _, _) = await NewMcpOperationAsync(database, "syslog_listeners_configure");
        var initial = (await database.Store.ReadListenerSettingsAsync(Ct)).Data!;
        var enabled = initial.Configuration with { Enabled = true };
        Assert.Equal("mcp_permission_denied", (await database.Store.SaveListenerSettingsAsync(new(initial.Version, enabled), actor, Ct)).Code);
        var child = initial.Configuration with { Listeners = initial.Configuration.Listeners.Select(item => item.Protocol == "tls" ? item with { Enabled = true } : item).ToArray() };
        Assert.Equal("mcp_permission_denied", (await database.Store.SaveListenerSettingsAsync(new(initial.Version, child), actor, Ct)).Code);
        var changed = initial.Configuration with { Listeners = initial.Configuration.Listeners.Select(item => item.Protocol == "tcp" ? item with { Port = 10514 } : item).ToArray() };
        Assert.True((await database.Store.SaveListenerSettingsAsync(new(initial.Version, changed), actor, Ct)).Success);
        var saved = (await database.Store.ReadListenerSettingsAsync(Ct)).Data!;
        Assert.False(saved.Configuration.Enabled);
        Assert.All(saved.Configuration.Listeners, item => Assert.False(item.Enabled));
        Assert.Equal(10514, saved.Configuration.Listeners.Single(item => item.Protocol == "tcp").Port);
    }

    [Fact]
    public async Task McpSubscriptionSwitchRetainsRecipientsAndContentInsideTransaction()
    {
        await using var database = await TestDatabase.OpenAsync();
        var admin = await database.BootstrapAsync();
        var (actor, _, token) = await NewMcpOperationAsync(database, "syslog_report_subscription_set_enabled");
        var now = database.Clock.GetUtcNow();
        var original = (await database.Store.SaveReportSubscriptionAsync(new(0, 0, "original", true, "daily", "08:00", "Asia/Shanghai", [],
            ["first@example.test"], now), admin.Id, token, Ct)).Data!;
        var result = await database.Store.SaveReportSubscriptionAsync(original with
            { Enabled = false, Name = "attempted", Frequency = "monthly", SendTime = "18:30", Recipients = ["other@example.test"] }, actor, Ct);
        Assert.True(result.Success);
        Assert.False(result.Data!.Enabled);
        Assert.Equal(original.Name, result.Data.Name);
        Assert.Equal(original.Frequency, result.Data.Frequency);
        Assert.Equal(original.SendTime, result.Data.SendTime);
        Assert.Equal(original.Recipients, result.Data.Recipients);
    }

    [Fact]
    public async Task McpValidKeyCannotBeUsedForAnUnrelatedBusinessEntryPoint()
    {
        await using var database = await TestDatabase.OpenAsync();
        await database.BootstrapAsync();
        var (actor, _, _) = await NewMcpOperationAsync(database, "syslog_sources_set_enabled");
        Assert.False((await database.Store.CreateUserAsync(new("unexpected", Password), actor, Ct)).Success);
        Assert.False((await database.Store.BeginLogClearAsync(actor, "synthetic", Ct)).Success);
        Assert.False((await database.Store.SaveSmtpSettingsAsync(SyntheticSmtp, actor, Ct)).Success);
        Assert.Equal(1L, await database.ScalarAsync("SELECT count(*) FROM users;"));
        Assert.Equal(0L, await database.ScalarAsync("SELECT count(*) FROM smtp_settings;"));
    }

    private static async Task<(OperationActor Actor, CreatedMcpKey Key, string Session)> NewMcpOperationAsync(TestDatabase database, string permission)
    {
        var session = (await database.Store.LoginAsync("admin", Password, Ct)).Data!;
        var settings = (await database.Store.ReadMcpSettingsAsync(Ct)).Data!;
        if (!settings.Enabled) Assert.True((await database.Store.SetMcpEnabledAsync(new(settings.Version, true), session.User.Id, session.Token, Ct)).Success);
        var created = (await database.Store.CreateMcpKeyAsync(new("Synthetic operation", "read_write", [permission], null), session.User.Id, session.Token, Ct)).Data!;
        return (OperationActor.Mcp(session.User.Id, long.Parse(created.Key.Id, CultureInfo.InvariantCulture), permission), created, session.Token);
    }
}
