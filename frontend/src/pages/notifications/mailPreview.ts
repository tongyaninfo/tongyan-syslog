const tags = new Set([
  'B',
  'BLOCKQUOTE',
  'BR',
  'CAPTION',
  'CODE',
  'DIV',
  'EM',
  'H1',
  'H2',
  'H3',
  'H4',
  'HR',
  'I',
  'LI',
  'OL',
  'P',
  'PRE',
  'SMALL',
  'SPAN',
  'STRONG',
  'TABLE',
  'TBODY',
  'TD',
  'TFOOT',
  'TH',
  'THEAD',
  'TR',
  'UL',
]);
const attributes = new Set([
  'align',
  'aria-label',
  'bgcolor',
  'cellpadding',
  'cellspacing',
  'colspan',
  'role',
  'rowspan',
  'scope',
  'style',
  'valign',
  'width',
]);

/** 先在惰性模板中过滤，再交给隔离的 iframe，保留邮件排版而不加载外部内容。 */
export function safeMailPreview(html: string) {
  const template = document.createElement('template');
  template.innerHTML = html;
  for (const element of template.content.querySelectorAll('*')) {
    if (!tags.has(element.tagName)) {
      element.remove();
      continue;
    }
    for (const { name } of [...element.attributes]) {
      if (!attributes.has(name)) element.removeAttribute(name);
    }
  }

  return `<!doctype html><html><head><meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'">
<style>body{font:14px/1.6 system-ui,sans-serif;color:#17233c;margin:0;overflow-wrap:anywhere}pre{white-space:pre-wrap}</style>
</head><body>${template.innerHTML}</body></html>`;
}
