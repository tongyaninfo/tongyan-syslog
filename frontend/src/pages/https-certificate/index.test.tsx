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
import type { HttpsCertificateSettings } from '@/services/syslog/types';
import HttpsCertificatePage from './index';

const api = vi.hoisted(() => ({ read: vi.fn(), upload: vi.fn() }));
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
  useText: () => (key: keyof typeof messages) => messages[key] ?? key,
}));
vi.mock('@/services/syslog/api', () => ({
  getHttpsCertificateSettings: api.read,
  uploadHttpsCertificate: api.upload,
}));

const initial: HttpsCertificateSettings = {
  current: null,
  pending: null,
  restartRequired: false,
  loadError: null,
  canUpload: true,
};
const certificate = {
  fingerprint: 'SYNTHETIC-FINGERPRINT',
  subject: 'CN=syslog.example',
  notBefore: '2026-10-01T00:00:00Z',
  notAfter: '2027-10-01T00:00:00Z',
};

beforeEach(() => {
  api.read.mockReset();
  api.upload.mockReset();
  api.read.mockResolvedValue(initial);
});

async function selectCertificate() {
  const chooseFile = await screen.findByRole('button', {
    name: '选择证书文件',
  });
  expect(screen.getByLabelText('证书文件')).toBe(chooseFile);
  const input = screen.getByTestId('https-certificate-file-input');
  expect(input).toHaveStyle({ display: 'none' });
  expect(input).not.toBeVisible();
  const openPicker = vi.spyOn(input, 'click');
  fireEvent.click(chooseFile);
  expect(openPicker).toHaveBeenCalledOnce();
  openPicker.mockRestore();
  const file = new File(['synthetic'], 'test.pfx');
  fireEvent.change(input, { target: { files: [file] } });
  expect(screen.getByText('test.pfx')).toBeInTheDocument();
  expect(
    screen.queryByLabelText('管理台访问域名或 IP'),
  ).not.toBeInTheDocument();
  return file;
}

async function confirmUpload() {
  fireEvent.click(screen.getByRole('button', { name: '保存证书' }));
  const dialog = await screen.findByRole('dialog', {
    name: '确认保存 HTTPS 证书',
  });
  expect(
    within(dialog).getByText('保存后，管理台的新 HTTPS 连接将自动使用新证书。'),
  ).toBeInTheDocument();
  expect(api.upload).not.toHaveBeenCalled();
  fireEvent.click(within(dialog).getByRole('button', { name: '保存证书' }));
}

async function enterCertificate() {
  const file = await selectCertificate();
  const password = screen.getByLabelText('证书密码（可选）');
  fireEvent.change(password, { target: { value: 'synthetic-password' } });
  await confirmUpload();
  return { file, password };
}

