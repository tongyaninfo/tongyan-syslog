# backend/src

四个后端工程已经建立；当前实现范围见[后端入口](../README.md)。

| 项目 | 当前职责 |
| --- | --- |
| [Syslog.Shared](Syslog.Shared/README.md) | ApiResult、事件/来源/检索 DTO、解析/发号/存储接口 |
| [Syslog.Engine](Syslog.Engine/README.md) | RFC 基础解析与确定性帧解码 |
| [Syslog.Ingest](Syslog.Ingest/README.md) | 接收工作器、有界队列、文件暂存、ClickHouse HTTP 适配 |
| [Syslog.Api](Syslog.Api/README.md) | 单宿主组合、SQLite 管理库、认证与管理 API |

依赖方向为 Api → Ingest / Engine / Shared；Ingest → Engine / Shared；Engine → Shared。Engine 不依赖 ASP.NET Core、网络或持久化，Ingest 不反向引用 Api。

业务代码按 Models、Service、Service/impl、Common、Options、Middleware、Workers、Data 分类。后续能力按真实实现增加，不把告警、导出或管理台预留目录当成已实现。

约定见[后端规则](../AGENTS.md)与[根规则](../../AGENTS.md)。
