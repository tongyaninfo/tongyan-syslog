# 接口合同

本目录维护管理 API 合同。2026-11-10 已实现首批账号会话、来源、检索和运行状态接口，并通过进程内 TestServer 验证；尚未部署或连接真实 ClickHouse。实际合同以后端 DTO 与路由为源。

后续解析器版本、告警、历史查询任务、导出、审计查询及留存删除仍在[一期实施方案](../spec/一期实施方案.md)规划中。运行和存储边界见[首批后端运行与验证](../spec/首批后端运行与验证.md)。

管理台已按本合同接入登录、总览、日志检索与详情、来源及保留设置。前端源码与合成预览不代表真实 ClickHouse 或设备接入验证，页面范围和运行说明见[前端说明](../../frontend/README.md)。

## 通用约定

实时看板新增 `/live-logs/settings`、`/live-logs/query`、`/live-logs/entries/{id}` 与 `/hubs/live-logs`，见[实时日志合同](../spec/实时日志看板.md)。

未登记设备新增三个只读管理员接口：`GET /unregistered-devices`、`GET /unregistered-devices/logs?peerAddress=...` 和 `GET /unregistered-devices/sample?peerAddress=...&sampleKey=...`（同用 `/api/admin/v1` 前缀）。设备列表全局有界，单 IP 摘要最多 100 条；原文字节只在单条样本接口返回。一键登记复用现有 `POST /sources`。完整合同、内存和权限边界见[未登记设备暂存](../spec/未登记设备暂存.md)。

- 管理 API 使用统一前缀 `/api/admin/v1`。
- 业务接口返回 `ApiResult<T>`：`success/code/message/data`，并保留真实 HTTP 状态码；成功码为 `OK`。
- 错误不泄露堆栈、连接串、密钥、内部地址、解析器内部路径或宿主命令。
- 分页使用 `pageSize/cursor`，响应 `items/nextCursor/hasMore`；没有 `total`，固定按 `receivedAt DESC,eventId DESC` 排序。
- 时间存储 UTC，传输使用带时区的 ISO 8601，界面默认按 Asia/Shanghai 展示，可显式切换；原始报文中的时间另保留原值。
- long 型 ID 使用十进制字符串；ID 由集中 ID 服务生成。
- 除登录、首次初始化状态/提交和 `/health/live` 外默认需要认证；当前业务接口只支持管理员角色。初始化提交另有本机连接与 HTTPS 限制。
- 所有 `/api` 下 POST/PUT 等非只读请求必须带 `X-Syslog-Request: 1`。有 `Origin` 时必须同源；不启用 CORS。
- 登录要求 HTTPS，使用 Secure / HttpOnly / SameSite=Strict 的 `__Host-syslog` Cookie；JSON 不返回会话 token。登录全局最多 10 次/分钟。
- 来源写入与审计同 SQLite 事务；目前更新为最后一次提交生效，尚无修订号并发冲突检测。没有删除接口。
- 原始报文只在详情的 `rawBytes` 中以 base64 JSON 字符串返回；显示正文 `message` 是解析结果，前端必须纯文本转义。列表 `rawBytes` 为空。

## 已实现接口

下表路径均相对于 `/api/admin/v1`。

