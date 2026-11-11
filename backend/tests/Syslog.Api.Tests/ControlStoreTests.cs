using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Syslog.Api.Models;
using Syslog.Api.Options;
using Syslog.Api.Service.impl;
using Xunit;
using Xunit.Abstractions;

namespace Syslog.Api.Tests;

public sealed partial class ControlStoreTests(ITestOutputHelper output)
{
    // 测试数据均为合成信息；每个测试使用独立内存库，不创建或删除文件。
    private const string Password = "Synthetic-Password-2026";
    private const string NewPassword = "Synthetic-New-Password-2026";
    private static readonly CancellationToken Ct = CancellationToken.None;
    private static readonly SourceInput Source = new("synthetic-switch", "192.0.2.10", "Asia/Shanghai", 365);

    [Fact]
    public async Task ReadingDoesNotInitializeSchema()
    {
        await using var database = await TestDatabase.OpenAsync(initialize: false);
        await Assert.ThrowsAsync<SqliteException>(() => database.Store.ListAsync(Ct));
        Assert.Equal(0L, await database.ScalarAsync("SELECT count(*) FROM sqlite_master WHERE type='table';"));
        var nonexistent = Path.Combine(Path.GetTempPath(), $"syslog-uninitialized-{Guid.NewGuid():N}.sqlite");
        var unopened = new ControlStore(new ControlStoreOptions { ConnectionString = $"Data Source={nonexistent};Mode=ReadWriteCreate" });
        await Assert.ThrowsAsync<SqliteException>(() => unopened.ListAsync(Ct));
        Assert.False(File.Exists(nonexistent));
    }

    [Fact]
    public async Task ExplicitInitializationIsIdempotentAndBootstrapIsOneTime()
    {
        await using var database = await TestDatabase.OpenAsync();
        await database.Store.InitializeAsync(Ct);
        var nativeVersion = Assert.IsType<string>(await database.ScalarAsync("SELECT sqlite_version();"));
        output.WriteLine($"Native SQLite version: {nativeVersion}");
        Assert.True(Version.Parse(nativeVersion) >= new Version(3, 50, 2), "Native SQLite must contain the CVE-2025-6965 fix.");
        var first = await database.Store.BootstrapAdminAsync("admin", Password, Ct);
        var second = await database.Store.BootstrapAdminAsync("other", Password, Ct);
        Assert.True(first.Success);
        Assert.False(second.Success);
        Assert.Equal("ALREADY_INITIALIZED", second.Code);
        Assert.Equal(1L, await database.ScalarAsync("SELECT count(*) FROM users;"));
        Assert.Equal(1L, await database.ScalarAsync("SELECT count(*) FROM audit_entries WHERE action='admin.bootstrap';"));
        var encoded = Assert.IsType<string>(await database.ScalarAsync("SELECT password_hash FROM users;"));
        Assert.StartsWith("pbkdf2-sha256$600000$", encoded);
        Assert.DoesNotContain(Password, encoded);
    }

