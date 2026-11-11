import { act, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import messages from '@/locales/zh-CN';
import { getLog } from '@/services/syslog/api';
import { liveRow } from '../../../tests/liveLogsFixtures';
import LogDetailDrawer from '../logs/components/LogDetailDrawer';

vi.mock('@/services/syslog/api', () => ({ getLog: vi.fn() }));
vi.mock('@umijs/max', () => ({ useIntl: () => ({ locale: 'zh-CN' }) }));
vi.mock('@/utils/i18n', () => ({
  useText: () => (key: string) => messages[key as keyof typeof messages] ?? key,
}));
describe('实时缓存证据详情', () => {
  it('同毫秒忽略日志共享 eventId=0 时，切换缓存身份不能闪现前一条详情', async () => {
    const first = liveRow('1', 'ignored');
    const second = liveRow('2', 'ignored');
    let resolve: (value: typeof first.log) => void = () => undefined;
    const load = vi
      .fn()
      .mockResolvedValueOnce(first.log)
      .mockImplementationOnce(
        () =>
          new Promise((done) => {
            resolve = done;
          }),
      );
    const view = render(
      <LogDetailDrawer
        selected={first.log}
        onClose={vi.fn()}
        cacheDetail={{ id: first.id, load }}
      />,
    );
    expect(await screen.findByText(first.log.message)).toBeInTheDocument();
    view.rerender(
      <LogDetailDrawer
        selected={second.log}
        onClose={vi.fn()}
        cacheDetail={{ id: second.id, load }}
      />,
    );
    expect(screen.queryByText(first.log.message)).not.toBeInTheDocument();
    await act(async () => resolve(second.log));
    expect(await screen.findByText(second.log.message)).toBeInTheDocument();
    expect(screen.getByText(second.id)).toBeInTheDocument();
    expect(
      screen.queryByText(messages['logs.expiresAt']),
    ).not.toBeInTheDocument();
  });
  it('忽略日志使用独立缓存身份取详情，不以 eventId=0 查询 ClickHouse；报文只显示文本', async () => {
    const record = liveRow('2', 'ignored');
    const text = '<script>window.bad=true</script>';
    const load = vi
      .fn()
      .mockResolvedValue({ ...record.log, rawBytes: btoa(text) });
    render(
      <LogDetailDrawer
        selected={record.log}
        onClose={vi.fn()}
        cacheDetail={{ id: record.id, load }}
      />,
    );
    await waitFor(() =>
      expect(load).toHaveBeenCalledWith('epoch-a:2', expect.any(AbortSignal)),
    );
    expect(await screen.findByText(text)).toBeInTheDocument();
    expect(document.querySelector('script')).toBeNull();
    expect(getLog).not.toHaveBeenCalled();
  });
});
