import {
  LeftOutlined,
  ReloadOutlined,
  RightOutlined,
  SearchOutlined,
} from '@ant-design/icons';
import { PageContainer } from '@ant-design/pro-components';
import { useIntl } from '@umijs/max';
import type { TableColumnsType } from 'antd';
import {
  Alert,
  Button,
  Card,
  Col,
  DatePicker,
  Empty,
  Form,
  Input,
  Row,
  Select,
  Space,
  Table,
  Typography,
} from 'antd';
import dayjs from 'dayjs';
import { useCallback, useEffect, useMemo, useState } from 'react';
import { ParseStatusTag, SeverityTag } from '@/components/LogTags';
import { getErrorKey } from '@/requestErrorConfig';
import { getSources } from '@/services/syslog/api';
import type { LogEvent, LogSource } from '@/services/syslog/types';
import { formatCount, formatTime } from '@/utils/format';
import { useText } from '@/utils/i18n';
import ClearLogsAction from './components/ClearLogsAction';
import LogDetailDrawer from './components/LogDetailDrawer';
import {
  buildLogQuery,
  getLogReadErrorKey,
  type SearchFormValues,
  truncateText,
} from './helpers';
import { useLogSearch } from './useLogSearch';
import './index.less';

function recentRange(hours: number): SearchFormValues['range'] {
  const end = dayjs().tz('Asia/Shanghai');
  return [end.subtract(hours, 'hour'), end];
}

