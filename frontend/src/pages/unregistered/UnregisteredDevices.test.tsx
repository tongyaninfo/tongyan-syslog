import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
} from '@testing-library/react';
import type { ReactNode } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import messages from '@/locales/zh-CN';
import { ApiError } from '@/requestErrorConfig';
import {
  createSource,
  getUnregisteredDevices,
  getUnregisteredLogs,
  getUnregisteredSample,
} from '@/services/syslog/api';
import type {
  LogSource,
  UnregisteredDevices,
  UnregisteredLogSample,
} from '@/services/syslog/types';
import DeviceLogsDrawer from './DeviceLogsDrawer';
import UnregisteredDevicesPage from './index';

vi.mock('@/services/syslog/api', () => ({
  createSource: vi.fn(),
  getUnregisteredDevices: vi.fn(),
  getUnregisteredLogs: vi.fn(),
  getUnregisteredSample: vi.fn(),
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
vi.mock('@umijs/max', () => ({ useIntl: () => ({ locale: 'zh-CN' }) }));
vi.mock('@/utils/i18n', () => ({
  useText: () => (key: string, values?: Record<string, string | number>) => {
    let value = (messages as Record<string, string>)[key] ?? key;
    for (const [name, replacement] of Object.entries(values ?? {}))
      value = value.replaceAll(`{${name}}`, String(replacement));
    return value;
  },
}));

const peerAddress = '192.0.2.25';
const device = {
  peerAddress,
  firstReceivedAt: '2026-11-10T01:00:00Z',
  lastReceivedAt: '2026-11-10T01:01:00Z',
  transports: ['udp', 'tcp'],
  received: 101,
  sampleCount: 100,
};
const data: UnregisteredDevices = {
  items: [device],
  status: {
    enabled: true,
    allowedNetworks: ['192.0.2.0/24'],
    deviceCount: 1,
    sampleCount: 100,
    bufferedBytes: 5000,
    maxBytes: 64 * 1024 * 1024,
    maxDevices: 1000,
    received: 101,
    evicted: 1,
    rejected: 2,
  },
};
const empty: UnregisteredDevices = {
  items: [],
  status: { ...data.status, deviceCount: 0, sampleCount: 0, bufferedBytes: 0 },
};
const malicious = '<img src=x onerror="alert(1)"><script>alert(2)</script>';
const summary = {
  sampleKey: 'sample-101',
  receivedAt: '2026-11-10T01:01:00Z',
  transport: 'udp',
  byteCount: malicious.length,
  preview: malicious,
};
const sample: UnregisteredLogSample = { ...summary, rawBytes: btoa(malicious) };

function deferred<T>() {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((done) => {
    resolve = done;
  });
  return { promise, resolve };
}

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(getUnregisteredDevices).mockReset();
  vi.mocked(getUnregisteredLogs).mockReset();
  vi.mocked(getUnregisteredSample).mockReset();
  vi.mocked(createSource).mockReset();
  vi.mocked(getUnregisteredDevices).mockResolvedValue(data);
  vi.mocked(getUnregisteredLogs).mockResolvedValue([summary]);
  vi.mocked(getUnregisteredSample).mockResolvedValue(sample);
  vi.mocked(createSource).mockImplementation(async (input) => ({
    id: '42',
    ...input,
  }));
});

describe('未登记设备与登记边界', () => {
  it('展示真实100条暂存计数，一键使用公开默认值且重复点击只提交一次', async () => {
    const pending = deferred<LogSource>();
    vi.mocked(createSource).mockReturnValue(pending.promise);
    vi.mocked(getUnregisteredDevices)
      .mockResolvedValueOnce(data)
      .mockResolvedValue(empty);
    render(<UnregisteredDevicesPage />);
    await screen.findByText('100 / 100');
    expect(screen.getByText(/重启后清空/)).toBeInTheDocument();
    expect(getUnregisteredLogs).not.toHaveBeenCalled();
    const add = screen.getByRole('button', { name: /一键添加$/ });
    fireEvent.click(add);
    fireEvent.click(add);
    expect(createSource).toHaveBeenCalledTimes(1);
    expect(createSource).toHaveBeenCalledWith({
      name: peerAddress,
      peerAddress,
      timeZoneId: 'Asia/Shanghai',
      retentionDays: 365,
      enabled: true,
    });
    await act(async () =>
      pending.resolve({
        id: '42',
        name: peerAddress,
        peerAddress,
        timeZoneId: 'Asia/Shanghai',
        retentionDays: 365,
        enabled: true,
      }),
    );
    await screen.findByText(`已登记来源 ${peerAddress}`);
    await screen.findByText('暂无未登记设备');
    expect(screen.getByText(/暂存历史不会回填/)).toBeInTheDocument();
  });

  it('保存503保留设备，允许重试且不显示成功', async () => {
    vi.mocked(createSource).mockRejectedValueOnce(
      new ApiError('service_unavailable', 503),
    );
    render(<UnregisteredDevicesPage />);
    await screen.findByText(peerAddress);
    fireEvent.click(screen.getByRole('button', { name: /一键添加$/ }));
    await screen.findByText('添加失败');
    expect(screen.getByText(peerAddress)).toBeInTheDocument();
    expect(
      screen.queryByText(`已登记来源 ${peerAddress}`),
    ).not.toBeInTheDocument();
    await waitFor(() =>
      expect(
        screen.getByRole('button', { name: /一键添加$/ }),
      ).not.toBeDisabled(),
    );
  });

  it('409冲突刷新列表但不假报成功或更新既有来源', async () => {
    vi.mocked(createSource).mockRejectedValueOnce(
      new ApiError('CONFLICT', 409),
    );
    vi.mocked(getUnregisteredDevices)
      .mockResolvedValueOnce(data)
      .mockResolvedValue(empty);
    render(<UnregisteredDevicesPage />);
    await screen.findByText(peerAddress);
    fireEvent.click(screen.getByRole('button', { name: /一键添加$/ }));
    await screen.findByText('该来源 IP 已登记，请检查重复配置。');
    await screen.findByText('暂无未登记设备');
    expect(
      screen.queryByText(`已登记来源 ${peerAddress}`),
    ).not.toBeInTheDocument();
    expect(createSource).toHaveBeenCalledTimes(1);
  });

  it('首次查询故障显示不可用，不能显示已确认的零设备', async () => {
    vi.mocked(getUnregisteredDevices)
      .mockRejectedValueOnce(new ApiError('service_unavailable', 503))
      .mockResolvedValue(empty);
    render(<UnregisteredDevicesPage />);
    await screen.findByText('暂存数据不可用');
    expect(screen.queryByText('当前筛选 0 个设备')).not.toBeInTheDocument();
    expect(screen.queryByText('暂无未登记设备')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: '重试加载' }));
    await screen.findByText('暂无未登记设备');
    expect(screen.getByText('当前筛选 0 个设备')).toBeInTheDocument();
  });
});

