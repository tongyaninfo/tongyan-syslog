import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import messages from '@/locales/zh-CN/logs';
import { TEXT_PREVIEW_LIMIT } from '../helpers';
import EvidencePreview, { LimitedText } from './EvidencePreview';

vi.mock('@umijs/max', () => ({ useIntl: () => ({ locale: 'zh-CN' }) }));
vi.mock('@/utils/i18n', () => ({
  useText: () => (key: string, values?: Record<string, string | number>) =>
    (messages[key as keyof typeof messages] ?? key).replace(
      /\{(\w+)\}/g,
      (_, name: string) => String(values?.[name] ?? ''),
    ),
}));

describe('日志正文安全渲染', () => {
  it('原文中的脚本与事件属性只作为文字出现', () => {
    const malicious = '<img src=x onerror="alert(1)"><script>alert(2)</script>';
    const { container } = render(<EvidencePreview base64={btoa(malicious)} />);
    expect(screen.getByText(malicious)).toBeInTheDocument();
    expect(container.querySelector('img')).toBeNull();
    expect(container.querySelector('script')).toBeNull();
  });

  it('大字段展示截断标记，DOM 不持有超出预览上限的全文', () => {
    const value = 'A'.repeat(TEXT_PREVIEW_LIMIT + 100);
    const { container } = render(<LimitedText text={value} />);
    expect(screen.getByText(/显示内容已截断/)).toBeInTheDocument();
    expect(container.querySelector('pre')?.textContent).toHaveLength(
      TEXT_PREVIEW_LIMIT,
    );
  });

  it('无效原始字节显示编码差异提示', () => {
    render(<EvidencePreview base64={btoa(String.fromCharCode(255, 0, 65))} />);
    expect(screen.getByText(/原文含非 UTF-8 字节/)).toBeInTheDocument();
  });
});
