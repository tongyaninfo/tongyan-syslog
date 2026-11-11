# Syslog.Engine.Tests

纯解析测试工程已建立，使用合成报文验证 RFC 5424/3164、结构化数据、非法 PRI、时间/编码边界，以及字节计数/换行帧的半包、粘包和长度限制。

RelpFrameDecoderTests 验证 RELP 事务号与命令边界、零负载帧、字节长度、二进制原文、半包/粘包和提前拒绝超长声明。

不启动监听器、不读真实日志、不写数据库或发送通知。厂商完整适配、告警与回放尚未实现，相关验收不能从基础解析测试推导。

从仓库根运行：

~~~sh
dotnet test backend/tests/Syslog.Engine.Tests/Syslog.Engine.Tests.csproj
~~~

[规则样本](../../../rules/README.md) · [后端规则](../../AGENTS.md)
