import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError, getErrorMessage } from '@/requestErrorConfig';
import {
  apiRequest,
  clearLogs,
  clearSourceLogs,
  createUser,
  getHttpsCertificateSettings,
  getListenerSettings,
  getLog,
  getSession,
  getSetupStatus,
  getUnregisteredDevices,
  getUnregisteredLogs,
  getUnregisteredSample,
  getUsers,
  initializeAdmin,
  login,
  resetUserPassword,
  SESSION_EXPIRED_EVENT,
  saveListenerSettings,
  searchLogs,
  setListenerReception,
  updateUser,
  uploadHttpsCertificate,
} from './api';

afterEach(() => {
  vi.restoreAllMocks();
  vi.useRealTimers();
});
const json = (data: unknown, status = 200) =>
  new Response(JSON.stringify(data), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });

describe('同源接口合同', () => {
  it('证书上传仅提交文件和密码，浏览器负责生成 multipart 边界', async () => {
    const fetcher = vi
      .spyOn(globalThis, 'fetch')
      .mockImplementation(async () => json({ success: true, data: {} }));
    await getHttpsCertificateSettings();
    const file = new File(['synthetic-certificate'], 'synthetic.p12');
    await uploadHttpsCertificate(file, 'synthetic-password');
    expect(fetcher.mock.calls[0][0]).toBe(
      '/api/admin/v1/system/https-certificate',
    );
    expect(fetcher.mock.calls[0][1]?.method).toBe('GET');
    const [path, init] = fetcher.mock.calls[1];
    expect(path).toBe('/api/admin/v1/system/https-certificate');
    expect(init).toMatchObject({
      method: 'POST',
      credentials: 'same-origin',
      headers: { Accept: 'application/json', 'X-Syslog-Request': '1' },
    });
    expect(new Headers(init?.headers).has('Content-Type')).toBe(false);
    expect(init?.body).toBeInstanceOf(FormData);
    const form = init?.body as FormData;
    expect(form.get('file')).toEqual(file);
    expect(form.get('password')).toBe('synthetic-password');
    expect([...form.keys()]).toEqual(['file', 'password']);
  });

  it('清空日志只提交确认词，并要求服务端明确确认成功', async () => {
    const fetcher = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(json({ success: true, data: true }))
      .mockResolvedValueOnce(json({ success: true, data: false }));
    await clearLogs('CLEAR');
    expect(fetcher.mock.calls[0][0]).toBe('/api/admin/v1/logs/clear');
    expect(fetcher.mock.calls[0][1]).toMatchObject({
      method: 'POST',
      credentials: 'same-origin',
      headers: { 'X-Syslog-Request': '1' },
      body: JSON.stringify({ confirmation: 'CLEAR' }),
    });
    await expect(clearLogs('CLEAR')).rejects.toMatchObject({
      code: 'log_clear_outcome_unknown',
    });
  });
  it('按设备清空使用来源 ID 路由，确认词为唯一请求字段', async () => {
    const fetcher = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(json({ success: true, data: true }));
    await clearSourceLogs('9007199254740993', 'CLEAR');
    expect(fetcher).toHaveBeenCalledWith(
      '/api/admin/v1/sources/9007199254740993/logs/clear',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ confirmation: 'CLEAR' }),
      }),
    );
  });
  it('监听配置使用同源 GET/PUT，并原样传递字符串版本和配置', async () => {
    const fetcher = vi
      .spyOn(globalThis, 'fetch')
      .mockImplementation(async () => json({ success: true, data: {} }));
    await getListenerSettings();
    const input = {
      version: '9007199254740994',
      configuration: { enabled: false, listeners: [] },
    };
    await saveListenerSettings(input);
    await setListenerReception(false, input.version);
    expect(fetcher.mock.calls[0][0]).toBe('/api/admin/v1/system/listeners');
    expect(fetcher.mock.calls[0][1]?.method).toBe('GET');
    expect(fetcher.mock.calls[1][1]?.method).toBe('PUT');
    expect(fetcher.mock.calls[1][1]?.credentials).toBe('same-origin');
    expect(JSON.parse(String(fetcher.mock.calls[1][1]?.body))).toEqual(input);
    expect(fetcher.mock.calls[2][0]).toBe(
      '/api/admin/v1/system/listeners/reception',
    );
    expect(fetcher.mock.calls[2][1]?.method).toBe('PUT');
    expect(JSON.parse(String(fetcher.mock.calls[2][1]?.body))).toEqual({
      enabled: false,
      version: input.version,
    });
  });
  it('未登记设备使用只读同源接口，IPv6与样本键按查询参数编码', async () => {
    const fetcher = vi
      .spyOn(globalThis, 'fetch')
      .mockImplementation(async () => json({ success: true, data: {} }));
    await getUnregisteredDevices();
    await getUnregisteredLogs('2001:db8::25');
    await getUnregisteredSample('2001:db8::25', 'opaque+sample/key');
    expect(fetcher.mock.calls[0][0]).toBe('/api/admin/v1/unregistered-devices');
    const url = new URL(
      String(fetcher.mock.calls[2][0]),
      'https://syslog.example',
    );
    expect(Object.fromEntries(url.searchParams)).toEqual({
      peerAddress: '2001:db8::25',
      sampleKey: 'opaque+sample/key',
    });
    for (const [, init] of fetcher.mock.calls) {
      expect(init?.method).toBe('GET');
      expect(init?.credentials).toBe('same-origin');
      expect(init?.body).toBeUndefined();
    }
  });
  it('初始化只提交所需字段，状态读取保持匿名且只读', async () => {
    const fetcher = vi
      .spyOn(globalThis, 'fetch')
      .mockImplementation(async () => json({ success: true, data: {} }));
    await getSetupStatus();
    await initializeAdmin({
      username: 'synthetic-admin',
      password: 'synthetic-password',
    });
    expect(fetcher.mock.calls[0][0]).toBe('/api/admin/v1/setup/status');
    expect(fetcher.mock.calls[0][1]?.method).toBe('GET');
    expect(fetcher.mock.calls[0][1]?.body).toBeUndefined();
    expect(fetcher.mock.calls[1][0]).toBe('/api/admin/v1/setup/admin');
    expect(JSON.parse(String(fetcher.mock.calls[1][1]?.body))).toEqual({
      username: 'synthetic-admin',
      password: 'synthetic-password',
    });
  });
  it('用户查询传递服务端分页与禁用筛选，密码仅在写请求正文中', async () => {
    const fetcher = vi
      .spyOn(globalThis, 'fetch')
      .mockImplementation(async () => json({ success: true, data: {} }));
    await getUsers({
      page: 2,
      pageSize: 20,
      search: 'example + ops',
      enabled: false,
    });
    const url = new URL(
      String(fetcher.mock.calls[0][0]),
      'https://syslog.example',
    );
    expect(Object.fromEntries(url.searchParams)).toEqual({
      page: '2',
      pageSize: '20',
      search: 'example + ops',
      enabled: 'false',
    });
    await createUser({
      username: 'synthetic-admin',
      password: 'synthetic-password',
    });
    await updateUser('9007199254740993', {
      username: 'example',
      enabled: false,
      expectedVersion: '9007199254740994',
    });
    await resetUserPassword(
      '9007199254740993',
      'synthetic-new-password',
      '9007199254740994',
    );
    expect(fetcher.mock.calls[2][0]).toBe(
      '/api/admin/v1/users/9007199254740993',
    );
    expect(
      JSON.parse(String(fetcher.mock.calls[2][1]?.body)).expectedVersion,
    ).toBe('9007199254740994');
    expect(fetcher.mock.calls[3][0]).toBe(
      '/api/admin/v1/users/9007199254740993/password',
    );
    for (const [url, init] of fetcher.mock.calls.slice(1)) {
      expect(String(url)).not.toContain('password=');
      expect(init?.headers).toMatchObject({ 'X-Syslog-Request': '1' });
    }
  });
  it('使用安全Cookie和保护头，长ID保持字符串', async () => {
    const user = {
      id: '9007199254740993',
      username: 'synthetic-operator',
      role: 'admin',
    };
    const fetcher = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValue(json({ success: true, code: 'OK', data: user }));
    expect(await login('synthetic-operator', 'synthetic-password')).toEqual(
      user,
    );
    expect(fetcher).toHaveBeenCalledWith(
      '/api/admin/v1/session/login',
      expect.objectContaining({
        credentials: 'same-origin',
        method: 'POST',
        headers: expect.objectContaining({ 'X-Syslog-Request': '1' }),
      }),
    );
  });
  it('所有检索条件仅进入POST正文', async () => {
    const fetcher = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      json({
        success: true,
        data: { items: [], hasMore: false, nextCursor: null },
      }),
    );
    const query = {
      from: '2026-11-09T00:00:00.000Z',
      to: '2026-11-10T00:00:00.000Z',
      keywords: 'synthetic-keyword',
    };
    await searchLogs(query);
    expect(fetcher.mock.calls[0][0]).toBe('/api/admin/v1/logs/search');
    expect(fetcher.mock.calls[0][1]?.body).toBe(JSON.stringify(query));
  });
  it('详情保持服务器时间精度且不把ID转成number', async () => {
    const fetcher = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValue(json({ success: true, data: {} }));
    await getLog('9007199254740993', '2026-11-10T03:20:30.123Z');
    expect(fetcher.mock.calls[0][0]).toBe(
      '/api/admin/v1/logs/9007199254740993?receivedAt=2026-11-10T03%3A20%3A30.123Z',
    );
  });
  it('受保护请求401发布会话失效事件，登录失败不会误触发', async () => {
    const listener = vi.fn();
    window.addEventListener(SESSION_EXPIRED_EVENT, listener);
    vi.spyOn(globalThis, 'fetch').mockImplementation(async () =>
      json({ success: false, code: 'authentication_required' }, 401),
    );
    await expect(getSession()).rejects.toBeInstanceOf(ApiError);
    expect(listener).not.toHaveBeenCalled();
    await expect(apiRequest('/sources')).rejects.toMatchObject({ status: 401 });
    expect(listener).toHaveBeenCalledTimes(1);
    window.removeEventListener(SESSION_EXPIRED_EVENT, listener);
  });
  it.each([
    null,
    '<html>proxy secret</html>',
  ])('非法响应不会回显内部正文 %s', async (payload) => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      typeof payload === 'string' ? new Response(payload) : json(payload),
    );
    try {
      await apiRequest('/sources');
      throw new Error('expected failure');
    } catch (error) {
      expect(error).toMatchObject({ code: 'invalid_response' });
      expect(getErrorMessage(error)).not.toContain('secret');
    }
  });
  it('响应大小超预算时拒绝读取未知大内容', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response('{}', { headers: { 'Content-Length': '999999999' } }),
    );
    await expect(apiRequest('/sources')).rejects.toMatchObject({
      code: 'invalid_response',
    });
  });
  it('取消查询不弹服务错误，调用者可忽略过期结果', async () => {
    const controller = new AbortController();
    controller.abort();
    vi.spyOn(globalThis, 'fetch').mockRejectedValue(
      new DOMException('canceled', 'AbortError'),
    );
    await expect(
      apiRequest('/sources', { signal: controller.signal }),
    ).rejects.toMatchObject({ name: 'AbortError' });
  });
  it('响应头已返回但正文停滞时仍报告请求超时', async () => {
    vi.useFakeTimers();
    vi.spyOn(globalThis, 'fetch').mockImplementation(
      async (_input, init) =>
        new Response(
          new ReadableStream({
            start(controller) {
              init?.signal?.addEventListener('abort', () =>
                controller.error(new DOMException('Timed out', 'AbortError')),
              );
            },
          }),
        ),
    );
    const assertion = expect(apiRequest('/sources')).rejects.toMatchObject({
      code: 'request_timeout',
    });
    await vi.advanceTimersByTimeAsync(15000);
    await assertion;
  });
});
