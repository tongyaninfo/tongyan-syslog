import { defineConfig } from '@umijs/max';
import settings from './defaultSettings';
import routes from './routes';

// 基于 Ant Design Pro 6.0.3 的必要扩展点；构建不执行 Git、部署或遥测动作。
export default defineConfig({
  title: '通研 Syslog',
  hash: true,
  // 隔离验证可以指定独立输出目录，避免构建工具清理既有产物。
  outputPath: process.env.SYSLOG_BUILD_OUTPUT_PATH || 'dist',
  // 为各异步块隔离压缩辅助函数，避免复用后的变量名在运行时冲突。
  esbuildMinifyIIFE: true,
  publicPath: '/',
  routes,
  model: {},
  initialState: {},
  access: {},
  layout: { locale: true, ...settings },
  locale: { default: 'zh-CN', antd: true, baseNavigator: false },
  antd: {
    appConfig: {},
    configProvider: {
      variant: 'filled',
      theme: { token: { colorPrimary: '#1849a9', borderRadius: 8 } },
    },
  },
  request: {},
  mock: false,
  favicons: ['/assets/brand/tyinfo-logo-transparent.png'],
  define: { __APP_VERSION__: '0.1.0' },
});
