import {
  Alert,
  Button,
  Card,
  Form,
  Input,
  Modal,
  Select,
  Space,
  Switch,
  Table,
  Tag,
  TimePicker,
} from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useRef, useState } from 'react';
import { getSources } from '@/services/syslog/api';
import {
  getReportSubscriptions,
  type ReportSubscription,
  type ReportSubscriptionInput,
  saveReportSubscription,
} from '@/services/syslog/notifications';
import type { LogSource } from '@/services/syslog/types';
import { formatTime } from '@/utils/format';
import { useText } from '@/utils/i18n';
import { getNotificationError, useNotificationQuery } from './common';
import styles from './styles.module.less';

const frequencies = ['daily', 'weekly', 'monthly'] as const;
type ReportForm = Omit<ReportSubscriptionInput, 'sendTime' | 'version'> & {
  sendTime: Dayjs;
};

function SubscriptionEditor({
  subscription,
  sources,
  onCancel,
  onSaved,
}: {
  subscription?: ReportSubscription;
  sources: LogSource[];
  onCancel: () => void;
  onSaved: (value: ReportSubscription) => void;
}) {
  const t = useText();
  const [form] = Form.useForm<ReportForm>();
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string>();
  const locked = useRef(false);
  const required = { required: true, message: t('error.input') };
  const sourceOptions = sources.map(({ id, name, peerAddress }) => ({
    value: id,
    label: `${name} · ${peerAddress}`,
  }));

  async function save(values: ReportForm) {
    if (locked.current) return;
    locked.current = true;
    setSaving(true);
    setError(undefined);
    try {
      const value = await saveReportSubscription(
        {
          ...values,
          name: values.name.trim(),
          sendTime: values.sendTime.format('HH:mm'),
          recipients: [
            ...new Set(values.recipients.map((recipient) => recipient.trim())),
          ],
          version: subscription?.version ?? 0,
        },
        subscription?.id,
      );
      onSaved(value);
    } catch (failure) {
      setError(getNotificationError(failure));
    } finally {
      setSaving(false);
      locked.current = false;
    }
  }

  return (
    <Modal
      open
      title={t(
        subscription ? 'notifications.editReport' : 'notifications.addReport',
      )}
      okText={t('app.save')}
      cancelText={t('app.cancel')}
      onOk={() => form.submit()}
      onCancel={() => {
        if (!locked.current) onCancel();
      }}
      confirmLoading={saving}
      closable={!saving}
      keyboard={!saving}
      mask={{ closable: false }}
      cancelButtonProps={{ disabled: saving }}
    >
      {error && (
        <Alert
          type="error"
          showIcon
          title={t(error)}
          className={styles.notice}
        />
      )}
      <Form
        name="reportSubscription"
        layout="vertical"
        form={form}
        initialValues={{
          name: subscription?.name,
          enabled: subscription?.enabled ?? true,
          frequency: subscription?.frequency ?? 'daily',
          sendTime: dayjs(`2000-01-01T${subscription?.sendTime ?? '09:00'}:00`),
          sourceIds: subscription?.sourceIds ?? [],
          recipients: subscription?.recipients ?? [],
        }}
        onFinish={save}
        disabled={saving}
      >
        <Form.Item
          name="name"
          label={t('notifications.reportName')}
          rules={[{ ...required, whitespace: true }]}
        >
          <Input maxLength={80} />
        </Form.Item>
        <div className={styles.fields}>
          <Form.Item
            name="frequency"
            label={t('notifications.frequency')}
            rules={[required]}
          >
            <Select
              options={frequencies.map((value) => ({
                value,
                label: t(`notifications.kind.${value}`),
              }))}
            />
          </Form.Item>
          <Form.Item
            name="sendTime"
            label={t('notifications.sendTime')}
            rules={[required]}
          >
            <TimePicker format="HH:mm" className={styles.fieldWidth} />
          </Form.Item>
        </div>
        <Form.Item
          name="sourceIds"
          label={t('logs.source')}
          extra={t('notifications.allSourcesHint')}
        >
          <Select
            mode="multiple"
            allowClear
            maxCount={1000}
            maxTagCount="responsive"
            options={sourceOptions}
            showSearch={{ optionFilterProp: 'label' }}
          />
        </Form.Item>
        <Form.Item
          name="recipients"
          label={t('notifications.recipients')}
          extra={t('notifications.recipientsHint')}
          rules={[
            {
              ...required,
              type: 'array',
              min: 1,
              max: 20,
              defaultField: {
                type: 'email',
                max: 254,
                message: t('notifications.invalidAddress'),
              },
            },
          ]}
        >
          <Select
            mode="tags"
            maxCount={20}
            tokenSeparators={[',', ';', '，', '；']}
          />
        </Form.Item>
        <Form.Item
          name="enabled"
          label={t('notifications.reportState')}
          valuePropName="checked"
        >
          <Switch />
        </Form.Item>
      </Form>
    </Modal>
  );
}

