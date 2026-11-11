using System.Globalization;
using Microsoft.Data.Sqlite;
using Syslog.Api.Models;
using Syslog.Api.Options;
using Syslog.Api.Service.impl;
using Xunit;

namespace Syslog.Api.Tests;

public sealed partial class ControlStoreTests
{
    [Fact]
    public async Task SetupStatusIsReadOnlyAndMissingFileIsCreatedOnlyAfterExplicitSubmission()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "TestResults");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "setup-" + Guid.NewGuid().ToString("N") + ".sqlite");
        var store = new ControlStore(new ControlStoreOptions { ConnectionString = $"Data Source={path};Pooling=False" });
        Assert.True(await store.IsSetupRequiredAsync(Ct));
        Assert.False(File.Exists(path));
        Assert.Equal("password_policy", (await store.SetupAdminAsync("admin", "            ", Ct)).Code);
        Assert.False(File.Exists(path));
        Assert.True((await store.SetupAdminAsync("admin", Password, Ct)).Success);
        Assert.True(File.Exists(path));
        Assert.False(await store.IsSetupRequiredAsync(Ct));
        var reopened = new ControlStore(new ControlStoreOptions { ConnectionString = $"Data Source={path};Pooling=False" });
        Assert.False(await reopened.IsSetupRequiredAsync(Ct));
        Assert.True((await reopened.LoginAsync("admin", Password, Ct)).Success);
        Assert.Equal("setup_already_completed", (await reopened.SetupAdminAsync("other", NewPassword, Ct)).Code);
    }

    [Fact]
    public async Task SetupNeverRepairsIncompleteSchemaOrUnsupportedVersion()
    {
        await using var incomplete = await TestDatabase.OpenAsync(initialize: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => incomplete.Store.IsSetupRequiredAsync(Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => incomplete.Store.SetupAdminAsync("admin", Password, Ct));
        Assert.Equal(0L, await incomplete.ScalarAsync("SELECT count(*) FROM sqlite_master WHERE type='table';"));

        await using var unknown = await TestDatabase.OpenAsync();
        await unknown.ExecuteAsync("UPDATE schema_versions SET version=99 WHERE version=(SELECT max(version) FROM schema_versions);");
        await Assert.ThrowsAsync<InvalidOperationException>(() => unknown.Store.IsSetupRequiredAsync(Ct));
        await Assert.ThrowsAsync<InvalidOperationException>(() => unknown.Store.SetupAdminAsync("admin", Password, Ct));
        Assert.Equal(99L, await unknown.ScalarAsync("SELECT max(version) FROM schema_versions;"));
        Assert.Equal(0L, await unknown.ScalarAsync("SELECT count(*) FROM users;"));
    }

    [Fact]
    public async Task SetupNeverOverwritesUnrelatedOrCorruptFiles()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "TestResults");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "corrupt-" + Guid.NewGuid().ToString("N") + ".sqlite");
        const string content = "synthetic invalid database";
        await File.WriteAllTextAsync(path, content);
        var store = new ControlStore(new ControlStoreOptions { ConnectionString = $"Data Source={path};Pooling=False" });
        await Assert.ThrowsAsync<SqliteException>(() => store.IsSetupRequiredAsync(Ct));
        await Assert.ThrowsAsync<SqliteException>(() => store.SetupAdminAsync("admin", Password, Ct));
        Assert.Equal(content, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task SetupIsOneTimeAcrossConcurrentStoresAndDoesNotReopenForDisabledAccounts()
    {
        await using var database = await TestDatabase.OpenAsync();
        Assert.True(await database.Store.IsSetupRequiredAsync(Ct));
        var other = new ControlStore(database.Options);
        var results = await Task.WhenAll(Task.Run(() => database.Store.SetupAdminAsync("first", Password, Ct)),
            Task.Run(() => other.SetupAdminAsync("second", Password, Ct)));
        Assert.Single(results, x => x.Success);
        Assert.Single(results, x => x.Code == "setup_already_completed");
        Assert.Equal(1L, await database.ScalarAsync("SELECT count(*) FROM users;"));
        Assert.Equal(1L, await database.ScalarAsync("SELECT count(*) FROM audit_entries WHERE action='admin.bootstrap';"));
        await database.ExecuteAsync("UPDATE users SET enabled=0;");
        Assert.False(await database.Store.IsSetupRequiredAsync(Ct));
        Assert.Equal("setup_already_completed", (await database.Store.SetupAdminAsync("third", Password, Ct)).Code);
    }

    [Fact]
    public async Task SetupRollsBackUserAndIdAllocationWhenAuditFails()
    {
        await using var database = await TestDatabase.OpenAsync();
        await database.RejectAuditAsync();
        await Assert.ThrowsAsync<SqliteException>(() => database.Store.SetupAdminAsync("admin", Password, Ct));
        Assert.Equal(0L, await database.ScalarAsync("SELECT count(*) FROM users;"));
        Assert.Equal(1L, await database.ScalarAsync("SELECT next_id FROM id_allocations;"));
        Assert.True(await database.Store.IsSetupRequiredAsync(Ct));
    }

    [Fact]
    public async Task UsersCanBeCreatedFilteredAndPagedWithoutExposingCredentials()
    {
        await using var database = await TestDatabase.OpenAsync();
        var admin = await database.BootstrapAsync();
        var session = (await database.Store.LoginAsync(admin.Username, Password, Ct)).Data!;
        var created = await database.Store.CreateUserAsync(new("operator", NewPassword), admin.Id, session.Token, Ct);
        Assert.True(created.Success);
        Assert.Equal("username_conflict", (await database.Store.CreateUserAsync(new("OPERATOR", Password), admin.Id, session.Token, Ct)).Code);
        Assert.Equal("username_invalid", (await database.Store.CreateUserAsync(new("bad user", Password), admin.Id, session.Token, Ct)).Code);
        Assert.Equal("password_policy", (await database.Store.CreateUserAsync(new("another", "             "), admin.Id, session.Token, Ct)).Code);
        var page = (await database.Store.ListUsersAsync(new(Page: 2, PageSize: 1), admin.Id, session.Token, Ct)).Data!;
        Assert.Equal(2, page.Total);
        Assert.Equal(created.Data, Assert.Single(page.Items));
        var filtered = (await database.Store.ListUsersAsync(new(Search: "OPERA", Enabled: true), admin.Id, session.Token, Ct)).Data!;
        Assert.Equal(1, filtered.Total);
        Assert.Equal(created.Data, Assert.Single(filtered.Items));
        Assert.Equal("invalid_request", (await database.Store.ListUsersAsync(new(Page: 0), admin.Id, session.Token, Ct)).Code);
        Assert.Equal("invalid_request", (await database.Store.ListUsersAsync(new(PageSize: 101), admin.Id, session.Token, Ct)).Code);
        Assert.Equal("invalid_request", (await database.Store.ListUsersAsync(new(Search: new string('a', 65)), admin.Id, session.Token, Ct)).Code);
        Assert.Equal(0, (await database.Store.ListUsersAsync(new(Search: "%"), admin.Id, session.Token, Ct)).Data!.Total);
        Assert.Empty((await database.Store.ListUsersAsync(new(Page: int.MaxValue), admin.Id, session.Token, Ct)).Data!.Items);
        var audit = Assert.IsType<string>(await database.ScalarAsync("SELECT details_json FROM audit_entries WHERE action='user.create';"));
        Assert.DoesNotContain(NewPassword, audit);
        Assert.DoesNotContain("password", audit, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisableRenameAndResetRevokeSessionsAndCheckVersions()
    {
        await using var database = await TestDatabase.OpenAsync();
        var admin = await database.BootstrapAsync();
        var adminSession = (await database.Store.LoginAsync(admin.Username, Password, Ct)).Data!;
        var user = (await database.Store.CreateUserAsync(new("operator", Password), admin.Id, adminSession.Token, Ct)).Data!;
        var login = (await database.Store.LoginAsync(user.Username, Password, Ct)).Data!;
        var renamed = await database.Store.UpdateUserAsync(IdText(user.Id), new("renamed", true, "1"), admin.Id, adminSession.Token, Ct);
        Assert.True(renamed.Success);
        Assert.Equal(2, renamed.Data!.Version);
        Assert.Null(await database.Store.ValidateSessionAsync(login.Token, Ct));
        Assert.Equal("version_conflict", (await database.Store.UpdateUserAsync(IdText(user.Id), new("old", true, "1"), admin.Id, adminSession.Token, Ct)).Code);
        var beforeReset = (await database.Store.LoginAsync("renamed", Password, Ct)).Data!;
        Assert.True((await database.Store.ResetUserPasswordAsync(IdText(user.Id), new(NewPassword, "2"), admin.Id, adminSession.Token, Ct)).Success);
        Assert.Null(await database.Store.ValidateSessionAsync(beforeReset.Token, Ct));
        Assert.Equal("AUTH_FAILED", (await database.Store.LoginAsync("renamed", Password, Ct)).Code);
        var beforeDisable = (await database.Store.LoginAsync("renamed", NewPassword, Ct)).Data!;
        Assert.True((await database.Store.UpdateUserAsync(IdText(user.Id), new("renamed", false, "3"), admin.Id, adminSession.Token, Ct)).Success);
        Assert.Null(await database.Store.ValidateSessionAsync(beforeDisable.Token, Ct));
        Assert.Equal("AUTH_FAILED", (await database.Store.LoginAsync("renamed", NewPassword, Ct)).Code);
        Assert.Single((await database.Store.ListUsersAsync(new(Enabled: false), admin.Id, adminSession.Token, Ct)).Data!.Items);
        Assert.True((await database.Store.UpdateUserAsync(IdText(user.Id), new("renamed", true, "4"), admin.Id, adminSession.Token, Ct)).Success);
        Assert.True((await database.Store.LoginAsync("renamed", NewPassword, Ct)).Success);
        Assert.Equal("{}", await database.ScalarAsync("SELECT details_json FROM audit_entries WHERE action='user.password_reset';"));
    }

    [Fact]
    public async Task LastAdminAndCurrentAccountAreProtected()
    {
        await using var database = await TestDatabase.OpenAsync();
        var admin = await database.BootstrapAsync();
        var session = (await database.Store.LoginAsync(admin.Username, Password, Ct)).Data!;
        Assert.Equal("last_admin_required", (await database.Store.UpdateUserAsync(IdText(admin.Id), new(admin.Username, false, "1"), admin.Id, session.Token, Ct)).Code);
        await database.Store.CreateUserAsync(new("operator", Password), admin.Id, session.Token, Ct);
        Assert.Equal("self_disable_forbidden", (await database.Store.UpdateUserAsync(IdText(admin.Id), new(admin.Username, false, "1"), admin.Id, session.Token, Ct)).Code);
        Assert.Equal("self_reset_forbidden", (await database.Store.ResetUserPasswordAsync(IdText(admin.Id), new(NewPassword, "1"), admin.Id, session.Token, Ct)).Code);
        Assert.Equal(admin, await database.Store.ValidateSessionAsync(session.Token, Ct));
    }

    [Fact]
    public async Task EveryUserOperationRequiresTheActorsCurrentSession()
    {
        await using var database = await TestDatabase.OpenAsync();
        var admin = await database.BootstrapAsync();
        var session = (await database.Store.LoginAsync(admin.Username, Password, Ct)).Data!;
        await database.Store.LogoutAsync(session.Token, Ct);
        Assert.Equal("authentication_required", (await database.Store.ListUsersAsync(new(), admin.Id, session.Token, Ct)).Code);
        Assert.Equal("authentication_required", (await database.Store.CreateUserAsync(new("operator", Password), admin.Id, session.Token, Ct)).Code);
        Assert.Equal("authentication_required", (await database.Store.UpdateUserAsync(IdText(admin.Id), new("changed", true, "1"), admin.Id, session.Token, Ct)).Code);
        Assert.Equal("authentication_required", (await database.Store.ResetUserPasswordAsync(IdText(admin.Id), new(NewPassword, "1"), admin.Id, session.Token, Ct)).Code);
        var fresh = (await database.Store.LoginAsync(admin.Username, Password, Ct)).Data!;
        Assert.Equal("authentication_required", (await database.Store.CreateUserAsync(new("operator", Password), admin.Id + 99, fresh.Token, Ct)).Code);
    }

    [Fact]
    public async Task UserChangesAndSessionRevocationsRollbackTogetherWhenAuditFails()
    {
        await using var database = await TestDatabase.OpenAsync();
        var admin = await database.BootstrapAsync();
        var session = (await database.Store.LoginAsync(admin.Username, Password, Ct)).Data!;
        var user = (await database.Store.CreateUserAsync(new("operator", Password), admin.Id, session.Token, Ct)).Data!;
        var userSession = (await database.Store.LoginAsync(user.Username, Password, Ct)).Data!;
        var hash = await database.ScalarAsync($"SELECT password_hash FROM users WHERE id={user.Id};");
        await database.RejectAuditAsync();
        await Assert.ThrowsAsync<SqliteException>(() => database.Store.CreateUserAsync(new("another", Password), admin.Id, session.Token, Ct));
        await Assert.ThrowsAsync<SqliteException>(() => database.Store.UpdateUserAsync(IdText(user.Id), new("changed", false, "1"), admin.Id, session.Token, Ct));
        await Assert.ThrowsAsync<SqliteException>(() => database.Store.ResetUserPasswordAsync(IdText(user.Id), new(NewPassword, "1"), admin.Id, session.Token, Ct));
        Assert.Equal(2L, await database.ScalarAsync("SELECT count(*) FROM users;"));
        Assert.Equal(hash, await database.ScalarAsync($"SELECT password_hash FROM users WHERE id={user.Id};"));
        Assert.NotNull(await database.Store.ValidateSessionAsync(userSession.Token, Ct));
        Assert.Equal(user, (await database.Store.ListUsersAsync(new(Search: user.Username), admin.Id, session.Token, Ct)).Data!.Items.Single());
    }

    [Fact]
    public async Task ConcurrentUpdatesCannotSilentlyOverwriteOrLeaveStaleSessions()
    {
        await using var database = await TestDatabase.OpenAsync();
        var admin = await database.BootstrapAsync();
        var session = (await database.Store.LoginAsync(admin.Username, Password, Ct)).Data!;
        var user = (await database.Store.CreateUserAsync(new("operator", Password), admin.Id, session.Token, Ct)).Data!;
        var other = new ControlStore(database.Options, database.Clock);
        var results = await Task.WhenAll(Task.Run(() => database.Store.UpdateUserAsync(IdText(user.Id), new("changed-one", true, "1"), admin.Id, session.Token, Ct)),
            Task.Run(() => other.UpdateUserAsync(IdText(user.Id), new("changed-two", true, "1"), admin.Id, session.Token, Ct)));
        Assert.Single(results, x => x.Success);
        Assert.Single(results, x => x.Code == "version_conflict");
    }

    private static string IdText(long value) => value.ToString(CultureInfo.InvariantCulture);

    [Fact]
    public async Task LegacyUnicodeNamesRemainUsableButNewNamesUseAsciiContract()
    {
        await using var database = await TestDatabase.OpenAsync();
        var admin = await database.BootstrapAsync();
        var session = (await database.Store.LoginAsync(admin.Username, Password, Ct)).Data!;
        var other = (await database.Store.CreateUserAsync(new("operator", Password), admin.Id, session.Token, Ct)).Data!;
        // 仅模拟历史账号数据，不改表或重写真实数据。
        await database.ExecuteAsync($"UPDATE users SET username='旧管理员',normalized_username='旧管理员' WHERE id={other.Id};");
        Assert.True((await database.Store.LoginAsync("旧管理员", Password, Ct)).Success);
        Assert.True((await database.Store.UpdateUserAsync(IdText(other.Id), new("旧管理员", false, "1"), admin.Id, session.Token, Ct)).Success);
        Assert.True((await database.Store.UpdateUserAsync(IdText(other.Id), new("旧管理员", true, "2"), admin.Id, session.Token, Ct)).Success);
        Assert.Equal("username_invalid", (await database.Store.CreateUserAsync(new("新管理员", Password), admin.Id, session.Token, Ct)).Code);
        Assert.Equal("username_invalid", (await database.Store.UpdateUserAsync(IdText(other.Id), new("新管理员", true, "3"), admin.Id, session.Token, Ct)).Code);
        Assert.True((await database.Store.UpdateUserAsync(IdText(other.Id), new("new-operator", true, "3"), admin.Id, session.Token, Ct)).Success);
    }
}
