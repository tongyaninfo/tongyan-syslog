using Microsoft.Data.Sqlite;

namespace Syslog.Api.Service.impl;

public sealed partial class ControlStore
{
    public async Task MigrateNotificationsAsync(CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = await OpenAsync(false, cancellationToken);
            using var transaction = connection.BeginTransaction();
            await ValidateSetupSchemaAsync(connection, transaction, cancellationToken, fullCheck: true);
            var version = await ReadSchemaVersionAsync(connection, transaction, cancellationToken);
            if (version < 3) throw new InvalidOperationException("请先执行来源筛选迁移。");
            if (version == 3)
            {
                await ApplyNotificationMigrationAsync(connection, transaction, cancellationToken);
                await ValidateSetupSchemaAsync(connection, transaction, cancellationToken, fullCheck: true);
            }
            transaction.Commit();
        }
        finally { _writeGate.Release(); }
    }

    private static async Task ApplyNotificationMigrationAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        await using var resource = typeof(ControlStore).Assembly.GetManifestResourceStream("Syslog.Api.Data.004_notification_settings.sql")
            ?? throw new InvalidOperationException("通知设置迁移资源缺失。");
        using var reader = new StreamReader(resource);
        await ExecuteAsync(connection, transaction, await reader.ReadToEndAsync(ct), ct);
    }

    private static async Task ValidateNotificationSchemaAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct)
    {
        // 在显式迁移和控制库校验时验证全部列。版本号存在但结构不完整时交给维护流程处理。
        foreach (var query in new[]
        {
            "SELECT singleton,version,host,port,security,username,sender_address,sender_name,subject_prefix,protected_password,updated_by,updated_at FROM smtp_settings LIMIT 0;",
            "SELECT id,version,name,enabled,frequency,send_time,time_zone_id,source_ids_json,recipients_json,next_send_at,updated_by,updated_at FROM report_subscriptions LIMIT 0;",
            "SELECT id,subscription_id,subscription_name,kind,status,recipient,recipient_key,subject_prefix,subject,html_body,text_body,source_ids_json,period_start,period_end,created_at,updated_at,available_at,attempts,failure_code FROM mail_deliveries LIMIT 0;"
        })
        {
            await using var command = Command(connection, transaction, query);
            await using var reader = await command.ExecuteReaderAsync(ct);
        }
    }

    private static async Task<bool> SupportsNotificationsAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken ct) =>
        (await ReadSchemaVersionAsync(connection, transaction, ct)) switch
        {
            1 or 2 or 3 => false,
            4 or 5 or 6 => true,
            _ => throw new InvalidOperationException("控制库版本不受支持。")
        };
}
