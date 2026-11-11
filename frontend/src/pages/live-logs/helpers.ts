import { ApiError, getErrorKey } from '@/requestErrorConfig';
import type {
  LiveLogDisposition,
  LiveLogFilter,
  LiveLogRecord,
} from '@/services/syslog/liveLogs';
import { toUtcIso } from '@/utils/format';
import { buildLogFilter, type SearchFormValues } from '../logs/helpers';

export const MAX_VISIBLE_LOGS = 5000;
export interface LiveFilterForm extends SearchFormValues {
  timeMode?: 'live' | 'fixed';
  disposition?: LiveLogDisposition;
}
export function buildLiveLogFilter(
  values: LiveFilterForm,
): { ok: true; filter: LiveLogFilter } | { ok: false; error: string } {
  const fields = buildLogFilter(values);
  if (!fields.ok) return fields;
  const filter: LiveLogFilter = {
    ...fields.filter,
    disposition: values.disposition ?? 'all',
  };
  if (!['all', 'normal', 'ignored'].includes(filter.disposition))
    return { ok: false, error: 'error.input' };
  if (values.timeMode === 'fixed') {
    const [from, to] = values.range ?? [];
    if (!from?.isValid() || !to?.isValid() || from.valueOf() >= to.valueOf())
      return { ok: false, error: 'logs.validation.range' };
    if (to.valueOf() - from.valueOf() > 30 * 86_400_000)
      return { ok: false, error: 'logs.validation.maxRange' };
    filter.from = toUtcIso(from);
    filter.to = toUtcIso(to);
  }
  return { ok: true, filter };
}

export const compareSequence = (left: string, right: string) =>
  left.length - right.length || left.localeCompare(right);

/** 实时行 ID 与正式事件 ID 分开；忽略日志的 eventId=0 也必须保持独立。 */
export function mergeLiveLogs(
  previous: LiveLogRecord[],
  incoming: LiveLogRecord[],
): LiveLogRecord[] {
  const rows = new Map(previous.map((record) => [record.id, record]));
  for (const record of incoming) rows.set(record.id, record);
  return Array.from(rows.values())
    .sort(
      (left, right) =>
        Date.parse(right.log.receivedAt) - Date.parse(left.log.receivedAt) ||
        compareSequence(right.sequence, left.sequence),
    )
    .slice(0, MAX_VISIBLE_LOGS);
}

const errors: Record<string, string> = {
  live_logs_migration_required: 'live.error.migration',
  live_logs_cache_reset: 'live.error.cacheReset',
  live_logs_unavailable: 'live.error.unavailable',
  live_log_unavailable: 'live.error.unavailable',
  live_logs_busy: 'live.error.busy',
  live_logs_entry_evicted: 'live.error.evicted',
  live_log_expired: 'live.error.evicted',
  not_found: 'live.error.evicted',
  version_conflict: 'live.error.conflict',
};
export const getLiveLogError = (error: unknown) =>
  (error instanceof ApiError && errors[error.code]) || getErrorKey(error);
