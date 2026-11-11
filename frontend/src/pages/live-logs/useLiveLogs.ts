import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError } from '@/requestErrorConfig';
import {
  type LiveLogCacheStats,
  type LiveLogFilter,
  type LiveLogRecord,
  type LiveLogUpdate,
  queryLiveLogs,
} from '@/services/syslog/liveLogs';
import { subscribeLiveLogs } from '@/services/syslog/liveLogsStream';
import type { OverviewConnectionState } from '@/services/syslog/overviewStream';
import { MAX_VISIBLE_LOGS, mergeLiveLogs } from './helpers';

export function useLiveLogs() {
  const [rows, setRows] = useState<LiveLogRecord[]>([]);
  const [filter, setFilter] = useState<LiveLogFilter>({ disposition: 'all' });
  const [generation, setGeneration] = useState(0);
  const [paused, setPaused] = useState(false);
  const [state, setState] = useState<OverviewConnectionState>('connecting');
  const [error, setError] = useState<unknown>(null);
  const [gap, setGap] = useState(false);
  const [budgetLimited, setBudgetLimited] = useState(false);
  const [cache, setCache] = useState<LiveLogCacheStats | null>(null);
  const [ingest, setIngest] = useState<LiveLogUpdate['ingest'] | null>(null);
  const [observedAt, setObservedAt] = useState<string | null>(null);
  const [hasOlder, setHasOlder] = useState(false);
  const [olderLoading, setOlderLoading] = useState(false);
  const resume = useRef<{ epoch?: string; afterSequence?: string }>({});
  const before = useRef<string | undefined>(undefined);
  const revision = useRef(0);
  const olderRequest = useRef<AbortController | undefined>(undefined);

  useEffect(() => {
    if (paused) {
      setState('paused');
      return;
    }
    const currentRevision = revision.current;
    return subscribeLiveLogs(
      { subscriptionId: `live-${generation}`, filter, ...resume.current },
      {
        update: (value) => {
          if (currentRevision !== revision.current) return;
          resume.current = { epoch: value.epoch, afterSequence: value.cursor };
          setCache(value.cache);
          setIngest(value.ingest);
          setObservedAt(value.observedAt);
          setError(null);
          setBudgetLimited(value.budgetLimited);
          if (value.kind === 'snapshot') {
            // 重启或淘汰导致缺口时，禁止把旧一代缓存与新快照混在一起。
            olderRequest.current?.abort();
            setOlderLoading(false);
            before.current = value.nextBeforeSequence ?? undefined;
            setHasOlder(value.hasMore && !!before.current);
            setRows(mergeLiveLogs([], value.items));
            if (value.resetReason) setGap(true);
          } else if (value.kind === 'append')
            setRows((previous) => mergeLiveLogs(previous, value.items));
        },
        state: setState,
        error: setError,
        expired: () => {
          resume.current = {};
          setRows([]);
          setState('disconnected');
        },
      },
    );
  }, [filter, generation, paused]);

  useEffect(() => () => olderRequest.current?.abort(), []);
  const apply = useCallback((next: LiveLogFilter) => {
    revision.current++;
    olderRequest.current?.abort();
    setOlderLoading(false);
    resume.current = {};
    before.current = undefined;
    setRows([]);
    setHasOlder(false);
    setGap(false);
    setError(null);
    setObservedAt(null);
    setFilter(next);
    setGeneration((value) => value + 1);
    setPaused(false);
  }, []);
  const refresh = useCallback(() => apply(filter), [apply, filter]);
  const loadOlder = async () => {
    if (!before.current || olderLoading || rows.length >= MAX_VISIBLE_LOGS)
      return;
    const currentRevision = revision.current;
    const epoch = resume.current.epoch;
    const controller = new AbortController();
    olderRequest.current = controller;
    setOlderLoading(true);
    setError(null);
    try {
      const result = await queryLiveLogs(
        { filter, epoch, beforeSequence: before.current, limit: 500 },
        controller.signal,
      );
      if (controller.signal.aborted || currentRevision !== revision.current)
        return;
      if (result.epoch !== epoch || result.gapReason) {
        setGap(true);
        refresh();
        setGap(true);
        return;
      }
      setRows((previous) => mergeLiveLogs(previous, result.items));
      before.current = result.nextBeforeSequence ?? undefined;
      setHasOlder(result.hasMore && !!before.current);
      setCache(result.stats);
      setBudgetLimited(result.budgetLimited);
    } catch (failure) {
      if (controller.signal.aborted || currentRevision !== revision.current)
        return;
      if (
        failure instanceof ApiError &&
        failure.code === 'live_logs_cache_reset'
      ) {
        refresh();
        setGap(true);
      } else setError(failure);
    } finally {
      if (!controller.signal.aborted && currentRevision === revision.current)
        setOlderLoading(false);
    }
  };
  const clear = () => {
    olderRequest.current?.abort();
    setOlderLoading(false);
    before.current = undefined;
    setRows([]);
    setHasOlder(false);
  };
  return {
    rows,
    filter,
    paused,
    setPaused,
    state,
    error,
    gap,
    budgetLimited,
    cache,
    ingest,
    observedAt,
    hasOlder,
    olderLoading,
    apply,
    refresh,
    loadOlder,
    clear,
  };
}
