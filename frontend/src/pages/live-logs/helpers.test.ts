import dayjs from 'dayjs';
import { describe, expect, it } from 'vitest';
import { ApiError } from '@/requestErrorConfig';
import { liveRow } from '../../../tests/liveLogsFixtures';
import {
  buildLiveLogFilter,
  getLiveLogError,
  MAX_VISIBLE_LOGS,
  mergeLiveLogs,
} from './helpers';

describe('实时筛选与有界行缓存', () => {
  it('缓存淘汰与未迁移错误具有可操作提示，不误报为 ClickHouse 查询失败', () => {
    expect(getLiveLogError(new ApiError('live_log_expired', 404))).toBe(
      'live.error.evicted',
    );
    expect(
      getLiveLogError(new ApiError('live_logs_migration_required', 503)),
    ).toBe('live.error.migration');
    expect(getLiveLogError(new ApiError('live_log_unavailable', 503))).toBe(
      'live.error.unavailable',
    );
  });
  it('默认持续订阅不附加时间范围，固定范围使用原有最长 30 天语义', () => {
    expect(
      buildLiveLogFilter({ keywords: '  LINK DOWN ', disposition: 'ignored' }),
    ).toEqual({
      ok: true,
      filter: {
        sourceId: undefined,
        severity: undefined,
        hostname: undefined,
        appName: undefined,
        keywords: 'LINK DOWN',
        disposition: 'ignored',
      },
    });
    expect(buildLiveLogFilter({ timeMode: 'fixed' }).ok).toBe(false);
    expect(
      buildLiveLogFilter({
        timeMode: 'fixed',
        range: [dayjs('2026-01-01'), dayjs('2026-02-02')],
      }),
    ).toEqual({ ok: false, error: 'logs.validation.maxRange' });
  });
  it('关键词保持 ASCII 全词 AND 校验，不静默放宽中文或非法严重级别', () => {
    expect(buildLiveLogFilter({ keywords: '中文' }).ok).toBe(false);
    expect(buildLiveLogFilter({ keywords: '!!!' }).ok).toBe(false);
    expect(buildLiveLogFilter({ severity: 8 }).ok).toBe(false);
    expect(buildLiveLogFilter({ hostname: 'a'.repeat(256) }).ok).toBe(false);
  });
  it('忽略日志 eventId=0 仍逐条保留，长序号不经过不安全 number 转换', () => {
    const first = liveRow('9007199254740992', 'ignored');
    const second = liveRow('9007199254740993', 'ignored');
    expect(
      mergeLiveLogs([first], [second, first]).map((row) => row.sequence),
    ).toEqual([second.sequence, first.sequence]);
    const updated = { ...second, messageTruncated: true };
    expect(mergeLiveLogs([first, second], [updated])).toEqual([updated, first]);
  });
  it('动态追加始终最多 5000 条摘要，移除最旧条目', () => {
    const rows = Array.from({ length: MAX_VISIBLE_LOGS }, (_, index) =>
      liveRow(String(index + 1)),
    );
    const result = mergeLiveLogs(rows, [liveRow('5001')]);
    expect(result).toHaveLength(MAX_VISIBLE_LOGS);
    expect(result[0].sequence).toBe('5001');
    expect(result.some((row) => row.sequence === '1')).toBe(false);
  });
});
