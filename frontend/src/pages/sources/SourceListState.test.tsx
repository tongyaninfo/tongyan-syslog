import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import sourceMessages from '@/locales/zh-CN';
import { ApiError } from '@/requestErrorConfig';
import { clearSourceLogs, getSources } from '@/services/syslog/api';
import type { LogSource } from '@/services/syslog/types';
import RetentionPage from '../retention';
import SourcesPage from './index';

vi.mock('@/services/syslog/api', () => ({
  getSources: vi.fn(),
  createSource: vi.fn(),
  clearSourceLogs: vi.fn(),
  updateSource: vi.fn(),
}));
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
vi.mock('@/utils/i18n', () => ({
  useText: () => (key: string, values?: Record<string, string | number>) => {
    let value = (sourceMessages as Record<string, string>)[key] ?? key;
    for (const [name, replacement] of Object.entries(values ?? {}))
      value = value.replaceAll(`{${name}}`, String(replacement));
    return value;
  },
}));

const source: LogSource = {
  id: '42',
  name: '合成交换机',
  peerAddress: '192.0.2.10',
  timeZoneId: 'Asia/Shanghai',
  retentionDays: 365,
  enabled: true,
};

beforeEach(() => {
  vi.mocked(getSources).mockReset();
  vi.mocked(clearSourceLogs).mockReset();
});

it('只清空选中设备的历史日志，并在确认框显示设备名和地址', async () => {
  const other = {
    ...source,
    id: '43',
    name: '另一台交换机',
    peerAddress: '192.0.2.11',
  };
  vi.mocked(getSources).mockResolvedValue([source, other]);
  vi.mocked(clearSourceLogs).mockResolvedValue(undefined);
  render(<SourcesPage />);

  const selected = within(
    await screen.findByRole('row', { name: /合成交换机/ }),
  );
  fireEvent.click(selected.getByRole('button', { name: '清空日志' }));
  const dialog = within(await screen.findByRole('dialog'));
  expect(
    dialog.getByText(/「合成交换机」（192\.0\.2\.10）/),
  ).toBeInTheDocument();
  expect(dialog.queryByText(/另一台交换机/)).not.toBeInTheDocument();
  const submit = dialog.getByRole('button', { name: '确认清空' });
  expect(submit).toBeDisabled();
  fireEvent.change(dialog.getByRole('textbox', { name: '请输入 CLEAR 确认' }), {
    target: { value: 'CLEAR' },
  });
  fireEvent.click(submit);
  await screen.findByText('已清空「合成交换机」的历史日志。');
  expect(clearSourceLogs).toHaveBeenCalledExactlyOnceWith(source.id, 'CLEAR');
});

async function refresh() {
  const button = screen.getByRole('button', { name: /刷\s*新$/ });
  // AntD 按钮在列表完成渲染后同步内部 loading 状态，再允许下一次交互。
  await waitFor(() => expect(button).not.toHaveClass('ant-btn-loading'));
  fireEvent.click(button);
}

describe.each([
  ['日志来源', SourcesPage, '尚未登记日志来源'],
  ['日志保留', RetentionPage, '暂无可配置保留期限的来源'],
] as const)('%s 加载状态', (_name, Page, emptyMessage) => {
  it('首次失败显示未知数量和重试，重试成功才显示已确认的空集合', async () => {
    vi.mocked(getSources)
      .mockRejectedValueOnce(new ApiError('service_unavailable', 503))
      .mockResolvedValueOnce([]);
    render(<Page />);

    await screen.findByText('来源加载失败，暂不可用');
    expect(screen.getByText('来源数量 —')).toBeInTheDocument();
    expect(screen.queryByText(emptyMessage)).not.toBeInTheDocument();
    expect(screen.queryByText(/共 0 个来源/)).not.toBeInTheDocument();
    expect(
      screen.queryByText('当前显示上次成功加载的数据。'),
    ).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: '重试加载' }));

    await screen.findByText(emptyMessage);
    expect(screen.getByText('共 0 个来源 · 当前筛选 0 个')).toBeInTheDocument();
    expect(screen.queryByText('来源数量 —')).not.toBeInTheDocument();
    expect(screen.queryByText('来源列表加载失败')).not.toBeInTheDocument();
    expect(getSources).toHaveBeenCalledTimes(2);
  });

  it('刷新失败保留上次成功加载的来源和数量，并标明旧数据', async () => {
    vi.mocked(getSources)
      .mockResolvedValueOnce([source])
      .mockRejectedValueOnce(new ApiError('service_unavailable', 503));
    render(<Page />);
    await screen.findByText(source.name);

    await refresh();

    await screen.findByText('当前显示上次成功加载的数据。');
    expect(screen.getByText(source.name)).toBeInTheDocument();
    expect(screen.getByText('共 1 个来源 · 当前筛选 1 个')).toBeInTheDocument();
    expect(screen.queryByText('来源数量 —')).not.toBeInTheDocument();
  });

  it('成功加载过空集合后刷新失败，仍注明当前为上次成功结果', async () => {
    vi.mocked(getSources)
      .mockResolvedValueOnce([])
      .mockRejectedValueOnce(new ApiError('service_unavailable', 503));
    render(<Page />);
    await screen.findByText(emptyMessage);

    await refresh();

    await screen.findByText('当前显示上次成功加载的数据。');
    expect(screen.getByText(emptyMessage)).toBeInTheDocument();
    expect(screen.getByText('共 0 个来源 · 当前筛选 0 个')).toBeInTheDocument();
    expect(screen.queryByText('来源数量 —')).not.toBeInTheDocument();
  });
});
