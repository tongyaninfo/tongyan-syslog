import dayjs from 'dayjs';
import { describe, expect, it } from 'vitest';
import {
  appendCursor,
  buildLogQuery,
  decodeEvidence,
  HEX_PREVIEW_LIMIT,
  MAX_MESSAGE_BYTES,
  resetCursorHistory,
  TEXT_PREVIEW_LIMIT,
  truncateText,
} from './helpers';

const from = dayjs('2026-11-09T00:00:00.000Z');
const to = dayjs('2026-11-10T00:00:00.000Z');

describe('日志查询合同', () => {
  it('保留长整型字符串 ID，上海时区仅影响显示，不偏移实际查询时刻', () => {
    const result = buildLogQuery({
      range: [from.tz('Asia/Shanghai'), to.tz('Asia/Shanghai')],
      sourceId: '9007199254740993',
      keywords: ' LINK DOWN ',
      hostname: ' ',
      pageSize: 100,
    });
    expect(result.ok).toBe(true);
    if (!result.ok) throw new Error('unexpected validation failure');
    expect(result.query).toMatchObject({
      from: '2026-11-09T00:00:00.000Z',
      to: '2026-11-10T00:00:00.000Z',
      sourceId: '9007199254740993',
      keywords: 'LINK DOWN',
      cursor: null,
    });
    expect(result.query.hostname).toBeUndefined();
  });

  it.each([
    { range: [to, from] },
    { range: [from, from.add(30, 'day').add(1, 'millisecond')] },
    { range: null },
    { range: [from, to], pageSize: 501 },
    { range: [from, to], severity: 8 },
    { range: [from, to], keywords: '交换机' },
    { range: [from, to], keywords: 'LINK 中文' },
    { range: [from, to], keywords: '---' },
    { range: [from, to], keywords: 'a'.repeat(513) },
    { range: [from, to], hostname: 'a'.repeat(256) },
  ])('拒绝超出后端合同的筛选条件 %o', (values) => {
    expect(
      buildLogQuery(values as Parameters<typeof buildLogQuery>[0]).ok,
    ).toBe(false);
  });

  it('恰好 30 天可查，空白关键词表示不加正文过滤', () => {
    const result = buildLogQuery({
      range: [from, from.add(30, 'day')],
      keywords: '   ',
    });
    expect(result.ok).toBe(true);
    if (result.ok) expect(result.query.keywords).toBeUndefined();
  });

  it('条件变更清空游标，回退后翻页截去旧分支', () => {
    const first = resetCursorHistory();
    const second = appendCursor(first, 0, 'next-1');
    const third = appendCursor(second, 1, 'next-2');
    expect(third).toEqual([null, 'next-1', 'next-2']);
    expect(appendCursor(third, 0, 'new-next')).toEqual([null, 'new-next']);
    expect(resetCursorHistory()).toEqual([null]);
  });
});

describe('原文证据预览', () => {
  it('保留 HTML 原样作为文本，不把它转换为可执行标记', () => {
    const value =
      '<img src=x onerror="globalThis.injected=true"><script>alert(1)</script>';
    const result = decodeEvidence(btoa(value));
    expect(result.valid).toBe(true);
    if (result.valid) {
      expect(result.text).toBe(value);
      expect(result.invalidUtf8).toBe(false);
    }
  });

  it('非法 UTF-8 仍保留可比对的原始十六进制字节', () => {
    const result = decodeEvidence(btoa(String.fromCharCode(0xff, 0x00, 0x41)));
    expect(result.valid).toBe(true);
    if (result.valid) {
      expect(result.invalidUtf8).toBe(true);
      expect(result.byteLength).toBe(3);
      expect(result.hex).toContain('ff 00 41');
      expect(result.text).toContain('\uFFFD');
    }
  });

  it('文本和十六进制分别受限并报告截断', () => {
    const result = decodeEvidence(btoa('A'.repeat(TEXT_PREVIEW_LIMIT + 64)));
    expect(result.valid).toBe(true);
    if (result.valid) {
      expect(result.text).toHaveLength(TEXT_PREVIEW_LIMIT);
      expect(result.textTruncated).toBe(true);
      expect(result.hexTruncated).toBe(true);
      expect(result.hex.split('\n')).toHaveLength(HEX_PREVIEW_LIMIT / 16);
    }
  });

  it('超字节预算或无效 Base64 明确失败，不产生无界预览', () => {
    expect(decodeEvidence(btoa('a'.repeat(MAX_MESSAGE_BYTES + 1)))).toEqual({
      valid: false,
      error: 'tooLarge',
    });
    expect(decodeEvidence('not$base64')).toEqual({
      valid: false,
      error: 'invalid',
    });
  });

  it('UTF-16 截断不会拆开代理对', () => {
    expect(truncateText('A😀B', 2)).toBe('A');
    expect(truncateText('A😀B', 3)).toBe('A😀');
  });
});
