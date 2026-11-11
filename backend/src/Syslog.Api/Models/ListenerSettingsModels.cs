using System.Text.Json.Serialization;
using Syslog.Ingest.Models;

namespace Syslog.Api.Models;

/// <summary>管理配置只引用证书标识，绝不保存或返回证书密码、私钥及服务器文件路径。</summary>
public sealed record ListenerSetting(string Protocol, bool Enabled, string Address, int Port,
    string Framing = "octet-counting", string CertificateId = "", int FrameTimeoutSeconds = 30, int IdleTimeoutSeconds = 1800);

public sealed record ListenerConfiguration(bool Enabled, IReadOnlyList<ListenerSetting> Listeners)
{
    public static ListenerConfiguration Default() => new(false,
    [
        new("udp", false, "0.0.0.0", 514),
        new("tcp", false, "0.0.0.0", 514),
        new("tls", false, "0.0.0.0", 6514),
        new("relp", false, "0.0.0.0", 2514)
    ]);
}

public sealed record SavedListenerSettings(string Version, ListenerConfiguration Configuration);
public sealed record SaveListenerSettingsRequest(string Version, ListenerConfiguration Configuration);
public sealed record SetListenerReceptionRequest([property: JsonRequired] bool Enabled, string? Version = null);
public sealed record SetListenerProtocolReceptionRequest([property: JsonRequired] bool Enabled, [property: JsonRequired] string Version);
public sealed record ListenerCertificateReference(string Id);
public sealed record ListenerRuntimeState(string Protocol, string Address, int Port, string State, string FailureCode, string FailureMessage = "");
public sealed record ListenerSettingsView(SavedListenerSettings Saved, ListenerConfiguration Running, bool RestartRequired,
    IReadOnlyList<ListenerCertificateReference> Certificates, IReadOnlyList<ListenerRuntimeState> States,
    IReadOnlyList<ListenerConnectionError> ConnectionErrors, string Revision);
public sealed record ListenerRuntimeView(string Revision, ListenerConfiguration Running, bool RestartRequired,
    IReadOnlyList<ListenerRuntimeState> States, IReadOnlyList<ListenerConnectionError> ConnectionErrors, SavedListenerSettings? Saved);
