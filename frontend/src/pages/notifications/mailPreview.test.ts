import { expect, it } from 'vitest';
import { safeMailPreview } from './mailPreview';

it('保留表格和标题，将外部资源及交互内容移出预览', () => {
  const preview = safeMailPreview(`
    <h2 style="color:blue" onclick="noop()">日志日报</h2>
    <table><tr><th colspan="2">合计</th></tr></table>
    <img src="https://example.test/image.png">
    <a href="https://example.test/link">Link</a>
    <script src="https://example.test/script.js"></script>
    <iframe src="https://example.test/frame"></iframe>
  `);
  expect(preview).toContain('<h2 style="color:blue">日志日报</h2>');
  expect(preview).toContain('<th colspan="2">合计</th>');
  expect(preview).toContain("default-src 'none'");
  expect(preview).not.toMatch(/example\.test|onclick|<script|<iframe/);
});
