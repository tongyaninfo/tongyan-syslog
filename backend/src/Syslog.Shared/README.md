# Syslog.Shared

公共合同类库已建立，不承载宿主或持久化实现。

当前类型包括 ApiResult、LogEvent、LogSearch/LogSearchPage、LogSource 和 ParseContext；接口包括 ISyslogParser、ILogStore、IIdService 与 ISourceRegistry。

实时看板合同为 `LiveLogFilter`、`LiveLogRecord`、`LiveLogCacheSettings`、快照/增量结果及 `ILiveLogBuffer`。实时 ID 使用进程内缓存世代与序列，不代替集中业务 ID；被忽略日志的 `LogEvent.EventId` 为 0。过滤器复用日志检索的字段、时间精度与 ASCII 全词 AND 语义。

- HTTP 响应字段为 success/code/message/data，并保留真实状态码。
- 事件原始字节与解析结果分开，失败原因与解析版本显式传递；时间使用带时区值。
- 业务 long ID 在 JSON 中以十进制字符串传输；来源 ID 同样作为字符串。
- 日志使用来源范围和游标分页，来源范围必须由服务端提供，不能相信客户端声明权限。
- 实时缓存默认未就绪，宿主读取获准配置后启用；分页包含结果与扫描预算标记，消费者必须在空页且 `hasMore=true` 时仍保留下一游标，不能将空页视为读取完成。

告警、导出、回放和多角色等合同按后续真实实现补充；不预先宣称类型或功能存在。合同变化同步[接口文档](../../../docs/api/README.md)，前端合同链建立前不虚构生成命令。

依赖方向：本项目不引用同仓库其他项目；Engine、Ingest、Api 与测试可以引用本项目。

[后端规则](../../AGENTS.md) · [根规则](../../../AGENTS.md)
