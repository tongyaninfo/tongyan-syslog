// 公开的合成账号。预览使用 HTTP，非示例输入应留在浏览器内。
export const PREVIEW_USERNAME = 'preview';
export const PREVIEW_PASSWORD = 'preview-only';
export const PREVIEW_NEW_PASSWORD = 'synthetic-preview-example';

export function isSyntheticPreview(): boolean {
  return Boolean(
    document.querySelector('meta[name="syslog-synthetic-preview"]'),
  );
}
