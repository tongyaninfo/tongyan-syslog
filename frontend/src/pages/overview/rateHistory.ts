export const RATE_WINDOW_SECONDS = 60;

const rateWindowMs = RATE_WINDOW_SECONDS * 1000;
const maxPoints = RATE_WINDOW_SECONDS * 2 + 1;
// 推送间隔约为一秒，容许采样抖动，但漏掉一帧时不跨空档平均计算。
export const MAX_RATE_SAMPLE_GAP_MS = 1750;

export interface RatePoint {
  at: number;
  received: number | null;
  written: number | null;
  ignored: number | null;
}

interface TrafficCounts {
  received: number;
  written: number;
  ignored?: number;
}

interface RateHistory {
  points: RatePoint[];
  baseline?: TrafficCounts & { at: number };
}

export const emptyRateHistory: RateHistory = { points: [] };

export function breakRateHistory(history: RateHistory): RateHistory {
  return history.baseline ? { points: history.points } : history;
}

export function appendRateSample(
  history: RateHistory,
  observedAt: string,
  counts?: TrafficCounts,
): RateHistory {
  const at = Date.parse(observedAt);
  const latest = history.points.at(-1);
  if (!Number.isFinite(at) || (latest && at <= latest.at)) return history;

  const previous = history.baseline;
  const point: RatePoint = { at, received: null, written: null, ignored: null };
  if (previous && counts) {
    const { received, written, ignored } = counts;
    const elapsedMs = at - previous.at;
    if (
      elapsedMs <= MAX_RATE_SAMPLE_GAP_MS &&
      received >= previous.received &&
      written >= previous.written
    ) {
      point.received = ((received - previous.received) * 1000) / elapsedMs;
      point.written = ((written - previous.written) * 1000) / elapsedMs;
      if (
        ignored != null &&
        previous.ignored != null &&
        ignored >= previous.ignored
      )
        point.ignored = ((ignored - previous.ignored) * 1000) / elapsedMs;
    }
  }

  return {
    baseline: counts && {
      at,
      received: counts.received,
      written: counts.written,
      ignored: counts.ignored,
    },
    points: [...history.points, point]
      .filter((sample) => sample.at >= at - rateWindowMs)
      .slice(-maxPoints),
  };
}
