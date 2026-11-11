using Syslog.Shared.Models;
namespace Syslog.Shared.Service;
public interface ISyslogParser
{
    LogEvent Parse(ReadOnlyMemory<byte> message, ParseContext context);
}
