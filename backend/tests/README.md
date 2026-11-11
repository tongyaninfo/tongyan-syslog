# backend/tests

当前已有 Engine、Ingest、Api 三个测试工程，Shared.Tests 仍为目录预留。

| 项目 | 当前验证方向 |
| --- | --- |
| [Syslog.Engine.Tests](Syslog.Engine.Tests/README.md) | 合成 RFC 报文与纯帧解码 |
| [Syslog.Ingest.Tests](Syslog.Ingest.Tests/README.md) | HTTP/流/暂存替身、查询安全预算与管线状态 |
| [Syslog.Api.Tests](Syslog.Api.Tests/README.md) | 已获准隔离 SQLite 的控制数据、账号会话和可靠审计 |
| [Syslog.Shared.Tests](Syslog.Shared.Tests/README.md) | 独立工程尚未建立 |

本轮测试不启动真实 Syslog 监听，不连接 ClickHouse，不调用文件暂存确认回收。账号测试使用合成数据，SQLite 仅限明确获准的隔离资源；测试不自动删除测试文件，清理仍按用户授权执行。

构建与测试命令见[后端入口](../README.md)，实际通过数量以本地运行结果为准。测试存在或通过都不推导为真实设备/数据库、浏览器、吞吐或全量历史性能验收通过。

[后端规则](../AGENTS.md) · [根规则](../../AGENTS.md)
