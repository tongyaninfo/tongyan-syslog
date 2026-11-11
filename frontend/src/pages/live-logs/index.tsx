import {
  ClearOutlined,
  PauseCircleOutlined,
  PlayCircleOutlined,
  ReloadOutlined,
  SearchOutlined,
  SettingOutlined,
} from '@ant-design/icons';
import { PageContainer } from '@ant-design/pro-components';
import {
  Alert,
  Button,
  Card,
  Col,
  DatePicker,
  Empty,
  Form,
  Input,
  Radio,
  Row,
  Select,
  Space,
  Table,
  type TableColumnsType,
  Tag,
  Typography,
} from 'antd';
import type { TableRef } from 'antd/es/table';
import dayjs from 'dayjs';
import { useEffect, useMemo, useRef, useState } from 'react';
import { ParseStatusTag, SeverityTag } from '@/components/LogTags';
import { getSources } from '@/services/syslog/api';
import { getLiveLog, type LiveLogRecord } from '@/services/syslog/liveLogs';
import type { LogSource } from '@/services/syslog/types';
import { formatBytes, formatCount, formatTime } from '@/utils/format';
import { useText } from '@/utils/i18n';
import LogDetailDrawer from '../logs/components/LogDetailDrawer';
import { truncateText } from '../logs/helpers';
import {
  buildLiveLogFilter,
  getLiveLogError,
  type LiveFilterForm,
  MAX_VISIBLE_LOGS,
} from './helpers';
import SettingsModal from './SettingsModal';
import { useLiveLogs } from './useLiveLogs';
import '../logs/index.less';
import './index.less';

const loadCacheDetail = async (id: string, signal: AbortSignal) =>
  (await getLiveLog(id, signal)).log;

