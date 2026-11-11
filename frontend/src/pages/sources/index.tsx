import {
  PlusOutlined,
  ReloadOutlined,
  SearchOutlined,
} from '@ant-design/icons';
import { PageContainer } from '@ant-design/pro-components';
import {
  Alert,
  Button,
  Card,
  Empty,
  Input,
  Select,
  Space,
  Table,
  Tag,
  Typography,
} from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { useMemo, useState } from 'react';
import type { LogSource } from '@/services/syslog/types';
import { useText } from '@/utils/i18n';
import { ClearLogsDialog } from '../logs/components/ClearLogsAction';
import SourceEditor from './SourceEditor';
import styles from './styles.module.less';
import { useSources } from './useSources';

export default function SourcesPage() {
  const t = useText();
  const { sources, hasLoaded, loading, error, reload, saved } = useSources();
  const [search, setSearch] = useState('');
  const [state, setState] = useState<'all' | 'enabled' | 'disabled'>('all');
  const [editing, setEditing] = useState<LogSource | 'new'>();
  const [clearing, setClearing] = useState<LogSource>();
  const [page, setPage] = useState(1);
  const rows = useMemo(() => {
    const keyword = search.trim().toLocaleLowerCase();
    return sources.filter(
      (source) =>
        (state === 'all' || source.enabled === (state === 'enabled')) &&
        (!keyword ||
          [source.name, source.peerAddress, source.timeZoneId].some((value) =>
            value.toLocaleLowerCase().includes(keyword),
          )),
    );
  }, [search, sources, state]);

  const columns: ColumnsType<LogSource> = [
    {
      title: t('source.name'),
      dataIndex: 'name',
      width: 220,
      ellipsis: true,
      render: (value: string) => (
        <Typography.Text strong>{value}</Typography.Text>
      ),
    },
    {
      title: t('source.address'),
      dataIndex: 'peerAddress',
      width: 180,
      ellipsis: true,
      render: (value: string) => <span className={styles.mono}>{value}</span>,
    },
    {
      title: t('source.timezone'),
      dataIndex: 'timeZoneId',
      width: 180,
      ellipsis: true,
    },
    {
      title: t('source.retention'),
      dataIndex: 'retentionDays',
      width: 140,
      render: (days: number) => t('retention.days', { days }),
    },
    {
      title: t('source.state'),
      dataIndex: 'enabled',
      width: 140,
      render: (enabled: boolean) => (
        <Tag color={enabled ? 'processing' : 'default'}>
          {t(enabled ? 'source.enabled' : 'source.disabled')}
        </Tag>
      ),
    },
    {
      title: t('source.actions'),
      key: 'actions',
      fixed: 'right',
      width: 224,
      render: (_, source) => (
        <Space size={4} wrap>
          <Button type="link" size="small" onClick={() => setEditing(source)}>
            {t('source.edit')}
          </Button>
          <Button
            type="link"
            size="small"
            danger
            onClick={() => setClearing(source)}
          >
            {t('logs.clearAction')}
          </Button>
        </Space>
      ),
    },
  ];

  return (
    <PageContainer
      title={t('source.title')}
      content={t('source.description')}
      extra={[
        <Button
          key="refresh"
          icon={<ReloadOutlined />}
          loading={loading}
          onClick={() => void reload()}
        >
          {t('source.refresh')}
        </Button>,
        <Button
          key="add"
          type="primary"
          icon={<PlusOutlined />}
          onClick={() => setEditing('new')}
        >
          {t('source.add')}
        </Button>,
      ]}
    >
      <div className={styles.stack}>
        {error && (
          <Alert
            type="warning"
            showIcon
            title={t('source.loadFailed')}
            description={
              <>
                {t(error)}
                {hasLoaded && <div>{t('source.stale')}</div>}
              </>
            }
          />
        )}
        <Card>
          <div className={styles.toolbar}>
            <Space wrap className={styles.filters}>
              <Input
                allowClear
                prefix={<SearchOutlined />}
                aria-label={t('source.search')}
                placeholder={t('source.search')}
                value={search}
                onChange={(event) => {
                  setSearch(event.target.value);
                  setPage(1);
                }}
                className={styles.search}
              />
              <Select
                aria-label={t('source.state')}
                value={state}
                className={styles.stateFilter}
                onChange={(value) => {
                  setState(value);
                  setPage(1);
                }}
                options={[
                  { value: 'all', label: t('source.all') },
                  { value: 'enabled', label: t('source.enabled') },
                  { value: 'disabled', label: t('source.disabled') },
                ]}
              />
            </Space>
            <Typography.Text type="secondary">
              {hasLoaded
                ? t('source.count', {
                    total: sources.length,
                    count: rows.length,
                  })
                : t('source.countUnknown')}
            </Typography.Text>
          </div>
          <Table<LogSource>
            rowKey="id"
            size="small"
            columns={columns}
            dataSource={rows}
            loading={loading}
            scroll={{ x: 1084 }}
            pagination={{
              current: page,
              defaultPageSize: 25,
              pageSizeOptions: [25, 50, 100],
              showSizeChanger: true,
              onChange: setPage,
            }}
            locale={{
              emptyText: (
                <Empty
                  image={Empty.PRESENTED_IMAGE_SIMPLE}
                  description={t(
                    !hasLoaded
                      ? loading
                        ? 'source.loading'
                        : 'source.unavailable'
                      : sources.length
                        ? 'source.noMatches'
                        : 'source.empty',
                  )}
                >
                  {!hasLoaded && error && (
                    <Button onClick={() => void reload()}>
                      {t('source.retry')}
                    </Button>
                  )}
                  {hasLoaded && !sources.length && !error && (
                    <Typography.Text type="secondary">
                      {t('source.emptyHint')}
                    </Typography.Text>
                  )}
                </Empty>
              ),
            }}
          />
        </Card>
        <Typography.Text type="secondary">
          {t('source.stateHint')}
        </Typography.Text>
      </div>
      {editing && (
        <SourceEditor
          open
          source={editing === 'new' ? undefined : editing}
          onCancel={() => setEditing(undefined)}
          onSaved={(source) => {
            saved(source);
            setEditing(undefined);
          }}
        />
      )}
      <ClearLogsDialog
        target={clearing ? { kind: 'source', source: clearing } : undefined}
        onDismiss={() => setClearing(undefined)}
      />
    </PageContainer>
  );
}
