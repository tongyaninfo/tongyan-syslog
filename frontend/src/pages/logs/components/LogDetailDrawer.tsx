import { Alert, Button, Descriptions, Drawer, Space, Typography } from 'antd';
import { type ReactNode, useEffect, useState } from 'react';
import { ParseStatusTag, SeverityTag } from '@/components/LogTags';
import { getLog } from '@/services/syslog/api';
import type { LogEvent } from '@/services/syslog/types';
import { formatTime } from '@/utils/format';
import { useText } from '@/utils/i18n';
import { getLogReadErrorKey } from '../helpers';
import EvidencePreview, { LimitedText } from './EvidencePreview';

interface DetailProps {
  selected: LogEvent | null;
  sourceName?: string;
  onClose: () => void;
  /** 实时缓存详情使用独立身份和读取接口，不回查 ClickHouse。 */
  cacheDetail?: {
    id: string;
    load: (id: string, signal: AbortSignal) => Promise<LogEvent>;
  };
  notice?: ReactNode;
  errorKey?: (error: unknown) => string;
}

const confidenceKeys: Record<string, string> = {
  explicit: 'logs.time.explicit',
  inferred_year_timezone: 'logs.time.inferred_year_timezone',
  inferred_ambiguous: 'logs.time.inferred_ambiguous',
  inferred_distant: 'logs.time.inferred_distant',
  unknown: 'logs.time.unknown',
};

export default function LogDetailDrawer({
  selected,
  sourceName,
  onClose,
  cacheDetail,
  notice,
  errorKey = getLogReadErrorKey,
}: DetailProps) {
  const t = useText();
  const [detail, setDetail] = useState<LogEvent | null>(null);
  const [loadedCacheId, setLoadedCacheId] = useState<string | undefined>();
  const [error, setError] = useState<unknown>(null);
  const [loading, setLoading] = useState(false);
  const [retry, setRetry] = useState(0);
  const eventId = selected?.eventId;
  const receivedAt = selected?.receivedAt;
  const cacheId = cacheDetail?.id;
  const loadCache = cacheDetail?.load;

  // biome-ignore lint/correctness/useExhaustiveDependencies: retry 是用户显式触发重试的代次，必须重新请求同一事件。
  useEffect(() => {
    if (!eventId || !receivedAt) return;
    const controller = new AbortController();
    setDetail(null);
    setLoadedCacheId(undefined);
    setError(null);
    setLoading(true);
    // 用服务端原始毫秒接收时间定位事件，不用页面显示字符串反向解析。
    (cacheId && loadCache
      ? loadCache(cacheId, controller.signal)
      : getLog(eventId, receivedAt, controller.signal)
    )
      .then((result) => {
        if (!controller.signal.aborted) {
          setDetail(result);
          setLoadedCacheId(cacheId);
        }
      })
      .catch((failure: unknown) => {
        if (!controller.signal.aborted) setError(failure);
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [eventId, receivedAt, retry, cacheId, loadCache]);

  const absent = t('logs.notProvided');
  return (
    <Drawer
      open={selected !== null}
      title={t('logs.detailTitle')}
      size="min(780px, 100vw)"
      loading={loading}
      destroyOnHidden
      onClose={onClose}
      className="syslog-log-detail"
      extra={<Button onClick={onClose}>{t('logs.close')}</Button>}
    >
      {notice}
      {error !== null && (
        <Alert
          type="error"
          showIcon
          title={t('logs.detailFailed')}
          description={t(errorKey(error))}
          action={
            <Button onClick={() => setRetry((value) => value + 1)}>
              {t('logs.retry')}
            </Button>
          }
        />
      )}
      {detail &&
        (!cacheId || loadedCacheId === cacheId) &&
        detail.eventId === eventId &&
        detail.receivedAt === receivedAt && (
          <>
            <Space wrap style={{ marginBottom: 20 }}>
              <SeverityTag value={detail.severity} />
              <ParseStatusTag value={detail.parseStatus} />
              <Typography.Text type="secondary">
                {detail.parserVersion}
              </Typography.Text>
            </Space>
            <Descriptions
              size="small"
              column={{ xs: 1, sm: 2, md: 2, lg: 2, xl: 2, xxl: 2 }}
              items={[
                {
                  key: 'id',
                  label: t(cacheId ? 'live.cacheId' : 'logs.eventId'),
                  children: cacheId || detail.eventId,
                },
                {
                  key: 'source',
                  label: t('logs.source'),
                  children: sourceName || detail.sourceId,
                },
                {
                  key: 'received',
                  label: t('logs.receivedAt'),
                  children: formatTime(detail.receivedAt),
                },
                {
                  key: 'eventTime',
                  label: t('logs.eventTime'),
                  children: formatTime(detail.eventTime),
                },
                {
                  key: 'originalTimestamp',
                  label: t('logs.originalTimestamp'),
                  children: detail.originalTimestamp || absent,
                },
                {
                  key: 'confidence',
                  label: t('logs.timeConfidence'),
                  children: t(
                    confidenceKeys[detail.timeConfidence] ??
                      'logs.time.unknown',
                  ),
                },
                {
                  key: 'peer',
                  label: t('logs.peerAddress'),
                  children: detail.peerAddress,
                },
                {
                  key: 'transport',
                  label: t('logs.transport'),
                  children: detail.transport.toUpperCase(),
                },
                {
                  key: 'host',
                  label: t('logs.hostname'),
                  children: detail.hostname || absent,
                },
                {
                  key: 'app',
                  label: t('logs.appName'),
                  children: detail.appName || absent,
                },
                {
                  key: 'proc',
                  label: t('logs.procId'),
                  children: detail.procId || absent,
                },
                {
                  key: 'messageId',
                  label: t('logs.msgId'),
                  children: detail.msgId || absent,
                },
                {
                  key: 'facility',
                  label: t('logs.facility'),
                  children: detail.facility ?? absent,
                },
                ...(!cacheId
                  ? [
                      {
                        key: 'expiry',
                        label: t('logs.expiresAt'),
                        children: formatTime(detail.expiresAt),
                      },
                    ]
                  : []),
                ...(detail.failureCode
                  ? [
                      {
                        key: 'failure',
                        label: t('logs.failureCode'),
                        children: detail.failureCode,
                        span: 'filled' as const,
                      },
                    ]
                  : []),
              ]}
            />
            <section className="syslog-detail-section">
              <Typography.Title level={5}>{t('logs.message')}</Typography.Title>
              <LimitedText text={detail.message} />
            </section>
            {detail.structuredData && (
              <section className="syslog-detail-section">
                <Typography.Title level={5}>
                  {t('logs.structuredData')}
                </Typography.Title>
                <LimitedText text={detail.structuredData} />
              </section>
            )}
            <section className="syslog-detail-section">
              <Typography.Title level={5}>
                {t('logs.original')}
              </Typography.Title>
              <Typography.Paragraph type="secondary">
                {t('logs.originalHint')}
              </Typography.Paragraph>
              <EvidencePreview base64={detail.rawBytes} />
            </section>
          </>
        )}
    </Drawer>
  );
}
