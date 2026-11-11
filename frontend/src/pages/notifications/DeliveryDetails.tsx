import {
  Alert,
  Button,
  Descriptions,
  Drawer,
  Tabs,
  Tag,
  Typography,
} from 'antd';
import { useCallback, useMemo } from 'react';
import {
  type DeliveryStatus,
  getMailDelivery,
} from '@/services/syslog/notifications';
import { formatTime } from '@/utils/format';
import { useText } from '@/utils/i18n';
import { getNotificationErrorKey, useNotificationQuery } from './common';
import { safeMailPreview } from './mailPreview';
import styles from './styles.module.less';

export const statuses = [
  'queued',
  'sending',
  'accepted',
  'failed',
  'unknown',
] as const;
const statusColors: Record<DeliveryStatus, string> = {
  queued: 'default',
  sending: 'processing',
  accepted: 'success',
  failed: 'error',
  unknown: 'warning',
};

export function DeliveryStatusTag({ status }: { status: DeliveryStatus }) {
  const t = useText();
  return (
    <Tag color={statusColors[status]}>
      {t(`notifications.status.${status}`)}
    </Tag>
  );
}

export default function DeliveryDetails({
  id,
  onClose,
}: {
  id: string;
  onClose: () => void;
}) {
  const t = useText();
  const read = useCallback(
    (signal: AbortSignal) => getMailDelivery(id, signal),
    [id],
  );
  const { data, loading, error, reload } = useNotificationQuery(read);
  const failureKey =
    data?.failureCode && getNotificationErrorKey(data.failureCode);
  const html = useMemo(
    () => (data?.htmlBody ? safeMailPreview(data.htmlBody) : ''),
    [data?.htmlBody],
  );

  return (
    <Drawer
      open
      title={t('notifications.deliveryDetail')}
      size="min(840px, 100vw)"
      onClose={onClose}
      loading={loading && !data}
      extra={
        <Button loading={loading} onClick={reload}>
          {t('app.retry')}
        </Button>
      }
    >
      <div className={styles.stack}>
        {error && <Alert type="error" showIcon title={t(error)} />}
        {data && (
          <>
            <Descriptions
              column={1}
              size="small"
              items={[
                {
                  key: 'subject',
                  label: t('notifications.subject'),
                  children: data.subject || '—',
                },
                {
                  key: 'kind',
                  label: t('notifications.mailKind'),
                  children: t(`notifications.kind.${data.kind}`),
                },
                {
                  key: 'subscription',
                  label: t('notifications.reportName'),
                  children: data.subscriptionName || '—',
                },
                {
                  key: 'recipient',
                  label: t('notifications.recipient'),
                  children: data.recipient,
                },
                {
                  key: 'status',
                  label: t('notifications.deliveryStatus'),
                  children: <DeliveryStatusTag status={data.status} />,
                },
                {
                  key: 'createdAt',
                  label: t('notifications.createdAt'),
                  children: formatTime(data.createdAt),
                },
                {
                  key: 'updatedAt',
                  label: t('notifications.updatedAt'),
                  children: formatTime(data.updatedAt),
                },
                {
                  key: 'attempts',
                  label: t('notifications.attempts'),
                  children: data.attempts,
                },
                ...(data.periodStart && data.periodEnd
                  ? [
                      {
                        key: 'period',
                        label: t('notifications.period'),
                        children: `${formatTime(data.periodStart)} ～ ${formatTime(data.periodEnd)}`,
                      },
                    ]
                  : []),
                ...(data.failureCode
                  ? [
                      {
                        key: 'failure',
                        label: t('notifications.failureCode'),
                        children: (
                          <div>
                            {failureKey && <div>{t(failureKey)}</div>}
                            <Typography.Text code>
                              {data.failureCode}
                            </Typography.Text>
                          </div>
                        ),
                      },
                    ]
                  : []),
              ]}
            />
            {html || data.textBody ? (
              <Tabs
                items={[
                  ...(html
                    ? [
                        {
                          key: 'html',
                          label: t('notifications.htmlPreview'),
                          children: (
                            <iframe
                              className={styles.preview}
                              title={t('notifications.htmlPreview')}
                              srcDoc={html}
                              sandbox=""
                              referrerPolicy="no-referrer"
                            />
                          ),
                        },
                      ]
                    : []),
                  ...(data.textBody
                    ? [
                        {
                          key: 'text',
                          label: t('notifications.textPreview'),
                          children: (
                            <pre className={styles.textBody}>
                              {data.textBody}
                            </pre>
                          ),
                        },
                      ]
                    : []),
                ]}
              />
            ) : (
              <Typography.Text type="secondary">
                {t('notifications.noBody')}
              </Typography.Text>
            )}
          </>
        )}
      </div>
    </Drawer>
  );
}
