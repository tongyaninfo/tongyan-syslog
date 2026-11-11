# 接收配置与 RELP

更新：2026-11-11。接收配置支持 UDP / TCP / TLS / RELP，保存到 SQLite。单独重新开启协议可应用该协议的最新参数，重新开启日志接收总开关可同时应用各协议的配置。

## 配置与生效

「接收配置」位于 `/listeners`，支持全局接收开关及四种协议各自的开关、绑定 IP、端口。TCP / TLS 可选 `auto`、`octet-counting` 或 `newline` 分帧。自动模式按连接识别字节计数前缀与以 `<PRI>` 开头的 LF/CRLF 报文，兼容混合部署，沿用现有帧长和超时上限。RELP 使用自己的事务帧，UDP 使用数据报边界。默认配置为：

| 协议 | 绑定地址 | 端口 | 默认状态 |
| --- | --- | --- | --- |
| UDP | `0.0.0.0` | 514 | 关闭 |
| TCP | `0.0.0.0` | 514 | 关闭 |
| TLS | `0.0.0.0` | 6514 | 关闭 |
| RELP | `0.0.0.0` | 2514 | 关闭 |

全局开关也默认关闭。`0.0.0.0` 表示监听宿主的所有 IPv4 接口，已有保存配置或部署配置中的显式地址继续按原值使用。管理台 HTTPS 的端口和以上设备日志接收端口分别配置。低位端口能否绑定取决于目标操作系统与进程权限，结果以各端点的运行状态为准。

配置保存到单例 `listener_settings` 表，与 `listener.configure` 操作审计在同一 SQLite 事务中提交。版本号为十进制字符串，首次未保存为 `0`，保存新内容时递增。旧版本提交返回冲突。审计不可用时整次保存回滚。保存后，当前监听端口继续使用原先应用的配置。

在「当前运行配置」里关闭日志接收总开关，所有接收端点与已有连接随即停止。总开关关闭期间，可分别保存四种协议的启用状态，等总开关打开后再按最新参数启动。总开关打开时，协议开关只启停对应端点，关闭该协议时断开其现有连接。其他协议保持接收，已入队和已暂存的数据继续处理。切换请求在 SQLite 事务内复核管理员会话，保存启用状态并记录审计。总开关可处于开启状态且四种协议均关闭。

宿主启动时也会读取已保存配置，版本大于 `0` 时覆盖部署配置中的接收总开关和监听器字段。队列、磁盘暂存、来源配额等其他设置继续使用部署配置。没有已保存配置时使用部署基线。版本 1 控制库继续使用原部署配置，普通启动保持只读。启动时若读取配置失败，接收保持关闭，管理台继续可用以便修复。

`Ingest:ForceDisabled=true` 为最高优先级紧急关闭开关，可由受控环境变量 `Ingest__ForceDisabled=true` 设置。启用时保留已保存的接收配置，拒绝运行期开启请求，并显示 `listener_forced_disabled`。一般 `Ingest:Enabled=false` 是部署基线，启动时以已保存配置为准。

界面和 API 分别显示「已保存配置」「当前运行配置」「待应用」及各端点状态。接收配置页面订阅 SignalR，初次连接和重连时接收完整运行快照，持续更新端口状态和客户端连接错误。运行快照不会覆盖编辑中的参数。未登记设备的内存样本在整个宿主退出后清空。

## 校验与故障

