import { useCallback, useEffect, useRef, useState } from 'react';
import { searchLogs } from '@/services/syslog/api';
import type { LogSearch, LogSearchPage } from '@/services/syslog/types';
import { appendCursor, resetCursorHistory } from './helpers';

interface SearchAttempt {
  query: LogSearch;
  cursor: string | null;
  pageIndex: number;
  history: Array<string | null>;
}

/** 同时取消网络与校验请求代次，即使服务端未响应取消，旧结果也不能覆盖新条件。 */
export function useLogSearch() {
  const request = useRef<AbortController | null>(null);
  const generation = useRef(0);
  const [query, setQuery] = useState<LogSearch | null>(null);
  const [result, setResult] = useState<LogSearchPage | null>(null);
  const [history, setHistory] =
    useState<Array<string | null>>(resetCursorHistory);
  const [pageIndex, setPageIndex] = useState(0);
  const [loading, setLoading] = useState(false);
  const [edited, setEdited] = useState(false);
  const [failure, setFailure] = useState<{
    cause: unknown;
    attempt: SearchAttempt;
  } | null>(null);

  const load = useCallback(async (attempt: SearchAttempt) => {
    request.current?.abort();
    const controller = new AbortController();
    request.current = controller;
    const current = ++generation.current;
    setLoading(true);
    setResult(null);
    setFailure(null);
    try {
      const page = await searchLogs(
        { ...attempt.query, cursor: attempt.cursor },
        controller.signal,
      );
      if (current !== generation.current || controller.signal.aborted) return;
      setResult(page);
      setPageIndex(attempt.pageIndex);
      setHistory(attempt.history);
    } catch (cause) {
      if (current !== generation.current || controller.signal.aborted) return;
      setFailure({ cause, attempt });
    } finally {
      if (current === generation.current && !controller.signal.aborted)
        setLoading(false);
    }
  }, []);

  const start = useCallback(
    (nextQuery: LogSearch) => {
      const firstQuery = { ...nextQuery, cursor: null };
      const firstHistory = resetCursorHistory();
      setEdited(false);
      setQuery(firstQuery);
      setPageIndex(0);
      setHistory(firstHistory);
      return load({
        query: firstQuery,
        cursor: null,
        pageIndex: 0,
        history: firstHistory,
      });
    },
    [load],
  );

  const invalidate = useCallback(() => {
    request.current?.abort();
    generation.current++;
    setLoading(false);
    setResult(null);
    setFailure(null);
    setQuery(null);
    setPageIndex(0);
    setHistory(resetCursorHistory());
    setEdited(true);
  }, []);

  const next = useCallback(() => {
    if (loading || !query || !result?.hasMore || !result.nextCursor) return;
    return load({
      query,
      cursor: result.nextCursor,
      pageIndex: pageIndex + 1,
      history: appendCursor(history, pageIndex, result.nextCursor),
    });
  }, [history, load, loading, pageIndex, query, result]);

  const previous = useCallback(() => {
    if (loading || !query || pageIndex === 0) return;
    return load({
      query,
      cursor: history[pageIndex - 1],
      pageIndex: pageIndex - 1,
      history,
    });
  }, [history, load, loading, pageIndex, query]);

  const retry = useCallback(() => {
    // 重试失败页时继续使用原筛选和游标，避免重查首页或移动时间窗口。
    if (loading || !failure) return;
    return load(failure.attempt);
  }, [failure, load, loading]);

  useEffect(
    () => () => {
      request.current?.abort();
      generation.current++;
    },
    [],
  );

  return {
    query,
    result,
    pageIndex,
    loading,
    edited,
    failure,
    start,
    invalidate,
    next,
    previous,
    retry,
  };
}
