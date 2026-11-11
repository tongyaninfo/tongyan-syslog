import { ReloadOutlined, SearchOutlined } from '@ant-design/icons';
import { PageContainer } from '@ant-design/pro-components';
import {
  Alert,
  Button,
  Card,
  Empty,
  Input,
  Table,
  Tag,
  Typography,
} from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { useMemo, useState } from 'react';
import type { LogSource } from '@/services/syslog/types';
import { useText } from '@/utils/i18n';
import SourceEditor from '../sources/SourceEditor';
import styles from '../sources/styles.module.less';
import { useSources } from '../sources/useSources';

export default function RetentionPage() {
  const t = useText();
  const { sources, hasLoaded, loading, error, reload, saved } = useSources();
  const [search, setSearch] = useState('');
  const [editing, setEditing] = useState<LogSource>();
  const [page, setPage] = useState(1);
  const rows = useMemo(() => {
    const keyword = search.trim().toLocaleLowerCase();
    return sources.filter(
      (source) =>
        !keyword ||
        [source.name, source.peerAddress, source.timeZoneId].some((value) =>
          value.toLocaleLowerCase().includes(keyword),
        ),
    );
  }, [sources, search]);
  const columns: ColumnsType<LogSource> = [
    {
      title: t('source.name'),
      dataIndex: 'name',
      width: 240,
      ellipsis: true,
      render: (value: string) => (
        <Typography.Text strong>{value}</Typography.Text>
      ),
    },
    {
      title: t('source.address'),
      dataIndex: 'peerAddress',
      width: 200,
      ellipsis: true,
      render: (value: string) => <span className={styles.mono}>{value}</span>,
    },
    {
      title: t('source.state'),
      dataIndex: 'enabled',
      width: 150,
      render: (enabled: boolean) => (
        <Tag color={enabled ? 'processing' : 'default'}>
          {t(enabled ? 'source.enabled' : 'source.disabled')}
        </Tag>
      ),
    },
    {
      title: t('source.retention'),
      dataIndex: 'retentionDays',
      width: 160,
      render: (days: number) => (
        <Typography.Text strong>
          {t('retention.days', { days })}
        </Typography.Text>
      ),
    },
    {
      title: t('source.actions'),
      key: 'actions',
      fixed: 'right',
      width: 150,
      render: (_, source) => (
        <Button type="link" size="small" onClick={() => setEditing(source)}>
          {t('retention.edit')}
        </Button>
      ),
    },
  ];

  return (
    <PageContainer
      title={t('retention.title')}
      content={t('retention.description')}
      extra={[
        <Button
          key="refresh"
          icon={<ReloadOutlined />}
          loading={loading}
          onClick={() => void reload()}
        >
          {t('source.refresh')}
        </Button>,
      ]}
    >
      <div className={styles.stack}>
        <Alert showIcon type="info" title={t('retention.boundary')} />
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
            <Input
              allowClear
              prefix={<SearchOutlined />}
              className={styles.search}
              aria-label={t('source.search')}
              placeholder={t('source.search')}
              value={search}
              onChange={(event) => {
                setSearch(event.target.value);
                setPage(1);
              }}
            />
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
            scroll={{ x: 900 }}
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
                        : 'retention.empty',
                  )}
                >
                  {!hasLoaded && error && (
                    <Button onClick={() => void reload()}>
                      {t('source.retry')}
                    </Button>
                  )}
                  {hasLoaded && !sources.length && !error && (
                    <Typography.Text type="secondary">
                      {t('retention.emptyHint')}
                    </Typography.Text>
                  )}
                </Empty>
              ),
            }}
          />
        </Card>
      </div>
      {editing && (
        <SourceEditor
          open
          source={editing}
          retentionOnly
          onCancel={() => setEditing(undefined)}
          onSaved={(source) => {
            saved(source);
            setEditing(undefined);
          }}
        />
      )}
    </PageContainer>
  );
}
