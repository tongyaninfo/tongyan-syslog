import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '@/requestErrorConfig';
import { liveUpdate } from '../../../tests/liveLogsFixtures';
import { getSession } from './api';
import { subscribeLiveLogs, validLiveLogUpdate } from './liveLogsStream';

const hub = vi.hoisted(() => {
  type Handler = (...args: unknown[]) => void;
  class Connection {
    handlers = new Map<string, Handler>();
    closed: (error?: Error) => void = () => undefined;
    start = vi.fn(() => Promise.resolve());
    stop = vi.fn(() => Promise.resolve());
    invoke = vi.fn(async (_method: string, request: unknown) => {
      firstFrame?.(this, request);
      return { success: true, code: 'ok', data: true };
    });
    on(name: string, handler: Handler) {
      this.handlers.set(name, handler);
    }
    off(name: string) {
      this.handlers.delete(name);
    }
    onclose(handler: (error?: Error) => void) {
      this.closed = handler;
    }
    emit(value: unknown) {
      this.handlers.get('LiveLogsUpdated')?.(value);
    }
  }
  let firstFrame:
    | ((connection: Connection, request: unknown) => void)
    | undefined;
  return {
    Connection,
    connections: [] as Connection[],
    url: vi.fn(),
    setFirstFrame: (value: typeof firstFrame) => {
      firstFrame = value;
    },
  };
});
vi.mock('@microsoft/signalr', () => ({
  HttpTransportType: { WebSockets: 1 },
  LogLevel: { None: 6 },
  HubConnectionBuilder: class {
    withUrl(...args: unknown[]) {
      hub.url(...args);
      return this;
    }
    configureLogging() {
      return this;
    }
    build() {
      const value = new hub.Connection();
      hub.connections.push(value);
      return value;
    }
  },
}));
vi.mock('./api', () => ({
  getSession: vi.fn(),
  SESSION_EXPIRED_EVENT: 'syslog:session-expired',
}));
let stops: (() => void)[] = [];
function subscribe() {
  const callbacks = {
    update: vi.fn(),
    state: vi.fn(),
    error: vi.fn(),
    expired: vi.fn(),
  };
  const stop = subscribeLiveLogs(
    {
      subscriptionId: 'live-0',
      filter: { disposition: 'all', keywords: 'SECRET' },
    },
    callbacks,
  );
  stops.push(stop);
  return { ...callbacks, stop };
}
beforeEach(() => {
  vi.useFakeTimers();
  hub.connections.length = 0;
  hub.url.mockReset();
  hub.setFirstFrame(undefined);
  vi.mocked(getSession)
    .mockReset()
    .mockResolvedValue({ id: '1', username: 'example', role: 'admin' });
});
afterEach(() => {
  for (const stop of stops) stop();
  stops = [];
  vi.useRealTimers();
});

describe('实时日志 SignalR 传输', () => {
  it('筛选通过 Subscribe 消息提交，接住 invoke 返回之前的首帧', async () => {
    hub.setFirstFrame((connection) => connection.emit(liveUpdate()));
    const result = subscribe();
    await vi.advanceTimersByTimeAsync(0);
    expect(hub.url).toHaveBeenCalledWith(
      '/hubs/live-logs',
      expect.objectContaining({ withCredentials: true, transport: 1 }),
    );
    expect(hub.connections[0].invoke).toHaveBeenCalledWith(
      'Subscribe',
      expect.objectContaining({
        filter: { disposition: 'all', keywords: 'SECRET' },
      }),
    );
    expect(result.update).toHaveBeenCalledWith(liveUpdate());
    expect(result.state).toHaveBeenLastCalledWith('connected');
  });
  it('空闲心跳维持连接；不匹配代次和倒退的游标不覆盖新状态', async () => {
    const result = subscribe();
    hub.connections[0].emit(liveUpdate());
    for (let index = 0; index < 6; index++) {
      await vi.advanceTimersByTimeAsync(5000);
      hub.connections[0].emit(liveUpdate({ kind: 'heartbeat', items: [] }));
    }
    expect(hub.connections).toHaveLength(1);
    hub.connections[0].emit(liveUpdate({ subscriptionId: 'old' }));
    hub.connections[0].emit(liveUpdate({ kind: 'append', cursor: '9' }));
    expect(result.update).toHaveBeenCalledTimes(7);
  });
  it('断线续传最后游标，旧连接的迟到事件和会话过期回调失效', async () => {
    const result = subscribe();
    const previous = hub.connections[0];
    const expired = previous.handlers.get('SessionExpired');
    const update = previous.handlers.get('LiveLogsUpdated');
    previous.emit(liveUpdate({ cursor: '9007199254740993' }));
    previous.closed();
    await vi.advanceTimersByTimeAsync(1000);
    expect(hub.connections[1].invoke).toHaveBeenCalledWith(
      'Subscribe',
      expect.objectContaining({
        epoch: 'epoch-a',
        afterSequence: '9007199254740993',
      }),
    );
    update?.(liveUpdate());
    expired?.();
    expect(result.update).toHaveBeenCalledTimes(1);
    expect(result.expired).not.toHaveBeenCalled();
  });
  it('401 停止重连；不在浏览器日志输出原始失败消息', async () => {
    vi.mocked(getSession).mockRejectedValue(
      new ApiError('authentication_required', 401),
    );
    const result = subscribe();
    hub.connections[0].closed();
    await vi.advanceTimersByTimeAsync(20000);
    expect(result.expired).toHaveBeenCalledOnce();
    expect(hub.connections).toHaveLength(1);
  });
  it('拒绝无限批次与无效行，避免畸形响应进入列表', () => {
    expect(
      validLiveLogUpdate(liveUpdate({ items: new Array(501).fill(null) })),
    ).toBe(false);
    expect(validLiveLogUpdate({ ...liveUpdate(), items: [null] })).toBe(false);
    expect(
      validLiveLogUpdate({ ...liveUpdate(), cache: { entries: -1 } }),
    ).toBe(false);
  });
});