describe('管理台 HTTPS 证书', () => {
  it('确认后上传，提示新旧连接使用的证书并清除密码', async () => {
    api.upload.mockResolvedValue({ ...initial, current: certificate });
    render(<HttpsCertificatePage />);
    const { file, password } = await enterCertificate();
    expect(
      await screen.findByText(
        /新建 HTTPS 连接会使用新证书。已有连接会保留原证书直至断开。/,
      ),
    ).toBeInTheDocument();
    expect(screen.getByText(certificate.fingerprint)).toBeInTheDocument();
    expect(screen.getByText('暂无待生效证书。')).toBeInTheDocument();
    expect(api.upload).toHaveBeenCalledWith(
      file,
      'synthetic-password',
      expect.any(AbortSignal),
    );
    expect(password).toHaveValue('');
    expect(screen.queryByText('test.pfx')).not.toBeInTheDocument();
  });

  it('从 UTF-8 TXT 读取密码并保留首尾空格，上传时仅提交读取后的密码', async () => {
    api.upload.mockResolvedValue({ ...initial, current: certificate });
    render(<HttpsCertificatePage />);
    const file = await selectCertificate();
    const password = screen.getByLabelText('证书密码（可选）');
    const input = screen.getByTestId('https-certificate-password-file');
    expect(input).toHaveStyle({ display: 'none' });
    const openPicker = vi.spyOn(input, 'click');
    fireEvent.click(screen.getByRole('button', { name: '读取密码文件' }));
    expect(openPicker).toHaveBeenCalledOnce();
    openPicker.mockRestore();
    fireEvent.change(input, {
      target: { files: [new File(['\uFEFF  密码  \r\n'], 'password.txt')] },
    });
    expect(screen.getByRole('button', { name: '保存证书' })).toBeDisabled();
    await waitFor(() => expect(password).toHaveValue('  密码  '));
    await confirmUpload();
    expect(api.upload).toHaveBeenCalledWith(
      file,
      '  密码  ',
      expect.any(AbortSignal),
    );
    await waitFor(() => expect(password).toHaveValue(''));
  });

  it.each([
    ['多行', 'first\nsecond'],
    ['超长', 'a'.repeat(1025)],
    ['无效 UTF-8', new Uint8Array([0xff])],
  ])('取消选择或读取%s文件时保留手输密码', async (_label, contents) => {
    render(<HttpsCertificatePage />);
    const password = await screen.findByLabelText('证书密码（可选）');
    fireEvent.change(password, { target: { value: 'manually-entered' } });
    const input = screen.getByTestId('https-certificate-password-file');
    fireEvent.change(input, { target: { files: [] } });
    expect(password).toHaveValue('manually-entered');
    fireEvent.change(input, {
      target: { files: [new File([contents], 'password.txt')] },
    });
    expect(
      await screen.findByText(messages['httpsCertificate.passwordFileInvalid']),
    ).toBeInTheDocument();
    expect(password).toHaveValue('manually-entered');
    expect(api.upload).not.toHaveBeenCalled();
  });

  it('等待密码文件读取结束后再清空选择，超时后保留手输密码', async () => {
    render(<HttpsCertificatePage />);
    const password = await screen.findByLabelText('证书密码（可选）');
    fireEvent.change(password, { target: { value: 'manually-entered' } });
    const input = screen.getByTestId(
      'https-certificate-password-file',
    ) as HTMLInputElement;
    const clearSelection = vi.spyOn(input, 'value', 'set');
    const file = new File(['late-password'], 'password.txt');
    const bytes = Promise.withResolvers<ArrayBuffer>();
    vi.spyOn(file, 'arrayBuffer').mockReturnValue(bytes.promise);
    vi.useFakeTimers();
    try {
      fireEvent.change(input, { target: { files: [file] } });
      expect(clearSelection).not.toHaveBeenCalled();
      const save = screen.getByRole('button', { name: '保存证书' });
      expect(save).toBeDisabled();
      await act(async () => {
        await vi.advanceTimersByTimeAsync(5000);
      });
      expect(clearSelection).toHaveBeenCalledWith('');
      expect(save).toBeEnabled();
      expect(
        screen.getByText(messages['httpsCertificate.passwordFileInvalid']),
      ).toBeInTheDocument();
      await act(async () =>
        bytes.resolve(new TextEncoder().encode('late-password').buffer),
      );
      expect(password).toHaveValue('manually-entered');
      expect(api.upload).not.toHaveBeenCalled();
    } finally {
      vi.useRealTimers();
      clearSelection.mockRestore();
    }
  });

  it('保留待生效状态，避免对旧版接口的响应误报成功', async () => {
    api.upload.mockResolvedValue({
      ...initial,
      pending: certificate,
      restartRequired: true,
    });
    render(<HttpsCertificatePage />);
    await enterCertificate();
    expect(
      await screen.findByText('证书已保存，等待生效。'),
    ).toBeInTheDocument();
    expect(
      screen.queryByText(messages['httpsCertificate.saved']),
    ).not.toBeInTheDocument();
  });

  it('错误密码使用脱敏提示，清除密码并保留证书文件', async () => {
    api.upload.mockRejectedValue(
      new ApiError('https_certificate_invalid', 400),
    );
    render(<HttpsCertificatePage />);
    const { password } = await enterCertificate();
    await waitFor(() => expect(password).toHaveValue(''));
    expect(
      screen.getByText(
        '证书或密码无效，请检查文件是否含有可用私钥且仍在有效期内。',
      ),
    ).toBeInTheDocument();
    expect(screen.getByText('test.pfx')).toBeInTheDocument();
    expect(
      screen.queryByText(messages['httpsCertificate.saved']),
    ).not.toBeInTheDocument();
  });
});
