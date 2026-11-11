# 参与贡献

感谢参与通研 Syslog。本文说明开发环境、代码约定、提交要求与脱敏边界。

## 开发环境

- **.NET SDK** 10.0.x（根 `global.json` 固定为 10.0.300，可用 `rollForward` 使用更新的补丁版本）
- **Node.js** 22 或更高版本（`frontend/package.json` 要求 `>=22.0.0`）
- **ClickHouse** 26.x（可选；只有验证真实日志存储与检索时才需要）
- **SQLite** 由应用内嵌，无需单独安装

## 本地验证

提交前请在仓库根目录运行与改动范围对应的检查：

```sh
# 文档：Markdown 本地相对链接
node scripts/check-docs.mjs

# 后端：构建与测试
dotnet build backend/src/Syslog.Api/Syslog.Api.csproj
dotnet test backend/tests/Syslog.Engine.Tests/Syslog.Engine.Tests.csproj
dotnet test backend/tests/Syslog.Ingest.Tests/Syslog.Ingest.Tests.csproj
dotnet test backend/tests/Syslog.Api.Tests/Syslog.Api.Tests.csproj

# 前端：类型与 lint、单元测试、构建
cd frontend
npm ci --ignore-scripts
npm run setup
npm run lint
npm run test
npm run build
```

没有实际运行的检查不要写成通过；被环境阻塞时注明原因与未验证范围。

## 代码约定

- 后端：Controller 只处理 HTTP 参数、状态码与服务调用；业务逻辑放服务实现。接口统一返回 `ApiResult<T>` 并保留真实 HTTP 状态码。长整型 ID 在 JSON 中以十进制字符串传输，时间存 UTC。
- 后端：日志存储写 ClickHouse、管理数据写 SQLite，两者不共享事务；预期业务失败不抛异常。
- 前端：路由在 `frontend/config/routes.ts`、页面在 `frontend/src/pages/`、请求封装在 `frontend/src/services/syslog/`、国际化在 `frontend/src/locales/`。前端不承担访问控制，权限以服务端校验为准。
- 日志与原始报文按纯文本转义渲染，不执行 HTML 或脚本；大字段按服务端上限截断并显示标记。
- 新增依赖、升级版本、修改公共组件或数据库结构前先说明范围与影响，不在无关改动中顺手重构。

## 提交信息

格式：

```
type(scope): 描述
```

- `type` 限 `feat`、`fix`、`docs`、`style`、`refactor`、`perf`、`test`、`chore`、`ci`、`revert`、`merge`。
- `type` 后紧跟括号与冒号，冒号后一个空格；描述使用中文，1～50 字。
- `scope` 建议使用模块名，例如 `api`、`ingest`、`engine`、`ui`、`docs`、`deploy`。

示例：`fix(ingest): 修正 RELP 半包重连后的帧偏移`。

## 提交前检查

1. 只涉及本次改动，不夹带无关格式化或依赖升级。
2. 不提交 `bin/`、`obj/`、`node_modules/`、`dist/`、运行数据、数据库、证书与备份。
3. 不提交真实主机、内网地址、账号、口令、Token、私钥、真实日志或可定位个人的截图。
4. 截图使用合成数据，来源地址使用文档保留网段（`192.0.2.0/24`、`198.51.100.0/24`、`203.0.113.0/24`、`2001:db8::/32`）。
5. 文档改动同步更新相关链接与状态标注，不留失效链接。

## 拉取请求

- 从 `main` 创建分支，保持单一主题；说明改动范围、验证命令与结果、未验证边界。
- 涉及行为变化时补充或更新测试；涉及接口变化时同步更新 `docs/api/` 与前端合同。
- 涉及安全敏感区域（认证、会话、证书、密钥、MCP 权限、日志清空）时说明威胁边界与测试方式。
- 评审意见处理完成后再请求合并；合并由维护者执行。

## 安全问题

不要通过公开 Issue 或 Pull Request 披露漏洞细节，请按 [SECURITY.md](SECURITY.md) 的流程报告。
