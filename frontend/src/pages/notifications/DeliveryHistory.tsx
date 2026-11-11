import {
  Alert,
  Button,
  Card,
  DatePicker,
  Form,
  Input,
  Select,
  Space,
  Table,
  Typography,
} from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useCallback, useState } from 'react';
import {
  type DeliveryQuery,
  getMailDeliveries,
  type MailDeliverySummary,
} from '@/services/syslog/notifications';
import { formatTime } from '@/utils/format';
import { useText } from '@/utils/i18n';
import { useNotificationQuery } from './common';
import DeliveryDetails, {
  DeliveryStatusTag,
  statuses,
} from './DeliveryDetails';
import styles from './styles.module.less';

const kinds = ['test', 'daily', 'weekly', 'monthly'] as const;
type HistoryForm = Pick<DeliveryQuery, 'kind' | 'status' | 'recipient'> & {
  range?: [Dayjs, Dayjs];
};

const toShanghaiInstant = (value: Dayjs) =>
  dayjs.tz(value.format('YYYY-MM-DD HH:mm:ss'), 'Asia/Shanghai').toISOString();

export default function DeliveryHistory({
  deliveryId,
  onViewDelivery,
}: {
  deliveryId?: string;
  onViewDelivery: (id?: string) => void;
}) {
  const t = useText();
  const [form] = Form.useForm<HistoryForm>();
  const [filters, setFilters] = useState<DeliveryQuery>({});
  const [cursors, setCursors] = useState<(string | undefined)[]>([undefined]);
  const read = useCallback(
    (signal: AbortSignal) =>
      getMailDeliveries({ ...filters, beforeId: cursors.at(-1) }, signal),
    [filters, cursors],
  );
  const { data, loading, error, reload } = useNotificationQuery(read);

  function filter({ kind, status, recipient, range }: HistoryForm) {
    setFilters({
      kind,
      status,
      recipient: recipient?.trim(),
      from: range?.[0] && toShanghaiInstant(range[0]),
      to: range?.[1] && toShanghaiInstant(range[1]),
    });
    setCursors([undefined]);
  }

  return (
    <div className={styles.stack}>
      <Card
        title={t('notifications.history')}
        extra={
          <Button loading={loading} onClick={reload}>
            {t('app.retry')}
          </Button>
        }
      >
        <Form
          name="mailDeliveryFilters"
          layout="vertical"
          form={form}
          onFinish={filter}
          disabled={loading}
        >
          <div className={styles.fields}>
            <Form.Item label={t('notifications.mailKind')} name="kind">
              <Select
                allowClear
                options={kinds.map((value) => ({
                  value,
                  label: t(`notifications.kind.${value}`),
                }))}
              />
            </Form.Item>
            <Form.Item label={t('notifications.deliveryStatus')} name="status">
              <Select
                allowClear
                options={statuses.map((value) => ({
                  value,
                  label: t(`notifications.status.${value}`),
                }))}
              />
            </Form.Item>
            <Form.Item label={t('notifications.recipient')} name="recipient">
              <Input allowClear maxLength={254} />
            </Form.Item>
            <Form.Item
              label={t('notifications.createdAt')}
              name="range"
              extra={t('logs.timezone')}
              rules={[
                {
                  validator: (_, value?: [Dayjs, Dayjs]) =>
                    !value || value[0].isBefore(value[1])
                      ? Promise.resolve()
                      : Promise.reject(new Error(t('logs.validation.range'))),
                },
              ]}
            >
              <DatePicker.RangePicker
                className={styles.fieldWidth}
                showTime
                format="YYYY-MM-DD HH:mm:ss"
              />
            </Form.Item>
          </div>
          <Space wrap className={styles.notice}>
            <Button type="primary" htmlType="submit">
              {t('notifications.filter')}
            </Button>
            <Button
              onClick={() => {
                form.resetFields();
                filter({});
              }}
            >
              {t('logs.reset')}
            </Button>
          </Space>
        </Form>
        {error && (
          <Alert
            type="error"
            showIcon
            title={t(error)}
            className={styles.notice}
          />
        )}
        <Table<MailDeliverySummary>
          rowKey="id"
          size="small"
          scroll={{ x: 1080 }}
          loading={loading}
          dataSource={error ? [] : data?.items}
          pagination={false}
          locale={{ emptyText: t('notifications.noDeliveries') }}
          columns={[
            {
              title: t('notifications.createdAt'),
              dataIndex: 'createdAt',
              width: 200,
              render: (value) => formatTime(value),
            },
            {
              title: t('notifications.mailKind'),
              dataIndex: 'kind',
              width: 120,
              render: (value) => t(`notifications.kind.${value}`),
            },
            {
              title: t('notifications.recipient'),
              dataIndex: 'recipient',
              ellipsis: true,
            },
            {
              title: t('notifications.subject'),
              dataIndex: 'subject',
              ellipsis: true,
            },
            {
              title: t('notifications.deliveryStatus'),
              key: 'status',
              width: 150,
              render: (_, row) => <DeliveryStatusTag status={row.status} />,
            },
            {
              title: t('source.actions'),
              key: 'actions',
              width: 120,
              render: (_, row) => (
                <Button
                  size="small"
                  type="link"
                  onClick={() => onViewDelivery(row.id)}
                >
                  {t('logs.detail')}
                </Button>
              ),
            },
          ]}
        />
        <div className={styles.paging}>
          <Typography.Text type="secondary">
            {t('logs.page', { page: cursors.length })}
          </Typography.Text>
          <Space>
            <Button
              disabled={loading || cursors.length === 1}
              onClick={() => setCursors((previous) => previous.slice(0, -1))}
            >
              {t('logs.previous')}
            </Button>
            <Button
              disabled={loading || Boolean(error) || !data?.nextBeforeId}
              onClick={() => {
                const next = data?.nextBeforeId;
                if (next) setCursors((previous) => [...previous, next]);
              }}
            >
              {t('logs.next')}
            </Button>
          </Space>
        </div>
      </Card>
      {deliveryId && (
        <DeliveryDetails
          key={deliveryId}
          id={deliveryId}
          onClose={() => onViewDelivery(undefined)}
        />
      )}
    </div>
  );
}
