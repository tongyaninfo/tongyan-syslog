import { apiRequest } from './api';
import type { LogEvent } from './types';

export type LiveLogDisposition = 'all' | 'normal' | 'ignored';
export interface LiveLogFilter {
  from?: string;
  to?: string;
  sourceId?: string;
  severity?: number;
  hostname?: string;
  appName?: string;
  keywords?: string;
  disposition: LiveLogDisposition;
}
export interface LiveLogRecord {
  id: string;
  sequence: string;
  disposition: 'normal' | 'ignored';
  log: LogEvent;
  messageTruncated: boolean;
}
export interface LiveLogCacheStats {
  epoch: string;
  latestSequence: string;
  entries: number;
  bytes: number;
  deviceCount: number;
  evicted: number;
  rejected: number;
  perDeviceLimit: number;
  maxBytes: number;
  available: boolean;
}
export interface LiveLogSettings {
  version: string;
  perDeviceLimit: number;
  maxBytes: number;
}
export interface LiveLogSettingsView {
  settings: LiveLogSettings;
  cache: LiveLogCacheStats;
}
export interface LiveLogSnapshot {
  items: LiveLogRecord[];
  epoch: string;
  watermark: string;
  nextBeforeSequence?: string | null;
  hasMore: boolean;
  budgetLimited: boolean;
  stats: LiveLogCacheStats;
  gapReason?: string | null;
}
export interface LiveLogUpdate {
  subscriptionId: string;
  kind: 'snapshot' | 'append' | 'heartbeat';
  epoch: string;
  cursor: string;
  items: LiveLogRecord[];
  hasMore: boolean;
  nextBeforeSequence?: string | null;
  resetReason?: string | null;
  budgetLimited: boolean;
  cache: LiveLogCacheStats;
  observedAt: string;
  ingest: {
    queued: number;
    dropped: number;
    spoolFull: boolean;
    state: string;
  };
}
export interface LiveLogSubscription {
  subscriptionId: string;
  filter: LiveLogFilter;
  epoch?: string;
  afterSequence?: string;
}

export const getLiveLogSettings = (signal?: AbortSignal) =>
  apiRequest<LiveLogSettingsView>('/live-logs/settings', { signal });
export const saveLiveLogSettings = (body: LiveLogSettings) =>
  apiRequest<LiveLogSettingsView>('/live-logs/settings', {
    method: 'PUT',
    body,
  });
export const queryLiveLogs = (
  body: {
    filter: LiveLogFilter;
    epoch?: string;
    beforeSequence?: string;
    limit: number;
  },
  signal?: AbortSignal,
) =>
  apiRequest<LiveLogSnapshot>('/live-logs/query', {
    method: 'POST',
    body,
    signal,
  });
export const getLiveLog = (id: string, signal?: AbortSignal) =>
  apiRequest<LiveLogRecord>(`/live-logs/entries/${encodeURIComponent(id)}`, {
    signal,
  });
