import { beforeEach, describe, expect, it, vi } from 'vitest';
import { apiRequest } from './api';
import {
  createMcpKey,
  deleteMcpKey,
  getMcpCall,
  getMcpCalls,
  getMcpConnectionMaterial,
  getMcpKeys,
  revokeMcpKey,
  setMcpEnabled,
  setMcpRetention,
} from './mcp';

vi.mock('./api', () => ({ apiRequest: vi.fn().mockResolvedValue({}) }));
beforeEach(() => vi.clearAllMocks());

describe('MCP 管理请求合同', () => {
  it('创建携带永久有效和细粒度权限，生命周期操作无编辑接口', async () => {
    const input = {
      name: '合成 Key',
      accessMode: 'read' as const,
      permissions: ['syslog_logs_search'],
      expiresAt: null,
    };
    await createMcpKey(input);
    expect(apiRequest).toHaveBeenLastCalledWith('/system/mcp/keys', {
      method: 'POST',
      body: input,
    });
    await revokeMcpKey('9007199254740993');
    expect(apiRequest).toHaveBeenLastCalledWith(
      '/system/mcp/keys/9007199254740993/revoke',
      { method: 'POST' },
    );
    await deleteMcpKey('9007199254740993');
    expect(apiRequest).toHaveBeenLastCalledWith(
      '/system/mcp/keys/9007199254740993',
      { method: 'DELETE' },
    );
    await getMcpConnectionMaterial('9007199254740993');
    expect(apiRequest).toHaveBeenLastCalledWith(
      '/system/mcp/keys/9007199254740993/connection-material',
      { method: 'POST' },
    );
  });

  it('设置修改传入完整字符串版本', async () => {
    await setMcpEnabled('9007199254740993', true);
    expect(apiRequest).toHaveBeenLastCalledWith('/system/mcp/enabled', {
      method: 'PUT',
      body: { version: '9007199254740993', enabled: true },
    });
    await setMcpRetention('9007199254740994', 30);
    expect(apiRequest).toHaveBeenLastCalledWith('/system/mcp/audit-retention', {
      method: 'PUT',
      body: { version: '9007199254740994', auditRetentionDays: 30 },
    });
  });

  it('审计查询使用游标，详情单独允许 JSON 转义后的 32 MiB 响应', async () => {
    await getMcpCalls({
      peerIp: '2001:db8::1',
      cursor: 'cursor+value',
      pageSize: 30,
    });
    expect(apiRequest).toHaveBeenLastCalledWith(
      '/system/mcp/calls?peerIp=2001%3Adb8%3A%3A1&cursor=cursor%2Bvalue&pageSize=30',
      { signal: undefined },
    );
    await getMcpCall('123');
    expect(apiRequest).toHaveBeenLastCalledWith('/system/mcp/calls/123', {
      signal: undefined,
      maxResponseBytes: 32 * 1024 * 1024,
    });
    await getMcpKeys();
    expect(apiRequest).toHaveBeenLastCalledWith('/system/mcp/keys', {
      signal: undefined,
    });
  });
});
