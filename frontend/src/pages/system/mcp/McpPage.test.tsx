import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import messages from '@/locales/zh-CN';
import { ApiError } from '@/requestErrorConfig';
import {
  createMcpKey,
  getMcpCall,
  getMcpConnectionMaterial,
  getMcpKeys,
  getMcpPermissions,
  getMcpService,
  type McpCallDetail,
  type McpKey,
  type McpPermission,
  setMcpEnabled,
} from '@/services/syslog/mcp';
import CallDetails from './CallDetails';
import ConnectionInstructions from './ConnectionInstructions';
import McpPage from './index';
import KeyEditor from './KeyEditor';
import KeyList from './KeyList';

vi.mock('@ant-design/pro-components', () => ({
  PageContainer: ({ children }: { children: React.ReactNode }) => children,
}));

vi.mock('@/services/syslog/mcp', () => ({
  createMcpKey: vi.fn(),
  getMcpCall: vi.fn(),
  getMcpConnectionMaterial: vi.fn(),
  getMcpKeys: vi.fn(),
  getMcpService: vi.fn(),
  getMcpPermissions: vi.fn(),
  setMcpEnabled: vi.fn(),
  revokeMcpKey: vi.fn(),
  deleteMcpKey: vi.fn(),
}));
vi.mock('@/utils/i18n', () => ({
  useText: () => (key: string, values?: Record<string, string | number>) => {
    let text = (messages as Record<string, string>)[key] ?? key;
    for (const [name, value] of Object.entries(values ?? {}))
      text = text.replaceAll(`{${name}}`, String(value));
    return text;
  },
}));

const permissions: McpPermission[] = [
  {
    name: 'syslog_logs_search',
    module: '日志',
    title: '查询日志',
    description: '检索合成日志',
    isWrite: false,
    isDestructive: false,
  },
  {
    name: 'syslog_logs_clear_all',
    module: '日志',
    title: '清空日志',
    description: '清空合成日志',
    isWrite: true,
    isDestructive: true,
  },
];
const key: McpKey = {
  id: '9007199254740993',
  name: '合成只读助手',
  prefix: 'syslog_synthetic',
  accessMode: 'read',
  permissions: ['syslog_logs_search'],
  createdBy: '9007199254740994',
  createdAt: '2026-11-11T00:00:00Z',
  status: 'active',
};
beforeEach(() => vi.clearAllMocks());

function checkPermission(title: string) {
  const checkbox = screen
    .getByText(title)
    .closest('.ant-tree-treenode')
    ?.querySelector('.ant-tree-checkbox');
  expect(checkbox).toBeTruthy();
  if (checkbox) fireEvent.click(checkbox);
}

