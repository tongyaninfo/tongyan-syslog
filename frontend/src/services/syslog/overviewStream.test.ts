import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '@/requestErrorConfig';
import { getSession, SESSION_EXPIRED_EVENT } from './api';
import { subscribeListenerRuntime, subscribeOverview } from './overviewStream';
import type { ListenerRuntimeUpdate, OverviewUpdate } from './types';

const hub = vi.hoisted(() => {
  type Handler = (...args: unknown[]) => void;
  class Connection {
    handlers = new Map<string, Handler>();
    closed: (error?: Error) => void = () => undefined;
    start = vi.fn<() => Promise<void>>(() => Promise.resolve());
    stop = vi.fn(() => Promise.resolve());
    on(name: string, handler: Handler) {
      this.handlers.set(name, handler);
    }
    off(name: string) {
      this.handlers.delete(name);
    }
    onclose(handler: (error?: Error) => void) {
      this.closed = handler;
    }
    emit(name: string, data?: unknown) {
      this.handlers.get(name)?.(data);
    }
  }
  return {
    Connection,
    connections: [] as Connection[],
    url: vi.fn(),
    startError: undefined as Error | undefined,
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
      const connection = new hub.Connection();
      if (hub.startError) connection.start.mockRejectedValue(hub.startError);
      hub.connections.push(connection);
      return connection;
    }
  },
}));
vi.mock('./api', () => ({
  getSession: vi.fn(),
  SESSION_EXPIRED_EVENT: 'syslog:session-expired',
}));

const update: OverviewUpdate = {
  status: {
    state: 'running',
    failureCode: '',
    listenerState: 'disabled',
    listenerFailureCode: '',
    received: 100,
    receivedBytes: 20480,
    dropped: 3,
    persisted: 95,
    parsed: 90,
    parseFailed: 4,
    parseUnknown: 1,
    written: 80,
    writeFailures: 1,
    queuedBytes: 1024,
    spoolBytes: 4096,
    pendingBatches: 2,
    lastReceiveUnixMilliseconds: 1794268799000,
    lastWriteUnixMilliseconds: 1794268800000,
  },
  sources: { total: 4, enabled: 3 },
  sourceError: null,
  observedAt: '2026-11-10T00:00:00Z',
};
const runtimeUpdate: ListenerRuntimeUpdate = {
  observedAt: update.observedAt,
  runtime: {
    revision: '9',
    running: { enabled: true, listeners: [] },
    restartRequired: false,
    saved: null,
    states: [
      {
        protocol: 'udp',
        address: '0.0.0.0',
        port: 514,
        state: 'listening',
        failureCode: '',
      },
    ],
    connectionErrors: [],
  },
};
let disposals: (() => void)[] = [];
const subscribe = (once = false, sourceId?: string) => {
  const handlers = { snapshot: vi.fn(), state: vi.fn(), expired: vi.fn() };
  const stop = subscribeOverview(handlers, once, sourceId);
  disposals.push(stop);
  return { ...handlers, stop };
};

beforeEach(() => {
  vi.useFakeTimers();
  hub.connections.length = 0;
  hub.url.mockReset();
  hub.startError = undefined;
  vi.mocked(getSession)
    .mockReset()
    .mockResolvedValue({ id: '42', username: 'example', role: 'admin' });
});
afterEach(() => {
  for (const stop of disposals) stop();
  disposals = [];
  vi.useRealTimers();
});

