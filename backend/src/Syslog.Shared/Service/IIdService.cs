namespace Syslog.Shared.Service;
public interface IIdService
{
    Task<long> NextAsync(CancellationToken cancellationToken);
}