export default function LiveLogsPage() {
  const t = useText();
  const stream = useLiveLogs();
  const [form] = Form.useForm<LiveFilterForm>();
  const table = useRef<TableRef>(null);
  const timeMode = Form.useWatch('timeMode', form);
  const [dirty, setDirty] = useState(false);
  const [validation, setValidation] = useState<string | null>(null);
  const [sources, setSources] = useState<LogSource[]>([]);
  const [sourceError, setSourceError] = useState<unknown>(null);
  const [sourceRetry, setSourceRetry] = useState(0);
  const [settings, setSettings] = useState(false);
  const [selected, setSelected] = useState<LiveLogRecord | null>(null);
  const sourceMap = useMemo(
    () => new Map(sources.map((source) => [source.id, source.name])),
    [sources],
  );
  const initial = useMemo<LiveFilterForm>(
    () => ({
      timeMode: 'live',
      disposition: 'all',
      range: [
        dayjs().tz('Asia/Shanghai').subtract(1, 'hour'),
        dayjs().tz('Asia/Shanghai'),
      ],
    }),
    [],
  );
  // biome-ignore lint/correctness/useExhaustiveDependencies: 用户显式重试来源读取。
  useEffect(() => {
    const controller = new AbortController();
    setSourceError(null);
    getSources(controller.signal)
      .then((value) => {
        if (!controller.signal.aborted) setSources(value);
      })
      .catch((error) => {
        if (!controller.signal.aborted) setSourceError(error);
      });
    return () => controller.abort();
  }, [sourceRetry]);
  const submit = (values: LiveFilterForm) => {
    const result = buildLiveLogFilter(values);
    if (!result.ok) {
      setValidation(result.error);
      return;
    }
    setValidation(null);
    setDirty(false);
    setSelected(null);
    stream.apply(result.filter);
    table.current?.scrollTo({ top: 0 });
  };
  const resume = () => {
    table.current?.scrollTo({ top: 0 });
    stream.setPaused(false);
  };
  const columns: TableColumnsType<LiveLogRecord> = [
    {
      title: t('logs.receivedAt'),
      width: 185,
      key: 'received',
      render: (_, row) => (
        <span className="syslog-time-cell">
          {formatTime(row.log.receivedAt)}
        </span>
      ),
    },
    {
      title: t('logs.severity'),
      width: 100,
      key: 'severity',
      render: (_, row) => <SeverityTag value={row.log.severity} />,
    },
    {
      title: t('live.disposition'),
      width: 125,
      key: 'disposition',
      render: (_, row) => (
        <Tag color={row.disposition === 'ignored' ? 'orange' : 'blue'}>
          {t(`live.disposition.${row.disposition}`)}
        </Tag>
      ),
    },
    {
      title: t('logs.source'),
      width: 150,
      key: 'source',
      ellipsis: true,
      render: (_, row) => sourceMap.get(row.log.sourceId) ?? row.log.sourceId,
    },
    {
      title: t('logs.hostname'),
      width: 130,
      key: 'hostname',
      ellipsis: true,
      render: (_, row) => row.log.hostname || '—',
    },
    {
      title: t('logs.appName'),
      width: 120,
      key: 'appName',
      ellipsis: true,
      render: (_, row) => row.log.appName || '—',
    },
    {
      title: t('logs.message'),
      width: 380,
      key: 'message',
      render: (_, row) => (
        <span className="syslog-live-message">
          {truncateText(row.log.message, 240)}
          {(row.messageTruncated || row.log.message.length > 240) && (
            <Typography.Text type="secondary">
              {' '}
              … {t('live.truncated')}
            </Typography.Text>
          )}
        </span>
      ),
    },
    {
      title: t('logs.parseStatus'),
      width: 130,
      key: 'parse',
      render: (_, row) => (
        <>
          <ParseStatusTag value={row.log.parseStatus} />
          <span className="syslog-parser-version">{row.log.parserVersion}</span>
        </>
      ),
    },
    {
      title: t('logs.action'),
      width: 80,
      fixed: 'right',
      key: 'action',
      render: (_, row) => (
        <Button type="link" size="small" onClick={() => setSelected(row)}>
          {t('logs.detail')}
        </Button>
      ),
    },
  ];
  const cache = stream.cache;
  return (
    <PageContainer
      className="syslog-live-page"
      title={t('live.title')}
      content={t('live.subtitle')}
      extra={
        <Button icon={<SettingOutlined />} onClick={() => setSettings(true)}>
          {t('live.settings')}
        </Button>
      }
    >
      <div className="syslog-live-metrics">
        {[
          [
            t('live.entries'),
            cache ? formatCount(cache.entries) : '—',
            cache
              ? `${t('live.devices')}: ${formatCount(cache.deviceCount)}`
              : '—',
          ],
          [
            t('live.memory'),
            cache ? formatBytes(cache.bytes) : '—',
            `${t('live.budget')}: ${cache ? formatBytes(cache.maxBytes) : '—'}`,
          ],
          [
            t('live.perDeviceLimit'),
            cache ? formatCount(cache.perDeviceLimit) : '—',
            t('live.cacheHint'),
          ],
          [
            t('live.evicted'),
            cache ? formatCount(cache.evicted) : '—',
            `${t('live.rejected')}: ${cache ? formatCount(cache.rejected) : '—'}`,
          ],
        ].map(([label, value, hint]) => (
          <Card key={label} size="small">
            <Typography.Text type="secondary">{label}</Typography.Text>
            <div className="syslog-live-metric-value">{value}</div>
            <Typography.Text type="secondary">{hint}</Typography.Text>
          </Card>
        ))}
      </div>
      {sourceError !== null && (
        <Alert
          type="warning"
          showIcon
          title={t('logs.sourcesFailed')}
          action={
            <Button onClick={() => setSourceRetry((value) => value + 1)}>
              {t('logs.retry')}
            </Button>
          }
        />
      )}
      <Card size="small" className="syslog-live-filter">
        <Form
          form={form}
          layout="vertical"
          initialValues={initial}
          onFinish={submit}
          onValuesChange={() => {
            setDirty(true);
            setValidation(null);
          }}
        >
          <Row gutter={16}>
            <Col xs={24} lg={12}>
              <Form.Item name="timeMode" label={t('logs.range')}>
                <Radio.Group
                  options={[
                    { label: t('live.time.live'), value: 'live' },
                    { label: t('live.time.fixed'), value: 'fixed' },
                  ]}
                />
              </Form.Item>
              {timeMode === 'fixed' && (
                <Form.Item name="range">
                  <DatePicker.RangePicker
                    showTime
                    format="YYYY-MM-DD HH:mm:ss"
                    style={{ width: '100%' }}
                  />
                </Form.Item>
              )}
            </Col>
            <Col xs={12} lg={6}>
              <Form.Item name="sourceId" label={t('logs.source')}>
                <Select
                  allowClear
                  showSearch={{ optionFilterProp: 'label' }}
                  placeholder={t('logs.allSources')}
                  options={sources.map((source) => ({
                    value: source.id,
                    label: `${source.name} · ${source.peerAddress}`,
                  }))}
                />
              </Form.Item>
            </Col>
            <Col xs={12} lg={6}>
              <Form.Item name="severity" label={t('logs.severity')}>
                <Select
                  allowClear
                  placeholder={t('logs.allSeverities')}
                  options={Array.from({ length: 8 }, (_, value) => ({
                    value,
                    label: <SeverityTag value={value} />,
                  }))}
                />
              </Form.Item>
            </Col>
            <Col xs={24} lg={9}>
              <Form.Item
                name="keywords"
                label={t('logs.keywords')}
                extra={t('logs.keywordHint')}
              >
                <Input allowClear maxLength={512} prefix={<SearchOutlined />} />
              </Form.Item>
            </Col>
            <Col xs={12} lg={5}>
              <Form.Item name="hostname" label={t('logs.hostname')}>
                <Input allowClear maxLength={255} />
              </Form.Item>
            </Col>
            <Col xs={12} lg={5}>
              <Form.Item name="appName" label={t('logs.appName')}>
                <Input allowClear maxLength={255} />
              </Form.Item>
            </Col>
            <Col xs={24} lg={5}>
              <Form.Item name="disposition" label={t('live.disposition')}>
                <Select
                  options={['all', 'normal', 'ignored'].map((value) => ({
                    value,
                    label: t(`live.disposition.${value}`),
                  }))}
                />
              </Form.Item>
            </Col>
          </Row>
          <Space wrap>
            <Button type="primary" htmlType="submit" icon={<SearchOutlined />}>
              {t('live.apply')}
            </Button>
            <Button
              onClick={() => {
                form.resetFields();
                submit(initial);
              }}
            >
              {t('logs.reset')}
            </Button>
            {dirty && (
              <Typography.Text type="warning">
                {t('live.dirty')}
              </Typography.Text>
            )}
          </Space>
          {validation && (
            <Alert
              style={{ marginTop: 12 }}
              type="error"
              showIcon
              title={t(validation)}
            />
          )}
        </Form>
      </Card>
      <Card
        size="small"
        title={
          <Space>
            <span>{t('live.stream')}</span>
            <Tag
              color={
                stream.state === 'connected'
                  ? 'green'
                  : stream.paused
                    ? 'gold'
                    : 'default'
              }
            >
              {t(`live.connection.${stream.state}`)}
            </Tag>
          </Space>
        }
        extra={
          <Space wrap>
            <Button
              icon={
                stream.paused ? <PlayCircleOutlined /> : <PauseCircleOutlined />
              }
              onClick={() =>
                stream.paused ? resume() : stream.setPaused(true)
              }
            >
              {t(stream.paused ? 'live.resume' : 'live.pause')}
            </Button>
            <Button
              icon={<ReloadOutlined />}
              onClick={() => {
                stream.refresh();
                table.current?.scrollTo({ top: 0 });
              }}
            >
              {t('live.refresh')}
            </Button>
            <Button
              icon={<ClearOutlined />}
              onClick={() => {
                setSelected(null);
                stream.clear();
              }}
            >
              {t('live.clear')}
            </Button>
          </Space>
        }
      >
        <Typography.Paragraph type="secondary">
          {t('live.evidenceHint')}
        </Typography.Paragraph>
        {stream.paused && (
          <Alert type="info" showIcon title={t('live.pausedHint')} />
        )}
        {stream.gap && <Alert type="warning" showIcon title={t('live.gap')} />}
        {stream.budgetLimited && (
          <Alert type="warning" showIcon title={t('live.budgetLimited')} />
        )}
        {stream.error !== null && (
          <Alert
            type="error"
            showIcon
            title={t(getLiveLogError(stream.error))}
            action={<Button onClick={stream.refresh}>{t('logs.retry')}</Button>}
          />
        )}
        {stream.ingest?.spoolFull && (
          <Alert type="warning" showIcon title={t('live.spoolFull')} />
        )}
        <Table<LiveLogRecord>
          ref={table}
          size="small"
          virtual
          rowKey="id"
          columns={columns}
          dataSource={stream.rows}
          pagination={false}
          scroll={{ x: 1400, y: 480 }}
          onScroll={(event) => {
            if (event.currentTarget.scrollTop > 60 && !stream.paused)
              stream.setPaused(true);
          }}
          locale={{
            emptyText: (
              <Empty
                image={Empty.PRESENTED_IMAGE_SIMPLE}
                description={t('live.empty')}
              />
            ),
          }}
        />
        <div className="syslog-live-footer">
          <Space wrap>
            <Typography.Text>
              {t('live.displayed')}: {formatCount(stream.rows.length)} /{' '}
              {formatCount(MAX_VISIBLE_LOGS)}
            </Typography.Text>
            <Typography.Text type="secondary">
              {t('live.heartbeat')}: {formatTime(stream.observedAt)}
            </Typography.Text>
            {stream.ingest && (
              <Typography.Text type="secondary">
                {t('live.queued')}: {formatCount(stream.ingest.queued)} ·{' '}
                {t('live.dropped')}: {formatCount(stream.ingest.dropped)}
              </Typography.Text>
            )}
          </Space>
          <Button
            disabled={
              !stream.hasOlder || stream.rows.length >= MAX_VISIBLE_LOGS
            }
            loading={stream.olderLoading}
            onClick={() => {
              stream.setPaused(true);
              void stream.loadOlder();
            }}
          >
            {t('live.loadOlder')}
          </Button>
        </div>
        <Typography.Text type="secondary">
          {t('live.limitHint')}
        </Typography.Text>
      </Card>
      <LogDetailDrawer
        selected={selected?.log ?? null}
        sourceName={selected ? sourceMap.get(selected.log.sourceId) : undefined}
        onClose={() => setSelected(null)}
        cacheDetail={
          selected ? { id: selected.id, load: loadCacheDetail } : undefined
        }
        errorKey={getLiveLogError}
        notice={
          selected && (
            <Alert
              style={{ marginBottom: 16 }}
              showIcon
              type={selected.disposition === 'ignored' ? 'warning' : 'info'}
              title={t(
                selected.disposition === 'ignored'
                  ? 'live.ignoredHint'
                  : 'live.normalHint',
              )}
            />
          )
        }
      />
      <SettingsModal
        open={settings}
        onClose={() => setSettings(false)}
        onSaved={() => stream.refresh()}
      />
    </PageContainer>
  );
}
