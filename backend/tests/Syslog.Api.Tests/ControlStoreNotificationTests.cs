using Microsoft.Data.Sqlite;
using Syslog.Api.Service;
using Syslog.Api.Service.impl;
using Xunit;

namespace Syslog.Api.Tests;

public sealed partial class ControlStoreTests
{
    private static StoredSmtpSettings SyntheticSmtp => new(0, "smtp.example.test", 465, "ssl", "syslog@example.test",
        "syslog@example.test", "通研 Syslog", "[通研 Syslog]", "synthetic-ciphertext");

    [Fact]
    public async Task NotificationsRequireExplicitMigrationAndPreserveVersionThreeSources()
    {
        await using var database = await TestDatabase.OpenAsync(initialize: false);
        await using var resource = typeof(ControlStore).Assembly.GetManifestResourceStream("Syslog.Api.Data.001_control.sql")!;
        using var reader = new StreamReader(resource);
        await database.ExecuteAsync(await reader.ReadToEndAsync(Ct));
        Assert.Equal("notification_migration_required", (await database.Store.GetSmtpSettingsAsync(Ct)).Code);
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Store.MigrateNotificationsAsync(Ct));
        await database.Store.MigrateListenersAsync(Ct);
        await database.Store.MigrateSourceFiltersAsync(Ct);
        var admin = await database.BootstrapAsync();
        var created = (await database.Store.CreateSourceAsync(Source with { IgnoredSeverities = [3, 6] }, admin.Id, Ct)).Data!;
        Assert.Equal("notification_migration_required", (await database.Store.GetSmtpSettingsAsync(Ct)).Code);
        Assert.Empty(await database.Store.ListDueReportSubscriptionsAsync(database.Clock.GetUtcNow(), 10, Ct));
        Assert.Null(await database.Store.ClaimMailDeliveryAsync(database.Clock.GetUtcNow(), Ct));
        await database.Store.RecoverInterruptedMailDeliveriesAsync(Ct);
        await database.Store.InitializeAsync(Ct);
        Assert.Equal(3L, await database.ScalarAsync("SELECT max(version) FROM schema_versions;"));
        Assert.Equal(0L, await database.ScalarAsync("SELECT count(*) FROM sqlite_master WHERE name='mail_deliveries';"));

