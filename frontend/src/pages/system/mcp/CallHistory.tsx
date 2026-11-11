import { ReloadOutlined, SearchOutlined } from '@ant-design/icons';
import {
  Alert,
  Button,
  Card,
  DatePicker,
  Form,
  Input,
  InputNumber,
  Modal,
  Select,
  Space,
  Table,
  Typography,
} from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useCallback, useEffect, useRef, useState } from 'react';
import {
  getMcpCalls,
  type McpCallQuery,
  type McpCallSummary,
  type McpPermission,
  type McpServiceView,
  setMcpRetention,
} from '@/services/syslog/mcp';
import { formatTime } from '@/utils/format';
import { useText } from '@/utils/i18n';
import CallDetails, { OutcomeTag, outcomes } from './CallDetails';
import { getMcpError, useMcpQuery } from './common';
import styles from './styles.module.less';

type Filters = Pick<
  McpCallQuery,
  'keyId' | 'toolName' | 'method' | 'peerIp' | 'outcome'
> & { range?: [Dayjs, Dayjs] };

export default function CallHistory({
  service,
  permissions,
  onSettingsSaved,
}: {
  service?: McpServiceView;
  permissions: McpPermission[];
  onSettingsSaved: () => void;
}) {
  const t = useText();
  const [filtersForm] = Form.useForm<Filters>();
  const [retentionForm] = Form.useForm<{ days: number }>();
  const [filters, setFilters] = useState<McpCallQuery>({});
  const [cursors, setCursors] = useState<(string | undefined)[]>([undefined]);
  const [selected, setSelected] = useState<string>();
  const [saving, setSaving] = useState(false);
  const [confirmDays, setConfirmDays] = useState<number>();
  const [saveError, setSaveError] = useState<string>();
  const [saved, setSaved] = useState(false);
  const locked = useRef(false);
  const read = useCallback(
    (signal: AbortSignal) =>
      getMcpCalls({ ...filters, cursor: cursors.at(-1), pageSize: 30 }, signal),
    [filters, cursors],
  );
  const { data, loading, error, reload } = useMcpQuery(read);

  useEffect(() => {
    if (service && !retentionForm.isFieldsTouched())
      retentionForm.setFieldValue('days', service.settings.auditRetentionDays);
  }, [service, retentionForm]);

  async function saveRetention(days: number) {
    if (!service || locked.current) return;
    locked.current = true;
    setSaving(true);
    setSaveError(undefined);
    setSaved(false);
    try {
      await setMcpRetention(service.settings.version, days);
      setSaved(true);
      setConfirmDays(undefined);
      onSettingsSaved();
    } catch (failure) {
      setSaveError(getMcpError(failure));
    } finally {
      setSaving(false);
      locked.current = false;
    }
  }

  function filter(values: Filters) {
    const toInstant = (date: Dayjs) =>
      dayjs
        .tz(date.format('YYYY-MM-DD HH:mm:ss'), 'Asia/Shanghai')
        .toISOString();
    setFilters({
      keyId: values.keyId?.trim(),
      toolName: values.toolName,
      method: values.method?.trim(),
      peerIp: values.peerIp?.trim(),
      outcome: values.outcome,
      from: values.range && toInstant(values.range[0]),
      to: values.range && toInstant(values.range[1]),
    });
    setCursors([undefined]);
  }

  return (
    <div className={styles.stack}>
      <Card size="small">
        <div className={styles.retention}>
          <div className={styles.retentionText}>
            <Typography.Text strong>{t('mcp.auditRetention')}</Typography.Text>
            <Typography.Text type="secondary">
              {t('mcp.retentionHint')}
            </Typography.Text>
            <Typography.Text type="secondary">
              {t('mcp.lastCleanup')}：
              {service?.lastCleanupAt
                ? formatTime(service.lastCleanupAt)
                : t('mcp.notCleaned')}
            </Typography.Text>
          </div>
          <Form
            layout="inline"
            form={retentionForm}
            disabled={!service || saving}
            onValuesChange={() => {
              setSaved(false);
              setSaveError(undefined);
            }}
            onFinish={({ days }) => {
              if (service && days < service.settings.auditRetentionDays)
                setConfirmDays(days);
              else void saveRetention(days);
            }}
          >
            <Form.Item
              name="days"
              label={t('mcp.retentionDays')}
              rules={[
                {
                  required: true,
                  type: 'number',
                  min: 1,
                  max: 3650,
                  message: t('mcp.retentionRule'),
                },
              ]}
            >
              <InputNumber
                min={1}
                max={3650}
                precision={0}
                style={{ width: 112 }}
              />
            </Form.Item>
            <Form.Item>
              <Button type="primary" htmlType="submit" loading={saving}>
                {t('mcp.save')}
              </Button>
            </Form.Item>
          </Form>
        </div>
        {saved && <Alert type="success" showIcon title={t('mcp.saved')} />}
        {saveError && <Alert type="error" showIcon title={t(saveError)} />}
        {service?.cleanupError && (
          <Alert
            type="warning"
            showIcon
            title={t('mcp.cleanupFailed')}
            description={
              <Typography.Text code>{service.cleanupError}</Typography.Text>
            }
          />
        )}
      </Card>
      <Card>
        <div className={styles.toolbar}>
          <Typography.Text strong>{t('mcp.calls')}</Typography.Text>
          <Button icon={<ReloadOutlined />} loading={loading} onClick={reload}>
            {t('mcp.refresh')}
          </Button>
        </div>
        <Form layout="vertical" form={filtersForm} onFinish={filter}>
          <div className={styles.filterGrid}>
            <Form.Item
              name="range"
              label={t('mcp.timeRange')}
              className={styles.wideField}
              rules={[
                {
                  validator: (_, range?: [Dayjs, Dayjs]) =>
                    !range || range[0].isBefore(range[1])
                      ? Promise.resolve()
                      : Promise.reject(new Error(t('logs.validation.range'))),
                },
              ]}
            >
              <DatePicker.RangePicker
                showTime
                format="YYYY-MM-DD HH:mm:ss"
                className={styles.fieldWidth}
              />
            </Form.Item>
            <Form.Item name="keyId" label={t('mcp.keyId')}>
              <Input
                allowClear
                maxLength={20}
                placeholder={t('mcp.keyFilterHint')}
              />
            </Form.Item>
            <Form.Item name="toolName" label={t('mcp.tool')}>
              <Select
                allowClear
                showSearch={{ optionFilterProp: 'label' }}
                options={permissions.map((item) => ({
                  value: item.name,
                  label: `${item.title} · ${item.name}`,
                }))}
              />
            </Form.Item>
            <Form.Item name="peerIp" label={t('mcp.ip')}>
              <Input allowClear maxLength={64} />
            </Form.Item>
            <Form.Item name="outcome" label={t('mcp.result')}>
              <Select
                allowClear
                options={outcomes.map((value) => ({
                  value,
                  label: t(`mcp.outcome.${value}`),
                }))}
              />
            </Form.Item>
            <Form.Item name="method" label={t('mcp.method')}>
              <Input allowClear maxLength={128} placeholder="tools/call" />
            </Form.Item>
          </div>
          <Space wrap className={styles.notice}>
            <Button
              type="primary"
              htmlType="submit"
              icon={<SearchOutlined />}
              loading={loading}
            >
              {t('mcp.filter')}
            </Button>
            <Button
              onClick={() => {
                filtersForm.resetFields();
                filter({});
              }}
            >
              {t('logs.reset')}
            </Button>
            <Typography.Text type="secondary">
              {t('logs.timezone')}
            </Typography.Text>
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
        <Table<McpCallSummary>
          rowKey="id"
          size="small"
          dataSource={error ? [] : data?.items}
          loading={loading}
          pagination={false}
          scroll={{ x: 1200 }}
          locale={{ emptyText: t('mcp.noCalls') }}
          columns={[
            {
              title: t('mcp.startedAt'),
              dataIndex: 'startedAt',
              width: 185,
              render: (value) => formatTime(value),
            },
            {
              title: t('mcp.keyName'),
              width: 160,
              ellipsis: true,
              render: (_, row) => (
                <div className={styles.callName}>
                  <Typography.Text>
                    {row.keyName || t('mcp.unauthenticated')}
                  </Typography.Text>
                  {row.keyId && (
                    <Typography.Text type="secondary" className={styles.mono}>
                      {row.keyId}
                    </Typography.Text>
                  )}
                </div>
              ),
            },
            {
              title: t('mcp.toolMethod'),
              width: 280,
              ellipsis: true,
              render: (_, row) => (
                <div className={styles.callName}>
                  <Typography.Text className={styles.mono}>
                    {row.toolName || row.method || '—'}
                  </Typography.Text>
                  {row.toolName && (
                    <Typography.Text type="secondary" className={styles.mono}>
                      {row.method}
                    </Typography.Text>
                  )}
                </div>
              ),
            },
            { title: t('mcp.ip'), dataIndex: 'peerIp', width: 150 },
            {
              title: t('mcp.result'),
              dataIndex: 'outcome',
              width: 110,
              render: (value) => <OutcomeTag value={value} />,
            },
            {
              title: 'HTTP',
              dataIndex: 'httpStatus',
              width: 75,
              render: (value) => value ?? '—',
            },
            {
              title: t('mcp.duration'),
              dataIndex: 'durationMilliseconds',
              width: 100,
              render: (value) => (value == null ? '—' : `${value} ms`),
            },
            {
              title: t('source.actions'),
              key: 'action',
              fixed: 'right',
              width: 80,
              render: (_, row) => (
                <Button
                  type="link"
                  size="small"
                  onClick={() => setSelected(row.id)}
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
              disabled={
                loading || Boolean(error) || !data?.hasMore || !data.nextCursor
              }
              onClick={() => {
                if (data?.nextCursor)
                  setCursors((previous) => [
                    ...previous,
                    data.nextCursor ?? undefined,
                  ]);
              }}
            >
              {t('logs.next')}
            </Button>
          </Space>
        </div>
      </Card>
      {selected && (
        <CallDetails
          key={selected}
          id={selected}
          onClose={() => setSelected(undefined)}
        />
      )}
      <Modal
        open={confirmDays !== undefined}
        title={t('mcp.confirmRetention')}
        confirmLoading={saving}
        okText={t('mcp.confirm')}
        cancelText={t('app.cancel')}
        onCancel={() => !saving && setConfirmDays(undefined)}
        onOk={() => {
          if (confirmDays !== undefined) void saveRetention(confirmDays);
        }}
        cancelButtonProps={{ disabled: saving }}
        closable={!saving}
        mask={{ closable: !saving }}
      >
        <Typography.Paragraph>
          {t('mcp.retentionDeleteHint', { days: confirmDays ?? 0 })}
        </Typography.Paragraph>
        {saveError && <Alert type="error" showIcon title={t(saveError)} />}
      </Modal>
    </div>
  );
}
