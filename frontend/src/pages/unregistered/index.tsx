import { ReloadOutlined, SearchOutlined } from '@ant-design/icons';
import { PageContainer } from '@ant-design/pro-components';
import {
  Alert,
  Button,
  Card,
  Descriptions,
  Empty,
  Input,
  Space,
  Table,
  Tag,
  Typography,
} from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { useEffect, useMemo, useRef, useState } from 'react';
import { ApiError, getErrorKey } from '@/requestErrorConfig';
import { createSource } from '@/services/syslog/api';
import type { UnregisteredDevice } from '@/services/syslog/types';
import { formatBytes, formatCount, formatTime } from '@/utils/format';
import { useText } from '@/utils/i18n';
import DeviceLogsDrawer from './DeviceLogsDrawer';
import styles from './styles.module.less';
import { useUnregisteredDevices } from './useUnregisteredDevices';

export default function UnregisteredDevicesPage() {
  const t = useText();
  const { data, loading, error, reload } = useUnregisteredDevices();
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [selected, setSelected] = useState<string>();
  const [saving, setSaving] = useState<string>();
  const [saveError, setSaveError] = useState<string>();
  const [saved, setSaved] = useState<string>();
  const submitting = useRef(false);
  const mounted = useRef(true);
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);
  const rows = useMemo(
    () =>
      (data?.items ?? []).filter((device) =>
        device.peerAddress.toLowerCase().includes(search.trim().toLowerCase()),
      ),
    [data, search],
  );
  const status = data?.status;

  const register = async (device: UnregisteredDevice) => {
    if (submitting.current) return;
    submitting.current = true;
    setSaving(device.peerAddress);
    setSaveError(undefined);
    setSaved(undefined);
    try {
      // 使用既有来源写接口，沿用服务端鉴权、重复地址校验及可靠审计。
      await createSource({
        name: device.peerAddress,
        peerAddress: device.peerAddress,
        timeZoneId: 'Asia/Shanghai',
        retentionDays: 365,
        enabled: true,
      });
      if (!mounted.current) return;
      setSaved(device.peerAddress);
      setSelected(undefined);
      await reload();
    } catch (failure) {
      if (!mounted.current) return;
      setSaveError(
        failure instanceof ApiError && failure.status === 409
          ? 'error.conflict'
          : getErrorKey(failure),
      );
      // 另一管理员可能已登记该地址。刷新真实列表，但冲突不显示为添加成功。
      if (failure instanceof ApiError && failure.status === 409) await reload();
    } finally {
      submitting.current = false;
      if (mounted.current) setSaving(undefined);
    }
  };

  const columns: ColumnsType<UnregisteredDevice> = [
    {
      title: t('source.address'),
      dataIndex: 'peerAddress',
      width: 185,
      render: (value: string) => <span className={styles.mono}>{value}</span>,
    },
    {
      title: t('unregistered.firstSeen'),
      dataIndex: 'firstReceivedAt',
      width: 200,
      render: (value: string) => formatTime(value),
    },
    {
      title: t('unregistered.lastSeen'),
      dataIndex: 'lastReceivedAt',
      width: 200,
      render: (value: string) => formatTime(value),
    },
    {
      title: t('logs.transport'),
      dataIndex: 'transports',
      width: 120,
      render: (values: string[]) =>
        values.map((value) => value.toUpperCase()).join(' / '),
    },
    {
      title: t('unregistered.received'),
      dataIndex: 'received',
      width: 125,
      render: (value: number) => formatCount(value),
    },
    {
      title: t('unregistered.samples'),
      dataIndex: 'sampleCount',
      width: 135,
      render: (value: number) => `${formatCount(value)} / 100`,
    },
    {
      title: t('source.actions'),
      key: 'actions',
      fixed: 'right',
      width: 205,
      render: (_, device) => (
        <Space wrap size={0}>
          <Button
            type="link"
            size="small"
            onClick={() => setSelected(device.peerAddress)}
          >
            {t('unregistered.viewLogs')}
          </Button>
          <Button
            type="link"
            size="small"
            loading={saving === device.peerAddress}
            disabled={Boolean(saving) || loading || Boolean(error)}
            onClick={() => void register(device)}
          >
            {t('unregistered.add')}
          </Button>
        </Space>
      ),
    },
  ];

  return (
    <PageContainer
      title={t('unregistered.title')}
      content={t('unregistered.description')}
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
        <Alert
          type="info"
          showIcon
          title={t('unregistered.boundary')}
          description={t('unregistered.defaults')}
        />
        {saved && (
          <Alert
            type="success"
            showIcon
            title={t('unregistered.added', { ip: saved })}
            description={t('unregistered.addedHint')}
          />
        )}
        {saveError && (
          <Alert
            type="error"
            showIcon
            title={t('unregistered.addFailed')}
            description={t(saveError)}
          />
        )}
        {error && (
          <Alert
            type="warning"
            showIcon
            title={t('unregistered.loadFailed')}
            description={
              <>
                {t(error)}
                {data && <div>{t('source.stale')}</div>}
              </>
            }
            action={
              <Button onClick={() => void reload()}>{t('source.retry')}</Button>
            }
          />
        )}
        {status && (
          <Card size="small">
            <Descriptions
              size="small"
              column={{ xs: 1, sm: 2, md: 2, lg: 3, xl: 3, xxl: 3 }}
              items={[
                {
                  key: 'state',
                  label: t('unregistered.state'),
                  children: (
                    <Tag color={status.enabled ? 'processing' : 'default'}>
                      {t(
                        status.enabled
                          ? 'unregistered.enabled'
                          : 'unregistered.disabled',
                      )}
                    </Tag>
                  ),
                },
                {
                  key: 'devices',
                  label: t('unregistered.devices'),
                  children: `${formatCount(status.deviceCount)} / ${formatCount(status.maxDevices)}`,
                },
                {
                  key: 'bytes',
                  label: t('unregistered.memory'),
                  children: `${formatBytes(status.bufferedBytes)} / ${formatBytes(status.maxBytes)}`,
                },
                {
                  key: 'samples',
                  label: t('unregistered.currentSamples'),
                  children: formatCount(status.sampleCount),
                },
                {
                  key: 'received',
                  label: t('unregistered.received'),
                  children: formatCount(status.received),
                },
                {
                  key: 'evicted',
                  label: t('unregistered.evicted'),
                  children: formatCount(status.evicted),
                },
                {
                  key: 'rejected',
                  label: t('unregistered.rejected'),
                  children: formatCount(status.rejected),
                },
                {
                  key: 'networks',
                  label: t('unregistered.networks'),
                  span: 'filled',
                  children: (
                    <span className={styles.networks}>
                      {status.allowedNetworks.join(', ') ||
                        t('unregistered.noNetworks')}
                    </span>
                  ),
                },
              ]}
            />
            <Typography.Paragraph
              type="secondary"
              style={{ marginBottom: 0, marginTop: 12 }}
            >
              {t('unregistered.counterHint')}
            </Typography.Paragraph>
          </Card>
        )}
        <Card>
          <div className={styles.toolbar}>
            <Input
              prefix={<SearchOutlined />}
              placeholder={t('unregistered.search')}
              aria-label={t('unregistered.search')}
              value={search}
              maxLength={64}
              allowClear
              className={styles.search}
              onChange={(event) => {
                setSearch(event.target.value);
                setPage(1);
              }}
            />
            <Typography.Text type="secondary">
              {data ? t('unregistered.count', { count: rows.length }) : '—'}
            </Typography.Text>
          </div>
          <Table<UnregisteredDevice>
            rowKey="peerAddress"
            dataSource={rows}
            columns={columns}
            loading={loading}
            size="small"
            scroll={{ x: 1170 }}
            pagination={{
              current: Math.min(page, Math.max(1, Math.ceil(rows.length / 20))),
              pageSize: 20,
              showSizeChanger: false,
              onChange: setPage,
            }}
            locale={{
              emptyText: (
                <Empty
                  image={Empty.PRESENTED_IMAGE_SIMPLE}
                  description={t(
                    error && !data
                      ? 'unregistered.unavailable'
                      : search
                        ? 'source.noMatches'
                        : 'unregistered.empty',
                  )}
                />
              ),
            }}
          />
        </Card>
      </div>
      {selected && (
        <DeviceLogsDrawer
          key={selected}
          peerAddress={selected}
          onClose={() => setSelected(undefined)}
        />
      )}
    </PageContainer>
  );
}