export default function ReportSubscriptions() {
  const t = useText();
  const reports = useNotificationQuery(getReportSubscriptions);
  const sources = useNotificationQuery(getSources);
  const [editing, setEditing] = useState<ReportSubscription | null>();
  const loading = reports.loading || sources.loading;
  const error = reports.error ?? sources.error;

  return (
    <div className={styles.stack}>
      <Alert type="info" showIcon title={t('notifications.scheduleHint')} />
      {error && <Alert type="error" showIcon title={t(error)} />}
      <Card
        title={t('notifications.reports')}
        extra={
          <Space wrap>
            <Button
              loading={loading}
              onClick={() => {
                reports.reload();
                sources.reload();
              }}
            >
              {t('app.retry')}
            </Button>
            <Button
              type="primary"
              disabled={loading || Boolean(error)}
              onClick={() => setEditing(null)}
            >
              {t('notifications.addReport')}
            </Button>
          </Space>
        }
      >
        <Table<ReportSubscription>
          rowKey="id"
          size="small"
          scroll={{ x: 920 }}
          dataSource={reports.data}
          loading={loading}
          locale={{ emptyText: t('notifications.noReports') }}
          pagination={{ pageSize: 10, hideOnSinglePage: true }}
          columns={[
            {
              title: t('notifications.reportName'),
              dataIndex: 'name',
              ellipsis: true,
            },
            {
              title: t('notifications.frequency'),
              key: 'schedule',
              render: (_, row) =>
                `${t(`notifications.kind.${row.frequency}`)} · ${row.sendTime}`,
            },
            {
              title: t('logs.source'),
              key: 'sources',
              render: (_, row) =>
                row.sourceIds.length
                  ? t('notifications.sourceCount', {
                      count: row.sourceIds.length,
                    })
                  : t('logs.allSources'),
            },
            {
              title: t('notifications.recipients'),
              key: 'recipients',
              ellipsis: true,
              render: (_, row) => row.recipients.join(', '),
            },
            {
              title: t('notifications.reportState'),
              key: 'enabled',
              render: (_, row) => (
                <Tag color={row.enabled ? 'success' : undefined}>
                  {t(
                    row.enabled
                      ? 'notifications.enabled'
                      : 'notifications.disabled',
                  )}
                </Tag>
              ),
            },
            {
              title: t('notifications.nextSend'),
              key: 'next',
              width: 196,
              render: (_, row) => formatTime(row.nextSendAt),
            },
            {
              title: t('source.actions'),
              key: 'actions',
              width: 110,
              render: (_, row) => (
                <Button
                  type="link"
                  size="small"
                  disabled={loading || Boolean(error)}
                  onClick={() => setEditing(row)}
                >
                  {t('notifications.edit')}
                </Button>
              ),
            },
          ]}
        />
      </Card>
      {editing !== undefined && (
        <SubscriptionEditor
          subscription={editing ?? undefined}
          sources={sources.data ?? []}
          onCancel={() => setEditing(undefined)}
          onSaved={(value) => {
            reports.setData((items) =>
              items?.some((row) => row.id === value.id)
                ? items.map((row) => (row.id === value.id ? value : row))
                : [value, ...(items ?? [])],
            );
            setEditing(undefined);
          }}
        />
      )}
    </div>
  );
}
