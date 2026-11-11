import { ApiError } from '@/requestErrorConfig';
import type {
  ApiResult,
  CurrentUser,
  HttpsCertificateSettings,
  IngestStatus,
  ListenerProtocol,
  ListenerSettings,
  ListenerSettingsUpdate,
  LogEvent,
  LogSearch,
  LogSearchPage,
  LogSource,
  ManagedUser,
  SetupStatus,
  SourceInput,
  UnregisteredDevices,
  UnregisteredLogSample,
  UnregisteredLogSummary,
  UserInput,
  UserPage,
  UserQuery,
  UserUpdate,
} from './types';

interface RequestOptions {
  method?: string;
  body?: unknown;
  signal?: AbortSignal;
  anonymous?: boolean;
  maxResponseBytes?: number;
}
export const SESSION_EXPIRED_EVENT = 'syslog:session-expired';

/** 只访问同源管理接口。凭据由 HttpOnly Cookie 管理，不读取或复制 token。 */
export async function apiRequest<T>(
  path: string,
  options: RequestOptions = {},
): Promise<T> {
  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), 15000);
  const cancel = () => controller.abort(options.signal?.reason);
  options.signal?.addEventListener('abort', cancel, { once: true });
  if (options.signal?.aborted) cancel();
  const method = options.method ?? 'GET';
  const { body } = options;
  const isForm = body instanceof FormData;
  const headers: Record<string, string> = { Accept: 'application/json' };
  if (method !== 'GET') {
    headers['X-Syslog-Request'] = '1';
    // multipart 边界由浏览器生成，同源写入校验与 JSON 请求共用。
    if (!isForm) headers['Content-Type'] = 'application/json';
  }
  try {
    const response = await fetch(`/api/admin/v1${path}`, {
      method,
      credentials: 'same-origin',
      signal: controller.signal,
      headers,
      body: isForm ? body : JSON.stringify(body),
    });
    if (response.status === 401 && !options.anonymous)
      window.dispatchEvent(new Event(SESSION_EXPIRED_EVENT));
    // 代理可能返回HTML或不符合合同的正文，不能把它直接显示为用户提示。
    let payload: ApiResult<T>;
    try {
      payload = await readJsonBounded(response, options.maxResponseBytes);
    } catch (error) {
      // 收到响应头后仍可能超时，保留取消原因交外层统一分类。
      if (controller.signal.aborted) throw error;
      throw new ApiError('invalid_response', response.status);
    }
    if (
      typeof payload !== 'object' ||
      payload === null ||
      typeof payload.success !== 'boolean'
    )
      throw new ApiError('invalid_response', response.status);
    if (!response.ok || payload.success !== true)
      throw new ApiError(
        typeof payload.code === 'string' ? payload.code : 'service_unavailable',
        response.status,
      );
    if (!Object.hasOwn(payload, 'data'))
      throw new ApiError('invalid_response', response.status);
    return payload.data;
  } catch (error) {
    if (options.signal?.aborted)
      throw new DOMException('Canceled', 'AbortError');
    if (error instanceof ApiError) throw error;
    throw new ApiError(
      controller.signal.aborted ? 'request_timeout' : 'network_unavailable',
      0,
    );
  } finally {
    clearTimeout(timeout);
    options.signal?.removeEventListener('abort', cancel);
  }
}

async function readJsonBounded<T>(
  response: Response,
  responseLimit?: number,
): Promise<T> {
  // 仅完整 MCP 审计详情扩大预算，以容纳原文再次 JSON 转义后的体积。
  const maximum = Math.min(responseLimit ?? 20 * 1024 * 1024, 32 * 1024 * 1024);
  if (
    Number(response.headers.get('Content-Length')) > maximum ||
    !response.body
  )
    throw new Error('invalid_response');
  const reader = response.body.getReader();
  const chunks: Uint8Array[] = [];
  let total = 0;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      total += value.byteLength;
      if (total > maximum) {
        await reader.cancel();
        throw new Error('response_too_large');
      }
      chunks.push(value);
    }
  } finally {
    reader.releaseLock();
  }
  const bytes = new Uint8Array(total);
  let position = 0;
  for (const chunk of chunks) {
    bytes.set(chunk, position);
    position += chunk.byteLength;
  }
  return JSON.parse(new TextDecoder().decode(bytes)) as T;
}

export const getSession = (signal?: AbortSignal) =>
  apiRequest<CurrentUser>('/session', { signal, anonymous: true });
export const getSetupStatus = (signal?: AbortSignal) =>
  apiRequest<SetupStatus>('/setup/status', { signal, anonymous: true });
export const initializeAdmin = (input: UserInput) =>
  apiRequest<CurrentUser>('/setup/admin', {
    method: 'POST',
    body: input,
    anonymous: true,
  });
