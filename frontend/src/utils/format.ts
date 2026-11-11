import dayjs, { type Dayjs } from 'dayjs';
import timezone from 'dayjs/plugin/timezone';
import utc from 'dayjs/plugin/utc';

dayjs.extend(utc);
dayjs.extend(timezone);
const numberFormats = new Map<string, Intl.NumberFormat>();
export function formatCount(value?: number | null, locale = 'zh-CN') {
  if (value == null || !Number.isFinite(value)) return '—';
  let formatter = numberFormats.get(locale);
  if (!formatter) {
    formatter = new Intl.NumberFormat(locale, { maximumFractionDigits: 1 });
    numberFormats.set(locale, formatter);
  }
  return formatter.format(value);
}
export const formatTime = (value?: string | number | null) =>
  value && dayjs(value).isValid()
    ? dayjs(value).tz('Asia/Shanghai').format('YYYY-MM-DD HH:mm:ss.SSS')
    : '—';
export function formatBytes(value?: number | null, locale?: string): string {
  if (value == null || !Number.isFinite(value) || value < 0) return '—';
  const unit =
    value > 0 ? Math.min(Math.floor(Math.log(value) / Math.log(1024)), 4) : 0;
  return `${formatCount(value / 1024 ** unit, locale)} ${['B', 'KiB', 'MiB', 'GiB', 'TiB'][unit]}`;
}
export const toUtcIso = (value: Dayjs) => value.toISOString();
