using System.Net;
using Syslog.Api.Models;

namespace Syslog.Api.Common;

/// <summary>保存和启动共用的有界配置校验；UDP 与流式端口分开，通配地址与具体地址视作冲突。</summary>
public static class ListenerSettingsValidation
{
    private static readonly string[] Protocols = ["udp", "tcp", "tls", "relp"];

    public static bool TryNormalize(ListenerConfiguration? configuration, out ListenerConfiguration normalized, out string code)
    {
        normalized = ListenerConfiguration.Default();
        code = "invalid_listener_configuration";
        if (configuration?.Listeners is not { Count: 4 }) return false;
        var listeners = new List<ListenerSetting>();
        foreach (var protocol in Protocols)
        {
            var matches = configuration.Listeners.Where(item => item is not null && item.Protocol == protocol).ToArray();
            if (matches.Length != 1) return false;
            var item = matches[0];
            var validFraming = item.Framing is "newline" or "octet-counting"
                || (item.Framing == "auto" && (protocol is "tcp" or "tls"));
            if (item.Address?.Length > 128 || !IPAddress.TryParse(item.Address, out var address)
                || item.Port is < 1 or > 65535 || !validFraming
                || item.FrameTimeoutSeconds is < 1 or > 300 || item.IdleTimeoutSeconds is < 1 or > 86400
                || item.CertificateId is null || item.CertificateId.Length > 64
                || !item.CertificateId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
                || (protocol != "tls" && item.CertificateId.Length != 0)) return false;
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            listeners.Add(item with { Address = address.ToString() });
        }
        var enabled = listeners.Where(item => item.Enabled).ToArray();
        for (var i = 0; i < enabled.Length; i++)
        for (var j = i + 1; j < enabled.Length; j++)
        {
            var left = enabled[i];
            var right = enabled[j];
            if ((left.Protocol == "udp") != (right.Protocol == "udp") || left.Port != right.Port) continue;
            var leftAddress = IPAddress.Parse(left.Address);
            var rightAddress = IPAddress.Parse(right.Address);
            if (leftAddress.AddressFamily == rightAddress.AddressFamily && (leftAddress.Equals(rightAddress)
                || leftAddress.Equals(IPAddress.Any) || leftAddress.Equals(IPAddress.IPv6Any)
                || rightAddress.Equals(IPAddress.Any) || rightAddress.Equals(IPAddress.IPv6Any)))
            { code = "listener_port_conflict"; return false; }
        }
        normalized = new(configuration.Enabled, listeners);
        code = "";
        return true;
    }
}
