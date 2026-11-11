import dayjs from 'dayjs';
import { describe, expect, it } from 'vitest';
import { formatBytes, formatCount, formatTime, toUtcIso } from './format';

describe('显示格式与时间合同', () => {
  it('UTC转换上海显示但保留查询的绝对时刻', () => {
    expect(formatTime('2026-11-10T00:00:00.123Z')).toBe(
      '2026-11-10 08:00:00.123',
    );
    expect(
      toUtcIso(dayjs('2026-11-10T00:00:00.123Z').tz('Asia/Shanghai')),
    ).toBe('2026-11-10T00:00:00.123Z');
  });
  it('缺失值不伪造成0', () => {
    expect(formatCount(undefined)).toBe('—');
    expect(formatBytes(null)).toBe('—');
    expect(formatTime(0)).toBe('—');
    expect(formatCount(0)).toBe('0');
    expect(formatBytes(1024)).toBe('1 KiB');
  });
});
