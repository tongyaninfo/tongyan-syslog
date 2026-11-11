import { fireEvent, render, screen, within } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import messages from '@/locales/zh-CN';
import { ApiError } from '@/requestErrorConfig';
import { clearLogs } from '@/services/syslog/api';
import ClearLogsAction from './ClearLogsAction';

vi.mock('@/services/syslog/api', () => ({ clearLogs: vi.fn() }));
vi.mock('@/utils/i18n', () => ({
  useText: () => (key: keyof typeof messages) => messages[key],
}));

beforeEach(() => vi.clearAllMocks());

async function openClearDialog(onCleared: () => void) {
  render(<ClearLogsAction onCleared={onCleared} />);
  fireEvent.click(screen.getByRole('button', { name: '清空日志' }));
  const dialog = within(await screen.findByRole('dialog'));
  return {
    dialog,
    confirmation: dialog.getByRole('textbox', { name: '请输入 CLEAR 确认' }),
    submit: dialog.getByRole('button', { name: '确认清空' }),
  };
}

describe('清空日志', () => {
  it('展示全库日志的影响范围，只在精确确认后提交并刷新', async () => {
    vi.mocked(clearLogs).mockResolvedValueOnce(undefined);
    const onCleared = vi.fn();
    const { dialog, confirmation, submit } = await openClearDialog(onCleared);
    expect(dialog.getByText(/涵盖当前筛选之外/)).toBeInTheDocument();
    expect(dialog.getByText(/待入库日志可能稍后出现/)).toBeInTheDocument();
    expect(submit).toBeDisabled();
    fireEvent.change(confirmation, { target: { value: 'CLEAR ' } });
    expect(submit).toBeDisabled();
    expect(clearLogs).not.toHaveBeenCalled();
    fireEvent.change(confirmation, { target: { value: 'CLEAR' } });
    fireEvent.click(submit);
    await screen.findByText('已清空日志。');
    expect(clearLogs).toHaveBeenCalledExactlyOnceWith('CLEAR');
    expect(onCleared).toHaveBeenCalledOnce();
  });

  it('请求断开后提示先核对结果，并要求重新确认', async () => {
    vi.mocked(clearLogs).mockRejectedValueOnce(
      new ApiError('network_unavailable', 0),
    );
    const onCleared = vi.fn();
    const { dialog, confirmation, submit } = await openClearDialog(onCleared);
    fireEvent.change(confirmation, { target: { value: 'CLEAR' } });
    fireEvent.click(submit);
    await dialog.findByText(/无法确认清空结果/);
    expect(confirmation).toHaveValue('');
    expect(submit).toBeDisabled();
    expect(clearLogs).toHaveBeenCalledTimes(1);
    expect(onCleared).not.toHaveBeenCalled();
  });
});
