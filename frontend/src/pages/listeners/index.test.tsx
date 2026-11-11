import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import messages from '@/locales/zh-CN';
import { ApiError } from '@/requestErrorConfig';
import type {
  ListenerRuntimeUpdate,
  ListenerSettings,
  ListenerSettingsUpdate,
} from '@/services/syslog/types';
import ListenersPage from './index';

const runtime = vi.hoisted(() => ({
  read: vi.fn(),
  save: vi.fn(),
  setReception: vi.fn(),
  setProtocol: vi.fn(),
  subscribe: vi.fn(),
}));
vi.mock('@ant-design/pro-components', () => ({
  PageContainer: ({
    children,
    extra,
  }: {
    children: React.ReactNode;
    extra: React.ReactNode;
  }) => (
    <>
      {extra}
      {children}
    </>
  ),
}));
vi.mock('@/utils/i18n', () => ({
  useText: () => (key: string) =>
    (messages as Record<string, string>)[key] ?? key,
}));
vi.mock('@/services/syslog/api', () => ({
  getListenerSettings: runtime.read,
  saveListenerSettings: runtime.save,
  setListenerProtocol: runtime.setProtocol,
  setListenerReception: runtime.setReception,
}));
vi.mock('@/services/syslog/overviewStream', () => ({
  subscribeListenerRuntime: runtime.subscribe,
}));

function push(snapshot: ListenerSettings) {
  const callbacks = runtime.subscribe.mock.lastCall?.[0];
  const update: ListenerRuntimeUpdate = {
    runtime: snapshot,
    observedAt: '2026-11-11T00:00:00Z',
  };
  act(() => {
    callbacks.state('connected');
    callbacks.snapshot(update);
  });
}

function snapshot(): ListenerSettings {
  const configuration = {
    enabled: false,
    listeners: (['udp', 'tcp', 'tls', 'relp'] as const).map(
      (protocol, index) => ({
        protocol,
        enabled: false,
        address: '0.0.0.0',
        port: [514, 514, 6514, 2514][index],
        framing: 'octet-counting' as const,
        certificateId: '',
        frameTimeoutSeconds: 30,
        idleTimeoutSeconds: 1800,
      }),
    ),
  };
  return {
    revision: '1',
    saved: { version: '7', configuration },
    running: structuredClone(configuration),
    restartRequired: false,
    certificates: [],
    connectionErrors: [],
    states: configuration.listeners.map(({ protocol, address, port }) => ({
      protocol,
      address,
      port,
      state: 'disabled',
      failureCode: '',
    })),
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  runtime.subscribe.mockReturnValue(vi.fn());
  runtime.read.mockResolvedValue(snapshot());
  runtime.save.mockImplementation(async (input: ListenerSettingsUpdate) => ({
    ...snapshot(),
    revision: '2',
    saved: { ...input, version: '8' },
    restartRequired: true,
  }));
});

