import type { Dayjs } from 'dayjs';
import { ApiError, getErrorKey } from '@/requestErrorConfig';
import type { LogSearch } from '@/services/syslog/types';
import { toUtcIso } from '@/utils/format';

export const MAX_MESSAGE_BYTES = 65_536;
export const TEXT_PREVIEW_LIMIT = 16_384;
export const HEX_PREVIEW_LIMIT = 4_096;

const logReadErrorKeys = new Map([
  ['storage_connection_failed', 'logs.error.storageConnection'],
  ['storage_access_denied', 'logs.error.storageAccess'],
  ['storage_schema_missing', 'logs.error.storageSchema'],
  ['storage_schema_incompatible', 'logs.error.storageSchemaIncompatible'],
  ['storage_timeout', 'logs.error.storageTimeout'],
  ['storage_unavailable', 'logs.error.storageUnavailable'],
  ['invalid_response', 'logs.error.invalidResponse'],
]);

/** 日志读失败只展示已知错误码对应的文案，避免把上游地址、SQL 或凭据显示在页面。 */
export function getLogReadErrorKey(error: unknown): string {
  if (error instanceof ApiError) {
    const specific = logReadErrorKeys.get(error.code);
    if (specific) return specific;
  }
  const fallback = getErrorKey(error);
  return fallback === 'error.generic'
    ? 'logs.error.queryUnavailable'
    : fallback;
}

export interface SearchFormValues {
  range?: [Dayjs | null, Dayjs | null] | null;
  sourceId?: string;
  severity?: number;
  hostname?: string;
  appName?: string;
  keywords?: string;
  pageSize?: number;
}

type QueryBuildResult =
  | { ok: true; query: LogSearch }
  | { ok: false; error: string };

export type LogFilterFields = Pick<
  LogSearch,
  'sourceId' | 'severity' | 'hostname' | 'appName' | 'keywords'
>;

/** 历史检索和实时订阅共用字段校验，不把缓存搜索变成另一种关键词语义。 */
export function buildLogFilter(
  values: SearchFormValues,
): { ok: true; filter: LogFilterFields } | { ok: false; error: string } {
  const keywords = values.keywords?.trim();
  if (keywords && keywords.length > 512)
    return { ok: false, error: 'logs.validation.keywordsLength' };
  if (
    keywords &&
    (Array.from(keywords).some((character) => character.charCodeAt(0) > 127) ||
      !/[A-Za-z0-9]/.test(keywords))
  )
    return { ok: false, error: 'logs.validation.keywords' };
  const hostname = values.hostname?.trim();
  const appName = values.appName?.trim();
  if ((hostname?.length ?? 0) > 255 || (appName?.length ?? 0) > 255)
    return { ok: false, error: 'logs.validation.fieldLength' };
  if (
    values.severity !== undefined &&
    (!Number.isInteger(values.severity) ||
      values.severity < 0 ||
      values.severity > 7)
  )
    return { ok: false, error: 'logs.validation.severity' };
  return {
    ok: true,
    filter: {
      sourceId: values.sourceId || undefined,
      severity: values.severity,
      hostname: hostname || undefined,
      appName: appName || undefined,
      keywords: keywords || undefined,
    },
  };
}

/** 搜索参数保存在当前页面内存，始终以固定时间窗和游标查询，避免翻页时窗口漂移。 */
export function buildLogQuery(values: SearchFormValues): QueryBuildResult {
  const [from, to] = values.range ?? [];
  if (!from?.isValid() || !to?.isValid() || from.valueOf() >= to.valueOf()) {
    return { ok: false, error: 'logs.validation.range' };
  }
  if (to.valueOf() - from.valueOf() > 30 * 86_400_000) {
    return { ok: false, error: 'logs.validation.maxRange' };
  }
  const fields = buildLogFilter(values);
  if (!fields.ok) return fields;
  const pageSize = values.pageSize ?? 100;
  if (!Number.isInteger(pageSize) || pageSize < 1 || pageSize > 500) {
    return { ok: false, error: 'logs.validation.pageSize' };
  }
  return {
    ok: true,
    query: {
      from: toUtcIso(from),
      to: toUtcIso(to),
      ...fields.filter,
      pageSize,
      cursor: null,
    },
  };
}

/** 每次条件变化都从首游标重新开始；不把客户端页码伪装成存储端偏移分页。 */
export function resetCursorHistory(): Array<string | null> {
  return [null];
}

export function appendCursor(
  history: Array<string | null>,
  pageIndex: number,
  nextCursor: string,
): Array<string | null> {
  return [...history.slice(0, pageIndex + 1), nextCursor];
}

export function truncateText(value: string, maximum: number): string {
  if (value.length <= maximum) return value;
  const result = value.slice(0, maximum);
  // 不在 UTF-16 代理对中间截断，避免预览末尾出现额外的替换字符。
  const last = result.charCodeAt(result.length - 1);
  return last >= 0xd800 && last <= 0xdbff ? result.slice(0, -1) : result;
}

export type DecodedEvidence =
  | { valid: false; error: 'invalid' | 'tooLarge' }
  | {
      valid: true;
      byteLength: number;
      text: string;
      hex: string;
      invalidUtf8: boolean;
      textTruncated: boolean;
      hexTruncated: boolean;
    };

/** 原始证据先校验字节预算再解码；UTF-8 替换字符只用于显示，十六进制仍以原始字节为准。 */
export function decodeEvidence(base64: string): DecodedEvidence {
  if (base64.length > Math.ceil(MAX_MESSAGE_BYTES / 3) * 4) {
    return { valid: false, error: 'tooLarge' };
  }
  let bytes: Uint8Array;
  try {
    const binary = atob(base64);
    if (binary.length > MAX_MESSAGE_BYTES)
      return { valid: false, error: 'tooLarge' };
    bytes = Uint8Array.from(binary, (character) => character.charCodeAt(0));
  } catch {
    return { valid: false, error: 'invalid' };
  }
  let fullText: string;
  let invalidUtf8 = false;
  try {
    fullText = new TextDecoder('utf-8', { fatal: true }).decode(bytes);
  } catch {
    fullText = new TextDecoder('utf-8').decode(bytes);
    invalidUtf8 = true;
  }
  const rows: string[] = [];
  const shown = bytes.subarray(0, HEX_PREVIEW_LIMIT);
  for (let offset = 0; offset < shown.length; offset += 16) {
    const row = shown.subarray(offset, offset + 16);
    const values = Array.from(row, (value) =>
      value.toString(16).padStart(2, '0'),
    ).join(' ');
    const ascii = Array.from(row, (value) =>
      value >= 32 && value <= 126 ? String.fromCharCode(value) : '.',
    ).join('');
    rows.push(
      `${offset.toString(16).padStart(4, '0')}  ${values.padEnd(47)}  ${ascii}`,
    );
  }
  return {
    valid: true,
    byteLength: bytes.length,
    text: truncateText(fullText, TEXT_PREVIEW_LIMIT),
    hex: rows.join('\n'),
    invalidUtf8,
    textTruncated: fullText.length > TEXT_PREVIEW_LIMIT,
    hexTruncated: bytes.length > HEX_PREVIEW_LIMIT,
  };
}
