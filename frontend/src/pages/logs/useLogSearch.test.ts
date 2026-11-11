import { act, renderHook } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '@/requestErrorConfig';
import { searchLogs } from '@/services/syslog/api';
import type {
  LogEvent,
  LogSearch,
  LogSearchPage,
} from '@/services/syslog/types';
import { useLogSearch } from './useLogSearch';

vi.mock('@/services/syslog/api', () => ({ searchLogs: vi.fn() }));

const query: LogSearch = {
  from: '2026-11-09T00:00:00.000Z',
  to: '2026-11-10T00:00:00.000Z',
  pageSize: 100,
};
const row = (message: string): LogEvent => ({
  eventId: '9007199254740993',
  batchId: 'synthetic',
  sourceId: '42',
  peerAddress: '192.0.2.10',
  transport: 'tcp',
  receivedAt: '2026-11-09T08:00:00.000Z',
  eventTime: null,
  expiresAt: '2027-11-09T08:00:00.000Z',
  originalTimestamp: '-',
  timeConfidence: 'unknown',
  hostname: 'switch-example',
  appName: 'syslog',
  procId: '',
  msgId: '',
  structuredData: '',
  facility: 1,
  severity: 6,
  message,
  rawBytes: '',
  parseStatus: 'parsed',
  failureCode: '',
  parserVersion: 'rfc-v1',
});
const page = (
  message: string,
  nextCursor: string | null = null,
): LogSearchPage => ({
  items: [row(message)],
  nextCursor,
  hasMore: nextCursor !== null,
});
function deferred<T>() {
  let resolve: (value: T) => void = () => {
    throw new Error('not initialized');
  };
  const promise = new Promise<T>((done) => {
    resolve = done;
  });
  return { promise, resolve };
}

describe('日志请求与分页生命周期', () => {
  beforeEach(() => vi.clearAllMocks());

  it('取消旧请求且忽略迟到响应，新的筛选结果不能被覆盖', async () => {
    const older = deferred<LogSearchPage>();
    const newer = deferred<LogSearchPage>();
    vi.mocked(searchLogs)
      .mockImplementationOnce(() => older.promise)
      .mockImplementationOnce(() => newer.promise);
    const { result } = renderHook(useLogSearch);
    act(() => {
      void result.current.start({ ...query, keywords: 'OLD' });
    });
    const oldSignal = vi.mocked(searchLogs).mock.calls[0][1];
    act(() => {
      void result.current.start({ ...query, keywords: 'NEW' });
    });
    expect(oldSignal?.aborted).toBe(true);
    await act(async () => newer.resolve(page('new result')));
    expect(result.current.result?.items[0].message).toBe('new result');
    await act(async () => older.resolve(page('stale result')));
    expect(result.current.result?.items[0].message).toBe('new result');
    expect(result.current.loading).toBe(false);
  });

  it('翻页沿用固定查询窗口，改筛选立即清空结果和旧游标', async () => {
    vi.mocked(searchLogs)
      .mockResolvedValueOnce(page('first', 'cursor-next'))
      .mockResolvedValueOnce(page('second'));
    const { result } = renderHook(useLogSearch);
    await act(() => result.current.start(query));
    await act(() => result.current.next());
    expect(searchLogs).toHaveBeenLastCalledWith(
      { ...query, cursor: 'cursor-next' },
      expect.any(AbortSignal),
    );
    expect(result.current.pageIndex).toBe(1);
    act(() => result.current.invalidate());
    expect(result.current.result).toBeNull();
    expect(result.current.query).toBeNull();
    expect(result.current.pageIndex).toBe(0);
    expect(result.current.edited).toBe(true);
    vi.mocked(searchLogs).mockResolvedValueOnce(page('new-filter'));
    await act(() => result.current.start({ ...query, sourceId: '84' }));
    expect(searchLogs).toHaveBeenLastCalledWith(
      { ...query, sourceId: '84', cursor: null },
      expect.any(AbortSignal),
    );
  });

  it('翻页失败时重试同一游标和筛选条件，错误不会被当成空结果', async () => {
    const error = new ApiError('storage_timeout', 504);
    const filtered = { ...query, sourceId: '84', keywords: 'LINK DOWN' };
    vi.mocked(searchLogs)
      .mockResolvedValueOnce(page('old', 'next'))
      .mockRejectedValueOnce(error)
      .mockResolvedValueOnce(page('retried'));
    const { result } = renderHook(useLogSearch);
    await act(() => result.current.start(filtered));
    await act(() => result.current.next());
    expect(result.current.failure?.cause).toBe(error);
    expect(result.current.failure?.attempt.pageIndex).toBe(1);
    expect(result.current.result).toBeNull();
    expect(result.current.loading).toBe(false);
    await act(() => result.current.retry());
    expect(searchLogs).toHaveBeenLastCalledWith(
      { ...filtered, cursor: 'next' },
      expect.any(AbortSignal),
    );
    expect(result.current.result?.items[0].message).toBe('retried');
    expect(result.current.pageIndex).toBe(1);
    expect(result.current.failure).toBeNull();
  });

  it('离开页面取消请求，不把待完成结果写回页面', async () => {
    const pending = deferred<LogSearchPage>();
    vi.mocked(searchLogs).mockImplementationOnce(() => pending.promise);
    const { result, unmount } = renderHook(useLogSearch);
    act(() => {
      void result.current.start(query);
    });
    const signal = vi.mocked(searchLogs).mock.calls[0][1];
    unmount();
    expect(signal?.aborted).toBe(true);
    await act(async () => pending.resolve(page('late')));
  });
});
