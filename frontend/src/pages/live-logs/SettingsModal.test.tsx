import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import messages from '@/locales/zh-CN';
import { ApiError } from '@/requestErrorConfig';
import {
  getLiveLogSettings,
  saveLiveLogSettings,
} from '@/services/syslog/liveLogs';
import { cache } from '../../../tests/liveLogsFixtures';
import SettingsModal from './SettingsModal';

vi.mock('@/services/syslog/liveLogs', () => ({
  getLiveLogSettings: vi.fn(),
  saveLiveLogSettings: vi.fn(),
}));
vi.mock('@/utils/i18n', () => ({
  useText: () => (key: string) => messages[key as keyof typeof messages] ?? key,
}));
beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(getLiveLogSettings).mockResolvedValue({
    settings: {
      version: '9007199254740993',
      perDeviceLimit: 5000,
      maxBytes: 536870912,
    },
    cache,
  });
});
describe('实时缓存配置', () => {
  it('缩容明确提示淘汰；失败保留输入，版本字段与 MiB 换算正确', async () => {
    vi.mocked(saveLiveLogSettings).mockRejectedValue(
      new ApiError('version_conflict', 409),
    );
    const close = vi.fn();
    render(<SettingsModal open onClose={close} onSaved={vi.fn()} />);
    const perDevice = await screen.findByLabelText('每设备缓存上限');
    await waitFor(() => expect(perDevice).toHaveValue('5000'));
    fireEvent.change(perDevice, { target: { value: '1000' } });
    fireEvent.change(screen.getByLabelText('内存总预算 (MiB)'), {
      target: { value: '128' },
    });
    expect(await screen.findByText(/缩小缓存会立即淘汰/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /保\s*存/ }));
    await waitFor(() =>
      expect(saveLiveLogSettings).toHaveBeenCalledWith({
        version: '9007199254740993',
        perDeviceLimit: 1000,
        maxBytes: 134217728,
      }),
    );
    expect(
      await screen.findByText(/缓存设置已被其他操作修改/),
    ).toBeInTheDocument();
    expect(perDevice).toHaveValue('1000');
    expect(close).not.toHaveBeenCalled();
  });
});
