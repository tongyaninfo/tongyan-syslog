using Syslog.Shared.Models;
namespace Syslog.Shared.Service;
public interface ILogStore
{
    Task EnsureReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    Task WriteBatchAsync(IReadOnlyList<LogEvent> events, CancellationToken cancellationToken);
    async Task WriteBatchAsync(IReadOnlyList<LogEvent> events, Func<CancellationToken, Task> beforeSend, CancellationToken cancellationToken)
    {
        await EnsureReadyAsync(cancellationToken);
        await beforeSend(cancellationToken);
        await WriteBatchAsync(events, cancellationToken);
    }
    Task<LogSearchPage> SearchAsync(LogSearch query, IReadOnlyCollection<string> allowedSourceIds, CancellationToken cancellationToken);
    Task<LogEvent?> GetAsync(long eventId, DateTimeOffset receivedAt, IReadOnlyCollection<string> allowedSourceIds, CancellationToken cancellationToken);
    Task<LogReport> GetReportAsync(DateTimeOffset from, DateTimeOffset to, IReadOnlyCollection<string> allowedSourceIds, CancellationToken cancellationToken)
        => throw new NotSupportedException("当前日志存储不支持报告查询。");
    Task<long?> GetDatabaseSizeAsync(CancellationToken cancellationToken) => Task.FromResult<long?>(null);
    Task ClearAsync(CancellationToken cancellationToken) => throw new NotSupportedException("当前存储不支持清空日志。");
    Task ClearSourceAsync(string sourceId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("当前存储不支持按来源清空日志。");
}
