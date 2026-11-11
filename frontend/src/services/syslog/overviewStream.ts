import {
  HttpTransportType,
  type HubConnection,
  HubConnectionBuilder,
  LogLevel,
} from '@microsoft/signalr';
import { ApiError } from '@/requestErrorConfig';
import { getSession, SESSION_EXPIRED_EVENT } from './api';
import type {
  ListenerConfiguration,
  ListenerRuntimeUpdate,
  ListenerSetting,
  OverviewUpdate,
} from './types';

export type OverviewConnectionState =
  | 'connecting'
  | 'connected'
  | 'reconnecting'
  | 'disconnected'
  | 'paused';

interface StreamCallbacks<T> {
  snapshot: (update: T) => void;
  state: (state: OverviewConnectionState) => void;
  expired: () => void;
}

interface StreamOptions<T> {
  url: string;
  event: string;
  valid: (value: unknown) => value is T;
  isOlder: (value: T, previous: T) => boolean;
}

const retryDelays = [1000, 2000, 5000, 10000];
const snapshotTimeout = 15000;
const counters = [
  'received',
  'receivedBytes',
  'dropped',
  'persisted',
  'parsed',
  'parseFailed',
  'parseUnknown',
  'written',
  'writeFailures',
  'queuedBytes',
  'spoolBytes',
  'pendingBatches',
  'lastReceiveUnixMilliseconds',
  'lastWriteUnixMilliseconds',
] as const;

const validCount = (value: number) => Number.isFinite(value) && value >= 0;
const validTimestamp = (value: unknown) =>
  typeof value === 'string' && Number.isFinite(Date.parse(value));
const validRevision = (value: unknown) =>
  typeof value === 'string' && /^(0|[1-9][0-9]{0,18})$/.test(value);

function validSnapshot(value: unknown): value is OverviewUpdate {
  if (!value || typeof value !== 'object') return false;
  const update = value as OverviewUpdate;
  const traffic = update.sourceTraffic;
  return (
    !!update.status &&
    ['state', 'failureCode', 'listenerState', 'listenerFailureCode'].every(
      (key) =>
        typeof update.status[key as keyof typeof update.status] === 'string',
    ) &&
    counters.every((key) => validCount(update.status[key])) &&
    (update.status.ignored == null || validCount(update.status.ignored)) &&
    (update.databaseSizeBytes == null ||
      validCount(update.databaseSizeBytes)) &&
    (traffic == null ||
      (typeof traffic.sourceId === 'string' &&
        traffic.sourceId.length > 0 &&
        validCount(traffic.received) &&
        validCount(traffic.written) &&
        (traffic.ignored == null || validCount(traffic.ignored)))) &&
    validTimestamp(update.observedAt) &&
    (update.sourceError === null || typeof update.sourceError === 'string') &&
    (update.sources === null
      ? !!update.sourceError
      : !!update.sources &&
        Number.isSafeInteger(update.sources.total) &&
        Number.isSafeInteger(update.sources.enabled) &&
        update.sources.enabled >= 0 &&
        update.sources.enabled <= update.sources.total)
  );
}

function validEndpoint(
  endpoint: Pick<ListenerSetting, 'protocol' | 'address' | 'port'>,
): boolean {
  return (
    !!endpoint &&
    ['udp', 'tcp', 'tls', 'relp'].includes(endpoint.protocol) &&
    typeof endpoint.address === 'string' &&
    Number.isInteger(endpoint.port) &&
    endpoint.port > 0 &&
    endpoint.port <= 65535
  );
}

function validListenerConfiguration(configuration: ListenerConfiguration) {
  return (
    !!configuration &&
    typeof configuration.enabled === 'boolean' &&
    Array.isArray(configuration.listeners) &&
    configuration.listeners.every(
      (listener) =>
        validEndpoint(listener) &&
        typeof listener.enabled === 'boolean' &&
        ['auto', 'octet-counting', 'newline'].includes(listener.framing) &&
        typeof listener.certificateId === 'string' &&
        Number.isInteger(listener.frameTimeoutSeconds) &&
        Number.isInteger(listener.idleTimeoutSeconds),
    )
  );
}

function validListenerSnapshot(value: unknown): value is ListenerRuntimeUpdate {
  if (!value || typeof value !== 'object') return false;
  const { runtime, observedAt } = value as ListenerRuntimeUpdate;
  return (
    validTimestamp(observedAt) &&
    !!runtime &&
    validRevision(runtime.revision) &&
    validListenerConfiguration(runtime.running) &&
    typeof runtime.restartRequired === 'boolean' &&
    Array.isArray(runtime.states) &&
    runtime.states.every(
      (state) =>
        validEndpoint(state) &&
        typeof state.state === 'string' &&
        typeof state.failureCode === 'string' &&
        (state.failureMessage === undefined ||
          typeof state.failureMessage === 'string'),
    ) &&
    Array.isArray(runtime.connectionErrors) &&
    runtime.connectionErrors.every(
      (error) =>
        validEndpoint(error) &&
        typeof error.peerAddress === 'string' &&
        typeof error.failureCode === 'string' &&
        typeof error.failureMessage === 'string' &&
        validTimestamp(error.occurredAt),
    ) &&
    (runtime.saved === null ||
      (!!runtime.saved &&
        validRevision(runtime.saved.version) &&
        validListenerConfiguration(runtime.saved.configuration)))
  );
}

