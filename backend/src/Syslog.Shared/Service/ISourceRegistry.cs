using Syslog.Shared.Models;
namespace Syslog.Shared.Service;
public interface ISourceRegistry
{
    Task<IReadOnlyList<LogSource>> ListAsync(CancellationToken cancellationToken);
}
