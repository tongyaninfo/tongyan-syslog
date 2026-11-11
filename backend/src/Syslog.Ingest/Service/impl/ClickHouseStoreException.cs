namespace Syslog.Ingest.Service.impl;

/// <summary>不携带服务端正文、地址或内部异常，避免上层意外泄露凭据。</summary>
public sealed class ClickHouseStoreException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
