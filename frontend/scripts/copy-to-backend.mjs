import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const frontend = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '..',
);
const source = path.join(frontend, 'dist');
const destination = path.resolve(frontend, '../backend/src/Syslog.Api/wwwroot');
await fs.access(path.join(source, 'index.html'));
// 只复制当前产物，不递归删除既有wwwroot；历史哈希文件的回收由已授权维护动作处理。
await fs.mkdir(destination, { recursive: true });
await fs.cp(source, destination, {
  recursive: true,
  force: true,
  filter: (file) => !file.endsWith('.map'),
});
console.log(
  'Copied frontend build to backend wwwroot. Existing files were not deleted.',
);
