import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { expect, it, vi } from 'vitest';
import messages from '@/locales/zh-CN';
import type { SmtpSettingsView } from '@/services/syslog/notifications';
import SmtpSettings from './SmtpSettings';

const api = vi.hoisted(() => ({ get: vi.fn(), save: vi.fn(), test: vi.fn() }));

vi.mock('@/services/syslog/notifications', () => ({
  getSmtpSettings: api.get,
  saveSmtpSettings: api.save,
  sendTestMail: api.test,
}));

vi.mock('@/utils/i18n', () => ({
  useText: () => (key: keyof typeof messages) => messages[key] ?? key,
}));

it('显示实际主题预览，清空前缀时不回传原授权码，测试仅提交收件地址', async () => {
  const settings: SmtpSettingsView = {
    version: 1,
    host: 'smtp.example.com',
    port: 465,
    security: 'ssl',
    username: 'syslog',
    senderAddress: 'syslog@example.com',
    senderName: 'Syslog',
    subjectPrefix: '[通研 Syslog]',
    hasPassword: true,
  };
  api.get.mockResolvedValue(settings);
  api.save.mockResolvedValue({ ...settings, subjectPrefix: '', version: 2 });
  api.test.mockResolvedValue({ id: '200' });
  const openHistory = vi.fn();
  render(<SmtpSettings onViewDelivery={openHistory} />);

  expect(
    await screen.findByText('[通研 Syslog] SMTP 测试邮件'),
  ).toBeInTheDocument();
  expect(screen.getByRole('checkbox', { name: '启用 SSL/TLS' })).toBeChecked();
  expect(screen.getByLabelText('SMTP 授权码')).toHaveValue('');
  fireEvent.change(screen.getByLabelText('邮件主题前缀'), {
    target: { value: '' },
  });
  expect(
    await screen.findByText('SMTP 测试邮件', { exact: true }),
  ).toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: /^保\s*存$/ }));

  await waitFor(() => expect(api.save).toHaveBeenCalledOnce());
  expect(api.save.mock.calls[0][0]).toMatchObject({
    version: 1,
    subjectPrefix: '',
  });
  expect(api.save.mock.calls[0][0]).not.toHaveProperty('password');
  expect(await screen.findByText('邮件服务设置已保存。')).toBeInTheDocument();

  fireEvent.change(screen.getByLabelText('SMTP 服务器'), {
    target: { value: 'unsaved.example.com' },
  });
  fireEvent.change(screen.getByLabelText('收件人邮箱'), {
    target: { value: 'recipient@example.com' },
  });
  fireEvent.click(screen.getByRole('button', { name: '发送测试邮件' }));
  expect(
    await screen.findByText('测试邮件已加入发送队列。'),
  ).toBeInTheDocument();
  expect(api.test).toHaveBeenCalledWith('recipient@example.com');
  fireEvent.click(screen.getByRole('button', { name: '发送记录' }));
  expect(openHistory).toHaveBeenCalledWith('200');
});
