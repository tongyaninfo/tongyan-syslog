using Syslog.Api.Models;
using Syslog.Shared.Models;

namespace Syslog.Api.Service;

public interface IListenerConfigurationService
{
    Task<ApiResult<ListenerSettingsView>> ReadAsync(CancellationToken cancellationToken);
    ListenerRuntimeView GetRuntimeSnapshot();
    Task<ApiResult<ListenerSettingsView>> SaveAsync(SaveListenerSettingsRequest input, long actorId, string token, CancellationToken cancellationToken);
    Task<ApiResult<ListenerSettingsView>> SetReceptionAsync(bool enabled, long actorId, string token, CancellationToken cancellationToken,
        string? expectedVersion = null);
    Task<ApiResult<ListenerSettingsView>> SetProtocolReceptionAsync(string protocol, SetListenerProtocolReceptionRequest input,
        long actorId, string token, CancellationToken cancellationToken);
}