describe('接收配置保存与运行状态', () => {
  it('区分已知监听失败原因，未知错误不回显服务端正文', async () => {
    const data = snapshot();
    data.states = [
      'listener_io_failed',
      'tls_listener_start_failed',
      'source_registry_unavailable',
      'invalid_listener_configuration',
      'listener_configuration_unavailable',
      'listener_forced_disabled',
      '<script>private-path</script>',
    ].map((failureCode, index) => ({
      protocol: 'tcp',
      address: '127.0.0.1',
      port: 5514 + index,
      state: 'failed',
      failureCode,
    }));
    runtime.read.mockResolvedValue(data);
    render(<ListenersPage />);
    expect(
      await screen.findByText(
        '监听绑定或网络读写失败，请检查端口占用、地址和权限。',
      ),
    ).toBeInTheDocument();
    expect(
      screen.getByText('TLS 启动失败，请检查服务器证书、私钥和有效期。'),
    ).toBeInTheDocument();
    expect(
      screen.getByText('日志来源配置暂不可用，接收已暂停。'),
    ).toBeInTheDocument();
    expect(
      screen.getByText('无法读取启动配置，请检查控制库和配置版本。'),
    ).toBeInTheDocument();
    expect(
      screen.getByText('服务器强制关闭接收，请检查启动配置。'),
    ).toBeInTheDocument();
    expect(
      screen.getByText('接收配置无效，请检查监听地址、端口和超时范围。'),
    ).toBeInTheDocument();
    expect(
      screen.getByText('接收异常，请检查监听地址、端口和证书配置。'),
    ).toBeInTheDocument();
    expect(screen.queryByText(/private-path/)).not.toBeInTheDocument();
  });
  it('显示四协议端口及当前未启用状态，不把建议端口显示为已监听', async () => {
    render(<ListenersPage />);
    expect(
      await screen.findByRole('spinbutton', { name: 'UDP 端口' }),
    ).toHaveValue('514');
    expect(screen.getByRole('spinbutton', { name: 'TCP 端口' })).toHaveValue(
      '514',
    );
    expect(screen.getByRole('spinbutton', { name: 'TLS 端口' })).toHaveValue(
      '6514',
    );
    expect(screen.getByRole('spinbutton', { name: 'RELP 端口' })).toHaveValue(
      '2514',
    );
    expect(screen.getByRole('textbox', { name: 'UDP 监听地址' })).toHaveValue(
      '0.0.0.0',
    );
    expect(screen.getAllByText('未启用')).toHaveLength(4);
    expect(screen.queryByText('监听中')).not.toBeInTheDocument();
  });
  it('按客户端展示连接错误，恢复后移除错误且保留正在编辑的设置', async () => {
    const data = snapshot();
    const detail = '客户端连接读写失败（ConnectionReset）。';
    data.running.enabled = true;
    data.states[1] = {
      ...data.states[1],
      state: 'listening',
    };
    data.connectionErrors = [
      {
        protocol: 'tcp',
        address: '0.0.0.0',
        port: 514,
        peerAddress: '192.0.2.20',
        failureCode: 'connection_io_failed',
        failureMessage: detail,
        occurredAt: '2026-11-10T05:00:00Z',
      },
    ];
    runtime.read.mockResolvedValue(data);
    render(<ListenersPage />);
    const listener = await screen.findByRole('row', { name: /TCP.*监听中/ });
    const error = screen.getByRole('row', { name: /192\.0\.2\.20/ });
    expect(within(listener).queryByText(detail)).not.toBeInTheDocument();
    expect(within(error).getByText(detail)).toBeInTheDocument();
    expect(
      within(error).getByText('2026-11-10 13:00:00.000'),
    ).toBeInTheDocument();

    const port = screen.getByRole('spinbutton', { name: 'TCP 端口' });
    fireEvent.change(port, { target: { value: '5514' } });
    fireEvent.blur(port);
    push({ ...data, revision: '2', connectionErrors: [] });
    expect(screen.queryByText('192.0.2.20')).not.toBeInTheDocument();
    expect(port).toHaveValue('5514');
    expect(runtime.read).toHaveBeenCalledTimes(1);
  });

  it('仅在选择自动识别后提交 TCP 分帧新值', async () => {
    render(<ListenersPage />);
    const framing = await screen.findByRole('combobox', {
      name: 'TCP 报文分帧方式',
    });
    fireEvent.mouseDown(framing);
    fireEvent.click(
      await screen.findByText('自动识别（字节计数 / 换行）', {
        selector: '.ant-select-item-option-content',
      }),
    );
    fireEvent.click(screen.getByRole('button', { name: '保存接收配置' }));
    const dialog = await screen.findByRole('dialog');
    fireEvent.click(within(dialog).getByRole('button', { name: '确认保存' }));
    await waitFor(() => expect(runtime.save).toHaveBeenCalledTimes(1));
    const { listeners } = runtime.save.mock.calls[0][0].configuration;
    expect(listeners[1].framing).toBe('auto');
    expect(listeners[2].framing).toBe('octet-counting');
  });

  it('按启停着色确认条目，保存后仍显示当前运行端口', async () => {
    const data = snapshot();
    data.saved.configuration.listeners[0].enabled = true;
    runtime.read.mockResolvedValue(data);
    render(<ListenersPage />);
    const port = await screen.findByRole('spinbutton', { name: 'UDP 端口' });
    fireEvent.change(port, { target: { value: '5514' } });
    fireEvent.blur(port);
    fireEvent.click(screen.getByRole('button', { name: '保存接收配置' }));
    const dialog = await screen.findByRole('dialog');
    expect(runtime.save).not.toHaveBeenCalled();
    expect(
      within(dialog).getByText(/UDP · 0\.0\.0\.0:5514 · 接收已启用/),
    ).toHaveClass('ant-typography-success');
    expect(
      within(dialog).getByText(/TLS · 0\.0\.0\.0:6514 · 接收已停用/),
    ).toHaveClass('ant-typography-danger');
    fireEvent.click(within(dialog).getByRole('button', { name: '确认保存' }));
    await waitFor(() => expect(runtime.save).toHaveBeenCalledTimes(1));
    const request = runtime.save.mock.calls[0][0];
    expect(request.version).toBe('7');
    expect(request.configuration.listeners[0].port).toBe(5514);
    expect(request.configuration.listeners[0].frameTimeoutSeconds).toBe(30);
    expect(request.configuration.listeners[0].certificateId).toBe('');
    expect(await screen.findByText('配置待应用')).toBeInTheDocument();
    expect(screen.getAllByText('未启用')).toHaveLength(4);
    expect(screen.getAllByText('514')).toHaveLength(2);
  });

  it('关闭并重新开启总开关后，按服务端返回值显示新端口', async () => {
    const data = snapshot();
    data.running.enabled = true;
    data.running.listeners[0].enabled = true;
    data.states[0].state = 'listening';
    data.saved.configuration.enabled = true;
    data.saved.configuration.listeners[0].enabled = true;
    data.saved.configuration.listeners[0].port = 5514;
    data.restartRequired = true;
    const stopped = structuredClone(data);
    stopped.revision = '2';
    stopped.running.enabled = false;
    stopped.saved.version = '8';
    stopped.saved.configuration.enabled = false;
    stopped.states[0].state = 'disabled';
    const started = structuredClone(stopped);
    started.revision = '3';
    started.saved.version = '9';
    started.saved.configuration.enabled = true;
    started.running = structuredClone(started.saved.configuration);
    started.states[0].port = 5514;
    started.states[0].state = 'listening';
    started.restartRequired = false;
    runtime.read.mockResolvedValue(data);
    runtime.setReception
      .mockResolvedValueOnce(stopped)
      .mockResolvedValueOnce(started);

    render(<ListenersPage />);
    const toggle = await screen.findByRole('switch', {
      name: '日志接收总开关',
    });
    expect(toggle).toBeChecked();
    fireEvent.click(toggle);
    expect(runtime.setReception).not.toHaveBeenCalled();
    const offDialog = await screen.findByRole('dialog', {
      name: '确认关闭日志接收',
    });
    fireEvent.click(
      within(offDialog).getByRole('button', { name: '确认切换' }),
    );
    await waitFor(() => expect(toggle).not.toBeChecked());
    expect(screen.queryByText('监听中')).not.toBeInTheDocument();
    expect(screen.getByText('配置待应用')).toBeInTheDocument();
    fireEvent.click(toggle);
    const onDialog = await screen.findByRole('dialog', {
      name: '确认开启日志接收',
    });
    // 前一次确认的 Ant Design loading 图标仍在执行退场动画。
    fireEvent.click(
      within(onDialog).getByRole('button', { name: /确认切换$/ }),
    );
    await waitFor(() => expect(toggle).toBeChecked());
    expect(screen.getByText('5514')).toBeInTheDocument();
    expect(screen.getByText('监听中')).toBeInTheDocument();
    expect(screen.queryByText('配置待应用')).not.toBeInTheDocument();
    expect(runtime.setReception.mock.calls).toEqual([
      [false, '7'],
      [true, '8'],
    ]);
    expect(runtime.save).not.toHaveBeenCalled();
  });

  it('总开关关闭时，协议启用只保存开关且保留未提交的参数', async () => {
    const data = snapshot();
    const changed = structuredClone(data);
    changed.revision = '2';
    changed.saved.version = '8';
    changed.saved.configuration.listeners[0].enabled = true;
    // 旧部署顺序与首次写入后的规范顺序不同。
    data.saved.configuration.listeners.reverse();
    runtime.read.mockResolvedValue(data);
    runtime.setProtocol.mockResolvedValue(changed);
    render(<ListenersPage />);
    const port = await screen.findByRole('spinbutton', { name: 'UDP 端口' });
    fireEvent.change(port, { target: { value: '5514' } });
    fireEvent.blur(port);
    const toggle = screen.getByRole('switch', { name: 'UDP 启用此协议' });
    fireEvent.click(toggle);
    const dialog = await screen.findByRole('dialog');
    expect(
      within(dialog).getByText(/本次仅保存该协议的启用状态/),
    ).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('button', { name: '确认切换' }));
    await waitFor(() => expect(toggle).toBeChecked());
    expect(runtime.setProtocol).toHaveBeenCalledWith('udp', true, '7');
    expect(screen.queryByText('监听中')).not.toBeInTheDocument();
    expect(port).toHaveValue('5514');
    expect(screen.getByRole('spinbutton', { name: 'TLS 端口' })).toHaveValue(
      '6514',
    );
  });

  it('较新的 SignalR 运行状态优先于较早的启停响应', async () => {
    const data = snapshot();
    data.running.enabled = true;
    data.saved.configuration.enabled = true;
    const starting = structuredClone(data);
    starting.revision = '9007199254740992';
    starting.saved.version = '8';
    starting.saved.configuration.listeners[0].enabled = true;
    starting.states[0].state = 'starting';
    const listening = structuredClone(starting);
    listening.revision = '9007199254740993';
    listening.states[0].state = 'listening';
    runtime.read.mockResolvedValue(data);
    runtime.setProtocol.mockImplementation(async () => {
      push(listening);
      return starting;
    });
    render(<ListenersPage />);
    fireEvent.click(
      await screen.findByRole('switch', { name: 'UDP 启用此协议' }),
    );
    const dialog = await screen.findByRole('dialog');
    fireEvent.click(within(dialog).getByRole('button', { name: '确认切换' }));
    expect(
      await screen.findByRole('row', { name: /UDP.*监听中/ }),
    ).toBeInTheDocument();
    expect(screen.queryByText('启动中')).not.toBeInTheDocument();
  });

  it('TLS 已启用且缺少证书时阻止保存参数', async () => {
    const data = snapshot();
    data.saved.configuration.listeners[2].enabled = true;
    runtime.read.mockResolvedValue(data);
    render(<ListenersPage />);
    await screen.findByRole('switch', {
      name: 'TLS 启用此协议',
      checked: true,
    });
    fireEvent.click(screen.getByRole('button', { name: '保存接收配置' }));
    expect(
      await screen.findByText('启用 TLS 时请选择可用的服务器证书。'),
    ).toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(runtime.save).not.toHaveBeenCalled();
  });

  it('显示两种系统证书及自定义证书，保存上传证书的稳定标识', async () => {
    const data = snapshot();
    data.certificates = [
      { id: 'system-self-signed' },
      { id: 'system-uploaded' },
      { id: 'synthetic-custom' },
    ];
    runtime.read.mockResolvedValue(data);
    render(<ListenersPage />);
    await screen.findByRole('switch', { name: 'TLS 启用此协议' });
    fireEvent.mouseDown(screen.getByRole('combobox', { name: 'TLS 证书' }));
    const option = { selector: '.ant-select-item-option-content' };
    expect(screen.getByText('服务自签 HTTPS 证书', option)).toBeInTheDocument();
    expect(screen.getByText('synthetic-custom', option)).toBeInTheDocument();
    fireEvent.click(screen.getByText('已上传的 HTTPS 证书', option));
    fireEvent.click(screen.getByRole('button', { name: '保存接收配置' }));
    const dialog = await screen.findByRole('dialog');
    fireEvent.click(within(dialog).getByRole('button', { name: '确认保存' }));
    await waitFor(() => expect(runtime.save).toHaveBeenCalledTimes(1));
    const submitted = runtime.save.mock.calls[0][0] as ListenerSettingsUpdate;
    expect(
      submitted.configuration.listeners.find(
        ({ protocol }) => protocol === 'tls',
      )?.certificateId,
    ).toBe('system-uploaded');
  });

  it('版本冲突保留用户输入，不能显示保存成功', async () => {
    runtime.save.mockRejectedValue(new ApiError('version_conflict', 409));
    render(<ListenersPage />);
    const port = await screen.findByRole('spinbutton', { name: 'RELP 端口' });
    fireEvent.change(port, { target: { value: '12514' } });
    fireEvent.blur(port);
    fireEvent.click(screen.getByRole('button', { name: '保存接收配置' }));
    const dialog = await screen.findByRole('dialog');
    fireEvent.click(within(dialog).getByRole('button', { name: '确认保存' }));
    expect(
      await screen.findByText(
        '配置已被其他管理员修改，请取消并重新读取配置后再修改。',
      ),
    ).toBeInTheDocument();
    expect(port).toHaveValue('12514');
    expect(screen.queryByText('配置已保存')).not.toBeInTheDocument();
  });

  it('读取失败时显示错误并允许重试，不展示可提交的默认配置', async () => {
    runtime.read.mockRejectedValueOnce(
      new ApiError('listener_migration_required', 503),
    );
    render(<ListenersPage />);
    expect(
      await screen.findByText(
        '控制库需要显式升级监听配置表，请联系管理员完成迁移后重试。',
      ),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: '保存接收配置' }),
    ).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: '重试加载' }));
    expect(
      await screen.findByRole('spinbutton', { name: 'UDP 端口' }),
    ).toHaveValue('514');
  });
});
