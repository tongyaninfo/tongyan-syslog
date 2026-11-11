using System.Net.Sockets;

namespace Syslog.Ingest.Common;

/// <summary>管理端只返回已知故障的安全说明，底层异常可能包含报文、路径或连接信息。</summary>
public static class ListenerFailureMessages
{
    public static string Describe(string code, string framing = "", SocketError? socketError = null) => code switch
    {
        "" => "",
        "invalid_listener_configuration" => "监听配置无效，请检查协议、绑定地址、端口和超时设置。",
        "listener_configuration_unavailable" => "无法读取接收配置，请检查控制库状态。",
        "listener_forced_disabled" => "宿主强制关闭了日志接收。",
        "source_registry_unavailable" => "无法读取日志来源配置，已暂停接收，恢复后将自动继续。",
        "tls_listener_start_failed" => "TLS 监听启动失败，请检查服务端证书是否可加载、包含私钥且仍在有效期内。",
        "listener_io_failed" => AppendSocketCode(DescribeListenerIo(socketError), socketError),
        "tls_handshake_timeout" => "连接在时限内未完成 TLS 握手，请检查发送端协议和网络。",
        "tls_handshake_failed" => "连接的 TLS 握手失败，请核对发送端是否使用 TLS，并检查证书信任和协议版本。",
        "invalid_length_prefix" => "该连接的帧长度前缀无效。当前按字节数分帧，要求以十进制字节数和空格开头。若设备按换行分帧，请选择自动识别或换行。",
        "unrecognized_frame_prefix" => "自动分帧无法识别该连接的报文开头。请核对发送协议，或显式选择设备使用的分帧方式。",
        "unexpected_tls_handshake" => "该连接的数据以 TLS 握手头开头。请核对设备的 TCP/TLS 协议与接收端口。",
        "empty_frame" => "该连接发送了空帧，请检查发送端是否发送了多余的分隔符。",
        "frame_too_large" or "relp_frame_too_large" => "该连接的帧超过接收大小上限，请检查发送端帧长度和接收配置。",
        "frame_timeout" => "该连接未在帧超时内发送完整帧，请检查分帧方式和发送速度。",
        "truncated_frame" when framing == "newline" => "该连接在收到换行分隔符前断开，请检查发送端是否以换行结束每条消息。",
        "truncated_frame" when framing == "octet-counting" => "该连接在收到完整帧前断开，请检查长度前缀是否与消息的字节数一致。",
        "truncated_frame" => "该连接在收到完整帧前断开，请检查发送端的帧长度和网络状态。",
        "relp_unexpected_tls_handshake" => "发送端使用 TLS，但连接到了 RELP 端口。请将目标改为已启用且设备可访问的 TLS 监听地址和端口。",
        "relp_negotiation_failed" => "该连接未完成 RELP 会话协商，请确认发送端使用 RELP，且协商版本为 0 或 1 并支持 syslog 命令。",
        "relp_invalid_frame" => "该连接发送的 RELP 帧格式无效，请核对发送端协议与目标端口。",
        "relp_invalid_command" => "该连接发送了不受支持的 RELP 命令或命令参数。",
        "relp_invalid_transaction" => "该连接的 RELP 事务编号不连续，请检查发送端的事务顺序。",
        "relp_ack_timeout" => "RELP 消息未在时限内完成接收确认，请检查日志暂存和写入状态。",
        "relp_response_timeout" => "未能在时限内向发送端发送 RELP 响应，请检查网络和发送端状态。",
        "connection_io_failed" => AppendSocketCode(DescribeConnectionIo(socketError), socketError),
        "connection_processing_failed" => "该连接处理失败，监听器仍可接收其他连接。",
        _ => "接收处理发生异常，请查看对应错误码。"
    };

    private static string DescribeListenerIo(SocketError? error) => error switch
    {
        SocketError.AddressAlreadyInUse => "监听端口已被占用，请检查同一地址和端口上的服务。",
        SocketError.AddressNotAvailable => "当前主机无法绑定该监听地址，请检查网络接口和地址配置。",
        SocketError.AccessDenied => "当前服务没有绑定该监听端口的权限，请检查操作系统和运行账号的权限。",
        _ => "监听器无法绑定端口或处理接收，请检查绑定地址、端口占用和运行权限。"
    };

    private static string DescribeConnectionIo(SocketError? error) => error switch
    {
        SocketError.ConnectionReset => "该连接被发送端重置，监听器仍可接收其他连接。",
        SocketError.ConnectionAborted => "该连接已中断，监听器仍可接收其他连接。",
        SocketError.TimedOut => "该连接的网络读写超时，请检查发送端和网络。",
        _ => "该连接在读写时中断或失败，请检查发送端和网络。监听器仍可接收其他连接。"
    };

    private static string AppendSocketCode(string message, SocketError? error) =>
        error is { } code && Enum.IsDefined(code) ? $"{message}（{code}）" : message;
}
