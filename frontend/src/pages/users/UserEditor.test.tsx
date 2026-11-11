import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PREVIEW_NEW_PASSWORD } from '@/components/Shell/preview';
import messages from '@/locales/zh-CN';
import { ApiError } from '@/requestErrorConfig';
import {
  createUser,
  resetUserPassword,
  updateUser,
} from '@/services/syslog/api';
import type { ManagedUser } from '@/services/syslog/types';
import UserEditor from './UserEditor';

vi.mock('@/services/syslog/api', () => ({
  createUser: vi.fn(),
  resetUserPassword: vi.fn(),
  updateUser: vi.fn(),
}));
vi.mock('@/utils/i18n', () => ({
  useText: () => (key: string, values?: Record<string, string | number>) => {
    let text = (messages as Record<string, string>)[key] ?? key;
    for (const [name, value] of Object.entries(values ?? {}))
      text = text.replaceAll(`{${name}}`, String(value));
    return text;
  },
}));
const user: ManagedUser = {
  id: '9007199254740993',
  username: 'synthetic-admin',
  role: 'admin',
  enabled: true,
  createdAt: '2026-11-10T00:00:00Z',
  version: '9007199254740994',
};
beforeEach(() => vi.clearAllMocks());

function fillPassword(
  password = 'synthetic-long-password',
  confirmation = password,
) {
  fireEvent.change(screen.getByLabelText('密码'), {
    target: { value: password },
  });
  fireEvent.change(screen.getByLabelText('确认密码'), {
    target: { value: confirmation },
  });
}
function save() {
  fireEvent.click(screen.getByRole('button', { name: /^保\s*存$/ }));
}

describe('用户配置表单与敏感操作', () => {
  it('既有 Unicode 用户名未改动时可保存，改名仍遵循当前规则', async () => {
    const legacy = { ...user, username: '历史管理员' };
    vi.mocked(updateUser).mockResolvedValue(legacy);
    render(
      <UserEditor
        mode="edit"
        user={legacy}
        onClose={vi.fn()}
        onSaved={vi.fn()}
      />,
    );
    save();
    await waitFor(() =>
      expect(updateUser).toHaveBeenCalledWith(user.id, {
        username: '历史管理员',
        enabled: true,
        expectedVersion: user.version,
      }),
    );
    fireEvent.change(screen.getByLabelText('用户名'), {
      target: { value: '新的管理员' },
    });
    save();
    await screen.findByText(messages['users.usernameRule']);
    expect(updateUser).toHaveBeenCalledTimes(1);
  });
  it('不合法用户名和不匹配密码不会创建账号', async () => {
    render(<UserEditor mode="create" onClose={vi.fn()} onSaved={vi.fn()} />);
    fireEvent.change(screen.getByLabelText('用户名'), {
      target: { value: 'invalid name' },
    });
    fillPassword('synthetic-long-password', 'different-password');
    save();
    await screen.findByText('两次输入的密码不一致。');
    expect(
      screen.getByText(messages['users.usernameRule']),
    ).toBeInTheDocument();
    expect(createUser).not.toHaveBeenCalled();
  });
  it.each([
    ['空白', ' '.repeat(8)],
    ['7 个字符的', 'short-7'],
  ])('%s密码不能提交', async (_case, password) => {
    render(<UserEditor mode="create" onClose={vi.fn()} onSaved={vi.fn()} />);
    fireEvent.change(screen.getByLabelText('用户名'), {
      target: { value: 'example' },
    });
    fillPassword(password);
    save();
    await waitFor(() =>
      expect(
        screen.getAllByText(messages['users.passwordRule']).length,
      ).toBeGreaterThan(1),
    );
    expect(createUser).not.toHaveBeenCalled();
  });
  it('编辑保存携带原始版本，冲突保留输入且不报告成功', async () => {
    vi.mocked(updateUser).mockRejectedValue(
      new ApiError('version_conflict', 409),
    );
    const saved = vi.fn();
    render(
      <UserEditor mode="edit" user={user} onClose={vi.fn()} onSaved={saved} />,
    );
    fireEvent.change(screen.getByLabelText('用户名'), {
      target: { value: 'renamed-example' },
    });
    save();
    await screen.findByText('账号已被其他操作修改，请刷新列表后重新编辑。');
    expect(updateUser).toHaveBeenCalledWith(user.id, {
      username: 'renamed-example',
      enabled: true,
      expectedVersion: user.version,
    });
    expect(screen.getByLabelText('用户名')).toHaveValue('renamed-example');
    expect(saved).not.toHaveBeenCalled();
  });
  it('允许使用 8 个字符重置密码，确认会话影响后只提交一次', async () => {
    vi.mocked(resetUserPassword).mockResolvedValue(true);
    const saved = vi.fn();
    render(
      <UserEditor
        mode="password"
        user={user}
        onClose={vi.fn()}
        onSaved={saved}
      />,
    );
    fillPassword('sample-8');
    save();
    await screen.findByText('确认重置密码');
    expect(resetUserPassword).not.toHaveBeenCalled();
    const confirm = screen.getByRole('button', { name: '确认操作' });
    fireEvent.click(confirm);
    fireEvent.click(confirm);
    await waitFor(() => expect(saved).toHaveBeenCalledTimes(1));
    expect(resetUserPassword).toHaveBeenCalledTimes(1);
    expect(resetUserPassword).toHaveBeenCalledWith(
      user.id,
      'sample-8',
      user.version,
    );
  });
  it('合成预览替换口令，不传输输入的密码', async () => {
    const meta = document.createElement('meta');
    meta.name = 'syslog-synthetic-preview';
    document.head.append(meta);
    try {
      vi.mocked(createUser).mockResolvedValue(user);
      render(<UserEditor mode="create" onClose={vi.fn()} onSaved={vi.fn()} />);
      fireEvent.change(screen.getByLabelText('用户名'), {
        target: { value: 'example' },
      });
      fillPassword();
      save();
      await waitFor(() =>
        expect(createUser).toHaveBeenCalledWith({
          username: 'example',
          password: PREVIEW_NEW_PASSWORD,
        }),
      );
    } finally {
      meta.remove();
    }
  });
});