export const getUsers = (query: UserQuery, signal?: AbortSignal) => {
  const params = new URLSearchParams({
    page: String(query.page),
    pageSize: String(query.pageSize),
  });
  if (query.search) params.set('search', query.search);
  if (query.enabled !== undefined) params.set('enabled', String(query.enabled));
  return apiRequest<UserPage>(`/users?${params}`, { signal });
};
export const createUser = (input: UserInput) =>
  apiRequest<ManagedUser>('/users', { method: 'POST', body: input });
export const updateUser = (id: string, input: UserUpdate) =>
  apiRequest<ManagedUser>(`/users/${encodeURIComponent(id)}`, {
    method: 'PUT',
    body: input,
  });
export const resetUserPassword = (
  id: string,
  newPassword: string,
  expectedVersion: string,
) =>
  apiRequest<boolean>(`/users/${encodeURIComponent(id)}/password`, {
    method: 'POST',
    body: { newPassword, expectedVersion },
  });
export const login = (username: string, password: string) =>
  apiRequest<CurrentUser>('/session/login', {
    method: 'POST',
    body: { username, password },
    anonymous: true,
  });
export const logout = () =>
  apiRequest<boolean>('/session/logout', { method: 'POST' });
export const changePassword = (currentPassword: string, newPassword: string) =>
  apiRequest<boolean>('/session/password', {
    method: 'POST',
    body: { currentPassword, newPassword },
  });
export const getSources = (signal?: AbortSignal) =>
  apiRequest<LogSource[]>('/sources', { signal });
export const getUnregisteredDevices = (signal?: AbortSignal) =>
  apiRequest<UnregisteredDevices>('/unregistered-devices', { signal });
export const getUnregisteredLogs = (
  peerAddress: string,
  signal?: AbortSignal,
) =>
  apiRequest<UnregisteredLogSummary[]>(
    `/unregistered-devices/logs?${new URLSearchParams({ peerAddress })}`,
    { signal },
  );
export const getUnregisteredSample = (
  peerAddress: string,
  sampleKey: string,
  signal?: AbortSignal,
) =>
  apiRequest<UnregisteredLogSample>(
    `/unregistered-devices/sample?${new URLSearchParams({ peerAddress, sampleKey })}`,
    { signal },
  );
export const createSource = (input: SourceInput) =>
  apiRequest<LogSource>('/sources', { method: 'POST', body: input });
export const updateSource = (id: string, input: SourceInput) =>
  apiRequest<LogSource>(`/sources/${encodeURIComponent(id)}`, {
    method: 'PUT',
    body: input,
  });
export const getStatus = (signal?: AbortSignal) =>
  apiRequest<IngestStatus>('/system/status', { signal });
export const getListenerSettings = (signal?: AbortSignal) =>
  apiRequest<ListenerSettings>('/system/listeners', { signal });
export const getHttpsCertificateSettings = (signal?: AbortSignal) =>
  apiRequest<HttpsCertificateSettings>('/system/https-certificate', { signal });
export const uploadHttpsCertificate = (
  file: File,
  password: string,
  signal?: AbortSignal,
) => {
  const body = new FormData();
  body.set('file', file);
  body.set('password', password);
  return apiRequest<HttpsCertificateSettings>('/system/https-certificate', {
    method: 'POST',
    body,
    signal,
  });
};
export const saveListenerSettings = (input: ListenerSettingsUpdate) =>
  apiRequest<ListenerSettings>('/system/listeners', {
    method: 'PUT',
    body: input,
  });
export const setListenerReception = (enabled: boolean, version: string) =>
  apiRequest<ListenerSettings>('/system/listeners/reception', {
    method: 'PUT',
    body: { enabled, version },
  });
export const setListenerProtocol = (
  protocol: ListenerProtocol,
  enabled: boolean,
  version: string,
) =>
  apiRequest<ListenerSettings>(`/system/listeners/reception/${protocol}`, {
    method: 'PUT',
    body: { enabled, version },
  });
export const searchLogs = (query: LogSearch, signal?: AbortSignal) =>
  apiRequest<LogSearchPage>('/logs/search', {
    method: 'POST',
    body: query,
    signal,
  });
export const clearLogs = (confirmation: string) =>
  clearLogsAt('/logs/clear', confirmation);
export const clearSourceLogs = (sourceId: string, confirmation: string) =>
  clearLogsAt(
    `/sources/${encodeURIComponent(sourceId)}/logs/clear`,
    confirmation,
  );

async function clearLogsAt(path: string, confirmation: string): Promise<void> {
  const cleared = await apiRequest<boolean>(path, {
    method: 'POST',
    body: { confirmation },
  });
  if (cleared !== true) throw new ApiError('log_clear_outcome_unknown', 200);
}
export const getLog = (id: string, receivedAt: string, signal?: AbortSignal) =>
  apiRequest<LogEvent>(
    `/logs/${encodeURIComponent(id)}?${new URLSearchParams({ receivedAt })}`,
    { signal },
  );
