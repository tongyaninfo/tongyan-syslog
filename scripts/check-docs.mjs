import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// 只检查公开仓库文档的本地链接；不读取私有台账、运行数据、依赖或 Git 状态。
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const excluded = new Set(['.git', 'node_modules', 'bin', 'obj', 'TestResults', 'run-data', 'App_Data', 'data', 'spool', '.local', 'dist']);
const privateNames = new Set(['交付服务器台账.md', '服务器交付台账.md']);
const documents = [];
function collect(directory) {
  for (const item of fs.readdirSync(directory, { withFileTypes: true })) {
    if (excluded.has(item.name) || privateNames.has(item.name) || item.isSymbolicLink()) continue;
    const full = path.join(directory, item.name);
    if (item.isDirectory()) collect(full);
    else if (item.name.endsWith('.md')) documents.push(full);
  }
}
collect(root);
let checked = 0;
const failures = [];
for (const file of documents) {
  const content = fs.readFileSync(file, 'utf8').replace(/```[\s\S]*?```/g, '');
  for (const match of content.matchAll(/!?\[[^\]\n]*\]\((<[^>]+>|[^\s)]+)(?:\s+"[^"]*")?\)/g)) {
    const target = match[1].replace(/^<|>$/g, '').split('#')[0];
    if (!target || /^[a-z][a-z\d+.-]*:/i.test(target) || target.startsWith('//')) continue;
    let decoded;
    try { decoded = decodeURIComponent(target); } catch { failures.push(`${path.relative(root, file)}: invalid URL ${target}`); continue; }
    if (privateNames.has(path.basename(decoded))) continue;
    const resolved = path.resolve(path.dirname(file), decoded);
    if (!resolved.startsWith(root + path.sep)) { failures.push(`${path.relative(root, file)}: outside repository ${target}`); continue; }
    checked++;
    if (!fs.existsSync(resolved)) failures.push(`${path.relative(root, file)}: missing ${target}`);
  }
}
for (const failure of failures) console.error(failure);
console.log(`Checked ${documents.length} Markdown files and ${checked} local links; ${failures.length} failures. Anchors and external URLs are not checked.`);
process.exitCode = failures.length ? 1 : 0;
