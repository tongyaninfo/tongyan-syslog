import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import sourceMessages from '@/locales/zh-CN';
import { ApiError } from '@/requestErrorConfig';
import { createSource, getSources, updateSource } from '@/services/syslog/api';
import type { LogSource } from '@/services/syslog/types';
import SourceEditor from './SourceEditor';

vi.mock('@/services/syslog/api', () => ({
  createSource: vi.fn(),
  getSources: vi.fn(),
  updateSource: vi.fn(),
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
  vi.clearAllMocks();
  vi.mocked(getSources).mockResolvedValue([source]);
  vi.mocked(updateSource).mockImplementation(async (_id, input) => ({
    ...input,
    id: source.id,
  }));
  vi.mocked(createSource).mockImplementation(async (input) => ({
    ...input,
    id: '43',
  }));
});

async function changeRetention() {
  fireEvent.click(screen.getByRole('button', { name: '2 年 · 730 天' }));
  fireEvent.click(screen.getByRole('button', { name: /^保\s*存$/ }));
  await screen.findByText('确认修改保留期限');
}

describe('来源与保留配置保存边界', () => {
  it('创建缺少必要字段时不发送请求', async () => {
    render(<SourceEditor open onCancel={vi.fn()} onSaved={vi.fn()} />);
    fireEvent.click(screen.getByRole('button', { name: /^保\s*存$/ }));
    await waitFor(() =>
      expect(screen.getAllByText('请填写此项').length).toBeGreaterThan(0),
    );
    expect(createSource).not.toHaveBeenCalled();
  });

  it.each([
    '0',
    '3651',
    '1.5',
  ])('保留期限 %s 不满足整数范围时不可保存', async (value) => {
    render(
      <SourceEditor
        open
        source={source}
        onCancel={vi.fn()}
        onSaved={vi.fn()}
      />,
    );
    fireEvent.change(screen.getByRole('spinbutton'), { target: { value } });
    fireEvent.click(screen.getByRole('button', { name: /^保\s*存$/ }));
    await screen.findByText('请输入 1～3650 的整数天数');
    expect(updateSource).not.toHaveBeenCalled();
  });

  it('改变保留期限先展示新日志作用范围，返回修改不会提交', async () => {
    render(
      <SourceEditor
        open
        source={source}
        onCancel={vi.fn()}
        onSaved={vi.fn()}
      />,
    );
    await changeRetention();
    expect(
      screen.getByText(
        '仅之后接收的新日志采用新期限。已有日志的到期时间保持不变，本操作不会删除历史日志。',
      ),
    ).toBeInTheDocument();
    expect(updateSource).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole('button', { name: '返回修改' }));
    expect(screen.getByRole('spinbutton')).toHaveValue('730');
    expect(updateSource).not.toHaveBeenCalled();
  });

  it('保存失败后保留用户输入，不假报成功', async () => {
    vi.mocked(updateSource).mockRejectedValueOnce(
      new ApiError('CONFLICT', 409),
    );
    const saved = vi.fn();
    render(
      <SourceEditor open source={source} onCancel={vi.fn()} onSaved={saved} />,
    );
    await changeRetention();
    fireEvent.click(screen.getByRole('button', { name: '确认并保存' }));
    await screen.findByText('保存失败，输入内容已保留');
    expect(screen.getByRole('spinbutton')).toHaveValue('730');
    expect(screen.getByLabelText('来源名称')).toHaveValue('合成交换机');
    expect(saved).not.toHaveBeenCalled();
    expect(updateSource).toHaveBeenCalledTimes(1);
  });

  it('修改忽略规则时确认选中的级别，并显示旧控制库的升级提示', async () => {
    vi.mocked(updateSource).mockRejectedValueOnce(
      new ApiError('source_filter_migration_required', 503),
    );
    const saved = vi.fn();
    render(
      <SourceEditor open source={source} onCancel={vi.fn()} onSaved={saved} />,
    );
    const select = screen.getByRole('combobox', { name: '忽略的严重级别' });
    fireEvent.mouseDown(select);
    fireEvent.click(
      await screen.findByText('7 · 调试', {
        selector: '.ant-select-item-option-content',
      }),
    );
    fireEvent.keyDown(select, { key: 'Escape', keyCode: 27 });
    fireEvent.click(screen.getByRole('button', { name: /^保\s*存$/ }));
    const confirmation = (await screen.findByText('确认保存来源配置')).closest(
      '[role="dialog"]',
    );
    expect(confirmation).toHaveTextContent('7 · 调试');
    expect(confirmation).toHaveTextContent(
      '没有严重级别的日志仍按正常流程处理',
    );
    expect(updateSource).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole('button', { name: '确认并保存' }));
    await screen.findByText('请先升级控制库，再保存忽略规则。');
    expect(updateSource).toHaveBeenCalledWith(
      source.id,
      expect.objectContaining({ ignoredSeverities: [7] }),
    );
    expect(saved).not.toHaveBeenCalled();
  });

  it('显式清空来源规则时提交空数组', async () => {
    const saved = vi.fn();
    render(
      <SourceEditor
        open
        source={{ ...source, ignoredSeverities: [7] }}
        onCancel={vi.fn()}
        onSaved={saved}
      />,
    );
    fireEvent.keyDown(
      screen.getByRole('combobox', { name: '忽略的严重级别' }),
      { key: 'Backspace', keyCode: 8 },
    );
    fireEvent.click(screen.getByRole('button', { name: /^保\s*存$/ }));
    expect(
      (await screen.findByText('确认保存来源配置')).closest('[role="dialog"]'),
    ).toHaveTextContent('不忽略任何级别');
    fireEvent.click(screen.getByRole('button', { name: '确认并保存' }));
    await waitFor(() => expect(saved).toHaveBeenCalled());
    expect(updateSource).toHaveBeenCalledWith(
      source.id,
      expect.objectContaining({ ignoredSeverities: [] }),
    );
  });

  it('独立留存页保存时合并最新来源配置，仅修改天数', async () => {
    const latest = {
      ...source,
      name: '更新后的合成设备',
      timeZoneId: 'UTC',
      enabled: false,
      ignoredSeverities: [7],
    };
    vi.mocked(getSources).mockResolvedValueOnce([latest]);
    const saved = vi.fn();
    render(
      <SourceEditor
        open
        source={source}
        retentionOnly
        onCancel={vi.fn()}
        onSaved={saved}
      />,
    );
    await changeRetention();
    fireEvent.click(screen.getByRole('button', { name: '确认并保存' }));
    await waitFor(() => expect(saved).toHaveBeenCalled());
    expect(updateSource).toHaveBeenCalledWith('42', {
      name: latest.name,
      peerAddress: latest.peerAddress,
      timeZoneId: 'UTC',
      enabled: false,
      retentionDays: 730,
    });
  });

  it('保留期限已被其他操作修改时要求重新打开编辑，避免覆盖', async () => {
    vi.mocked(getSources).mockResolvedValueOnce([
      { ...source, retentionDays: 1095 },
    ]);
    render(
      <SourceEditor
        open
        source={source}
        retentionOnly
        onCancel={vi.fn()}
        onSaved={vi.fn()}
      />,
    );
    await changeRetention();
    fireEvent.click(screen.getByRole('button', { name: '确认并保存' }));
    await screen.findByText('来源配置已更新，请重新打开编辑窗口后重试。');
    expect(updateSource).not.toHaveBeenCalled();
  });

  it('重复点击确认时只有一个正在提交的写请求', async () => {
    let resolve!: (value: LogSource) => void;
    vi.mocked(updateSource).mockReturnValueOnce(
      new Promise<LogSource>((complete) => {
        resolve = complete;
      }),
    );
    const saved = vi.fn();
    render(
      <SourceEditor open source={source} onCancel={vi.fn()} onSaved={saved} />,
    );
    await changeRetention();
    const confirm = screen.getByRole('button', { name: '确认并保存' });
    fireEvent.click(confirm);
    fireEvent.click(confirm);
    expect(updateSource).toHaveBeenCalledTimes(1);
    resolve({ ...source, retentionDays: 730 });
    await waitFor(() => expect(saved).toHaveBeenCalledTimes(1));
  });
});