- 当前管理合同要求四种协议各一项，端口为 1～65535；绑定地址必须为 IP 字面值，IPv4 映射 IPv6 地址会归一为 IPv4。
- 旧部署文件若为同一种协议配置多个监听端点，继续按原文件运行；网页返回 `legacy_listener_configuration`，提示先整理服务器配置再重启，不静默压缩或覆盖旧端点。网页当前只管理每种协议一项的配置。
- UDP 与 TCP 可以使用相同数字端口。TCP / TLS / RELP 都占用 TCP 端口；同地址族、同端口且地址相同或含通配地址时，两个启用项视为冲突。IPv6 监听采用 `DualMode=false`。
- `frameTimeoutSeconds` 为 1～300，`idleTimeoutSeconds` 为 1～86400，默认分别为 30 和 1800。报文、并发、速率和暂存仍受现有接收配额限制。
- 每个端点分别记录 `disabled/starting/listening/failed/stopped`、稳定错误码和可读诊断。某个端口占用或 TLS 证书加载失败时，其他端点继续监听。来源快照或全局存储故障按现有接收授权与故障规则处理。
- 监听状态表报告绑定等端点故障。下方的客户端错误表按来源 IP 与监听端点保留最近一次连接或分帧故障，最多 256 条。该来源在同一端点成功接收新帧后移除对应错误；重设监听端点会清除其诊断。单连接失败只结束该连接。诊断可附稳定的套接字枚举名，不回传原始报文、文件路径、异常原文或堆栈。
- 参数保存时校验配置和证书引用。启停独立保存协议的启用状态。真实端口占用、绑定权限和证书可用性在启动监听时确定，单个端点启动失败只更新该端点的异常状态。

## TLS 证书

TLS 为 Syslog over TLS，支持 TLS 1.2 / 1.3，不会在证书失败时回退明文。证书由服务器受控配置提供，网页只选择标识。配置层级为：

```text
Ingest:TlsCertificates:<id>:CertificatePath
Ingest:TlsCertificates:<id>:CertificatePassword
```

`CertificatePath` 指向服务器已有的 PKCS#12 / PFX，`CertificatePassword` 由受控私有配置或部署秘密机制提供。不要通过命令行、网页或公开样例传递真实密码。本次功能不创建、上传或部署证书。

`<id>` 最多 64 个 ASCII 字母、数字、点、横线或下划线；界面返回的可用引用还要求以字母或数字开头，且已配置非空证书路径。启用 TLS 必须选择存在的引用。SQLite、审计与 API 只保存/返回 `certificateId`，不回传私钥、密码或证书路径。原有 `Listeners` 中直接提供的 TLS PFX 配置仍可转换为进程内引用以兼容部署基线。

## RELP 确认语义

支持明文 TCP 上的 RELP 版本 0（librelp）和版本 1，包含 `open` 协商、`syslog` 事务、`rsp` 响应和 `close`。实现有界帧长度、事务编号、半包/粘包、读取和响应超时，复用来源授权、连接及速率预算；每个连接依次处理确认。TLS Syslog 必须使用已启用且发送端可访问的 TLS 监听端口。TLS 握手发到 RELP 端口时，会在该客户端的错误行中提示协议与端口不匹配。当前不支持 RELP over TLS。

已登记且启用的来源：消息先进入已有接收/解析流水线，只有 `IEventSpool.AppendAsync` 成功后才发成功 ACK。这个 ACK 表示已按当前文件暂存合同接受，不代表 ClickHouse 已入库。队列拒绝、暂存失败或确认等待超时时不发成功 ACK，发送端按自身策略保留和重试未确认事务。

未登记来源：仍遵循已确认的[内存暂存方案](未登记设备暂存.md)，许可网段内每 IP 最多 100 条、全局有界、重启清空。此时 RELP 成功 ACK 只代表进入内存样本缓存，样本可能被淘汰，不获得正式事件身份。要获得正式日志的暂存确认语义，必须先将设备登记为启用来源。