        await database.Store.MigrateNotificationsAsync(Ct);
        await database.Store.MigrateNotificationsAsync(Ct);
        Assert.Equal(4L, await database.ScalarAsync("SELECT max(version) FROM schema_versions;"));
        Assert.Equal(4L, await database.ScalarAsync("SELECT count(*) FROM schema_versions;"));
        var settings = (await database.Store.GetSmtpSettingsAsync(Ct)).Data!;
        Assert.Equal((0L, "ssl", 465), (settings.Version, settings.Security, settings.Port));
        var migrated = Assert.Single(await database.Store.ListAsync(Ct));
        Assert.Equal(created.Id, migrated.Id);
        Assert.Equal(new[] { 3, 6 }, migrated.IgnoredSeverities);
        Assert.False(await database.Store.IsSetupRequiredAsync(Ct));
    }

    [Fact]
    public async Task NotificationSettingsProtectCredentialsAndRollbackWhenAuditFails()
    {
        await using var database = await TestDatabase.OpenAsync();
        var admin = await database.BootstrapAsync();
        var token = (await database.Store.LoginAsync("admin", Password, Ct)).Data!.Token;
        var store = database.Store;
        Assert.Equal("authentication_required", (await store.SaveSmtpSettingsAsync(SyntheticSmtp, admin.Id, "invalid", Ct)).Code);
        Assert.Equal("smtp_not_configured", (await store.EnqueueTestEmailAsync("receiver@example.test", admin.Id, token, Ct)).Code);
        var saved = (await store.SaveSmtpSettingsAsync(SyntheticSmtp, admin.Id, token, Ct)).Data!;
        Assert.Equal(1L, saved.Version);
        Assert.Equal("version_conflict", (await store.SaveSmtpSettingsAsync(SyntheticSmtp, admin.Id, token, Ct)).Code);
        Assert.Equal("smtp_password_required", (await store.SaveSmtpSettingsAsync(saved with
            { Host = "another.example.test", ProtectedPassword = null }, admin.Id, token, Ct)).Code);
        var unchangedPassword = (await store.SaveSmtpSettingsAsync(saved with
            { SenderName = "新名称", ProtectedPassword = null }, admin.Id, token, Ct)).Data!;
        Assert.Equal(saved.ProtectedPassword, unchangedPassword.ProtectedPassword);
        var cleared = (await store.SaveSmtpSettingsAsync(unchangedPassword with
            { ProtectedPassword = "" }, admin.Id, token, Ct)).Data!;
        Assert.Null(cleared.ProtectedPassword);
        var audit = Assert.IsType<string>(await database.ScalarAsync("SELECT group_concat(details_json) FROM audit_entries WHERE action='notification.smtp.configure';"));
        Assert.DoesNotContain("synthetic-ciphertext", audit, StringComparison.Ordinal);

        await database.RejectAuditAsync();
        await Assert.ThrowsAsync<SqliteException>(() => store.SaveSmtpSettingsAsync(cleared with { SenderName = "未保存" }, admin.Id, token, Ct));
        await Assert.ThrowsAsync<SqliteException>(() => store.EnqueueTestEmailAsync("receiver@example.test", admin.Id, token, Ct));
        Assert.Equal(cleared, (await store.GetSmtpSettingsAsync(Ct)).Data);
        Assert.Equal(0L, await database.ScalarAsync("SELECT count(*) FROM mail_deliveries;"));
    }

    [Fact]
    public async Task ReportSchedulingClaimsOnceAndPreservesDeliveryContentAndRecoveryState()
    {
        await using var database = await TestDatabase.OpenAsync();
        var admin = await database.BootstrapAsync();
        var token = (await database.Store.LoginAsync("admin", Password, Ct)).Data!.Token;
        var store = database.Store;
        Assert.True((await store.SaveSmtpSettingsAsync(SyntheticSmtp, admin.Id, token, Ct)).Success);
        var now = database.Clock.GetUtcNow();
        var input = new StoredReportSubscription(0, 0, "设备日报", true, "daily", "08:00", "Asia/Shanghai",
            [], ["first@example.test", "second@example.test"], now);
        var subscription = (await store.SaveReportSubscriptionAsync(input, admin.Id, token, Ct)).Data!;
        Assert.Equal(subscription.Id, Assert.Single(await store.ListDueReportSubscriptionsAsync(now, 10, Ct)).Id);
        var scheduling = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            store.ScheduleReportAsync(subscription, now.AddDays(-1), now, now.AddDays(1), Ct)));
        Assert.Single(scheduling, result => result);
        Assert.Empty(await store.ListDueReportSubscriptionsAsync(now, 10, Ct));

        var first = Assert.IsType<StoredMailDelivery>(await store.ClaimMailDeliveryAsync(now, Ct));
        Assert.Equal(("first@example.test", "sending", 1), (first.Recipient, first.Status, first.Attempts));
        Assert.True(await store.StoreMailContentAsync(first.Id, "测试日报", "<p>测试日报</p>", "测试日报", Ct));
        Assert.False(await store.StoreMailContentAsync(first.Id, "不能覆盖", "<p>不能覆盖</p>", "不能覆盖", Ct));
        var second = Assert.IsType<StoredMailDelivery>(await store.ClaimMailDeliveryAsync(now, Ct));
        Assert.Equal("<p>测试日报</p>", second.HtmlBody);
        Assert.Null(await store.ClaimMailDeliveryAsync(now, Ct));
        await store.FinishMailDeliveryAsync(first.Id, "accepted", null, null, Ct);
        await store.RecoverInterruptedMailDeliveriesAsync(Ct);
        Assert.Equal("unknown", (await store.GetMailDeliveryAsync(second.Id, Ct)).Data!.Status);
        Assert.Equal("<p>测试日报</p>", (await store.GetMailDeliveryAsync(first.Id, Ct)).Data!.HtmlBody);

        var listed = (await store.ListMailDeliveriesAsync("daily", "accepted", "FIRST@EXAMPLE.TEST", now, now.AddDays(1), null, 2, Ct)).Data!;
        var summary = Assert.Single(listed);
        Assert.Equal(first.Id, summary.Id);
        Assert.Null(summary.HtmlBody);
        Assert.Null(summary.TextBody);
        var older = (await store.ListMailDeliveriesAsync(null, null, null, null, null, second.Id, 2, Ct)).Data!;
        Assert.Equal(first.Id, Assert.Single(older).Id);

        database.Clock.Advance(TimeSpan.FromDays(1));
        var due = Assert.Single(await store.ListDueReportSubscriptionsAsync(now.AddDays(1), 10, Ct));
        Assert.True(await store.ScheduleReportAsync(due, now, now.AddDays(1), now.AddDays(2), Ct));
        Assert.True((await store.SaveReportSubscriptionAsync(due with { Enabled = false, NextSendAt = now.AddDays(2) }, admin.Id,
            (await store.LoginAsync("admin", Password, Ct)).Data!.Token, Ct)).Success);
        Assert.Equal(2L, await database.ScalarAsync("SELECT count(*) FROM mail_deliveries WHERE status='failed' AND failure_code='report_subscription_changed';"));
        Assert.Null(await store.ClaimMailDeliveryAsync(now.AddDays(1), Ct));
    }
}
