import { apiRequest } from './api';

export type McpAccessMode = 'read' | 'read_write';

export interface McpSettings {
  version: string;
  enabled: boolean;
  auditRetentionDays: number;
}

export interface McpServiceView {
  settings: McpSettings;
  state: string;
  activeCalls: number;
  toolCount: number;
  lastCleanupAt?: string | null;
  cleanupError?: string | null;
  recoveryError?: string | null;
}

export interface McpPermission {
  name: string;
  title: string;
  module: string;
  description: string;
  isWrite: boolean;
  isDestructive: boolean;
}

export interface McpKey {
  id: string;
  name: string;
  prefix: string;
  accessMode: McpAccessMode;
  permissions: string[];
  createdBy: string;
  createdAt: string;
  expiresAt?: string | null;
  revokedAt?: string | null;
  lastUsedAt?: string | null;
  status: string;
}

export interface CreateMcpKeyInput {
  name: string;
  accessMode: McpAccessMode;
  permissions: string[];
  expiresAt: string | null;
}

export interface McpConnectionMaterial {
  keyId: string;
  name: string;
  secret: string;
  accessMode: McpAccessMode;
  permissions: string[];
  expiresAt?: string | null;
}

export interface McpCallSummary {
  id: string;
  keyId?: string | null;
  keyName?: string | null;
  actorId?: string | null;
  actorName?: string | null;
  traceId: string;
  rpcId?: string | null;
  method?: string | null;
  toolName?: string | null;
  peerIp: string;
  httpMethod: string;
  url: string;
  httpProtocol: string;
  startedAt: string;
  completedAt?: string | null;
  durationMilliseconds?: number | null;
  httpStatus?: number | null;
  outcome: string;
  code?: string | null;
  requestBytes: number;
  responseBytes?: number | null;
  requestComplete: boolean;
  responseComplete?: boolean | null;
  captureError?: string | null;
}

export interface McpCallDetail {
  summary: McpCallSummary;
  requestHeaders: Record<string, string[]>;
  requestBody: string;
  responseHeaders: Record<string, string[]>;
  responseBody?: string | null;
}

export interface McpCallQuery {
  from?: string;
  to?: string;
  keyId?: string;
  toolName?: string;
  method?: string;
  peerIp?: string;
  outcome?: string;
  cursor?: string;
  pageSize?: number;
}

export interface McpCallPage {
  items: McpCallSummary[];
  nextCursor?: string | null;
  hasMore: boolean;
}

const base = '/system/mcp';
export const getMcpService = (signal?: AbortSignal) =>
  apiRequest<McpServiceView>(base, { signal });
export const setMcpEnabled = (version: string, enabled: boolean) =>
  apiRequest<McpServiceView>(`${base}/enabled`, {
    method: 'PUT',
    body: { version, enabled },
  });
export const setMcpRetention = (version: string, auditRetentionDays: number) =>
  apiRequest<McpSettings>(`${base}/audit-retention`, {
    method: 'PUT',
    body: { version, auditRetentionDays },
  });
export const getMcpPermissions = (signal?: AbortSignal) =>
  apiRequest<McpPermission[]>(`${base}/permissions`, { signal });
export const getMcpKeys = (signal?: AbortSignal) =>
  apiRequest<McpKey[]>(`${base}/keys`, { signal });
export const createMcpKey = (body: CreateMcpKeyInput) =>
  apiRequest<{ key: McpKey; secret: string }>(`${base}/keys`, {
    method: 'POST',
    body,
  });
export const revokeMcpKey = (id: string) =>
  apiRequest<boolean>(`${base}/keys/${encodeURIComponent(id)}/revoke`, {
    method: 'POST',
  });
export const deleteMcpKey = (id: string) =>
  apiRequest<boolean>(`${base}/keys/${encodeURIComponent(id)}`, {
    method: 'DELETE',
  });
export const getMcpConnectionMaterial = (id: string) =>
  apiRequest<McpConnectionMaterial>(
    `${base}/keys/${encodeURIComponent(id)}/connection-material`,
    { method: 'POST' },
  );
export const getMcpCalls = (query: McpCallQuery, signal?: AbortSignal) => {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== '') params.set(key, String(value));
  }
  return apiRequest<McpCallPage>(`${base}/calls?${params}`, { signal });
};
export const getMcpCall = (id: string, signal?: AbortSignal) =>
  apiRequest<McpCallDetail>(`${base}/calls/${encodeURIComponent(id)}`, {
    signal,
    maxResponseBytes: 32 * 1024 * 1024,
  });
