namespace Syslog.Ingest.Models;

public sealed record RelpFrame(int Transaction, string Command, byte[] Payload);
