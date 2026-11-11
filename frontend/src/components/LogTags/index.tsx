import { Tag } from 'antd';
import { useText } from '@/utils/i18n';

export function SeverityTag({ value }: { value: number | null }) {
  const t = useText();
  if (value === null || value < 0 || value > 7) return <span>—</span>;
  return (
    <Tag
      color={
        [
          'red',
          'red',
          'volcano',
          'error',
          'warning',
          'cyan',
          'blue',
          'default',
        ][value]
      }
    >
      {value} · {t(`severity.${value}`)}
    </Tag>
  );
}
export function ParseStatusTag({ value }: { value: string }) {
  const t = useText();
  const status = value === 'parsed' || value === 'failed' ? value : 'unknown';
  return (
    <Tag
      color={
        status === 'parsed'
          ? 'success'
          : status === 'failed'
            ? 'error'
            : 'default'
      }
    >
      {t(`parse.${status}`)}
    </Tag>
  );
}
