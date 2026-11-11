using Xunit;

namespace Syslog.Api.Tests;

public sealed partial class ControlStoreTests
{
    public static IEnumerable<object[]> NewPasswordCases()
    {
        yield return ["1234567", false];
        yield return ["12345678", true];
        yield return ["12345678901", true];
        yield return [new string('p', 1024), true];
        yield return [new string('p', 1025), false];
        yield return ["", false];
        yield return ["        ", false];
        yield return [new string(' ', 1024), false];
        yield return [" abcdef ", true];
        // 每个 emoji 占两个 UTF-16 单元，保持既有长度语义，不按字素或 UTF-8 字节重算。
        yield return ["😀😀😀a", false];
        yield return ["😀😀😀😀", true];
    }

    public static IEnumerable<object[]> AdminNewPasswordCases() =>
        NewPasswordCases().SelectMany(input => new[] { new object[] { false, input[0], input[1] }, new object[] { true, input[0], input[1] } });

    [Theory]
    [MemberData(nameof(AdminNewPasswordCases))]
    public async Task SetupAndCliBootstrapShareNewPasswordBounds(bool cli, string password, bool accepted)
    {
        await using var database = await TestDatabase.OpenAsync();
        var result = cli
            ? await database.Store.BootstrapAdminAsync("admin", password, Ct)
            : await database.Store.SetupAdminAsync("admin", password, Ct);
        Assert.Equal(accepted, result.Success);
        if (accepted)
            Assert.True((await database.Store.LoginAsync("admin", password, Ct)).Success);
        else
        {
            Assert.Equal(cli ? "INVALID_INPUT" : "password_policy", result.Code);
            Assert.True(await database.Store.IsSetupRequiredAsync(Ct));
            Assert.Equal(0L, await database.ScalarAsync("SELECT count(*) FROM users;"));
        }
    }

    [Theory]
    [MemberData(nameof(NewPasswordCases))]
    public async Task CreateResetAndSelfChangeShareBoundsAndPreserveSessionRules(string password, bool accepted)
    {
        await using var database = await TestDatabase.OpenAsync();
        var admin = await database.BootstrapAsync();
        var adminSession = (await database.Store.LoginAsync(admin.Username, Password, Ct)).Data!;
        var created = await database.Store.CreateUserAsync(new("created", password), admin.Id, adminSession.Token, Ct);
        Assert.Equal(accepted, created.Success);

        var target = (await database.Store.CreateUserAsync(new("reset-target", Password), admin.Id, adminSession.Token, Ct)).Data!;
        var targetSession = (await database.Store.LoginAsync(target.Username, Password, Ct)).Data!;
        var reset = await database.Store.ResetUserPasswordAsync(IdText(target.Id), new(password, "1"), admin.Id, adminSession.Token, Ct);
        var changed = await database.Store.ChangePasswordAsync(admin.Id, adminSession.Token, Password, password, Ct);
        Assert.Equal(accepted, reset.Success);
        Assert.Equal(accepted, changed.Success);

        if (accepted)
        {
            Assert.True((await database.Store.LoginAsync("created", password, Ct)).Success);
            Assert.True((await database.Store.LoginAsync(target.Username, password, Ct)).Success);
            Assert.True((await database.Store.LoginAsync(admin.Username, password, Ct)).Success);
            Assert.Null(await database.Store.ValidateSessionAsync(targetSession.Token, Ct));
            Assert.Null(await database.Store.ValidateSessionAsync(adminSession.Token, Ct));
        }
        else
        {
            Assert.Equal("password_policy", created.Code);
            Assert.Equal("password_policy", reset.Code);
            Assert.Equal("password_policy", changed.Code);
            Assert.True((await database.Store.LoginAsync(target.Username, Password, Ct)).Success);
            Assert.True((await database.Store.LoginAsync(admin.Username, Password, Ct)).Success);
            Assert.NotNull(await database.Store.ValidateSessionAsync(targetSession.Token, Ct));
            Assert.NotNull(await database.Store.ValidateSessionAsync(adminSession.Token, Ct));
        }
    }
}
