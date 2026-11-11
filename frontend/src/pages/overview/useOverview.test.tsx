import { act, renderHook } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { subscribeOverview } from '@/services/syslog/overviewStream';
import type { OverviewUpdate } from '@/services/syslog/types';
import { RATE_WINDOW_SECONDS } from './rateHistory';
import { useOverview } from './useOverview';

vi.mock('@/services/syslog/overviewStream', () => ({
  subscribeOverview: vi.fn(),
}));

const update: OverviewUpdate = {
  status: {
    state: 'running',
    failureCode: '',
    listenerState: 'listening',
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
const stops: ReturnType<typeof vi.fn>[] = [];
const callbacks = (index = 0) =>
  vi.mocked(subscribeOverview).mock.calls[index][0];
const start = Date.parse(update.observedAt);
const sample = (
  seconds: number,
  received: number,
  written: number,
): OverviewUpdate => ({
  ...update,
  observedAt: new Date(start + seconds * 1000).toISOString(),
  status: { ...update.status, received, written },
});

beforeEach(() => {
  stops.length = 0;
  vi.mocked(subscribeOverview)
    .mockReset()
    .mockImplementation((handlers) => {
      const stop = vi.fn();
      stops.push(stop);
      handlers.state('connecting');
      return stop;
    });
});

describe('overview SignalR state', () => {
  it('waits for the first server snapshot and keeps receiving, persistence and confirmed writes separate', () => {
    const { result } = renderHook(() => useOverview());
    expect(result.current.status).toBeUndefined();
    expect(result.current.connectionState).toBe('connecting');
    act(() => callbacks().snapshot(update));
    expect(result.current.loading).toBe(false);
    expect(result.current.connectionState).toBe('connected');
    expect(result.current.status?.received).toBe(100);
    expect(result.current.status?.persisted).toBe(95);
    expect(result.current.status?.written).toBe(80);
    expect(result.current.sources).toEqual({ total: 4, enabled: 3 });
    expect(result.current.refreshedAt).toBe(update.observedAt);
  });

  it('keeps the last log receive time unchanged when a new dashboard snapshot has the same counters', () => {
    const { result } = renderHook(() => useOverview());
    act(() => callbacks().snapshot(update));
    const observedAt = '2026-11-10T00:00:02Z';
    act(() => callbacks().snapshot({ ...update, observedAt }));
    expect(result.current.refreshedAt).toBe(observedAt);
    expect(result.current.status?.lastReceiveUnixMilliseconds).toBe(
      update.status.lastReceiveUnixMilliseconds,
    );
  });

  it('derives independent rates from elapsed server time and skips repeated or older chart samples', () => {
    const { result } = renderHook(() => useOverview());
    act(() => {
      callbacks().snapshot(sample(0, 100, 80));
      callbacks().snapshot(sample(0.8, 104, 80));
      callbacks().snapshot(sample(0.8, 400, 200));
      callbacks().snapshot(sample(0.4, 300, 200));
      callbacks().snapshot(sample(2.05, 109, 90));
    });
    expect(result.current.rateHistory).toEqual([
      { at: start, received: null, written: null, ignored: null },
      { at: start + 800, received: 5, written: 0, ignored: null },
      { at: start + 2050, received: 4, written: 8, ignored: null },
    ]);
  });

  it('switches the chart to selected source counters while keeping global status and database size', () => {
    const { result } = renderHook(() => useOverview());
    act(() => {
      callbacks().snapshot(sample(0, 100, 80));
      callbacks().snapshot(sample(1, 110, 88));
    });
    expect(result.current.rateHistory.at(-1)).toMatchObject({
      received: 10,
      written: 8,
    });

    const old = callbacks();
    act(() => result.current.setRateSourceId('42'));
    expect(result.current.rateHistory).toEqual([]);
    expect(stops[0]).toHaveBeenCalledOnce();
    expect(vi.mocked(subscribeOverview).mock.calls[1][2]).toBe('42');
    const selected = callbacks(1);
    act(() => {
      old.snapshot(sample(2, 120, 90));
      selected.snapshot({
        ...sample(2, 125, 97),
        sourceTraffic: { sourceId: '7', received: 20, written: 20 },
      });
      selected.snapshot({
        ...sample(3, 140, 108),
        sourceTraffic: {
          sourceId: '42',
          received: 25,
          written: 18,
          ignored: 3,
        },
      });
      selected.snapshot({
        ...sample(4, 155, 120),
        sourceTraffic: {
          sourceId: '42',
          received: 28,
          written: 20,
          ignored: 4,
        },
        databaseSizeBytes: 8192,
      });
    });
    expect(result.current.status?.received).toBe(155);
    expect(result.current.databaseSizeBytes).toBe(8192);
    expect(result.current.rateHistory).toEqual([
      { at: start + 2000, received: null, written: null, ignored: null },
      { at: start + 3000, received: null, written: null, ignored: null },
      { at: start + 4000, received: 3, written: 2, ignored: 1 },
    ]);

    act(() => result.current.setAutoRefresh(false));
    act(() => result.current.setRateSourceId(undefined));
    expect(result.current.connectionState).toBe('paused');
    expect(result.current.rateHistory).toEqual([]);
    expect(subscribeOverview).toHaveBeenCalledTimes(2);
  });

  it('breaks the rate lines across missing samples, reconnects and counter resets', () => {
    const { result } = renderHook(() => useOverview());
    act(() => {
      callbacks().snapshot(sample(0, 100, 80));
      callbacks().snapshot(sample(1, 104, 82));
      callbacks().snapshot(sample(3, 200, 150));
      callbacks().snapshot(sample(4, 202, 152));
      callbacks().state('reconnecting');
      callbacks().snapshot(sample(5, 204, 154));
      callbacks().snapshot(sample(6, 2, 1));
      callbacks().snapshot(sample(7, 5, 5));
    });
    expect(
      result.current.rateHistory.map(({ received, written }) => [
        received,
        written,
      ]),
    ).toEqual([
      [null, null],
      [4, 2],
      [null, null],
      [2, 2],
      [null, null],
      [null, null],
      [3, 4],
    ]);
  });

  it('rolls old samples out of the time window and bounds unusually dense updates', () => {
    const { result } = renderHook(() => useOverview());
    act(() => {
      for (let second = 0; second <= 75; second++) {
        callbacks().snapshot(sample(second, second, second));
      }
    });
    expect(result.current.rateHistory).toHaveLength(RATE_WINDOW_SECONDS + 1);
    expect(result.current.rateHistory[0]?.at).toBe(
      start + (75 - RATE_WINDOW_SECONDS) * 1000,
    );
    act(() => {
      for (let index = 1; index <= 150; index++) {
        callbacks().snapshot(sample(75 + index / 100, 75 + index, 75));
      }
    });
    expect(result.current.rateHistory).toHaveLength(
      2 * RATE_WINDOW_SECONDS + 1,
    );
    expect(result.current.rateHistory.at(-1)?.at).toBe(start + 76500);
  });

  it('keeps the last snapshot visibly stale during disconnect and reconnect', () => {
    const { result } = renderHook(() => useOverview());
    act(() => callbacks().snapshot(update));
    act(() => callbacks().state('reconnecting'));
    expect(result.current.connectionState).toBe('reconnecting');
    expect(result.current.status?.received).toBe(100);
    expect(result.current.refreshedAt).toBe(update.observedAt);
    act(() => callbacks().state('disconnected'));
    expect(result.current.connectionState).toBe('disconnected');
    act(() => result.current.refresh());
    expect(stops[0]).toHaveBeenCalledOnce();
    expect(result.current.connectionState).toBe('connecting');
    act(() =>
      callbacks(1).snapshot({ ...update, observedAt: '2026-11-10T00:00:02Z' }),
    );
    expect(result.current.connectionState).toBe('connected');
  });

  it('does not turn failed source aggregation into zero registered devices', () => {
    const { result } = renderHook(() => useOverview());
    act(() =>
      callbacks().snapshot({
        ...update,
        sources: null,
        sourceError: 'private-error',
      }),
    );
    expect(result.current.sources).toBeUndefined();
    expect(result.current.sourceError).toBe('error.generic');
    expect(result.current.status).toEqual(update.status);
  });

  it('stops on pause, requests one snapshot on manual refresh, and resumes live updates', () => {
    const { result } = renderHook(() => useOverview());
    act(() => {
      callbacks().snapshot(update);
      callbacks().snapshot(sample(1, 102, 81));
    });
    const lastLiveHistory = result.current.rateHistory;
    act(() => result.current.setAutoRefresh(false));
    expect(stops[0]).toHaveBeenCalledOnce();
    expect(result.current.connectionState).toBe('paused');
    expect(subscribeOverview).toHaveBeenCalledTimes(1);
    act(() => result.current.refresh());
    expect(vi.mocked(subscribeOverview).mock.calls[1][1]).toBe(true);
    act(() => callbacks(1).snapshot(sample(2, 104, 82)));
    expect(result.current.autoRefresh).toBe(false);
    expect(result.current.connectionState).toBe('paused');
    expect(result.current.rateHistory).toBe(lastLiveHistory);
    act(() => result.current.setAutoRefresh(true));
    expect(stops[1]).toHaveBeenCalledOnce();
    expect(vi.mocked(subscribeOverview).mock.calls[2][1]).toBe(false);
    act(() => callbacks(2).snapshot(sample(2.5, 112, 91)));
    expect(result.current.rateHistory.at(-1)).toEqual({
      at: start + 2500,
      received: null,
      written: null,
      ignored: null,
    });
  });

  it('ignores old subscriptions after refresh and disposes the active subscription on leave', () => {
    const { result, unmount } = renderHook(() => useOverview());
    const old = callbacks();
    act(() => result.current.refresh());
    act(() => callbacks(1).snapshot(update));
    act(() => {
      old.snapshot({ ...update, sources: { total: 0, enabled: 0 } });
      old.state('disconnected');
      old.expired();
    });
    expect(result.current.sources?.total).toBe(4);
    expect(result.current.connectionState).toBe('connected');
    unmount();
    expect(stops[1]).toHaveBeenCalledOnce();
  });

  it('clears all protected data when the session is revoked', () => {
    const { result } = renderHook(() => useOverview());
    act(() => callbacks().snapshot(update));
    act(() => callbacks().expired());
    expect(result.current.status).toBeUndefined();
    expect(result.current.sources).toBeUndefined();
    expect(result.current.refreshedAt).toBeUndefined();
    expect(result.current.rateHistory).toEqual([]);
    expect(result.current.statusError).toBe('error.session');
  });
});
