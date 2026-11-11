import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import messages from '@/locales/zh-CN';
import type { ManagedUser } from '@/services/syslog/types';
import Users from './index';

const runtime = vi.hoisted(() => ({
  update: vi.fn(),
  replace: vi.fn(),
  reload: vi.fn(),
  version: '1',
}));
const user: ManagedUser = {
  id: '42',
  username: 'synthetic-admin',
  role: 'admin',
  enabled: true,
  createdAt: '2026-11-10T00:00:00Z',
  version: '1',
};
vi.mock('@umijs/max', () => ({
  history: { replace: runtime.replace },
  useModel: () => ({
    initialState: { currentUser: user },
    setInitialState: runtime.update,
  }),
}));
vi.mock('@ant-design/pro-components', () => ({
  PageContainer: ({ children }: { children: React.ReactNode }) => children,
}));
vi.mock('@/utils/i18n', () => ({
  useText: () => (key: string) =>
    (messages as Record<string, string>)[key] ?? key,
}));
vi.mock('./useUsers', () => ({
  useUsers: () => ({
    data: { items: [user], total: 1, page: 1, pageSize: 20 },
    loading: false,
    reload: runtime.reload,
  }),
}));
vi.mock('./UserEditor', () => ({
  default: ({ onSaved }: { onSaved: (updated: ManagedUser) => void }) => (
    <button
      type="button"
      onClick={() => onSaved({ ...user, version: runtime.version })}
    >
      apply-editor
    </button>
  ),
}));

beforeEach(() => {
  vi.clearAllMocks();
  runtime.version = '1';
  runtime.reload.mockResolvedValue(undefined);
  runtime.update.mockResolvedValue(undefined);
});

describe('当前管理员保护与无变更保存', () => {
  it('当前账号不能从管理列表停用或重置密码', () => {
    render(<Users />);
    const row = screen.getByText('synthetic-admin').closest('tr');
    expect(row).not.toBeNull();
    const controls = within(row as HTMLTableRowElement);
    expect(controls.getByRole('button', { name: '重置密码' })).toBeDisabled();
    expect(controls.getByRole('button', { name: /^停\s*用$/ })).toBeDisabled();
  });
  it('无变更保存不清空本人会话', async () => {
    render(<Users />);
    fireEvent.click(screen.getByRole('button', { name: '编辑用户' }));
    fireEvent.click(screen.getByRole('button', { name: 'apply-editor' }));
    await waitFor(() => expect(runtime.reload).toHaveBeenCalled());
    expect(runtime.update).not.toHaveBeenCalled();
    expect(runtime.replace).not.toHaveBeenCalled();
  });
  it('真实修改本人版本后清除会话并进入登录', async () => {
    runtime.version = '2';
    render(<Users />);
    fireEvent.click(screen.getByRole('button', { name: '编辑用户' }));
    fireEvent.click(screen.getByRole('button', { name: 'apply-editor' }));
    await waitFor(() =>
      expect(runtime.replace).toHaveBeenCalledWith('/user/login'),
    );
    const updated = runtime.update.mock.calls[0][0]({ currentUser: user });
    expect(updated.currentUser).toBeUndefined();
    expect(updated.bootNotice).toBe('users.selfChanged');
  });
});