export function subscribeOverview(
  callbacks: StreamCallbacks<OverviewUpdate>,
  once = false,
  sourceId?: string,
): () => void {
  return subscribeStream(
    callbacks,
    {
      url: sourceId
        ? `/hubs/overview?sourceId=${encodeURIComponent(sourceId)}`
        : '/hubs/overview',
      event: 'OverviewUpdated',
      valid: validSnapshot,
      isOlder: (value, previous) =>
        Date.parse(value.observedAt) < Date.parse(previous.observedAt),
    },
    once,
  );
}

export function subscribeListenerRuntime(
  callbacks: StreamCallbacks<ListenerRuntimeUpdate>,
): () => void {
  return subscribeStream(callbacks, {
    url: '/hubs/overview?view=listeners',
    event: 'ListenerRuntimeUpdated',
    valid: validListenerSnapshot,
    // 运行快照可能超过 JS 的安全整数范围，按规范化十进制字符串排序。
    isOlder: ({ runtime: { revision } }, { runtime: { revision: previous } }) =>
      revision.length < previous.length ||
      (revision.length === previous.length && revision < previous),
  });
}

/** 两类实时订阅共用同源 Cookie、会话验证和重连。 */
function subscribeStream<T>(
  callbacks: StreamCallbacks<T>,
  { url, event, valid, isOlder }: StreamOptions<T>,
  once = false,
): () => void {
  let active = true;
  let current: HubConnection | undefined;
  let retryCount = 0;
  let timer: ReturnType<typeof setTimeout> | undefined;
  let watchdog: ReturnType<typeof setTimeout> | undefined;
  let probe: AbortController | undefined;

  const clearTimers = () => {
    clearTimeout(timer);
    clearTimeout(watchdog);
    timer = undefined;
    watchdog = undefined;
  };
  const stopCurrent = () => {
    const previous = current;
    current = undefined;
    if (!previous) return;
    previous.off(event);
    previous.off('SessionExpired');
    // 先解除当前身份再停止，避免 stop 的 onclose 触发第二条重连链。
    void previous.stop().catch(() => undefined);
  };
  const expire = () => {
    if (!active) return;
    active = false;
    clearTimers();
    probe?.abort();
    stopCurrent();
    callbacks.expired();
    window.dispatchEvent(new Event(SESSION_EXPIRED_EVENT));
  };

  const reconnect = async (connection: HubConnection, error?: unknown) => {
    if (!active || current !== connection) return;
    clearTimeout(watchdog);
    stopCurrent();
    callbacks.state('reconnecting');
    // SignalR 可能包装握手异常，失败时只读核实会话，不能把 401 无限当网络故障重试。
    if (error instanceof ApiError && error.status === 401) return expire();
    probe = new AbortController();
    try {
      await getSession(probe.signal);
    } catch (failure) {
      if (!active) return;
      if (failure instanceof ApiError && failure.status === 401)
        return expire();
    }
    if (!active) return;
    if (retryCount >= retryDelays.length) {
      callbacks.state('disconnected');
      return;
    }
    timer = setTimeout(connect, retryDelays[retryCount++]);
  };

  const connect = () => {
    if (!active) return;
    const connection = new HubConnectionBuilder()
      .withUrl(url, {
        transport: HttpTransportType.WebSockets,
        withCredentials: true,
        headers: { 'X-Syslog-Request': '1' },
      })
      .configureLogging(LogLevel.None)
      .build();
    current = connection;
    let lastUpdate: T | undefined;
    const armWatchdog = () => {
      clearTimeout(watchdog);
      watchdog = setTimeout(() => void reconnect(connection), snapshotTimeout);
    };
    connection.on(event, (value: unknown) => {
      if (!active || current !== connection) return;
      if (!valid(value)) return void reconnect(connection);
      if (lastUpdate !== undefined && isOlder(value, lastUpdate)) return;
      lastUpdate = value;
      retryCount = 0;
      callbacks.snapshot(value);
      if (once) {
        active = false;
        clearTimers();
        stopCurrent();
        callbacks.state('paused');
      } else {
        callbacks.state('connected');
        armWatchdog();
      }
    });
    connection.on('SessionExpired', () => {
      if (active && current === connection) expire();
    });
    connection.onclose((error) => void reconnect(connection, error));
    // 限定首快照与后续快照等待时间，心跳正常但没有业务数据时也不假报实时。
    armWatchdog();
    void connection.start().catch((error) => void reconnect(connection, error));
  };

  callbacks.state('connecting');
  connect();
  return () => {
    active = false;
    clearTimers();
    probe?.abort();
    stopCurrent();
  };
}
