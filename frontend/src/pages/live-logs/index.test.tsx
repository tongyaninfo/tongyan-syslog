import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import messages from '@/locales/zh-CN';
import { getSources } from '@/services/syslog/api';
import { cache } from '../../../tests/liveLogsFixtures';
import LiveLogsPage from './index';
import { useLiveLogs } from './useLiveLogs';

vi.mock('@ant-design/pro-components', () => ({
  PageContainer: ({
    children,
    extra,
  }: {
    children: ReactNode;
    extra: ReactNode;
  }) => (
    <main>
      {extra}
      {children}
    </main>
  ),
}));
vi.mock('@umijs/max', () => ({ useIntl: () => ({ locale: 'zh-CN' }) }));
vi.mock('@/utils/i18n', () => ({
  useText: () => (key: string) => messages[key as keyof typeof messages] ?? key,
}));
vi.mock('@/services/syslog/api', () => ({ getSources: vi.fn() }));
vi.mock('./useLiveLogs', () => ({ useLiveLogs: vi.fn() }));
const apply = vi.fn();
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(getSources).mockResolvedValue([]);
  vi.mocked(useLiveLogs).mockReturnValue({
    rows: [],
    filter: { disposition: 'all' },
    paused: false,
    state: 'connected',
    error: null,
    gap: false,
    budgetLimited: true,
    cache,
    ingest: null,
    observedAt: null,
    hasOlder: true,
    olderLoading: false,
    apply,
    setPaused: vi.fn(),
    refresh: vi.fn(),
    loadOlder: vi.fn(),
    clear: vi.fn(),
  });
});
describe('实时页面筛选行为', () => {
  it('编辑条件不立即切订阅，点击应用后生效；空匹配批次仍能继续向前加载', async () => {
    render(<LiveLogsPage />);
    const keyword = screen.getByLabelText(messages['logs.keywords']);
    fireEvent.change(keyword, { target: { value: 'LINK DOWN' } });
    expect(apply).not.toHaveBeenCalled();
    expect(screen.getByText(messages['live.dirty'])).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: messages['live.loadOlder'] }),
    ).not.toBeDisabled();
    fireEvent.click(screen.getByRole('button', { name: /应用筛选/ }));
    await waitFor(() =>
      expect(apply).toHaveBeenCalledWith(
        expect.objectContaining({ keywords: 'LINK DOWN', disposition: 'all' }),
      ),
    );
    expect(apply.mock.calls[0][0].from).toBeUndefined();
    expect(window.location.search).toBe('');
  });
});
