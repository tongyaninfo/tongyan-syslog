# 前端开发规则

本文件补充根 [AGENTS.md](../AGENTS.md)，适用于 `frontend/`。管理台已建立 React 19 + Umi Max + Ant Design 6 源码，沿用 Ant Design Pro 6.0.3 模板（MIT）的最小工程基线；以下规则在模板约定之上补充项目差异。当前页面与运行边界见 [README](README.md)。

## 工具链

- 使用 `npm` 与 `package-lock.json`，不引入 yarn / pnpm 锁文件；依赖升级单独提交并说明原因。
- Biome 是唯一 lint / format 工具，不使用 ESLint 或 Prettier；提交前必须通过 `npm run lint`（Biome + `tsc --noEmit`）。
- 写 antd 组件前先用 `npx antd info <Component>` 查 API，不凭记忆猜属性。
- 首次安装使用 `npm install --ignore-scripts`，再执行 `npm run setup` 生成 Umi 文件；锁文件复现使用 `npm ci --ignore-scripts`。不添加 Git hooks 或会操作 Git 的安装脚本，不安装全局依赖。
- 实际命令以 `package.json` 为准，当前含 `setup/lint/test/build/build:backend/preview:synthetic`；当前没有 `simple` 模板裁剪脚本。删除模板或其他文件仍须取得用户明确确认。

## 结构约定

- 路由统一写在 `config/routes.ts`；路由 `name` 对应 `src/locales/*/menu.ts` 的 i18n key，权限用 `access` 字段控制展示。
- 页面放 `src/pages/<模块>/`，请求封装放 `src/services/<模块>/`，可复用组件放 `src/components/`；不为单页需求提前抽公共包。
- 当前 Syslog 类型与请求封装位于 `src/services/syslog/`，依照后端 DTO 维护，尚无自动生成链。若后续引入 `src/services/ant-design-pro/` 模板生成目录，不手改；建立 Syslog 合同生成链后按合同生成并逐字校验。
- 全局运行时逻辑在 `src/app.tsx`，权限判定在 `src/access.ts`，错误处理在 `src/requestErrorConfig.ts`；修改时保持模板扩展点，不复制一套并行机制。

## Syslog 业务约定

- 管理台只访问本项目同源 API，不直连第三方服务、不内嵌其他系统页面。
- 前端权限只影响展示；接口鉴权、日志范围与导出授权以服务端为准，不能靠隐藏按钮代替。
- 日志正文与原始报文按纯文本渲染（转义 HTML、不使用 `dangerouslySetInnerHTML`），不执行其中的脚本、链接或富文本；超长内容按服务端上限截断并显示截断标记。
- 检索结果显示实际时间范围、当前页条数及游标前后页状态。当前 API 只返回 `items/nextCursor/hasMore`，没有精确命中总数；不能伪造总数或总页数，不把「本页条数」当作「总数」，不做无上限全量拉取。后续增加总数必须先建立对应服务端合同。
- 解析状态（已解析 / 解析失败 / 未匹配解析器）与解析器版本必须可见；原始报文与结构化字段不一致时以原文为准并注明。
- 接收、解析、入库与丢弃是不同状态：界面分别展示接收速率、解析失败率、队列深度、写入失败与丢弃数，不把接收到表述为已记录。
- 告警页面区分「规则命中」「已通知」与「通知失败」；静默与冷却状态可见，不把命中直接显示为已送达。
- 导出、删除日志、变更保留期、导入解析器、修改监听器属于危险操作，需要二次确认并显示影响范围。
- 不把 Token、Cookie、密钥、日志导出内容或原始报文写入 `localStorage`、URL 或前端日志；错误提示不展示内部地址与堆栈。
- 单次查询最长 30 天，关键词仅支持区分大小写的 ASCII 全词 AND；不能用前端静默扩展为中文、子串或全量扫描。来源保留变更只影响新日志，过期查询不可见不等于物理清理完成。
- 生产管理台使用同源 HTTPS、Secure / HttpOnly / SameSite Cookie 与服务端来源校验。`preview:synthetic` 默认监听 `127.0.0.1:8019`，获明确授权后可设置 `SYSLOG_PREVIEW_HOST=0.0.0.0` 供局域网查看合成数据。预览保留主机与写请求来源校验，场景切换仅限回环访问。
- `build:backend` 复制构建产物但不删除旧 `wwwroot` 文件；历史哈希资源回收属于单独需授权的删除操作，不在构建时自动执行。

## 国际化与品牌

- 默认语言为简体中文。新增和修改的界面文案同步维护简体中文、繁體中文、日本語、한국어、Русский、English 六份字典，原始日志和来源名称按实际数据展示。
- 品牌资源统一放 `public/`（通研技术 Logo 位于 `assets/brand/`），页面文案与页脚保持「通研 Syslog」「通研技术」口径，不混用模板品牌。
- 界面沿用通研统一管理台的蓝色主色、浅底白卡和紧凑布局；不引入参考项目的 AI、地图、外网字体或业务服务，不修改参考仓库，不为尚未实现能力添加占位导航。

## 验证

- 前端改动至少运行 `npm run lint` 与 `npm run test`；页面或构建配置改动再运行 `npm run build`。
- 涉及真实页面时在浏览器中验证主要视口、空态、错误态与权限差异，重点检查原始报文转义、截断标记与大字段性能；只通过类型检查或 mock 不能视为业务验收。
- 未安装依赖、未运行构建或未做浏览器验证时，在交付说明中如实标注，不编造结果。