describe('overview authenticated SignalR transport', () => {
  it('uses only same-origin Cookie credentials and waits for a valid snapshot before marking live', () => {
    const callbacks = subscribe();
    expect(hub.url).toHaveBeenCalledWith('/hubs/overview', {
      transport: 1,
      withCredentials: true,
      headers: { 'X-Syslog-Request': '1' },
    });
    expect(callbacks.state).toHaveBeenLastCalledWith('connecting');
    expect(callbacks.snapshot).not.toHaveBeenCalled();
    hub.connections[0].emit('OverviewUpdated', update);
    expect(callbacks.snapshot).toHaveBeenCalledWith(update);
    expect(callbacks.state).toHaveBeenLastCalledWith('connected');
    expect(getSession).not.toHaveBeenCalled();
  });

  it('encodes the selected source in the subscription and accepts its metrics and database size', () => {
    const sourceId = 'device?source=42';
    const callbacks = subscribe(false, sourceId);
    expect(hub.url).toHaveBeenCalledWith(
      '/hubs/overview?sourceId=device%3Fsource%3D42',
      expect.objectContaining({ withCredentials: true }),
    );
    const selected = {
      ...update,
      sourceTraffic: { sourceId, received: 7, written: 6 },
      databaseSizeBytes: 0,
    };
    hub.connections[0].emit('OverviewUpdated', selected);
    expect(callbacks.snapshot).toHaveBeenCalledWith(selected);
    hub.connections[0].emit('OverviewUpdated', {
      ...selected,
      sourceTraffic: { ...selected.sourceTraffic, received: -1 },
    });
    expect(callbacks.snapshot).toHaveBeenCalledTimes(1);
    expect(callbacks.state).toHaveBeenLastCalledWith('reconnecting');
  });

  it('retries failed initial connections with finite backoff and can be manually restarted', async () => {
    hub.startError = new Error(
      'Failed to complete negotiation: Status code 503',
    );
    const callbacks = subscribe();
    await vi.advanceTimersByTimeAsync(18001);
    expect(hub.connections).toHaveLength(5);
    expect(callbacks.state).toHaveBeenLastCalledWith('disconnected');
    await vi.advanceTimersByTimeAsync(60000);
    expect(hub.connections).toHaveLength(5);
    callbacks.stop();
    hub.startError = undefined;
    const fresh = subscribe();
    hub.connections[5].emit('OverviewUpdated', update);
    expect(fresh.state).toHaveBeenLastCalledWith('connected');
  });

  it('reconnects after loss and rejects old connection callbacks including late session expiry', async () => {
    const callbacks = subscribe();
    const old = hub.connections[0];
    const oldSnapshot = old.handlers.get('OverviewUpdated');
    const oldExpired = old.handlers.get('SessionExpired');
    old.emit('OverviewUpdated', update);
    old.closed(new Error('socket closed'));
    await vi.advanceTimersByTimeAsync(1000);
    const fresh = hub.connections[1];
    fresh.emit('OverviewUpdated', {
      ...update,
      observedAt: '2026-11-10T00:00:03Z',
    });
    oldSnapshot?.({ ...update, sources: { total: 0, enabled: 0 } });
    oldExpired?.();
    old.closed();
    expect(callbacks.expired).not.toHaveBeenCalled();
    expect(callbacks.snapshot).toHaveBeenCalledTimes(2);
    expect(callbacks.state).toHaveBeenLastCalledWith('connected');
    expect(fresh.stop).not.toHaveBeenCalled();
  });

  it('ignores out-of-order snapshots within one connection', () => {
    const callbacks = subscribe();
    hub.connections[0].emit('OverviewUpdated', {
      ...update,
      observedAt: '2026-11-10T00:00:03Z',
    });
    hub.connections[0].emit('OverviewUpdated', update);
    expect(callbacks.snapshot).toHaveBeenCalledTimes(1);
  });

  it('turns wrapped negotiation 401 into session expiry without reconnecting', async () => {
    hub.startError = new Error(
      'Failed to complete negotiation: Status code 401',
    );
    vi.mocked(getSession).mockRejectedValue(
      new ApiError('authentication_required', 401),
    );
    const expiredEvent = vi.fn();
    window.addEventListener(SESSION_EXPIRED_EVENT, expiredEvent);
    const callbacks = subscribe();
    await vi.advanceTimersByTimeAsync(60000);
    expect(callbacks.expired).toHaveBeenCalledOnce();
    expect(expiredEvent).toHaveBeenCalledOnce();
    expect(hub.connections).toHaveLength(1);
    expect(hub.connections[0].stop).toHaveBeenCalledOnce();
    window.removeEventListener(SESSION_EXPIRED_EVENT, expiredEvent);
  });

  it('stops immediately when the server revokes the authenticated session', async () => {
    const callbacks = subscribe();
    hub.connections[0].emit('OverviewUpdated', update);
    const late = hub.connections[0].handlers.get('OverviewUpdated');
    hub.connections[0].emit('SessionExpired');
    late?.(update);
    await vi.advanceTimersByTimeAsync(60000);
    expect(callbacks.expired).toHaveBeenCalledOnce();
    expect(callbacks.snapshot).toHaveBeenCalledOnce();
    expect(hub.connections).toHaveLength(1);
  });

  it('marks a silent stream stale even when transport heartbeats keep the socket open', async () => {
    const callbacks = subscribe();
    hub.connections[0].emit('OverviewUpdated', update);
    await vi.advanceTimersByTimeAsync(15000);
    expect(callbacks.state).toHaveBeenLastCalledWith('reconnecting');
    expect(hub.connections[0].stop).toHaveBeenCalledOnce();
    await vi.advanceTimersByTimeAsync(1000);
    expect(hub.connections).toHaveLength(2);
  });

  it('cancels pending retries and session probes when leaving the page', async () => {
    let resolveProbe: (() => void) | undefined;
    vi.mocked(getSession).mockImplementation(
      () =>
        new Promise((resolve) => {
          resolveProbe = () =>
            resolve({ id: '42', username: 'example', role: 'admin' });
        }),
    );
    const callbacks = subscribe();
    const late = hub.connections[0].handlers.get('OverviewUpdated');
    hub.connections[0].closed();
    const signal = vi.mocked(getSession).mock.calls[0][0];
    callbacks.stop();
    expect(signal?.aborted).toBe(true);
    resolveProbe?.();
    late?.(update);
    await vi.advanceTimersByTimeAsync(60000);
    expect(hub.connections).toHaveLength(1);
    expect(callbacks.snapshot).not.toHaveBeenCalled();
    expect(callbacks.expired).not.toHaveBeenCalled();
  });

  it('closes a paused manual refresh after one snapshot', async () => {
    const callbacks = subscribe(true);
    const connection = hub.connections[0];
    connection.emit('OverviewUpdated', update);
    expect(connection.stop).toHaveBeenCalledOnce();
    expect(callbacks.state).toHaveBeenLastCalledWith('paused');
    await vi.advanceTimersByTimeAsync(60000);
    expect(hub.connections).toHaveLength(1);
  });

  it('rejects malformed snapshots instead of displaying fabricated metrics', async () => {
    const callbacks = subscribe();
    hub.connections[0].emit('OverviewUpdated', {
      ...update,
      status: { received: 0 },
    });
    expect(callbacks.snapshot).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(1000);
    expect(hub.connections).toHaveLength(2);
  });
});