| 方法 / 路径 | 请求 | 返回 |
| --- | --- | --- |
| `POST /session/login` | `username/password` | 当前用户 `{id,username,role}`，同时设置安全 Cookie |
| `POST /session/password` | `currentPassword/newPassword` | `ApiResult<bool>`，成功时 `data: true`，撤销该账号所有会话并清除当前 Cookie |
| `POST /session/logout` | 无正文，仍需请求保护头 | 撤销当前会话；审计失败不报告成功 |
| `GET /session` | 无 | 当前用户 |
| `GET /setup/status` | 匿名，只读 | `{setupRequired,canInitialize}`，故障返回 503，不伪装为待初始化 |
| `POST /setup/admin` | 本机首次提交 `username/password` | 首个管理员 `{id,username,role}`，不签发会话，完成后进入登录 |
| `GET /users` | `search/enabled/page/pageSize` | 管理员账号分页 `{items,total,page,pageSize}` |
| `POST /users` | `username/password` | 新建启用的管理员账号 |
| `PUT /users/{id}` | `username/enabled/expectedVersion` | 修改后的管理员账号 |
| `POST /users/{id}/password` | `newPassword/expectedVersion` | `ApiResult<bool>`，重置其他账号密码并撤销其全部会话 |
| `GET /sources` | 无 | 来源数组，包括禁用来源 |
| `POST /sources` | `name/peerAddress/timeZoneId/retentionDays/enabled` 和可选 `ignoredSeverities` | 保存后的来源（含字符串 `id`） |
| `PUT /sources/{id}` | 同创建 | 保存后的来源，保留期和忽略级别仅影响后续接收事件 |
| `POST /sources/{id}/logs/clear` | `{confirmation:"CLEAR"}` | `ApiResult<bool>`，确认该来源已入库日志清空时返回 `true` |
| `POST /logs/search` | 见下例 | `{items,nextCursor,hasMore}` |
| `POST /logs/clear` | `{confirmation:"CLEAR"}` | `ApiResult<bool>`，已确认清空配置指向的 ClickHouse 日志表时返回 `true` |
| `GET /logs/{id}?receivedAt=...` | 字符串 ID 和精确 UTC 毫秒接收时间 | 单个事件及原始字节，过期或不存在为 404 |
| `GET /system/status` | 无 | 接收/解析/暂存/写入计数、占用、最后写入时间及独立监听器状态 |
| `GET /system/listeners` | 无 | `{revision,saved,running,restartRequired,certificates,states,connectionErrors}`，分别报告端点状态与来源连接错误，证书仅返回标识 |
| `PUT /system/listeners` | `{version,configuration}` | 保存后的同一视图，配置与审计同事务 |
| `PUT /system/listeners/reception` | `{enabled,version?}` | 切换接收总开关后的同一视图，重新开启时应用最新保存的端点 |
| `PUT /system/listeners/reception/{protocol}` | `{enabled,version}` | 切换单个协议，返回同一视图。总开关关闭时仅保存启用状态 |
| `GET /system/https-certificate` | 无 | `{current,pending,restartRequired,loadError,canUpload}`，证书只返回 SHA-256 指纹、主题和有效期 |
| `POST /system/https-certificate` | multipart `file`（PFX/P12，最多 256 KiB）与可选 `password` | 保存证书并供新 HTTPS 连接使用，返回同一状态结构 |

`GET /health/live` 为匿名存活探针，只证明 Web 进程能响应，不能说明控制库、ClickHouse 或设备接入可用。

修改密码要求有效会话、HTTPS 和写请求保护头。账号由当前会话确定。新密码长度为 8～1024 个 UTF-16 代码单元，不能全为空白，也不能与当前密码相同。每个账号每分钟最多提交 5 次。密码更新、会话撤销与审计同事务提交，审计只记录账号和操作结果。成功后使用新密码重新登录。

来源地址必须是有效 IP，重复地址返回 409；支持 IANA 时区（例如 `Asia/Shanghai`）和 1～3650 天保留。禁止用客户端提交的来源范围替代服务端权限范围；当前管理员可以检索所有已登记来源，禁用来源只停止继续接收，历史日志仍可查询。

## 首次初始化和用户管理

初始化状态查询与普通启动不创建数据库。确实缺少控制库或受支持完整控制库没有任何账号时，需要首次初始化。结构残缺、权限错误、版本不支持和损坏返回 503，禁止自动补表。新库提交顺序执行版本 1 至版本 6 SQL，账号和审计可靠提交后才返回成功。已有版本 1 需显式迁移才能保存监听配置，已有版本 2 需显式迁移才能保存忽略级别。任意账号已存在时关闭匿名初始化，包括所有账号被停用的情况。