ACK 丢失或连接中断可能让发送端重传已经暂存的消息，从而出现重复。本次不增加跨连接事务去重，不承诺 exactly-once、断电零丢失或完整自动重放；文件系统目录项持久性与 ClickHouse 未知提交的原有边界仍然适用。协议参考 [librelp 官方仓库](https://github.com/rsyslog/librelp) 与 [rsyslog RELP 输入说明](https://docs.rsyslog.com/doc/configuration/modules/imrelp.html)。

## 管理 API 合同

根路径为 `/api/admin/v1/system/listeners`。GET 和 PUT 都要求有效管理员会话。PUT 还要求 HTTPS、`X-Syslog-Request: 1` 和同源请求保护，并复用每账号每分钟 20 次的管理写入限流。所有业务结果采用 `ApiResult<T>`。

GET 的 `data`：

```text
{
  revision: string,
  saved: { version: string, configuration: ListenerConfiguration },
  running: ListenerConfiguration,
  restartRequired: boolean,
  certificates: [{ id: string }],
  states: [{ protocol, address, port, state, failureCode, failureMessage }],
  connectionErrors: [{ protocol, address, port, peerAddress, failureCode, failureMessage, occurredAt }]
}
```

PUT 根路径接受 `{version, configuration}`，成功返回相同视图。`restartRequired` 表示保存配置与运行配置有差异。`PUT /reception` 接受 `{enabled, version?}` 并切换总开关。`PUT /reception/{protocol}` 接受 `{enabled, version}`，对 `udp`、`tcp`、`tls` 或 `relp` 保存开关，运行中的总开关开启时应用该协议。`version` 对应 `saved.version`，`revision` 为 HTTP 与 SignalR 共用的运行快照序号。每项 `ListenerConfiguration` 包含 `{enabled, listeners}`，监听器字段如下。

```text
{
  protocol, enabled, address, port,
  framing, certificateId,
  frameTimeoutSeconds, idleTimeoutSeconds
}
```

实际类型以 [ListenerSettingsModels.cs](../../backend/src/Syslog.Api/Models/ListenerSettingsModels.cs) 为准。

| HTTP | 错误码 | 含义 |
| --- | --- | --- |
| 400 | `invalid_request` / `invalid_listener_configuration` | 版本格式或配置字段无效 |
| 400 | `listener_port_conflict` | 启用的流式监听地址与端口冲突 |
| 400 | `listener_certificate_missing` | TLS 证书引用未配置 |
| 400 | `listener_forced_disabled` | 宿主紧急关闭设置禁止开启日志接收 |
| 400 | `version_exhausted` | 配置版本已达上限 |
| 400 | `legacy_listener_configuration` | 旧部署含同协议多个端点，需先整理服务器配置再重启 |
| 401 / 403 | `authentication_required` / `csrf_rejected` | 会话失效或请求保护失败 |
| 409 | `version_conflict` | 其他操作已保存新版本，需刷新后重试 |
| 429 | `rate_limited` | 管理写入限流 |
| 503 | `listener_migration_required` | 控制库仍为版本 1，尚未显式迁移 |
| 503 | `listener_runtime_unavailable` | 接收监听器当前无法切换 |
| 503 | `service_unavailable` | 控制库、审计等服务不可用，错误脱敏 |

## 控制库升级

独立启停与实时状态沿用现有表结构。回退到只支持总开关的旧二进制前，若总开关开启且四种协议均关闭，请先关闭总开关。若 TLS 启用状态已保存但证书标识为空，请先选定证书或关闭 TLS。旧版不能读取这两种配置组合。

新增 [002_listener_settings.sql](../../backend/src/Syslog.Api/Data/002_listener_settings.sql)，保留历史 `001_control.sql` 不变。全新显式初始化和全新首次管理员提交依次执行 `001`、`002`；已有版本 1 库的普通启动、状态检查和配置查询都不迁移。

已有版本 1 库在明确授权、备份及维护窗口后执行以下维护命令。该命令不启动 HTTP 或 Syslog 监听，连接配置必须指向获准的目标控制库：

```sh
dotnet run --project backend/src/Syslog.Api -- --migrate-listeners
```

命令验证既有结构和版本，在事务中新增表与版本记录；版本 2 重复执行只验证。未知版本、残缺结构、版本 1 中意外存在监听配置表会拒绝，不自动修复。新库版本记录保留 1、2 两条，历史账号、会话、来源和保留记录不重建。`--initialize-control` 对已有完整版本 1 库只验证，不替代迁移命令。

升级回退需先停止实例并按已授权的备份恢复流程恢复控制库与匹配程序。真实设备、证书信任链、RELP 发送端互操作和 ClickHouse 需要在获准环境分别验收。
