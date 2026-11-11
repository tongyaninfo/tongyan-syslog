import { describe, expect, it } from 'vitest';
import type { McpKey, McpPermission } from '@/services/syslog/mcp';
import {
  allowedPermissions,
  buildConnectionInstructions,
  formatPayload,
  keyStatus,
  mcpEndpoint,
  mergeVisiblePermissions,
} from './common';

const permissions: McpPermission[] = [
  {
    name: 'syslog_logs_search',
    module: '日志',
    title: '查询日志',
    description: '只读查询',
    isWrite: false,
    isDestructive: false,
  },
  {
    name: 'syslog_logs_clear_all',
    module: '日志',
    title: '清空日志',
    description: '清空全部日志',
    isWrite: true,
    isDestructive: true,
  },
  {
    name: 'syslog_sources_list',
    module: '来源',
    title: '查询来源',
    description: '只读查询来源',
    isWrite: false,
    isDestructive: false,
  },
];

describe('MCP 接入和授权边界', () => {
  it.each([
    [
      'http://192.0.2.5:8030/system/mcp?tab=keys#details',
      'http://192.0.2.5:8030/mcp',
    ],
    [
      'https://syslog.example.test:7443/system/mcp',
      'https://syslog.example.test:7443/mcp',
    ],
    [
      'https://[2001:db8::10]:7443/system/mcp',
      'https://[2001:db8::10]:7443/mcp',
    ],
    [
      'https://syslog.example.test:443/system/mcp',
      'https://syslog.example.test/mcp',
    ],
  ])('使用浏览器入口 %s', (origin, endpoint) =>
    expect(mcpEndpoint(origin)).toBe(endpoint));

  it('接入文本携带当前入口及明确认证，不使用管理路径', () => {
    const text = buildConnectionInstructions(
      {
        keyId: '9007199254740993',
        name: '测试 Key',
        secret: 'synthetic-key-only',
        accessMode: 'read',
        permissions: ['syslog_logs_search'],
        expiresAt: null,
      },
      'https://browser.example.test:7443/system/mcp',
      '按客户端配置并验证工具列表',
    );
    expect(text).toContain('https://browser.example.test:7443/mcp');
    expect(text).toContain('Bearer synthetic-key-only');
    expect(text).not.toContain('/system/mcp');
    expect(text).toContain('"expiresAt": null');
  });

  it('只读模式过滤写权限、父分组及未知权限', () => {
    const input = [
      'module:日志',
      'syslog_logs_search',
      'syslog_logs_clear_all',
      'unexpected',
      'syslog_logs_search',
    ];
    expect(allowedPermissions(input, permissions, 'read')).toEqual([
      'syslog_logs_search',
    ]);
    expect(allowedPermissions(input, permissions, 'read_write')).toEqual([
      'syslog_logs_search',
      'syslog_logs_clear_all',
    ]);
  });

  it('过滤树后取消可见权限，保留其他搜索范围内已经勾选的权限', () => {
    expect(
      mergeVisiblePermissions(
        ['syslog_logs_search', 'syslog_sources_list'],
        [],
        ['syslog_logs_search'],
      ),
    ).toEqual(['syslog_sources_list']);
  });

  it('到期与吊销分开处理，吊销优先', () => {
    const key = {
      status: 'active',
      revokedAt: null,
      expiresAt: '2026-11-11T00:00:00Z',
    } as McpKey;
    const now = new Date('2026-11-11T01:00:00Z').getTime();
    expect(keyStatus(key, now)).toBe('expired');
    expect(keyStatus({ ...key, revokedAt: '2026-11-10T00:00:00Z' }, now)).toBe(
      'revoked',
    );
    expect(keyStatus({ ...key, expiresAt: null }, now)).toBe('active');
    expect(
      keyStatus({ ...key, expiresAt: null, status: 'owner_disabled' }, now),
    ).toBe('owner_disabled');
  });

  it('原文视图不改变换行和空白，非法 JSON 也不丢弃', () => {
    const raw = '  {"Authorization":"synthetic-value"}\r\n';
    expect(formatPayload(raw, false)).toBe(raw);
    expect(formatPayload(raw, true)).toContain('synthetic-value');
    expect(formatPayload('<script>synthetic</script>', true)).toBe(
      '<script>synthetic</script>',
    );
  });
});