来源的 `ignoredSeverities` 支持多选 Syslog 严重级别 0 至 7。更新时省略该字段保留现有设置，传空数组清除设置。已登记来源中匹配规则的新报文计入忽略量，跳过磁盘暂存和入库。无有效严重级别的报文仍按现有流程处理。旧控制库保存非空规则返回 HTTP 503 `source_filter_migration_required`，可在维护窗口用 `--migrate-source-filters` 显式升级版本 2。

正式首次提交要求 HTTPS、服务器本机直连和现有写请求保护，拒绝将转发头作为本机身份。初始化入口不能通过反向代理对外开放。完成后用户正常登录，不存在系统默认管理员或默认口令。具体边界见[实施方案](../spec/用户管理与首次启动实施方案.md)与 [ADR-0002](../adr/ADR-0002-管理员配置与显式网页初始化.md)。

账号 DTO 为 `{id,username,role,enabled,createdAt,version}`，其中 `id/version` 是十进制字符串，`createdAt` 为 UTC 时间。列表默认 `page=1&pageSize=20`，每页最多 100 条，`search` 最多 64 字符，`enabled` 可省略或为布尔值。按 ID 升序分页，总数来自同一数据库读取快照。这一精确总数仅用于账号表，不改变日志检索没有精确总数的合同。

新建及改名使用 3～64 位 ASCII 字母、数字、点、横线或下划线，不区分大小写查重。历史 Unicode 用户名仍可登录，原名不变时可保存和启停。密码沿用非全空白及 8～1024 UTF-16 代码单元策略。创建账号默认启用，所有账号当前均为管理员。修改用户名或状态、重置密码会提升账号版本并撤销该账号全部会话，旧 `expectedVersion` 返回 409。完全未改变的账号保存不主动撤销会话。

账号写操作在事务中再次核对操作者有效管理员会话，并与审计一并提交。最后一个启用管理员不能停用，当前账号也不能自行停用。本人密码修改继续使用 `/session/password` 验证当前密码，不能通过管理重置入口绕过。没有用户删除、只读角色或设备组授权接口。

新增错误码如下。

| HTTP | 错误码 | 含义 |
| --- | --- | --- |
| 400 | `username_invalid` / `password_policy` | 账号或密码格式无效 |
| 403 | `setup_local_required` | 初始化必须由服务器本机直连提交 |
| 403 | `self_disable_forbidden` / `last_admin_required` | 禁止本人停用或必须保留启用管理员 |
| 403 | `self_reset_forbidden` | 本人请使用验证当前密码的修改密码入口 |
| 404 | `user_not_found` | 目标账号不存在 |
| 409 | `username_conflict` / `version_conflict` | 重名或账号已被其他操作修改 |
| 409 | `setup_already_completed` | 首位管理员已经创建 |

首次创建管理员全局每分钟最多 5 次，用户管理写入每个认证账号每分钟最多 20 次。未登录、HTTPS、CSRF、限速和服务故障继续使用已有统一错误码。密码、哈希及会话凭据不进入列表、审计或错误响应。

检索请求示例（合成条件）：

```json
{
  "from": "2026-11-09T00:00:00.000Z",
  "to": "2026-11-10T00:00:00.000Z",
  "sourceId": "42",
  "severity": 3,
  "hostname": "switch-example",
  "appName": "syslog",
  "keywords": "LINK DOWN",
  "pageSize": 100,
  "cursor": null
}
```

仅 `from/to` 为必要边界，其余过滤可省略。时间范围是 `[from,to)`，按接收时间检索，最多 30 天；端点含小于毫秒的尾数返回 400。`pageSize` 范围 1～500。关键词最多 512 字符，仅支持区分大小写的 ASCII 全词 AND，中文/非 ASCII/纯符号返回 `unsupported_search_token`，不隐式执行全表子串扫描。游标与条件、页大小及服务端来源范围绑定。列表不计算总数，查询默认最多 4 个并发；预算失败不是成功分页。

检索和详情的存储错误按原因返回。`storage_connection_failed` 表示无法连接 ClickHouse，`storage_access_denied` 表示存储拒绝认证或读取，`storage_schema_missing` 表示所需数据库或表缺失，均返回 503。超时返回 504 和 `storage_timeout`。这些错误的 `message` 为固定的脱敏说明，底层响应正文、地址及凭据不进入管理 API。

