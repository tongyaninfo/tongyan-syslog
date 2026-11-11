# Docker 部署

本目录提供容器部署资产：Dockerfile、单机 Compose 模板与生产配置模板。示例中的镜像仓库、标签、部署目录均为占位符；主机地址、口令与证书只存在于部署方自己的受控环境，不写入仓库。

## 资产

| 文件 | 用途 |
| --- | --- |
| [Dockerfile](Dockerfile) | 基于 `mcr.microsoft.com/dotnet/aspnet:10.0` 的运行时镜像；构建上下文是 `dotnet publish` 产物，镜像内不编译 |
| [compose.example.yaml](compose.example.yaml) | 单机 Compose：`api` + 单节点 ClickHouse，端口映射、数据卷、SELinux `:Z` 与健康检查 |
| [appsettings.Production.example.json](appsettings.Production.example.json) | 生产配置模板：HTTPS 绑定、监听器、ClickHouse 与暂存路径 |

## 构建

```sh
cd frontend && npm run build:backend
cd .. && dotnet publish backend/src/Syslog.Api/Syslog.Api.csproj -c Release -r linux-x64 --self-contained false -o /tmp/syslog-publish
cp deploy/docker/Dockerfile /tmp/syslog-publish/
docker build --platform linux/amd64 -t <harbor>/syslog/api:<tag> /tmp/syslog-publish
```

镜像不含 .NET SDK、Node.js、SQLite 数据、证书或任何口令；构建上下文只含发布产物。

## 部署要点

1. 宿主目录用 `/home/<app>/{compose.yaml,config,data}`，`data` 与 `config/certs` 绑定宿主目录并带 SELinux `:Z`。
2. 低端口 514 由非 root 的 `app` 用户（uid 1654）绑定，Compose 需设置 `sysctls: net.ipv4.ip_unprivileged_port_start=0`。
3. 控制库不自动建库：普通启动只读；新数据目录必须先显式执行一次
   `dotnet Syslog.Api.dll --initialize-control --bootstrap-admin`（可用 `docker run --rm -i`）。
4. 挂载的 PFX 是管理 HTTPS 的启动和回退证书，也可独立用于 Syslog TLS。口令通过环境变量注入，不写入 JSON 或仓库。
5. ClickHouse 首次连接时由应用创建缺失库表；`default` 用户在建好 `syslog_app` 后被禁用，健康检查与应用都必须使用 `syslog_app`。
6. 按设备清空需要 ClickHouse 应用账号拥有日志表的 `ALTER DELETE` 权限。全量清空继续使用 `TRUNCATE` 权限。

## SMTP 授权码存储

新保存的 SMTP 授权码与邮件配置一起存入 SQLite 控制库，保留数据库卷即可。SQLite 文件及其备份包含授权码明文，应由受信账号访问。升级时，原密钥目录只用于读取此前加密的授权码。若旧密钥已丢失，在通知设置中重新输入授权码并保存即可。

## 在管理台更新 HTTPS 证书

新版镜像通过 `HttpsCertificate__Directory` 将网页上传的证书保存在 `/app/data/control/https-certificate`。现有 Compose 的 `/app/data` 挂载已覆盖该路径。宿主的 `data/api/control` 必须允许容器内的 `app` 用户写入，证书子目录和证书文件分别以 `0700` 和 `0600` 创建。

管理员在「HTTPS 证书」上传含私钥的 PFX 或 P12 后，新建 HTTPS 连接自动使用新证书和中间证书链。已建立的连接保持原证书直到断开。容器重启或重建后从持久卷加载证书，候选失效时尝试上次可用证书，最终使用配置的启动证书。Kestrel 显式配置的端点专用证书、SNI 或 HTTP/3 仍由部署侧管理。

管理台可通过任意域名或 IP 访问，无需配置 HTTP Host 白名单。升级后，旧配置中残留的 `AllowedHosts` 不再限制管理台访问。浏览器仍会验证证书是否涵盖访问地址；如果证书只覆盖域名，请通过该域名访问。

配置了服务器证书引用的 Syslog TLS 监听与管理 HTTPS 独立。若 Syslog TLS 已选用「已上传证书」，关闭再开启日志接收总开关可让该监听加载新的证书。

## 端口

| 端口 | 协议 | 绑定 | 说明 |
| --- | --- | --- | --- |
| 514 | UDP | 配置决定 | Syslog 接收（数据报边界） |
| 514 | TCP | 配置决定 | Syslog 接收（自动识别 octet-counting / newline 分帧） |
| 6514 | TCP/TLS | 配置决定 | Syslog over TLS；客户端需信任服务器证书 |
| 2514 | TCP | 配置决定 | RELP |
| 7443 | HTTPS | 配置决定 | 管理台与 API |

示例 Compose 将上述端口映射到宿主 `0.0.0.0` 仅用于演示。实际放行范围、绑定地址与来源限制必须按环境单独确认，默认不对公网开放。

## 升级流程（控制库结构变更时）

```sh
cd /home/<app>
docker compose -p syslog -f compose.yaml stop api
tar -czf backups/pre-upgrade-$(date +%Y%m%d-%H%M).tar.gz config data/api
docker run --rm -i --user app \
  -v /home/<app>/config/appsettings.Production.json:/app/appsettings.Production.json:ro,Z \
  -v /home/<app>/config/certs:/app/certs:ro,Z \
  -v /home/<app>/data/api:/app/data:Z \
  <harbor>/syslog/api:<new-tag> dotnet Syslog.Api.dll --migrate-notifications
# 修改 compose.yaml 的 api 镜像 tag 后：
docker compose -p syslog -f compose.yaml up -d
```

迁移命令只建缺失结构，报告 `Control maintenance completed.`；旧镜像保留以便回滚。控制库版本可在宿主用 `python3` 读取 `schema_versions` 复核。

## 部署验证清单（占位）

在目标环境完成部署后，按以下顺序核对并记录结果；未实际执行的项不得标记为通过：

- 四个监听器状态均为 `listening`，容器内 `/proc/net` 与实际端口绑定一致。
- 从管理网段发送合成 UDP/TCP/TLS 报文，确认入库并可在「日志检索」查到；TLS 握手使用目标证书。
- 管理台通过目标域名或地址返回 200；确认证书覆盖访问地址。
- 核对 ClickHouse 写入与待写入指标、SQLite 控制库备份可用。
- 记录未验证边界：真实设备、长期运行、吞吐/延迟、证书信任链、备份恢复、ClickHouse 真库性能。

## 边界

- 保留期到期只隐藏查询结果，当前版本没有物理清理任务，磁盘不会自动释放。
- UDP 无重传保证，队列/暂存满时丢弃并计数，不能把「已接收」描述为「已完整记录」。
- 首次初始化必须显式执行；普通启动不建表、不迁移。ClickHouse 既有表的结构迁移仍需维护操作。
- 端口放行、转发与防火墙修改按环境单独确认，部署脚本不自动改宿主防火墙。
