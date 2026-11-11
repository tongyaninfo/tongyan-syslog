import {
  Alert,
  Button,
  Descriptions,
  Drawer,
  Segmented,
  Tabs,
  Tag,
  Typography,
} from 'antd';
import { useCallback, useState } from 'react';
import { getMcpCall } from '@/services/syslog/mcp';
import { formatBytes, formatTime } from '@/utils/format';
import { useText } from '@/utils/i18n';
import { formatPayload, useMcpQuery } from './common';
import styles from './styles.module.less';

export const outcomes = [
  'started',
  'success',
  'failed',
  'rejected',
  'cancelled',
  'unknown',
] as const;
export function OutcomeTag({ value }: { value: string }) {
  const t = useText();
  const color =
    value === 'success'
      ? 'green'
      : value === 'failed' || value === 'rejected'
        ? 'red'
        : value === 'started'
          ? 'processing'
          : 'gold';
  return (
    <Tag color={color}>
      {outcomes.some((item) => item === value)
        ? t(`mcp.outcome.${value}`)
        : value}
    </Tag>
  );
}

export function PayloadBlock({
  title,
  value,
}: {
  title: string;
  value: string;
}) {
  const t = useText();
  const [mode, setMode] = useState('formatted');
  return (
    <div>
      <div className={styles.payloadActions}>
        <Typography.Text strong>{title}</Typography.Text>
        <Segmented
          size="small"
          value={mode}
          onChange={setMode}
          options={[
            { value: 'formatted', label: t('mcp.formatted') },
            { value: 'raw', label: t('mcp.raw') },
          ]}
        />
      </div>
      <pre className={styles.payload}>
        {value
          ? formatPayload(value, mode === 'formatted')
          : t('mcp.emptyBody')}
      </pre>
    </div>
  );
}

export default function CallDetails({
  id,
  onClose,
}: {
  id: string;
  onClose: () => void;
}) {
  const t = useText();
  const read = useCallback(
    (signal: AbortSignal) => getMcpCall(id, signal),
    [id],
  );
  const { data, loading, error, reload } = useMcpQuery(read);
  const row = data?.summary;
  return (
    <Drawer
      open
      title={t('mcp.callDetails')}
      size="min(1000px, 100vw)"
      onClose={onClose}
      loading={loading && !data}
      extra={
        <Button loading={loading} onClick={reload}>
          {t('mcp.refresh')}
        </Button>
      }
    >
      <div className={styles.stack}>
        {error && <Alert type="error" showIcon title={t(error)} />}
        {data && row && (
          <>
            <Descriptions
              size="small"
              column={{ xs: 1, sm: 2 }}
              items={[
                {
                  key: 'id',
                  label: t('mcp.callId'),
                  children: <span className={styles.mono}>{row.id}</span>,
                },
                {
                  key: 'status',
                  label: t('mcp.result'),
                  children: <OutcomeTag value={row.outcome} />,
                },
                {
                  key: 'key',
                  label: t('mcp.keyName'),
                  children: `${row.keyName || '—'}${row.keyId ? ` (${row.keyId})` : ''}`,
                },
                {
                  key: 'actor',
                  label: t('mcp.actor'),
                  children: `${row.actorName || '—'}${row.actorId ? ` (${row.actorId})` : ''}`,
                },
                { key: 'ip', label: t('mcp.ip'), children: row.peerIp || '—' },
                {
                  key: 'http',
                  label: t('mcp.httpStatus'),
                  children: `${row.httpStatus ?? '—'} · ${row.httpMethod} · ${row.httpProtocol}`,
                },
                {
                  key: 'start',
                  label: t('mcp.startedAt'),
                  children: formatTime(row.startedAt),
                },
                {
                  key: 'end',
                  label: t('mcp.completedAt'),
                  children: formatTime(row.completedAt),
                },
                {
                  key: 'duration',
                  label: t('mcp.duration'),
                  children:
                    row.durationMilliseconds == null
                      ? '—'
                      : `${row.durationMilliseconds} ms`,
                },
                {
                  key: 'bytes',
                  label: t('mcp.exchangeSize'),
                  children: `${formatBytes(row.requestBytes)} / ${formatBytes(row.responseBytes)}`,
                },
                {
                  key: 'trace',
                  label: t('mcp.traceId'),
                  children: <span className={styles.mono}>{row.traceId}</span>,
                },
                {
                  key: 'rpc',
                  label: 'JSON-RPC ID',
                  children: (
                    <span className={styles.mono}>{row.rpcId ?? '—'}</span>
                  ),
                },
                {
                  key: 'method',
                  label: t('mcp.method'),
                  children: row.method || '—',
                },
                {
                  key: 'tool',
                  label: t('mcp.tool'),
                  children: (
                    <span className={styles.mono}>{row.toolName || '—'}</span>
                  ),
                },
                {
                  key: 'url',
                  label: 'URL',
                  span: 2,
                  children: <span className={styles.mono}>{row.url}</span>,
                },
                ...(row.code
                  ? [
                      {
                        key: 'code',
                        label: t('mcp.resultCode'),
                        span: 2,
                        children: (
                          <Typography.Text code>{row.code}</Typography.Text>
                        ),
                      },
                    ]
                  : []),
              ]}
            />
            {(!row.requestComplete ||
              row.responseComplete === false ||
              row.captureError) && (
              <Alert
                showIcon
                type="warning"
                title={t('mcp.incompleteCapture')}
                description={row.captureError || t('mcp.incompleteHint')}
              />
            )}
            {row.outcome === 'started' && (
              <Alert showIcon type="info" title={t('mcp.inProgressHint')} />
            )}
            <Tabs
              destroyOnHidden
              items={[
                {
                  key: 'request',
                  label: t('mcp.request'),
                  children: (
                    <div className={styles.stack}>
                      <PayloadBlock
                        title={t('mcp.requestHeaders')}
                        value={JSON.stringify(data.requestHeaders)}
                      />
                      <PayloadBlock
                        title={t('mcp.requestBody')}
                        value={data.requestBody}
                      />
                    </div>
                  ),
                },
                {
                  key: 'response',
                  label: t('mcp.response'),
                  children: (
                    <div className={styles.stack}>
                      <PayloadBlock
                        title={t('mcp.responseHeaders')}
                        value={JSON.stringify(data.responseHeaders)}
                      />
                      <PayloadBlock
                        title={t('mcp.responseBody')}
                        value={data.responseBody ?? ''}
                      />
                    </div>
                  ),
                },
              ]}
            />
          </>
        )}
      </div>
    </Drawer>
  );
}