清空日志要求 HTTPS、同源保护头和有效的管理员会话，服务端在写操作意图审计时再次验证会话。操作覆盖配置指向的 `events` 表全部记录，当前筛选条件不参与。SQLite 管理数据和审计、接收队列及本地暂存独立保留。确认词错误返回 400 与 `log_clear_confirmation_required`，已有清空操作进行时返回 409 与 `log_clear_in_progress`。清空或结果审计无法确认时返回 503 与 `log_clear_outcome_unknown`，调用方应先核对结果。

按设备清空沿用确认和审计，通过已登记来源的 ID 限定范围，停用的来源也可清空。删除范围为开始执行时已入库且早于删除时间边界的目标来源日志。接收队列和本地暂存中的日志随后仍可入库。ClickHouse 应用账号需有目标表的 `ALTER DELETE` 权限，缺少权限时返回 `storage_access_denied`。删除成功后目标旧记录对查询不可见，磁盘空间由后台合并回收。

事件 DTO 见 [LogEvent](../../backend/src/Syslog.Shared/Models/LogEvent.cs)，搜索 DTO 见 [LogSearch](../../backend/src/Syslog.Shared/Models/LogSearch.cs)。`parseStatus` 为 `parsed/failed/unknown`，失败保留原文和 `failureCode`。`eventTime` 是解释后的设备时间，可空；`receivedAt` 是服务端接收时间，二者不可混用。

## 当前 UI 接入

| 页面 | 使用的接口与显示边界 |
| --- | --- |
| 登录与布局 | 会话查询、登录与退出；非只读请求携带保护头，凭据由 HttpOnly Cookie 管理，不存储 token |
| 首次配置 | 先查询初始化状态，确实待初始化才显示创建表单，服务故障提供重试且不能提交 |
| 用户管理 | 账号搜索、筛选、真实分页、创建、改名、启停及重置密码；只有管理员可访问 |
| 总览 | 订阅经管理员会话鉴权的 SignalR 完整快照；分别显示接收、解析、暂存、入库与丢弃，接收配置启用不代表设备在线 |
| 日志检索 | 来源查询、日志搜索；固定本次时间范围与条件后游标翻页，只显示本页条数和继续翻页状态，不显示虚构总数 |
| 日志详情 | 用事件 ID 与服务端原始毫秒接收时间定位；正文和原始字节安全预览，显示解析版本、编码及截断状态 |
| 日志来源 | 来源查询、新增和更新；保存失败保留输入，没有删除接口或入口 |
| 接收配置 | 查询和保存四协议设置，支持总开关和独立协议启停，通过 SignalR 更新端点状态与连接错误 |
| 日志保留 | 复用来源更新接口，确认只影响之后接收的新日志；不代表历史调整、删除或物理清理已实现 |

独立留存页提交前重新读取来源并合并最新其他字段，发现保留天数已变化时提示重新编辑。这只是客户端减少旧快照覆盖的措施，服务端仍没有修订号或原子并发冲突检测。前端不新增虚构的全局留存 API，也不为告警、导出等未实现能力添加占位导航。

生产请求始终同源且遵循本页 HTTPS 与安全 Cookie 合同；只绑定回环地址的合成预览服务是独立开发验证工具，生产不依赖它，也不为它放宽 API 安全要求。

## 接收配置合同

