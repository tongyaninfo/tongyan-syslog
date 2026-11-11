using System.Text;
using Syslog.Api.Service;

namespace Syslog.Api.Common;

/// <summary>维护命令只在显式调用时写库，不在 Web 启动期间创建表或默认管理员。</summary>
public static class ControlCommands
{
    public static async Task<int> RunAsync(IServiceProvider services, bool initialize, bool bootstrap, bool migrateListeners = false,
        bool migrateSourceFilters = false, bool migrateNotifications = false, bool migrateMcp = false, bool migrateLiveLogs = false)
    {
        try
        {
            var store = services.GetRequiredService<IControlStore>();
            if (initialize) await store.InitializeAsync(CancellationToken.None);
            if (migrateListeners) await store.MigrateListenersAsync(CancellationToken.None);
            if (migrateSourceFilters) await store.MigrateSourceFiltersAsync(CancellationToken.None);
            if (migrateNotifications) await store.MigrateNotificationsAsync(CancellationToken.None);
            if (migrateMcp) await store.MigrateMcpAsync(CancellationToken.None);
            if (migrateLiveLogs) await store.MigrateLiveLogsAsync(CancellationToken.None);
            if (bootstrap)
            {
                Console.Write("Admin username: ");
                var username = Console.ReadLine() ?? "";
                Console.Write("Admin password (8-1024 characters): ");
                var password = ReadPassword();
                var result = await store.BootstrapAdminAsync(username, password, CancellationToken.None);
                if (!result.Success) { Console.Error.WriteLine(result.Code + ": " + result.Message); return 1; }
            }
            Console.WriteLine("Control maintenance completed.");
            return 0;
        }
        catch (Exception) { Console.Error.WriteLine("Control maintenance failed. Verify configuration and schema status."); return 1; }
    }

    private static string ReadPassword()
    {
        if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return buffer.ToString(); }
            if (key.Key == ConsoleKey.Backspace && buffer.Length > 0) buffer.Length--;
            else if (!char.IsControl(key.KeyChar) && buffer.Length < 1024) buffer.Append(key.KeyChar);
        }
    }
}
