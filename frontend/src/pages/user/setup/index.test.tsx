import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PREVIEW_NEW_PASSWORD } from '@/components/Shell/preview';
import messages from '@/locales/zh-CN';
import { initializeAdmin } from '@/services/syslog/api';
import type { StartupState } from '@/utils/startup';
import Setup from './index';

const runtime = vi.hoisted(() => ({
  state: {} as StartupState,
  update: vi.fn(),
  replace: vi.fn(),
}));
vi.mock('@umijs/max', () => ({
  history: { replace: runtime.replace },
  useModel: () => ({
    initialState: runtime.state,
    setInitialState: runtime.update,
  }),
}));
vi.mock('@/components/Shell', () => ({ PreviewBanner: () => null }));
vi.mock('@/components/Shell/Appearance', () => ({
  AppearanceProvider: ({ children }: { children: React.ReactNode }) => children,
  useSystemAppearance: () => undefined,
}));
vi.mock('@/components/Shell/LanguageMenu', () => ({ default: () => null }));
vi.mock('@/components/Shell/SiteFooter', () => ({ default: () => null }));
vi.mock('@/services/syslog/api', () => ({
  initializeAdmin: vi.fn(),
  getSetupStatus: vi.fn(),
}));
vi.mock('@/utils/i18n', () => ({
  useText: () => (key: string) =>
    (messages as Record<string, string>)[key] ?? key,
}));
beforeEach(() => {
  vi.clearAllMocks();
  runtime.state = {};
  runtime.update.mockResolvedValue(undefined);
});

describe('管理员初始化页面', () => {
  it('状态未知时只有故障提示与重试，不显示创建表单', () => {
    render(<Setup />);
    expect(screen.getByText('暂时无法读取初始化状态')).toBeInTheDocument();
    expect(screen.queryByLabelText('用户名')).not.toBeInTheDocument();
    expect(initializeAdmin).not.toHaveBeenCalled();
  });
  it('非本机允许状态不提供提交控件', () => {
    runtime.state = {
      setupStatus: { setupRequired: true, canInitialize: false },
    };
    render(<Setup />);
    expect(screen.getByText('请在服务器本机完成初始化')).toBeInTheDocument();
    expect(
      screen.queryByRole('button', { name: '创建管理员并继续' }),
    ).not.toBeInTheDocument();
  });
  it('成功才切换已初始化状态，合成预览不发送输入密码', async () => {
    runtime.state = {
      setupStatus: { setupRequired: true, canInitialize: true },
    };
    vi.mocked(initializeAdmin).mockResolvedValue({
      id: '42',
      username: 'example-admin',
      role: 'admin',
    });
    const meta = document.createElement('meta');
    meta.name = 'syslog-synthetic-preview';
    document.head.append(meta);
    try {
      render(<Setup />);
      fireEvent.change(screen.getByLabelText('用户名'), {
        target: { value: 'example-admin' },
      });
      fireEvent.change(screen.getByLabelText('密码'), {
        target: { value: 'synthetic-entered-password' },
      });
      fireEvent.change(screen.getByLabelText('确认密码'), {
        target: { value: 'synthetic-entered-password' },
      });
      fireEvent.click(screen.getByRole('button', { name: '创建管理员并继续' }));
      await waitFor(() => expect(runtime.update).toHaveBeenCalled());
      expect(initializeAdmin).toHaveBeenCalledWith({
        username: 'example-admin',
        password: PREVIEW_NEW_PASSWORD,
      });
      const state = runtime.update.mock.calls[0][0](runtime.state);
      expect(state.setupStatus).toEqual({
        setupRequired: false,
        canInitialize: false,
      });
      expect(state.loginUsername).toBe('example-admin');
      expect(state.currentUser).toBeUndefined();
      expect(JSON.stringify(state)).not.toContain('synthetic-entered-password');
    } finally {
      meta.remove();
    }
  });
});
