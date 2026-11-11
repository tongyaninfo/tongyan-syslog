using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.AspNetCore.DataProtection;

namespace Syslog.Api.Service.impl;

/// <summary>授权码随 SQLite 配置存储。密钥环仅用于读取旧版保存的密文。</summary>
public sealed class NotificationSecrets(IWebHostEnvironment environment, IConfiguration? configuration = null)
{
    // 旧密文使用 Base64URL。带冒号的标识可区分新旧值，也不会限制授权码本身的内容。
    private const string PlainPrefix = "plain:";
    private readonly Lazy<IDataProtector> legacyProtector = new(
        () => CreateLegacyProtector(environment, configuration?["Notifications:KeyDirectory"]), LazyThreadSafetyMode.PublicationOnly);

    public static string Store(string value) => PlainPrefix + value;

    public string Read(string stored) => stored.StartsWith(PlainPrefix, StringComparison.Ordinal)
        ? stored[PlainPrefix.Length..] : legacyProtector.Value.Unprotect(stored);

    private static IDataProtector CreateLegacyProtector(IWebHostEnvironment environment, string? configuredDirectory)
    {
        var path = Path.GetFullPath(string.IsNullOrWhiteSpace(configuredDirectory)
            ? Path.Combine("run-data", "notification-keys") : configuredDirectory, environment.ContentRootPath);
        if (environment.WebRootPath is { Length: > 0 } webRoot)
        {
            var relative = Path.GetRelativePath(webRoot, path);
            if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidOperationException("邮件密钥目录必须位于静态文件根目录之外。");
        }

        var directory = new DirectoryInfo(path);
        if (!directory.Exists) throw new DirectoryNotFoundException("旧版 SMTP 密钥目录不可用。");
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("邮件密钥目录不能是链接。");
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            var user = identity.User ?? throw new InvalidOperationException("无法识别邮件密钥的服务账号。");
            var rules = directory.GetAccessControl().GetAccessRules(true, true, typeof(SecurityIdentifier));
            foreach (FileSystemAccessRule rule in rules)
                if (rule.AccessControlType == AccessControlType.Allow && !rule.IdentityReference.Equals(user))
                    throw new InvalidOperationException("邮件密钥目录权限不符合要求。");
        }
        else
        {
            const UnixFileMode ownerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            if ((File.GetUnixFileMode(path) & ~ownerOnly) != 0)
                throw new InvalidOperationException("邮件密钥目录权限不符合要求。");
        }
        return DataProtectionProvider.Create(directory, options => options.SetApplicationName("Tongyan.Syslog.Notifications")
                .DisableAutomaticKeyGeneration())
            .CreateProtector("smtp-authorization-code", "v1");
    }
}