describe('listener runtime SignalR subscription', () => {
  function subscribeToListeners() {
    const callbacks = { snapshot: vi.fn(), state: vi.fn(), expired: vi.fn() };
    disposals.push(subscribeListenerRuntime(callbacks));
    return callbacks;
  }

  it('uses the same-origin listener view and applies runtime revisions in decimal order', () => {
    const callbacks = subscribeToListeners();
    expect(hub.url).toHaveBeenCalledWith('/hubs/overview?view=listeners', {
      transport: 1,
      withCredentials: true,
      headers: { 'X-Syslog-Request': '1' },
    });
    const connection = hub.connections[0];
    connection.emit('OverviewUpdated', update);
    expect(callbacks.snapshot).not.toHaveBeenCalled();
    const frame = (revision: string) => ({
      ...runtimeUpdate,
      runtime: { ...runtimeUpdate.runtime, revision },
    });
    connection.emit('ListenerRuntimeUpdated', frame('10'));
    connection.emit('ListenerRuntimeUpdated', frame('9'));
    connection.emit('ListenerRuntimeUpdated', frame('9007199254740993'));
    connection.emit('ListenerRuntimeUpdated', frame('9007199254740992'));
    expect(callbacks.snapshot.mock.calls).toEqual([
      [frame('10')],
      [frame('9007199254740993')],
    ]);
    expect(callbacks.state).toHaveBeenLastCalledWith('connected');
  });

  it('accepts the first full snapshot after reconnect and ignores callbacks from the old socket', async () => {
    const callbacks = subscribeToListeners();
    const old = hub.connections[0];
    const late = old.handlers.get('ListenerRuntimeUpdated');
    old.emit('ListenerRuntimeUpdated', runtimeUpdate);
    old.closed(new Error('socket closed'));
    await vi.advanceTimersByTimeAsync(1000);
    const fresh = hub.connections[1];
    const restarted = {
      ...runtimeUpdate,
      runtime: { ...runtimeUpdate.runtime, revision: '1' },
    };
    fresh.emit('ListenerRuntimeUpdated', restarted);
    late?.(runtimeUpdate);
    expect(callbacks.snapshot.mock.calls).toEqual([
      [runtimeUpdate],
      [restarted],
    ]);
    expect(callbacks.state).toHaveBeenLastCalledWith('connected');
  });
});