export default function LogsPage() {
  const t = useText();
  const { locale } = useIntl();
  const [form] = Form.useForm<SearchFormValues>();
  const initialValues = useMemo<SearchFormValues>(
    () => ({ range: recentRange(1), pageSize: 100 }),
    [],
  );
  const search = useLogSearch();
  const { start, invalidate } = search;
  const [validationError, setValidationError] = useState<string | null>(null);
  const [sources, setSources] = useState<LogSource[]>([]);
  const [sourcesLoading, setSourcesLoading] = useState(true);
  const [sourcesError, setSourcesError] = useState<unknown>(null);
  const [sourceRetry, setSourceRetry] = useState(0);
  const [selected, setSelected] = useState<LogEvent | null>(null);
  const sourceMap = useMemo(
    () => new Map(sources.map((source) => [source.id, source.name])),
    [sources],
  );

  // biome-ignore lint/correctness/useExhaustiveDependencies: sourceRetry 是来源加载失败后的显式重试代次。
  useEffect(() => {
    const controller = new AbortController();
    setSourcesLoading(true);
    setSourcesError(null);
    getSources(controller.signal)
      .then((items) => {
        if (!controller.signal.aborted) setSources(items);
      })
      .catch((error: unknown) => {
        if (!controller.signal.aborted) setSourcesError(error);
      })
      .finally(() => {
        if (!controller.signal.aborted) setSourcesLoading(false);
      });
    return () => controller.abort();
  }, [sourceRetry]);

  useEffect(() => {
    const value = buildLogQuery(initialValues);
    if (value.ok) void start(value.query);
  }, [initialValues, start]);

  const submit = useCallback(
    (values: SearchFormValues) => {
      setSelected(null);
      const value = buildLogQuery(values);
      if (!value.ok) {
        invalidate();
        setValidationError(value.error);
        return;
      }
      setValidationError(null);
      void start(value.query);
    },
    [invalidate, start],
  );

  const changeFilters = () => {
    setValidationError(null);
    setSelected(null);
    invalidate();
  };
  const quickRange = (hours: number) => {
    const values = { ...form.getFieldsValue(), range: recentRange(hours) };
    form.setFieldsValue(values);
    submit(values);
  };
  const reset = () => {
    form.resetFields();
    const values: SearchFormValues = { range: recentRange(1), pageSize: 100 };
    form.setFieldsValue(values);
    submit(values);
  };

  const columns: TableColumnsType<LogEvent> = [
    {
      title: t('logs.receivedAt'),
      dataIndex: 'receivedAt',
      width: 185,
      render: (value: string) => (
        <span className="syslog-time-cell">{formatTime(value)}</span>
      ),
    },
    {
      title: t('logs.severity'),
      dataIndex: 'severity',
      width: 98,
      render: (value: number | null) => <SeverityTag value={value} />,
    },
    {
      title: t('logs.source'),
      dataIndex: 'sourceId',
      width: 160,
      ellipsis: true,
      render: (value: string) => sourceMap.get(value) ?? value,
    },
    {
      title: t('logs.hostname'),
      dataIndex: 'hostname',
      width: 140,
      ellipsis: true,
      render: (value: string) => value || t('logs.notApplicable'),
    },
    {
      title: t('logs.appName'),
      dataIndex: 'appName',
      width: 130,
      ellipsis: true,
      render: (value: string) => value || t('logs.notApplicable'),
    },
    {
      title: t('logs.message'),
      dataIndex: 'message',
      width: 350,
      render: (value: string) => (
        <span className="syslog-log-message">
          {truncateText(value, 240)}
          {value.length > 240 ? '…' : ''}
        </span>
      ),
    },
    {
      title: t('logs.parseStatus'),
      dataIndex: 'parseStatus',
      width: 140,
      render: (value: string, row) => (
        <>
          <ParseStatusTag value={value} />
          <span className="syslog-parser-version">{row.parserVersion}</span>
        </>
      ),
    },
    {
      title: t('logs.action'),
      key: 'action',
      width: 100,
      fixed: 'right',
      render: (_, row) => (
        <Button type="link" size="small" onClick={() => setSelected(row)}>
          {t('logs.detail')}
        </Button>
      ),
    },
  ];

  return (
    <PageContainer
      className="syslog-log-page"
      title={t('logs.title')}
      content={t('logs.subtitle')}
      extra={
        <ClearLogsAction
          onCleared={() => {
            setSelected(null);
            if (search.query) void start(search.query);
            else invalidate();
          }}
        />
      }
    >
      {sourcesError !== null && (
        <Alert
          style={{ marginBottom: 16 }}
          type="warning"
          showIcon
          title={t('logs.sourcesFailed')}
          description={t(getErrorKey(sourcesError))}
          action={
            <Button onClick={() => setSourceRetry((value) => value + 1)}>
              {t('logs.sourcesRetry')}
            </Button>
          }
        />
      )}
      {!sourcesLoading && sourcesError === null && sources.length === 0 && (
        <Alert
          style={{ marginBottom: 16 }}
          type="info"
          showIcon
          title={t('logs.noSources')}
          description={t('logs.noSourcesHint')}
        />
      )}
      <Card className="syslog-filter-card">
        <Form
          form={form}
          layout="vertical"
          initialValues={initialValues}
          onFinish={submit}
          onValuesChange={changeFilters}
          requiredMark={false}
        >
          <Row gutter={[20, 0]}>
            <Col xs={24} xl={12}>
              <Form.Item
                name="range"
                label={t('logs.range')}
                extra={t('logs.rangeHint')}
              >
                <DatePicker.RangePicker
                  className="syslog-range-field"
                  classNames={{ popup: { root: 'syslog-range-popup' } }}
                  showTime
                  format="YYYY-MM-DD HH:mm:ss"
                  allowClear={false}
                  styles={{
                    popup: {
                      root: {
                        maxWidth: 'calc(100vw - 24px)',
                      },
                      container: {
                        maxWidth: 'calc(100vw - 24px)',
                      },
                    },
                  }}
                />
              </Form.Item>
              <Space wrap size={0} className="syslog-time-shortcuts">
                {[
                  [1, 'logs.lastHour'],
                  [24, 'logs.lastDay'],
                  [168, 'logs.lastWeek'],
                  [720, 'logs.lastMonth'],
                ].map(([hours, label]) => (
                  <Button
                    key={hours}
                    type="link"
                    size="small"
                    onClick={() => quickRange(Number(hours))}
                  >
                    {t(String(label))}
                  </Button>
                ))}
              </Space>
            </Col>
            <Col xs={24} sm={12} xl={6}>
              <Form.Item name="sourceId" label={t('logs.source')}>
                <Select
                  allowClear
                  showSearch={{ optionFilterProp: 'label' }}
                  loading={sourcesLoading}
                  placeholder={t('logs.allSources')}
                  options={sources.map((source) => ({
                    value: source.id,
                    label: `${source.name} · ${source.peerAddress}`,
                  }))}
                />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12} xl={6}>
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
          </Row>
          <Row gutter={[20, 0]} style={{ marginTop: 12 }}>
            <Col xs={24} lg={12}>
              <Form.Item
                name="keywords"
                label={t('logs.keywords')}
                extra={t('logs.keywordHint')}
              >
                <Input
                  prefix={<SearchOutlined />}
                  allowClear
                  maxLength={512}
                  placeholder={t('logs.keywordsPlaceholder')}
                />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12} lg={6}>
              <Form.Item name="hostname" label={t('logs.hostname')}>
                <Input
                  allowClear
                  maxLength={255}
                  placeholder={t('logs.hostnamePlaceholder')}
                />
              </Form.Item>
            </Col>
            <Col xs={24} sm={12} lg={6}>
              <Form.Item name="appName" label={t('logs.appName')}>
                <Input
                  allowClear
                  maxLength={255}
                  placeholder={t('logs.appNamePlaceholder')}
                />
              </Form.Item>
            </Col>
          </Row>
          {validationError && (
            <Alert
              style={{ marginBottom: 16 }}
              type="warning"
              showIcon
              title={t(validationError)}
            />
          )}
          <div className="syslog-filter-footer">
            <Space wrap>
              <Button
                type="primary"
                htmlType="submit"
                icon={<SearchOutlined />}
                loading={search.loading}
              >
                {t('logs.search')}
              </Button>
              <Button onClick={reset}>{t('logs.reset')}</Button>
              <Typography.Text type="secondary">
                {t('logs.timezone')}
              </Typography.Text>
            </Space>
            <Form.Item
              name="pageSize"
              label={t('logs.pageSize')}
              layout="horizontal"
              style={{ marginBottom: 0 }}
            >
              <Select
                style={{ width: 112 }}
                options={[50, 100, 200, 500].map((count) => ({
                  value: count,
                  label: t('logs.pageSizeValue', { count }),
                }))}
              />
            </Form.Item>
          </div>
        </Form>
      </Card>
      <Card>
        <div className="syslog-results-heading">
          <div>
            <Typography.Title level={5} style={{ margin: 0 }}>
              {t('logs.results')}
            </Typography.Title>
            <Typography.Text type="secondary">
              {t('logs.resultsHint')}
            </Typography.Text>
          </div>
          {search.query && (
            <Space wrap>
              <Typography.Text
                className="syslog-results-range"
                type="secondary"
              >
                {formatTime(search.query.from)} ～ {formatTime(search.query.to)}
              </Typography.Text>
              {(!search.failure || search.failure.attempt.pageIndex > 0) && (
                <Button
                  icon={<ReloadOutlined />}
                  onClick={() => search.query && void start(search.query)}
                  disabled={search.loading}
                >
                  {t(
                    search.failure
                      ? 'logs.restartFromFirstPage'
                      : 'logs.refresh',
                  )}
                </Button>
              )}
            </Space>
          )}
        </div>
        {search.failure ? (
          <Alert
            type="error"
            showIcon
            title={
              search.failure.attempt.pageIndex === 0
                ? t('logs.queryFailed')
                : t('logs.pageFailed', {
                    page: search.failure.attempt.pageIndex + 1,
                  })
            }
            description={t(getLogReadErrorKey(search.failure.cause))}
            action={
              <Button onClick={() => void search.retry()}>
                {t('logs.retry')}
              </Button>
            }
          />
        ) : (
          <Table<LogEvent>
            rowKey={(row) => `${row.eventId}:${row.receivedAt}`}
            columns={columns}
            dataSource={search.result?.items ?? []}
            pagination={false}
            size="small"
            scroll={{ x: 1303 }}
            loading={search.loading}
            locale={{
              emptyText: (
                <Empty
                  image={Empty.PRESENTED_IMAGE_SIMPLE}
                  description={
                    <>
                      <div>
                        {t(
                          search.loading
                            ? 'logs.loading'
                            : search.edited
                              ? 'logs.pending'
                              : 'logs.empty',
                        )}
                      </div>
                      <Typography.Text type="secondary">
                        {!search.loading &&
                          t(
                            search.edited
                              ? 'logs.pendingHint'
                              : 'logs.emptyHint',
                          )}
                      </Typography.Text>
                    </>
                  }
                />
              ),
            }}
          />
        )}
        {search.result && (
          <div className="syslog-results-pagination">
            <Space wrap>
              <Typography.Text>
                {t('logs.page', { page: search.pageIndex + 1 })}
              </Typography.Text>
              <Typography.Text type="secondary">
                {t('logs.rows', {
                  count: formatCount(search.result.items.length, locale),
                })}
              </Typography.Text>
              {!search.result.hasMore && (
                <Typography.Text type="secondary">
                  {t('logs.noMore')}
                </Typography.Text>
              )}
            </Space>
            <Space>
              <Button
                icon={<LeftOutlined />}
                disabled={search.pageIndex === 0 || search.loading}
                onClick={() => void search.previous()}
              >
                {t('logs.previous')}
              </Button>
              <Button
                icon={<RightOutlined />}
                iconPlacement="end"
                disabled={
                  !search.result.hasMore ||
                  !search.result.nextCursor ||
                  search.loading
                }
                onClick={() => void search.next()}
              >
                {t('logs.next')}
              </Button>
            </Space>
          </div>
        )}
      </Card>
      <LogDetailDrawer
        selected={selected}
        sourceName={selected ? sourceMap.get(selected.sourceId) : undefined}
        onClose={() => setSelected(null)}
      />
    </PageContainer>
  );
}