describe('未登记日志证据', () => {
  it('摘要和原文的恶意HTML保持纯文本，原文只在点击时读取', async () => {
    render(<DeviceLogsDrawer peerAddress={peerAddress} onClose={vi.fn()} />);
    await screen.findByText(malicious);
    expect(getUnregisteredSample).not.toHaveBeenCalled();
    expect(document.querySelector('img[src="x"]')).toBeNull();
    expect(document.querySelector('script')).toBeNull();
    fireEvent.click(screen.getByRole('button', { name: '查看原文' }));
    await waitFor(() => expect(screen.getAllByText(malicious)).toHaveLength(2));
    expect(getUnregisteredSample).toHaveBeenCalledWith(
      peerAddress,
      summary.sampleKey,
      expect.any(AbortSignal),
    );
    expect(document.querySelector('img[src="x"]')).toBeNull();
    expect(document.querySelector('script')).toBeNull();
  });

  it('选中的样本已淘汰时显示404提示，刷新重新读取摘要', async () => {
    vi.mocked(getUnregisteredSample).mockRejectedValueOnce(
      new ApiError('not_found', 404),
    );
    render(<DeviceLogsDrawer peerAddress={peerAddress} onClose={vi.fn()} />);
    await screen.findByText(malicious);
    fireEvent.click(screen.getByRole('button', { name: '查看原文' }));
    await screen.findByText('该样本已淘汰或设备已登记，请刷新日志列表。');
    expect(
      screen.queryByRole('tab', { name: '文本预览' }),
    ).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /刷\s*新$/ }));
    await waitFor(() => expect(getUnregisteredLogs).toHaveBeenCalledTimes(2));
  });

  it('原文达到64KiB时复用截断提示而不显示完整文本', async () => {
    vi.mocked(getUnregisteredSample).mockResolvedValueOnce({
      ...sample,
      rawBytes: btoa('A'.repeat(65_536)),
    });
    render(<DeviceLogsDrawer peerAddress={peerAddress} onClose={vi.fn()} />);
    await screen.findByText(malicious);
    fireEvent.click(screen.getByRole('button', { name: '查看原文' }));
    await screen.findByText(/文本预览已截断/);
    const original = document.querySelector('pre.syslog-evidence-text');
    expect(original?.textContent).toHaveLength(16_384);
  });

  it('关闭抽屉取消原文读取，延迟结果不会再次展示', async () => {
    const pending = deferred<UnregisteredLogSample>();
    vi.mocked(getUnregisteredSample).mockReturnValue(pending.promise);
    const view = render(
      <DeviceLogsDrawer peerAddress={peerAddress} onClose={vi.fn()} />,
    );
    await screen.findByText(malicious);
    fireEvent.click(screen.getByRole('button', { name: '查看原文' }));
    await waitFor(() => expect(getUnregisteredSample).toHaveBeenCalled());
    const signal = vi.mocked(getUnregisteredSample).mock.calls[0][2];
    view.unmount();
    expect(signal?.aborted).toBe(true);
    await act(async () => pending.resolve(sample));
    expect(screen.queryByText(malicious)).not.toBeInTheDocument();
  });
});
