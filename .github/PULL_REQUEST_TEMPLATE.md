## 改动说明

<!-- 说明改动的目的、范围和影响。 -->

## 涉及范围

- [ ] 后端（`backend/`）
- [ ] 前端管理台（`frontend/`）
- [ ] 部署与配置（`deploy/`）
- [ ] 文档（`docs/`、`README.md`、`CHANGELOG.md`）
- [ ] 脚本与 CI（`scripts/`、`.github/`）

## 验证

<!-- 列出实际运行的命令与结果；未运行的检查请注明原因。 -->

```sh
# 示例
dotnet build backend/Syslog.slnx
dotnet test backend/Syslog.slnx
node scripts/check-docs.mjs
node scripts/scan-open-source-sensitive.mjs --root .
```

## 安全与脱敏

- [ ] 未包含真实主机、内网地址、账号、口令、Token、私钥或真实日志
- [ ] 截图与示例数据为合成内容，来源地址使用文档保留网段
- [ ] 涉及认证、会话、证书、密钥、MCP 权限或日志清空的改动已说明威胁边界

## 未验证项

<!-- 例如：未在真实 ClickHouse、真实设备、浏览器或目标环境验证。 -->
