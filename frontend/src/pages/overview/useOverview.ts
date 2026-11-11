import { useEffect, useState } from 'react';
import { ApiError, getErrorKey } from '@/requestErrorConfig';
import {
  type OverviewConnectionState,
  subscribeOverview,
} from '@/services/syslog/overviewStream';
import type { IngestStatus, OverviewUpdate } from '@/services/syslog/types';
import {
  appendRateSample,
  breakRateHistory,
  emptyRateHistory,
} from './rateHistory';

export interface OverviewSnapshot {
  status?: IngestStatus;
  sources?: NonNullable<OverviewUpdate['sources']>;
  databaseSizeBytes?: number | null;
  statusError?: string;
  sourceError?: string;
  refreshedAt?: string;
  loading: boolean;
  connectionState: OverviewConnectionState;
}

/** 每条推送是完整快照；断线保留最后结果并明确标为未更新，不做 HTTP 轮询。 */
export function useOverview() {
  const [snapshot, setSnapshot] = useState<OverviewSnapshot>({
    loading: true,
    connectionState: 'connecting',
  });
  const [subscription, setSubscription] = useState({
    autoRefresh: true,
    request: 0,
    sourceId: undefined as string | undefined,
  });
  const [rates, setRates] = useState(emptyRateHistory);

  useEffect(() => {
    let active = true;
    const { autoRefresh, request, sourceId } = subscription;
    setRates(breakRateHistory);
    if (!autoRefresh && request === 0) {
      setSnapshot((current) => ({
        ...current,
        loading: false,
        connectionState: 'paused',
      }));
      return undefined;
    }
    const stop = subscribeOverview(
      {
        snapshot(update) {
          if (!active) return;
          if (autoRefresh) {
            const traffic = update.sourceTraffic;
            // 来源尚未返回匹配的计数时保留缺口，避免图表混入全部来源或旧来源的数据。
            const counts = sourceId
              ? traffic?.sourceId === sourceId
                ? traffic
                : undefined
              : update.status;
            setRates((current) =>
              appendRateSample(current, update.observedAt, counts),
            );
          }
          setSnapshot({
            status: update.status,
            sources: update.sources ?? undefined,
            databaseSizeBytes: update.databaseSizeBytes,
            sourceError: update.sourceError
              ? getErrorKey(new ApiError(update.sourceError, 503))
              : undefined,
            refreshedAt: update.observedAt,
            loading: false,
            connectionState: autoRefresh ? 'connected' : 'paused',
          });
        },
        state(connectionState) {
          if (!active) return;
          if (connectionState !== 'connected') setRates(breakRateHistory);
          setSnapshot((current) => ({
            ...current,
            connectionState,
            loading: connectionState === 'connecting',
          }));
        },
        expired() {
          if (!active) return;
          setRates(emptyRateHistory);
          setSnapshot({
            loading: false,
            connectionState: 'disconnected',
            statusError: 'error.session',
          });
        },
      },
      !autoRefresh,
      sourceId,
    );
    return () => {
      active = false;
      stop();
    };
  }, [subscription]);

  return {
    ...snapshot,
    rateHistory: rates.points,
    rateSourceId: subscription.sourceId,
    setRateSourceId: (sourceId?: string) => {
      if (sourceId === subscription.sourceId) return;
      setRates(emptyRateHistory);
      setSubscription((current) => ({ ...current, sourceId, request: 0 }));
    },
    autoRefresh: subscription.autoRefresh,
    setAutoRefresh: (autoRefresh: boolean) =>
      setSubscription((current) => ({ ...current, autoRefresh, request: 0 })),
    refresh: () => {
      setSubscription((current) => ({
        ...current,
        request: current.request + 1,
      }));
    },
  };
}