describe('MCP 管理交互', () => {
  it('恢复故障展示明确提示并允许在已保存开启状态下重试启动', async () => {
    const service = {
      settings: { version: '1', enabled: true, auditRetentionDays: 30 },
      state: 'faulted',
      activeCalls: 0,
      toolCount: 39,
      recoveryError: 'mcp_audit_recovery_failed',
    };
    vi.mocked(getMcpService).mockResolvedValue(service);
    vi.mocked(getMcpPermissions).mockResolvedValue(permissions);
    vi.mocked(getMcpKeys).mockResolvedValue([]);
    vi.mocked(setMcpEnabled).mockResolvedValue({
      ...service,
      state: 'running',
      recoveryError: null,
    });
    render(<McpPage />);
    await screen.findByText(messages['mcp.error.recovery']);
    expect(screen.getByRole('button', { name: /停止服务/ })).toBeEnabled();
    fireEvent.click(screen.getByRole('button', { name: /重试启动/ }));
    await screen.findByText(messages['mcp.serviceState.running']);
    expect(setMcpEnabled).toHaveBeenCalledWith('1', true);
    expect(
      screen.queryByText(messages['mcp.error.recovery']),
    ).not.toBeInTheDocument();
  });

  it('创建只读 Key 时永久有效，写权限不可选，保存失败保留名称和授权', async () => {
    vi.mocked(createMcpKey).mockRejectedValue(
      new ApiError('mcp_migration_required', 503),
    );
    render(
      <KeyEditor
        permissions={permissions}
        onClose={vi.fn()}
        onCreated={vi.fn()}
      />,
    );
    fireEvent.change(screen.getByLabelText('Key 名称'), {
      target: { value: '合成只读助手' },
    });
    expect(
      screen.getByText('清空日志').closest('.ant-tree-treenode'),
    ).toHaveClass('ant-tree-treenode-disabled');
    checkPermission('查询日志');
    fireEvent.click(screen.getByRole('button', { name: '创建 Key' }));
    await screen.findByText(messages['mcp.error.migration']);
    expect(createMcpKey).toHaveBeenCalledWith({
      name: '合成只读助手',
      accessMode: 'read',
      permissions: ['syslog_logs_search'],
      expiresAt: null,
    });
    expect(screen.getByLabelText('Key 名称')).toHaveValue('合成只读助手');
    expect(screen.getByText('1 项')).toBeInTheDocument();
  });

  it('切回只读清除已选写权限，不把父节点传给后端', async () => {
    vi.mocked(createMcpKey).mockResolvedValue({
      key,
      secret: 'synthetic-key-only',
    });
    render(
      <KeyEditor
        permissions={permissions}
        onClose={vi.fn()}
        onCreated={vi.fn()}
      />,
    );
    fireEvent.change(screen.getByLabelText('Key 名称'), {
      target: { value: '合成只读助手' },
    });
    fireEvent.click(screen.getByLabelText('读写'));
    checkPermission('查询日志');
    checkPermission('清空日志');
    expect(screen.getByText('2 项')).toBeInTheDocument();
    fireEvent.click(screen.getByLabelText('只读'));
    expect(screen.getByText('1 项')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: '创建 Key' }));
    await waitFor(() =>
      expect(createMcpKey).toHaveBeenCalledWith(
        expect.objectContaining({
          accessMode: 'read',
          permissions: ['syslog_logs_search'],
        }),
      ),
    );
  });

  it('未选择任何功能不能创建 Key', async () => {
    render(
      <KeyEditor
        permissions={permissions}
        onClose={vi.fn()}
        onCreated={vi.fn()}
      />,
    );
    fireEvent.change(screen.getByLabelText('Key 名称'), {
      target: { value: '合成只读助手' },
    });
    fireEvent.click(screen.getByRole('button', { name: '创建 Key' }));
    await screen.findByText(messages['mcp.error.permissions']);
    expect(createMcpKey).not.toHaveBeenCalled();
  });

  it('Key 列表没有编辑操作，到期 Key 需先吊销，已吊销才显示删除', async () => {
    vi.mocked(getMcpKeys).mockResolvedValue([
      {
        ...key,
        name: '已到期样本',
        expiresAt: '2020-01-01T00:00:00Z',
        status: 'expired',
      },
      {
        ...key,
        id: '2',
        name: '已吊销样本',
        status: 'revoked',
        revokedAt: '2020-01-01T00:00:00Z',
      },
    ]);
    render(<KeyList permissions={permissions} permissionsReady />);
    const expired = (await screen.findByText('已到期样本')).closest('tr');
    const revoked = screen.getByText('已吊销样本').closest('tr');
    expect(expired).toBeTruthy();
    expect(revoked).toBeTruthy();
    if (expired && revoked) {
      expect(
        within(expired).getByRole('button', { name: /复制接入指令/ }),
      ).toBeDisabled();
      expect(
        within(expired).getByRole('button', { name: /吊\s*销/ }),
      ).toBeEnabled();
      expect(
        within(expired).queryByRole('button', { name: /删\s*除/ }),
      ).not.toBeInTheDocument();
      expect(
        within(revoked).getByRole('button', { name: /删\s*除/ }),
      ).toBeEnabled();
    }
    expect(
      screen.queryByRole('button', { name: /编辑/ }),
    ).not.toBeInTheDocument();
  });

  it('接入指令使用浏览器实际 origin，列表不长期展示 Key 原文', async () => {
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: undefined,
    });
    vi.mocked(getMcpKeys).mockResolvedValue([key]);
    vi.mocked(getMcpConnectionMaterial).mockResolvedValue({
      keyId: key.id,
      name: key.name,
      secret: 'synthetic-key-only',
      accessMode: 'read',
      permissions: key.permissions,
      expiresAt: null,
    });
    render(<KeyList permissions={permissions} permissionsReady />);
    await screen.findByText(key.name);
    expect(screen.queryByText('synthetic-key-only')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /复制接入指令/ }));
    const content = await screen.findByRole('textbox', {
      name: 'MCP 接入指令',
    });
    expect((content as HTMLTextAreaElement).value).toContain(
      `${window.location.origin}/mcp`,
    );
    expect((content as HTMLTextAreaElement).value).toContain(
      'Bearer synthetic-key-only',
    );
    expect(getMcpConnectionMaterial).toHaveBeenCalledWith(key.id);
  });

  it('剪贴板允许时列表操作直接复制，不需要第二次点击', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText },
    });
    vi.mocked(getMcpKeys).mockResolvedValue([key]);
    vi.mocked(getMcpConnectionMaterial).mockResolvedValue({
      keyId: key.id,
      name: key.name,
      secret: 'synthetic-key-only',
      accessMode: 'read',
      permissions: key.permissions,
    });
    render(<KeyList permissions={permissions} permissionsReady />);
    await screen.findByText(key.name);
    fireEvent.click(screen.getByRole('button', { name: /复制接入指令/ }));
    await screen.findByText(messages['mcp.copied']);
    expect(writeText).toHaveBeenCalledWith(
      expect.stringContaining(`${window.location.origin}/mcp`),
    );
    expect(
      screen.queryByRole('textbox', { name: 'MCP 接入指令' }),
    ).not.toBeInTheDocument();
  });

  it('剪贴板不可用时显示手动复制说明，不报告成功', async () => {
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText: vi.fn().mockRejectedValue(new Error('Not allowed')) },
    });
    render(
      <ConnectionInstructions text="synthetic instruction" onClose={vi.fn()} />,
    );
    fireEvent.click(screen.getByRole('button', { name: /复制指令/ }));
    await screen.findByText(messages['mcp.copyManual']);
    expect(screen.queryByText(messages['mcp.copied'])).not.toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'MCP 接入指令' })).toHaveValue(
      'synthetic instruction',
    );
  });

  it('完整显示合成认证头和响应原文，但不执行其中脚本', async () => {
    const detail: McpCallDetail = {
      summary: {
        id: '123',
        keyId: key.id,
        keyName: key.name,
        traceId: 'synthetic-trace',
        rpcId: '2',
        method: 'tools/call',
        toolName: 'syslog_logs_search',
        peerIp: '192.0.2.20',
        httpMethod: 'POST',
        url: '/mcp',
        httpProtocol: 'HTTP/1.1',
        startedAt: '2026-11-11T00:00:00Z',
        completedAt: '2026-11-11T00:00:01Z',
        durationMilliseconds: 1000,
        httpStatus: 200,
        outcome: 'success',
        requestBytes: 100,
        responseBytes: 100,
        requestComplete: true,
        responseComplete: true,
      },
      requestHeaders: {
        Authorization: ['Bearer synthetic-audit-key'],
        'X-Synthetic': ['one', 'two'],
      },
      requestBody: '{"name":"syslog_logs_search"}',
      responseHeaders: { 'Content-Type': ['application/json'] },
      responseBody: '{"message":"<script>window.mcpExecuted=true</script>"}',
    };
    vi.mocked(getMcpCall).mockResolvedValue(detail);
    const { container } = render(<CallDetails id="123" onClose={vi.fn()} />);
    await screen.findByText(/Bearer synthetic-audit-key/);
    expect(screen.getByText(/"one"/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('tab', { name: '响应' }));
    await screen.findByText(/window.mcpExecuted=true/);
    expect(container.querySelector('script')).toBeNull();
    expect(
      (window as unknown as Record<string, unknown>).mcpExecuted,
    ).toBeUndefined();
  });
});
