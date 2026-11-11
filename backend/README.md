# 后端

当前为 0.1.0 首批后端实现。Syslog.Api 是单一 ASP.NET Core 宿主，组合 Syslog.Ingest 接收/存储模块与 Syslog.Engine 解析引擎；Syslog.Shared 保存公共合同。架构依据为已接受的 [ADR-0001](../docs/adr/ADR-0001-轻量化长周期日志架构.md)。

## 当前状态

- 新增未登记设备内存暂存与只读管理员 API：每 IP 最多 100 条，许可网段和独立资源预算，登记复用既有来源与审计事务。详见[行为与配置](../docs/spec/未登记设备暂存.md)，不新增数据库结构。

- Syslog.slnx、四个 .NET 工程、Directory.Build.props、Directory.Packages.props 和依赖锁文件已建立。
- 已有 RFC 5424 / 3164 基础解析、帧解码、UDP/TCP/TLS/RELP 接收工作器、有界队列、文件暂存、ClickHouse HTTP 批量写入与查询代码；接收默认关闭。
- 已有 SQLite 控制库 SQL、显式初始化/管理员引导入口、会话认证、来源配置、保留天数和操作审计；当前仅管理员角色。
- 已有首次配置状态与管理员初始化接口、账号分页查询、新建、改名、启停和重置密码接口。复用现有版本 1 表结构，账号变更与审计同事务，旧会话即时失效。
- 接收配置 API 使用版本 2 监听配置表，配置与审计同事务保存。重新开启日志接收总开关可应用已保存端点。旧版本需显式 `--migrate-listeners` 升级。详细配置、TLS 引用和 RELP 确认边界见[接收配置与 RELP](../docs/spec/接收配置与RELP.md)。
- 来源的严重级别忽略规则随来源设置和审计同事务保存。版本 2 控制库经授权备份后使用 `--migrate-source-filters` 升级至版本 3。新初始化当前依次执行 `001` 至 `006`。匹配规则的新报文只计入忽略量，跳过磁盘暂存和 ClickHouse 写入。
- 管理台可上传 PFX/P12 HTTPS 证书。证书保存在内容根目录下的服务私有 `run-data/https-certificate/`，下次启动由 Kestrel 加载。Syslog TLS 接收器可独立选择自签证书、上传证书或服务器预配证书。
- 已有日志检索、详情、来源、会话与状态 API。全文首版仅区分大小写的 ASCII 全词 AND；中文或纯符号明确拒绝。
- 看板通过 `/hubs/overview` 推送真实运行快照，复用当前管理员安全 Cookie，并在每次推送前重新验证会话；仅允许同源 HTTPS WebSocket，未登录或外站请求无法订阅。
- ClickHouse 初始化 SQL 可审阅，最低目标版本 26.8；未连接真实 ClickHouse，也未验证真实监听、厂商设备或性能。
- Web 管理台已有首批页面和同源静态托管，告警、自定义解析、回放、导出、历史保留变更、物理清理和备份恢复尚未实现。当前不是完整一期交付。

## 结构与依赖

~~~text
backend/
├── Syslog.slnx
├── Directory.Build.props
├── Directory.Packages.props
├── src/
│   ├── Syslog.Shared/       # 公共合同
│   ├── Syslog.Engine/       # 纯函数解析与帧解码
│   ├── Syslog.Ingest/       # 接收、暂存与 ClickHouse 适配模块
│   └── Syslog.Api/          # 组合宿主、SQLite 控制数据/监听配置与管理 API
└── tests/
    ├── Syslog.Shared.Tests/ # 目录预留，独立工程待建立
    ├── Syslog.Engine.Tests/
    ├── Syslog.Ingest.Tests/
    └── Syslog.Api.Tests/
~~~

依赖方向：Syslog.Api → Ingest / Engine / Shared；Ingest → Engine / Shared；Engine → Shared。Ingest 不反向引用 Api。管理服务负责 SQLite 写入，日志存储适配器负责 ClickHouse 写入；不能把旧文档的“全库 Ingest 唯一写者”用于当前实现。

## 运行与验证边界

MCP 已加入独立管理页面、39 个业务工具与完整调用审计。MCP 三表对应版本 5；现有版本 4 控制库使用显式 `--migrate-mcp` 升级，普通启动不自动建表或迁移。版本 1～3 必须先完成已有监听、来源忽略和邮件迁移。迁移命令沿用 `dotnet <应用路径>/Syslog.Api.dll --migrate-mcp`，应指向获准的控制库；本轮仅授权本机隔离验证，不代表正式库升级或部署。详细合同见 [MCP 服务需求与设计](../docs/spec/MCP服务需求与设计.md)。

MCP 复用 Web 监听的 `/mcp`，支持 HTTP/HTTPS、独立 Bearer 和有限 JSON/SSE 响应；默认停止，在管理台启动后生效。管理 API 仍使用管理员 HTTPS 会话。专用审计按用户要求保存完整应用请求/响应，包含实际提交的凭据，不复制到普通应用日志。

实时看板将新初始化版本提升至 v6，仅新增一张配置表；既有 v5 库通过 `--migrate-live-logs` 显式升级。日志仅驻内存，默认每设备 5000 条、总预算 512 MiB，包括忽略且不入库的日志。配置保存立即应用，见[实时日志看板](../docs/spec/实时日志看板.md)。

SQLite 初始化和既有库表迁移必须显式执行。ClickHouse 首次访问时检查并创建缺失的日志库表。管理员可确认清空已入库日志，清空意图和执行结果均记录在 SQLite。生产端口、证书与服务器操作分别授权。管理宿主与接收工作器共享进程故障域，应用退出会同时中断两者。未确认写入的批次在恢复时以原内容、原事件标识和相同去重标记自动重试。

首次配置页的提交属于显式初始化入口，只接受 HTTPS、回环连接和本机 Host，拒绝转发头。远程安装需通过受控的本机访问或 SSH 隧道，初始化时不得由反向代理公开提交入口。状态查询不建库，缺少目录、损坏/空白文件或未知版本均返回服务不可用，不覆盖现有数据。新库提交把既定结构、首个账号和审计同事务完成，若进程在认领新文件后中断而留下空文件，需要显式维护处理，系统不自动删除或重建。既有账号全部停用也不会重新开放初始化。

从仓库根目录可运行以下实际项目命令；测试结果以本地实际运行记录为准：

~~~sh
dotnet build backend/src/Syslog.Api/Syslog.Api.csproj
dotnet test backend/tests/Syslog.Engine.Tests/Syslog.Engine.Tests.csproj
dotnet test backend/tests/Syslog.Ingest.Tests/Syslog.Ingest.Tests.csproj
dotnet test backend/tests/Syslog.Api.Tests/Syslog.Api.Tests.csproj
~~~

测试使用合成报文、内存/HTTP 替身和已获准的隔离 SQLite；本轮不以测试启动真实接收端口、初始化 ClickHouse 或执行文件暂存删除。ClickHouse SQL、索引执行计划和用户查询性能须在获准环境另行验收。

[首批存储实施合同](../docs/spec/首批存储实施合同.md) · [接口合同](../docs/api/README.md) · [后端规则](AGENTS.md)
