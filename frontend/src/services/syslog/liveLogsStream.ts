import {
  HttpTransportType,
  type HubConnection,
  HubConnectionBuilder,
  LogLevel,
} from '@microsoft/signalr';
import { ApiError } from '@/requestErrorConfig';
import { getSession, SESSION_EXPIRED_EVENT } from './api';
import type { LiveLogSubscription, LiveLogUpdate } from './liveLogs';
import type { OverviewConnectionState } from './overviewStream';

export interface LiveLogCallbacks {
  update: (value: LiveLogUpdate) => void;
  state: (value: OverviewConnectionState) => void;
  error: (value: unknown) => void;
  expired: () => void;
}
const sequence = (value: unknown) =>
  typeof value === 'string' && /^(0|[1-9][0-9]{0,18})$/.test(value);
const count = (value: unknown) =>
  typeof value === 'number' && Number.isFinite(value) && value >= 0;
export function validLiveLogUpdate(value: unknown): value is LiveLogUpdate {
  if (!value || typeof value !== 'object') return false;
  const update = value as LiveLogUpdate;
  return (
    typeof update.subscriptionId === 'string' &&
    typeof update.epoch === 'string' &&
    sequence(update.cursor) &&
    ['snapshot', 'append', 'heartbeat'].includes(update.kind) &&
    Array.isArray(update.items) &&
    update.items.length <= 500 &&
    update.items.every(
      (row) =>
        !!row &&
        typeof row.id === 'string' &&
        row.id.length <= 128 &&
        sequence(row.sequence) &&
        ['normal', 'ignored'].includes(row.disposition) &&
        typeof row.messageTruncated === 'boolean' &&
        !!row.log &&
        Number.isFinite(Date.parse(row.log.receivedAt)) &&
        typeof row.log.message === 'string' &&
        row.log.message.length <= 4096 &&
        typeof row.log.sourceId === 'string' &&
        typeof row.log.rawBytes === 'string',
    ) &&
    !!update.cache &&
    [
      'entries',
      'bytes',
      'deviceCount',
      'evicted',
      'rejected',
      'perDeviceLimit',
      'maxBytes',
    ].every((key) => count(update.cache[key as keyof typeof update.cache])) &&
    !!update.ingest &&
    count(update.ingest.queued) &&
    count(update.ingest.dropped) &&
    typeof update.ingest.spoolFull === 'boolean' &&
    typeof update.ingest.state === 'string' &&
    typeof update.budgetLimited === 'boolean' &&
    typeof update.hasMore === 'boolean' &&
    Number.isFinite(Date.parse(update.observedAt))
  );
}

/** 筛选只通过 Hub 方法传递；同源 Cookie 鉴权，不在 URL 或浏览器存储中保留日志条件。 */
export function subscribeLiveLogs(
  request: LiveLogSubscription,
  callbacks: LiveLogCallbacks,
): () => void {
  let active = true;
  let current: HubConnection | undefined;
  let cursor = { epoch: request.epoch, afterSequence: request.afterSequence };
  let retry = 0;
  let timer: ReturnType<typeof setTimeout> | undefined;
  let heartbeat: ReturnType<typeof setTimeout> | undefined;
  let probe: AbortController | undefined;
  const stopConnection = () => {
    const previous = current;
    current = undefined;
    previous?.off('LiveLogsUpdated');
    previous?.off('SessionExpired');
    void previous?.stop().catch(() => undefined);
  };
  const expire = () => {
    if (!active) return;
    active = false;
    clearTimeout(timer);
    clearTimeout(heartbeat);
    probe?.abort();
    stopConnection();
    callbacks.expired();
    window.dispatchEvent(new Event(SESSION_EXPIRED_EVENT));
  };
  const reconnect = async (connection: HubConnection, error?: unknown) => {
    if (!active || current !== connection) return;
    clearTimeout(heartbeat);
    stopConnection();
    callbacks.state('reconnecting');
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
    if (retry >= 4) {
      callbacks.state('disconnected');
      callbacks.error(error ?? new Error('live stream disconnected'));
      return;
    }
    timer = setTimeout(connect, [1000, 2000, 5000, 10000][retry++]);
  };
  const connect = () => {
    if (!active) return;
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/live-logs', {
        withCredentials: true,
        transport: HttpTransportType.WebSockets,
        headers: { 'X-Syslog-Request': '1' },
      })
      .configureLogging(LogLevel.None)
      .build();
    current = connection;
    const watch = () => {
      clearTimeout(heartbeat);
      // 服务端空闲时也每 5 秒发心跳，不能用“没有新日志”判定连接失败。
      heartbeat = setTimeout(() => void reconnect(connection), 20000);
    };
    connection.on('SessionExpired', () => {
      if (active && current === connection) expire();
    });
    connection.on('LiveLogsUpdated', (value: unknown) => {
      if (!active || current !== connection) return;
      if (!validLiveLogUpdate(value)) {
        void reconnect(connection);
        return;
      }
      if (value.subscriptionId !== request.subscriptionId) return;
      if (
        value.kind !== 'snapshot' &&
        cursor.epoch &&
        value.epoch !== cursor.epoch
      ) {
        void reconnect(connection);
        return;
      }
      if (
        value.epoch === cursor.epoch &&
        cursor.afterSequence &&
        (value.cursor.length < cursor.afterSequence.length ||
          (value.cursor.length === cursor.afterSequence.length &&
            value.cursor < cursor.afterSequence))
      )
        return;
      cursor = { epoch: value.epoch, afterSequence: value.cursor };
      retry = 0;
      watch();
      callbacks.state('connected');
      callbacks.update(value);
    });
    connection.onclose((error) => {
      void reconnect(connection, error);
    });
    watch();
    void connection
      .start()
      .then(async () => {
        if (!active || current !== connection) return;
        // 首帧会早于 Invoke 返回；上方必须先完成事件注册。
        const response = await connection.invoke<{
          success: boolean;
          code: string;
          message: string;
          data: boolean;
        }>('Subscribe', { ...request, ...cursor });
        if (!active || current !== connection) return;
        if (!response.success) {
          stopConnection();
          clearTimeout(heartbeat);
          callbacks.state('disconnected');
          callbacks.error(new ApiError(response.code, 400));
        }
      })
      .catch((error) => {
        void reconnect(connection, error);
      });
  };
  callbacks.state('connecting');
  connect();
  return () => {
    active = false;
    clearTimeout(timer);
    clearTimeout(heartbeat);
    probe?.abort();
    stopConnection();
  };
}
