import { act, renderHook } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  type LiveLogSnapshot,
  queryLiveLogs,
} from '@/services/syslog/liveLogs';
import {
  type LiveLogCallbacks,
  subscribeLiveLogs,
} from '@/services/syslog/liveLogsStream';
import { cache, liveRow, liveUpdate } from '../../../tests/liveLogsFixtures';
import { useLiveLogs } from './useLiveLogs';

vi.mock('@/services/syslog/liveLogsStream', () => ({
  subscribeLiveLogs: vi.fn(),
}));
vi.mock('@/services/syslog/liveLogs', () => ({ queryLiveLogs: vi.fn() }));
let callbacks: LiveLogCallbacks[];
let stops: ReturnType<typeof vi.fn>[];
beforeEach(() => {
  vi.clearAllMocks();
  callbacks = [];
  stops = [];
  vi.mocked(subscribeLiveLogs).mockImplementation((_request, value) => {
    callbacks.push(value);
    const stop = vi.fn();
    stops.push(stop);
    return stop;
  });
});

describe('实时列表生命周期', () => {
  it('暂停关闭浏览器订阅、冻结行；恢复携带 epoch 和长序号续传', () => {
    const { result } = renderHook(useLiveLogs);
    act(() => callbacks[0].update(liveUpdate({ cursor: '9007199254740993' })));
    act(() => result.current.setPaused(true));
    expect(stops[0]).toHaveBeenCalledOnce();
    expect(result.current.state).toBe('paused');
    expect(result.current.rows).toHaveLength(1);
    act(() => result.current.setPaused(false));
    expect(subscribeLiveLogs).toHaveBeenLastCalledWith(
      expect.objectContaining({
        epoch: 'epoch-a',
        afterSequence: '9007199254740993',
      }),
      expect.any(Object),
    );
  });
  it('换筛选清空旧游标和旧行，晚到事件不能污染新条件', () => {
    const { result } = renderHook(useLiveLogs);
    act(() => callbacks[0].update(liveUpdate()));
    act(() => result.current.apply({ disposition: 'ignored', sourceId: '84' }));
    act(() => callbacks[0].update(liveUpdate()));
    expect(result.current.rows).toHaveLength(0);
    expect(subscribeLiveLogs).toHaveBeenLastCalledWith(
      {
        subscriptionId: 'live-1',
        filter: { disposition: 'ignored', sourceId: '84' },
      },
      expect.any(Object),
    );
    act(() =>
      callbacks[1].update(
        liveUpdate({ kind: 'snapshot', items: [liveRow('11', 'ignored')] }),
      ),
    );
    expect(result.current.rows[0].disposition).toBe('ignored');
  });
  it('缓存缺口快照替换旧行；清屏保留后端游标不操作服务缓存', () => {
    const { result } = renderHook(useLiveLogs);
    act(() => callbacks[0].update(liveUpdate()));
    act(() =>
      callbacks[0].update(
        liveUpdate({
          epoch: 'epoch-b',
          cursor: '1',
          resetReason: 'cache_reset',
          items: [{ ...liveRow('1'), id: 'epoch-b:1' }],
        }),
      ),
    );
    expect(result.current.gap).toBe(true);
    expect(result.current.rows.map((row) => row.id)).toEqual(['epoch-b:1']);
    act(() => result.current.clear());
    expect(result.current.rows).toHaveLength(0);
    act(() => result.current.setPaused(true));
    act(() => result.current.setPaused(false));
    expect(subscribeLiveLogs).toHaveBeenLastCalledWith(
      expect.objectContaining({ epoch: 'epoch-b', afterSequence: '1' }),
      expect.any(Object),
    );
  });
  it('向前查询最多 500 条；条件变更取消请求并丢弃迟到结果', async () => {
    let resolve: (value: LiveLogSnapshot) => void = () => undefined;
    vi.mocked(queryLiveLogs).mockImplementation(
      () =>
        new Promise((done) => {
          resolve = done;
        }),
    );
    const { result } = renderHook(useLiveLogs);
    act(() => callbacks[0].update(liveUpdate()));
    act(() => {
      void result.current.loadOlder();
    });
    expect(queryLiveLogs).toHaveBeenCalledWith(
      {
        filter: { disposition: 'all' },
        epoch: 'epoch-a',
        beforeSequence: '10',
        limit: 500,
      },
      expect.any(AbortSignal),
    );
    const signal = vi.mocked(queryLiveLogs).mock.calls[0][1];
    act(() => result.current.apply({ disposition: 'normal' }));
    expect(signal?.aborted).toBe(true);
    await act(async () =>
      resolve({
        items: [liveRow('2')],
        epoch: 'epoch-a',
        watermark: '10',
        hasMore: false,
        budgetLimited: false,
        stats: cache,
      }),
    );
    expect(result.current.rows).toHaveLength(0);
    expect(result.current.olderLoading).toBe(false);
  });
});