`/system/listeners` 及其 `reception` 子路径只允许管理员访问。PUT 要求 HTTPS 与现有同源请求保护，并在事务内复核当前会话。配置版本为十进制字符串，旧版本返回 `version_conflict`。旧控制库返回 HTTP 503 与 `listener_migration_required`，升级由显式维护命令执行。保存参数后，重新开启对应协议可独立应用其配置，重新开启总开关可应用所有启用的协议。完整字段、错误码、默认端口和 RELP 确认边界见[接收配置与 RELP](../spec/接收配置与RELP.md#管理-api-合同)，DTO 见 [ListenerSettingsModels.cs](../../backend/src/Syslog.Api/Models/ListenerSettingsModels.cs)。

接收配置通过 `/hubs/overview?view=listeners` 订阅 `ListenerRuntimeUpdated`。消息包含 `{runtime,observedAt}`，`runtime` 包含 `revision`、`saved`、`running`、`restartRequired`、`states` 与 `connectionErrors`。连接后立即发送快照，随后每秒推送一次。运行快照与 GET/PUT 响应共用可排序的十进制字符串序号 `revision`。监听订阅和首页 `sourceId` 不能在同一连接指定。

管理台证书接口要求 HTTPS 和管理员会话。上传需要 `X-Syslog-Request: 1`，跳过 Origin 比对。上传表单提交 `file` 与可选 `password`。旧客户端发送的 `host` 字段会被忽略。系统校验证书私钥、服务端用途和有效期，复核管理员会话并记录上传意图，再保存证书。密码只用于解包。浏览器在建立 HTTPS 连接时自行检查访问地址与证书是否匹配。保存成功后，Kestrel 在新 HTTPS 握手中使用更新后的证书及证书链。启动时优先读取上传证书，其次尝试上次可用证书，再使用部署默认证书或内置自签证书。显式配置的 Kestrel 端点专用证书、SNI 或 HTTP/3 仍由宿主管理。接收 TLS 可独立选择 `system-self-signed`、有效的 `system-uploaded` 或部署预配的证书引用。

## MCP 服务与管理合同

MCP 业务入口为 `/mcp`，使用官方 SDK 的 Streamable HTTP，支持 Web 宿主已配置的 HTTP 和 HTTPS。使用 `Authorization: Bearer <API_KEY>` 独立认证，不接受管理 Cookie 代替 Key。协议响应为有限 JSON 或 SSE，结构化工具结果保持 `ApiResult<T>` 字段，协议错误使用 JSON-RPC 错误。工具目录、动作权限和参数边界见 [MCP 服务需求与设计](../spec/MCP服务需求与设计.md)。

以下管理接口统一位于 `/api/admin/v1/system/mcp`，只允许有效管理员会话，写操作继续要求 HTTPS 与 `X-Syslog-Request: 1`；MCP Key 不能访问这些接口。

| 方法与相对路径 | 行为与参数 |
| --- | --- |
| `GET /` | 读取保存设置、运行/停止/停止处理中/故障状态、活动调用数、工具数和审计维护结果 |
| `PUT /enabled` | `{version, enabled}`，独立启停 MCP，不重启宿主 |
| `GET /permissions` | 返回同一业务目录中的 39 个权限叶子及模块、读写和影响标记 |
| `GET /keys` | Key 元信息，不返回原文；状态包括有效、到期、吊销和所属账号停用 |
| `POST /keys` | `{name, accessMode, permissions, expiresAt}`；模式为 `read`/`read_write`，到期为 `null` 则永久。成功 201，返回 `{key, secret}` |
| `POST /keys/{keyId}/connection-material` | 管理员主动取得当前有效 Key 的完整接入材料，响应禁止缓存；浏览器自行按当前 `window.location.origin` 拼接地址 |
| `POST /keys/{keyId}/revoke` | 吊销后不再接受后续调用，不恢复、不编辑 |
| `DELETE /keys/{keyId}` | 仅允许删除已吊销 Key，保留历史审计中的身份快照 |
| `GET /calls` | `from/to/keyId/toolName/method/peerIp/outcome/cursor/pageSize` 筛选，1～100 条游标分页，列表只返回摘要 |
| `GET /calls/{id}` | 完整应用请求头、原始正文、响应头/体、HTTP/JSON-RPC 结果及捕获完整性 |
| `PUT /audit-retention` | `{version, auditRetentionDays}`，1～3650 天，默认 30 天 |

Key 创建后无 PUT/PATCH 编辑接口。授权在发现、调用和业务写事务中校验；仅勾选父模块不构成未来工具的通配授权。调用记录按本轮用户要求不脱敏，包含应用实际收到的 Authorization、Cookie 和工具参数；这些原文不写普通应用日志。审计详情只对交互式管理员开放。

请求正文最多 1 MiB，协议响应最多 4 MiB；32 个捕获租约、16 个业务并发、每 Key 4 个并发，IP/Key 每分钟分别 240/120 个请求。预算内完整记录，超限和中断明确标记不完整。执行前审计失败不执行工具；结果补写失败返回 `mcp_audit_result_unknown` 和调用 ID，不能自动重试写操作。重启只恢复中断状态，不重放操作。

新增三表通过版本 5 显式 `--migrate-mcp` 迁移建立，旧库读取 MCP 返回 `mcp_migration_required`，不因探测或普通启动自动升级。主要管理错误为 409 `version_conflict/mcp_key_inactive/mcp_key_revoke_required/mcp_key_limit`、503 `mcp_migration_required/mcp_audit_unavailable/mcp_recovery_unavailable`；协议入口的拒绝码位于 JSON-RPC `error.data.code`，工具业务失败位于 `structuredContent.code`。

## 实时看板 SignalR 合同

端点为 `/hubs/overview`，只启用 WebSocket 传输。连接建立后立即发送 `OverviewUpdated` 完整快照，随后按 1 秒周期统一采样并推送；没有订阅者时不读取控制库或来源。多浏览器共享来源采样，最多 64 个活跃订阅，每次存储操作及发送超时为 2 秒，慢连接会被关闭。周期不是网络延迟或会话撤销延迟的硬性承诺。

消息负载由 [OverviewSnapshot](../../backend/src/Syslog.Api/Models/OverviewSnapshot.cs) 定义：`status` 沿用运行状态 DTO，`sources` 为 `{total,enabled}`，`sourceError` 为 `null` 或 `service_unavailable`，`observedAt` 为本次看板采样的 UTC ISO 时间。实际接收时间使用 `status.lastReceiveUnixMilliseconds`（UTC Unix 毫秒，当前进程尚未计数时为 0），只在计入 `received` 的报文到达时推进；接收计数包含部分已丢弃和未登记报文，不代表已入库。来源读取失败时 `sources=null`，不能显示成正常零值；控制库不能验证会话时关闭连接。

连接时可在查询参数中传入已登记来源的十进制字符串 ID（`sourceId`）。该连接的 `sourceTraffic` 仅包含对应来源的 `{sourceId,received,written,ignored}`，供速率图计算。不选来源、来源不存在或来源列表读取失败时为 `null`。全局 `status` 仍使用全局口径。来源接收量统计应用观察到且可归属的报文，写入量统计确认入库的记录，忽略量统计命中来源规则的报文，均从当前进程启动计数。

`databaseSizeBytes` 是配置指向的 ClickHouse 日志数据库中活跃 MergeTree 数据分片的磁盘占用（字节）。大小通过后台只读查询更新，订阅期间约每 30 秒刷新。尚未获取、数据库不存在或读取失败时为 `null`。

鉴权与安全边界：

- 浏览器使用当前登录的 `__Host-syslog` HttpOnly、Secure、SameSite=Strict Cookie，前端设置 `withCredentials`。不新增第二套 token，不向 URL、localStorage、推送内容或日志写入凭据。
- Hub 要求已认证的 `admin`，匿名、伪造或失效会话不能订阅。`access_token` 查询参数不被接受；只有 Authorization Bearer 也不能代替当前会话。
- 必须 HTTPS。WebSocket 的 HTTP/1.1 GET 与 HTTP/2 extended CONNECT 握手都必须携带与请求 Scheme/Host 一致的 Origin；CONNECT 仅在服务器确认扩展连接协议为 `websocket` 时接受。协商等普通写请求要求 `X-Syslog-Request: 1`，存在 Origin 时也须同源。不启用跨站 CORS 或任意订阅分组。
- 首次与每次推送前重新验证控制库中的账号、角色和会话。退出、改密、重置、改名或停用后，失效会话在后续检查中收到 `SessionExpired` 并断开；来源读取期间撤销的会话也不能收到首帧业务数据。
- 客户端只在失败后核实会话并按 1/2/5/10 秒有限重试，401 结束会话。15 秒未收到有效快照进入重连状态；断线保留最后数据并标记未更新，不使用 HTTP 定时轮询或合成数据补位。暂停会断开订阅，手动刷新只取一份完整快照后断开。

Cookie 自动随浏览器连接发送的依据见 [Microsoft SignalR 身份验证文档](https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz?view=aspnetcore-10.0)。本项目显式实现持续会话复核，不依赖长连接建立时的一次性身份结果。

HTTP/2 握手采用 CONNECT 的协议依据见 [Microsoft WebSocket 文档](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/websockets?view=aspnetcore-10.0)。不把合法浏览器握手误判为缺少自定义写请求头，也不将任意 CONNECT 放行。

## 状态和错误

| HTTP | 使用场景 |
| --- | --- |
| 200 | 首批接口的查询、创建或更新成功 |
| 400 | 参数或业务校验失败 |
| 401 | 未认证 |
| 403 | 无权限、CSRF 或作用范围失败 |
| 404 | 不存在或不可见 |
| 409 | 来源 IP、用户名或配置版本冲突 |
| 429 | 限速 |
| 503 | 控制库、审计或存储失败，响应不回显底层异常 |
| 504 | ClickHouse 请求超时 |

当前错误码包括 `authentication_required`、`csrf_rejected`、`https_required`、`invalid_request`、`invalid_query`、`unsupported_search_token`、`source_forbidden`、`storage_timeout`、`storage_connection_failed`、`storage_access_denied`、`storage_schema_missing`、`storage_unavailable`、`storage_reconciliation_required`、`rate_limited`、`service_unavailable`；控制服务另有 `AUTH_FAILED/INVALID_INPUT/FORBIDDEN/NOT_FOUND/CONFLICT`。客户端应按实际码与 HTTP 状态处理，不假设所有错误为同一前缀。

改密校验失败使用 HTTP 400，代码为 `current_password_invalid`（当前密码错误）、`password_policy`（必填、长度或空白校验）或 `password_unchanged`（新旧密码相同）。会话失效仍使用 HTTP 401 和 `authentication_required`。

## 后续规划接口分组

| 分组 | 内容 |
| --- | --- |
| 认证与用户 | 细分角色及来源范围授权；管理员账号管理与重置密码已纳入当前接口 |
| 运行状态 | 接收速率、解析成功率、队列深度、写入与丢弃、存储与保留状态 |
| 日志检索 | 时间范围、来源、级别、主机、应用、关键字、结构化字段查询与详情 |
| 来源与监听器 | UDP / TCP / TLS / RELP 配置支持通过接收总开关运行期应用。更多动态监听与精细来源策略另行规划 |
| 解析器 | 列表、详情、观察/生效、测试、回放、导入与回滚 |
| 告警 | 规则、静默、冷却、通知渠道、命中与通知状态 |
| 留存与存储 | 保留期、容量、轮转、导出、备份与删除 |
| 审计 | 操作审计查询与脱敏导出 |
| HTTPS | 管理证书与重启（按方案） |

一期不提供依据日志内容执行封禁或阻断的接口；与家族产品的联动仅限事件接收与查询。

## 合同链

后端 DTO 和路由是合同来源。当前 [前端类型](../../frontend/src/services/syslog/types.ts)与[同源请求封装](../../frontend/src/services/syslog/api.ts)按实际 DTO 维护，尚未建立 OpenAPI 导出与前端自动生成链；以下为后续目标：

    后端 DTO/路由
      → 合同导出
      → 前端类型与服务封装
      → 合同漂移检查

合同链建立前，不手改生成文件，不把模板 mock 当作业务合同。原始报文、结构化字段与解析状态的字段命名和截断语义必须在合同文档中固定，前端据此展示。

入口见[文档中心](../README.md)。
