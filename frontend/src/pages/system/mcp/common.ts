import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError, getErrorKey } from '@/requestErrorConfig';
import type {
  McpAccessMode,
  McpConnectionMaterial,
  McpKey,
  McpPermission,
} from '@/services/syslog/mcp';

const errors: Record<string, string> = {
  mcp_migration_required: 'mcp.error.migration',
  mcp_disabled: 'mcp.error.stopped',
  mcp_key_invalid: 'mcp.error.invalidKey',
  mcp_key_inactive: 'mcp.error.invalidKey',
  mcp_key_revoked: 'mcp.error.invalidKey',
  mcp_key_expired: 'mcp.error.invalidKey',
  mcp_key_not_found: 'error.notFound',
  mcp_key_revoke_required: 'mcp.error.revokeFirst',
  mcp_key_limit: 'mcp.error.keyLimit',
  mcp_expiry_invalid: 'mcp.expiryInvalid',
  mcp_revoke_required: 'mcp.error.revokeFirst',
  mcp_permission_invalid: 'mcp.error.permissions',
  mcp_permissions_invalid: 'mcp.error.permissions',
  mcp_permission_denied: 'error.permission',
  mcp_audit_unavailable: 'mcp.error.audit',
  mcp_recovery_unavailable: 'mcp.error.recovery',
  version_conflict: 'mcp.error.conflict',
};

export function getMcpError(error: unknown): string {
  return (
    (error instanceof ApiError && errors[error.code]) || getErrorKey(error)
  );
}

export function useMcpQuery<T>(read: (signal: AbortSignal) => Promise<T>) {
  const [data, setData] = useState<T>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  const pending = useRef<AbortController | undefined>(undefined);
  const reload = useCallback(() => {
    pending.current?.abort();
    const request = new AbortController();
    pending.current = request;
    setLoading(true);
    setError(undefined);
    void read(request.signal)
      .then((value) => {
        if (!request.signal.aborted) setData(value);
      })
      .catch((failure) => {
        if (!request.signal.aborted) setError(getMcpError(failure));
      })
      .finally(() => {
        if (!request.signal.aborted) setLoading(false);
      });
  }, [read]);
  useEffect(() => {
    reload();
    return () => pending.current?.abort();
  }, [reload]);
  return { data, setData, loading, error, setError, reload };
}

export function keyStatus(key: McpKey, now = Date.now()): string {
  if (key.revokedAt) return 'revoked';
  if (key.expiresAt && new Date(key.expiresAt).getTime() <= now)
    return 'expired';
  return key.status;
}

// 过滤的是提交给后端的叶子标识，父分组永远不成为持久化通配授权。
export function allowedPermissions(
  selected: string[],
  permissions: McpPermission[],
  mode: McpAccessMode,
): string[] {
  const allowed = new Set(
    permissions
      .filter((item) => mode === 'read_write' || !item.isWrite)
      .map((item) => item.name),
  );
  return [...new Set(selected.filter((item) => allowed.has(item)))];
}

export function mergeVisiblePermissions(
  current: string[],
  checked: string[],
  visible: string[],
): string[] {
  const shown = new Set(visible);
  return [
    ...new Set([
      ...current.filter((item) => !shown.has(item)),
      ...checked.filter((item) => shown.has(item)),
    ]),
  ];
}

export function mcpEndpoint(origin: string): string {
  const url = new URL(origin);
  if (!['http:', 'https:'].includes(url.protocol))
    throw new Error('Unsupported MCP URL');
  return `${url.origin}/mcp`;
}

export function buildConnectionInstructions(
  material: McpConnectionMaterial,
  origin: string,
  intro: string,
): string {
  const endpoint = mcpEndpoint(origin);
  return `${intro}\n\n${JSON.stringify(
    {
      serverName: 'tongyan-syslog',
      transport: 'streamable-http',
      url: endpoint,
      headers: { Authorization: `Bearer ${material.secret}` },
      keyName: material.name,
      accessMode: material.accessMode,
      permissions: material.permissions,
      expiresAt: material.expiresAt ?? null,
    },
    null,
    2,
  )}`;
}

export function formatPayload(value: string, formatted: boolean): string {
  if (!formatted || !value) return value;
  try {
    return JSON.stringify(JSON.parse(value), null, 2);
  } catch {
    return value;
  }
}