    [Fact]
    public async Task SessionPersistsOnlyDigestAndSurvivesStoreRecreation()
    {
        await using var database = await TestDatabase.OpenAsync();
        var admin = await database.BootstrapAsync();
        var response = await database.Store.LoginAsync("ADMIN", Password, Ct);
        Assert.True(response.Success);
        var ticket = Assert.IsType<SessionTicket>(response.Data);
        Assert.Equal(admin.Id, ticket.User.Id);
        var digest = Assert.IsType<byte[]>(await database.ScalarAsync("SELECT token_hash FROM sessions;"));
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(ticket.Token)), digest);
        var reopened = new ControlStore(database.Options, database.Clock);
        Assert.Equal(admin, await reopened.ValidateSessionAsync(ticket.Token, Ct));
        Assert.Null(await reopened.ValidateSessionAsync(new string('x', 43), Ct));
        database.Clock.Advance(TimeSpan.FromMinutes(database.Options.SessionLifetimeMinutes));
        Assert.Null(await reopened.ValidateSessionAsync(ticket.Token, Ct));
    }

    [Fact]
    public async Task InvalidCredentialsDoNotIssueSessionAndAreAudited()
    {
        await using var database = await TestDatabase.OpenAsync();
        await database.BootstrapAsync();
        var wrong = await database.Store.LoginAsync("admin", "incorrect-password", Ct);
        var missing = await database.Store.LoginAsync("unknown", Password, Ct);
        Assert.Equal("AUTH_FAILED", wrong.Code);
        Assert.Equal(wrong.Code, missing.Code);
        Assert.Equal(0L, await database.ScalarAsync("SELECT count(*) FROM sessions;"));
        Assert.Equal(2L, await database.ScalarAsync("SELECT count(*) FROM audit_entries WHERE action='auth.login_failed';"));
    }

    [Fact]
    public async Task AccountRevocationAndLogoutInvalidateSessions()
    {
        await using var database = await TestDatabase.OpenAsync();
        await database.BootstrapAsync();
        var first = (await database.Store.LoginAsync("admin", Password, Ct)).Data!;
        Assert.True((await database.Store.LogoutAsync(first.Token, Ct)).Success);
        Assert.Null(await database.Store.ValidateSessionAsync(first.Token, Ct));
        var second = (await database.Store.LoginAsync("admin", Password, Ct)).Data!;
        await database.ExecuteAsync("UPDATE users SET session_version=session_version+1;");
        Assert.Null(await database.Store.ValidateSessionAsync(second.Token, Ct));
        var third = (await database.Store.LoginAsync("admin", Password, Ct)).Data!;
        await database.ExecuteAsync("UPDATE users SET enabled=0;");
        Assert.Null(await database.Store.ValidateSessionAsync(third.Token, Ct));
    }

    [Fact]
    public async Task PasswordChangeChecksTheCurrentSessionAndRevokesEverySession()
    {
        await using var database = await TestDatabase.OpenAsync();
        var admin = await database.BootstrapAsync();
        var first = (await database.Store.LoginAsync(admin.Username, Password, Ct)).Data!;
        var second = (await database.Store.LoginAsync(admin.Username, Password, Ct)).Data!;

        Assert.Equal("password_policy", (await database.Store.ChangePasswordAsync(admin.Id, first.Token, Password, "short", Ct)).Code);
        Assert.Equal("password_unchanged", (await database.Store.ChangePasswordAsync(admin.Id, first.Token, Password, Password, Ct)).Code);
        Assert.Equal("authentication_required", (await database.Store.ChangePasswordAsync(admin.Id + 1, first.Token, Password, NewPassword, Ct)).Code);
        Assert.Equal("current_password_invalid", (await database.Store.ChangePasswordAsync(admin.Id, first.Token, "synthetic-incorrect", NewPassword, Ct)).Code);
        Assert.Equal(admin, await database.Store.ValidateSessionAsync(first.Token, Ct));

        Assert.True((await database.Store.ChangePasswordAsync(admin.Id, first.Token, Password, NewPassword, Ct)).Success);
        Assert.Null(await database.Store.ValidateSessionAsync(first.Token, Ct));
        Assert.Null(await database.Store.ValidateSessionAsync(second.Token, Ct));
        Assert.Equal(0L, await database.ScalarAsync("SELECT count(*) FROM sessions WHERE revoked=0;"));
        Assert.Equal("authentication_required", (await database.Store.ChangePasswordAsync(admin.Id, first.Token, NewPassword, Password, Ct)).Code);
        Assert.Equal("AUTH_FAILED", (await database.Store.LoginAsync(admin.Username, Password, Ct)).Code);
        Assert.True((await database.Store.LoginAsync(admin.Username, NewPassword, Ct)).Success);
        Assert.Equal("{}", await database.ScalarAsync("SELECT details_json FROM audit_entries WHERE action='auth.password_change';"));
        Assert.Equal("{}", await database.ScalarAsync("SELECT details_json FROM audit_entries WHERE action='auth.password_change_failed';"));
    }

    [Fact]
    public async Task PasswordChangeRollsBackCredentialAndSessionChangesWhenAuditFails()
    {
        await using var database = await TestDatabase.OpenAsync();
        var admin = await database.BootstrapAsync();
        var session = (await database.Store.LoginAsync(admin.Username, Password, Ct)).Data!;
        var originalHash = await database.ScalarAsync("SELECT password_hash FROM users;");
        await database.RejectAuditAsync();

        await Assert.ThrowsAsync<SqliteException>(() => database.Store.ChangePasswordAsync(admin.Id, session.Token, Password, NewPassword, Ct));

        Assert.Equal(originalHash, await database.ScalarAsync("SELECT password_hash FROM users;"));
        Assert.Equal(1L, await database.ScalarAsync("SELECT session_version FROM users;"));
        Assert.Equal(admin, await database.Store.ValidateSessionAsync(session.Token, Ct));
    }

    [Fact]
    public async Task ConcurrentPasswordChangesAndOldPasswordLoginCannotLeaveAStaleSession()
    {
        await using var database = await TestDatabase.OpenAsync();
        var admin = await database.BootstrapAsync();
        var session = (await database.Store.LoginAsync(admin.Username, Password, Ct)).Data!;
        const string otherPassword = "Synthetic-Other-Password-2026";
        var oldLogin = Task.Run(() => database.Store.LoginAsync(admin.Username, Password, Ct));
        var first = Task.Run(() => database.Store.ChangePasswordAsync(admin.Id, session.Token, Password, NewPassword, Ct));
        var second = Task.Run(() => database.Store.ChangePasswordAsync(admin.Id, session.Token, Password, otherPassword, Ct));
        var results = await Task.WhenAll(first, second);

        Assert.Single(results, result => result.Success);
        Assert.Single(results, result => result.Code == "authentication_required");
        var staleSession = (await oldLogin).Data;
        if (staleSession is not null) Assert.Null(await database.Store.ValidateSessionAsync(staleSession.Token, Ct));
        Assert.True((await database.Store.LoginAsync(admin.Username, results[0].Success ? NewPassword : otherPassword, Ct)).Success);
    }

    [Fact]
    public async Task SourceAndRetentionChangesRequireAdminAndHaveReliableAudit()
    {
        await using var database = await TestDatabase.OpenAsync();
        var admin = await database.BootstrapAsync();
        Assert.Equal("FORBIDDEN", (await database.Store.CreateSourceAsync(Source, 999_999, Ct)).Code);
        var created = await database.Store.CreateSourceAsync(Source, admin.Id, Ct);
        Assert.True(created.Success);
        var changed = await database.Store.UpdateSourceAsync(created.Data!.Id, Source with { RetentionDays = 1095, Enabled = false }, admin.Id, Ct);
        Assert.True(changed.Success);
        Assert.Equal(1095, changed.Data!.RetentionDays);
        Assert.False(changed.Data.Enabled);
        Assert.Equal(changed.Data, Assert.Single(await database.Store.ListAsync(Ct)));
        Assert.Equal(2L, await database.ScalarAsync("SELECT count(*) FROM audit_entries WHERE action LIKE 'source.%';"));
        var details = Assert.IsType<string>(await database.ScalarAsync("SELECT details_json FROM audit_entries WHERE action='source.update';"));
        Assert.Contains("365", details);
        Assert.Contains("1095", details);
    }

    [Fact]
    public async Task SourceValidationAndAddressNormalizationPreventAmbiguousRouting()
    {
        await using var database = await TestDatabase.OpenAsync();
        var admin = await database.BootstrapAsync();
        Assert.Equal("INVALID_INPUT", (await database.Store.CreateSourceAsync(Source with { RetentionDays = 0 }, admin.Id, Ct)).Code);
        Assert.Equal("INVALID_INPUT", (await database.Store.CreateSourceAsync(Source with { TimeZoneId = "invalid/zone" }, admin.Id, Ct)).Code);
        Assert.Equal("INVALID_INPUT", (await database.Store.CreateSourceAsync(Source with { PeerAddress = "example.invalid" }, admin.Id, Ct)).Code);
        Assert.True((await database.Store.CreateSourceAsync(Source, admin.Id, Ct)).Success);
        Assert.Equal("CONFLICT", (await database.Store.CreateSourceAsync(Source with { PeerAddress = "::ffff:192.0.2.10" }, admin.Id, Ct)).Code);
        Assert.Equal("NOT_FOUND", (await database.Store.UpdateSourceAsync("987654", Source, admin.Id, Ct)).Code);
        Assert.Single(await database.Store.ListAsync(Ct));
    }

    [Fact]
    public async Task AuditFailureRollsBackSourceAndRetentionTogether()
    {
        await using var database = await TestDatabase.OpenAsync();
        var admin = await database.BootstrapAsync();
        var source = (await database.Store.CreateSourceAsync(Source, admin.Id, Ct)).Data!;
        await database.RejectAuditAsync();
        await Assert.ThrowsAsync<SqliteException>(() => database.Store.UpdateSourceAsync(source.Id, Source with { Name = "changed", RetentionDays = 1095 }, admin.Id, Ct));
        Assert.Equal(source, Assert.Single(await database.Store.ListAsync(Ct)));
        await Assert.ThrowsAsync<SqliteException>(() => database.Store.CreateSourceAsync(Source with { PeerAddress = "192.0.2.11" }, admin.Id, Ct));
        Assert.Single(await database.Store.ListAsync(Ct));
        Assert.Equal(1L, await database.ScalarAsync("SELECT count(*) FROM retention_policies;"));
    }

    [Fact]
    public async Task AuditFailureRollsBackBootstrapAndSessionIssuance()
    {
        await using var empty = await TestDatabase.OpenAsync();
        await empty.RejectAuditAsync();
        await Assert.ThrowsAsync<SqliteException>(() => empty.Store.BootstrapAdminAsync("admin", Password, Ct));
        Assert.Equal(0L, await empty.ScalarAsync("SELECT count(*) FROM users;"));

        await using var initialized = await TestDatabase.OpenAsync();
        await initialized.BootstrapAsync();
        await initialized.RejectAuditAsync();
        await Assert.ThrowsAsync<SqliteException>(() => initialized.Store.LoginAsync("admin", Password, Ct));
        Assert.Equal(0L, await initialized.ScalarAsync("SELECT count(*) FROM sessions;"));
    }

    [Fact]
    public async Task IdBlocksAreUniqueAcrossConcurrentCallsAndRecreatedStores()
    {
        await using var database = await TestDatabase.OpenAsync();
        var ids = await Task.WhenAll(Enumerable.Range(0, 5000).Select(_ => Task.Run(() => database.Store.NextAsync(Ct))));
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.All(ids, value => Assert.True(value > 0));
        var reopened = new ControlStore(database.Options);
        var resumed = await reopened.NextAsync(Ct);
        Assert.True(resumed > ids.Max());
        Assert.True(Assert.IsType<long>(await database.ScalarAsync("SELECT next_id FROM id_allocations WHERE name='global';")) > resumed);
        Assert.DoesNotContain(await database.Store.NextAsync(Ct), ids.Append(resumed));
    }

    [Fact]
    public async Task ExhaustedIdRangeFailsWithoutWrappingOrResetting()
    {
        await using var database = await TestDatabase.OpenAsync();
        await database.ExecuteAsync($"UPDATE id_allocations SET next_id={long.MaxValue};");
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.Store.NextAsync(Ct));
        Assert.Equal(long.MaxValue, await database.ScalarAsync("SELECT next_id FROM id_allocations;"));
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection _anchor;
        internal ControlStoreOptions Options { get; }
        internal MutableClock Clock { get; } = new();
        internal ControlStore Store { get; }

        private TestDatabase(SqliteConnection anchor, ControlStoreOptions options)
        {
            _anchor = anchor;
            Options = options;
            Store = new ControlStore(options, Clock);
        }

        internal static async Task<TestDatabase> OpenAsync(bool initialize = true)
        {
            var options = new ControlStoreOptions
            {
                ConnectionString = $"Data Source=control-test-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False",
                IdBlockSize = 32
            };
            var anchor = new SqliteConnection(options.ConnectionString);
            await anchor.OpenAsync();
            var database = new TestDatabase(anchor, options);
            if (initialize) await database.Store.InitializeAsync(Ct);
            return database;
        }

        internal async Task<ControlUser> BootstrapAsync()
        {
            var result = await Store.BootstrapAdminAsync("admin", Password, Ct);
            Assert.True(result.Success);
            return Assert.IsType<ControlUser>(result.Data);
        }

        internal async Task<object?> ScalarAsync(string sql)
        {
            await using var command = _anchor.CreateCommand();
            command.CommandText = sql;
            return await command.ExecuteScalarAsync();
        }

        internal async Task ExecuteAsync(string sql)
        {
            await using var command = _anchor.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        internal Task RejectAuditAsync() => ExecuteAsync("CREATE TRIGGER reject_audit BEFORE INSERT ON audit_entries BEGIN SELECT RAISE(ABORT,'synthetic audit failure'); END;");
        public ValueTask DisposeAsync() => _anchor.DisposeAsync();
    }

    private sealed class MutableClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.Parse("2026-01-01T00:00:00Z", global::System.Globalization.CultureInfo.InvariantCulture);
        public override DateTimeOffset GetUtcNow() => _now;
        internal void Advance(TimeSpan elapsed) => _now += elapsed;
    }
}
