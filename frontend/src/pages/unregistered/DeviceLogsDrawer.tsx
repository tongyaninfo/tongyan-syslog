import {
  Alert,
  Button,
  Card,
  Drawer,
  Empty,
  Space,
  Table,
  Typography,
} from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { useEffect, useState } from 'react';
import EvidencePreview from '@/pages/logs/components/EvidencePreview';
import '@/pages/logs/index.less';
import { ApiError, getErrorKey } from '@/requestErrorConfig';
import {
  getUnregisteredLogs,
  getUnregisteredSample,
} from '@/services/syslog/api';
import type {
  UnregisteredLogSample,
  UnregisteredLogSummary,
} from '@/services/syslog/types';
import { formatBytes, formatTime } from '@/utils/format';
import { useText } from '@/utils/i18n';
import styles from './styles.module.less';

export default function DeviceLogsDrawer({
  peerAddress,
  onClose,
}: {
  peerAddress: string;
  onClose: () => void;
}) {
  const t = useText();
  const [logs, setLogs] = useState<UnregisteredLogSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  const [revision, setRevision] = useState(0);
  const [selected, setSelected] = useState<string>();
  const [sample, setSample] = useState<UnregisteredLogSample>();
  const [sampleLoading, setSampleLoading] = useState(false);
  const [sampleError, setSampleError] = useState<string>();
  const [sampleRetry, setSampleRetry] = useState(0);

  // biome-ignore lint/correctness/useExhaustiveDependencies: revision 是显式刷新代次。
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError(undefined);
    setSelected(undefined);
    getUnregisteredLogs(peerAddress, controller.signal)
      .then((value) => {
        if (!controller.signal.aborted) setLogs(value);
      })
      .catch((failure: unknown) => {
        if (!controller.signal.aborted) {
          setLogs([]);
          setError(getErrorKey(failure));
        }
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false);
      });
    return () => controller.abort();
  }, [peerAddress, revision]);

  // biome-ignore lint/correctness/useExhaustiveDependencies: sampleRetry 是原文加载失败后的显式重试。
  useEffect(() => {
    setSample(undefined);
    setSampleError(undefined);
    if (!selected) {
      setSampleLoading(false);
      return;
    }
    const controller = new AbortController();
    setSampleLoading(true);
    // 原文按单条读取。环形暂存可能已淘汰该条，404 必须显示失效而不是空白成功。
    getUnregisteredSample(peerAddress, selected, controller.signal)
      .then((value) => {
        if (!controller.signal.aborted) setSample(value);
      })
      .catch((failure: unknown) => {
        if (!controller.signal.aborted)
          setSampleError(
            failure instanceof ApiError && failure.status === 404
              ? 'unregistered.sampleExpired'
              : getErrorKey(failure),
          );
      })
      .finally(() => {
        if (!controller.signal.aborted) setSampleLoading(false);
      });
    return () => controller.abort();
  }, [peerAddress, selected, sampleRetry]);

  const columns: ColumnsType<UnregisteredLogSummary> = [
    {
      title: t('logs.receivedAt'),
      dataIndex: 'receivedAt',
      width: 200,
      render: (value: string) => formatTime(value),
    },
    {
      title: t('logs.transport'),
      dataIndex: 'transport',
      width: 90,
      render: (value: string) => value.toUpperCase(),
    },
    {
      title: t('unregistered.byteCount'),
      dataIndex: 'byteCount',
      width: 90,
      render: (value: number) => formatBytes(value),
    },
    {
      title: t('unregistered.preview'),
      dataIndex: 'preview',
      width: 320,
      render: (value: string) => <pre className={styles.preview}>{value}</pre>,
    },
    {
      title: t('source.actions'),
      key: 'actions',
      width: 100,
      fixed: 'right',
      render: (_, row) => (
        <Button
          type="link"
          size="small"
          onClick={() => setSelected(row.sampleKey)}
        >
          {t('unregistered.viewRaw')}
        </Button>
      ),
    },
  ];

  return (
    <Drawer
      open
      title={t('unregistered.logsTitle', { ip: peerAddress })}
      size="min(1000px, 100vw)"
      onClose={onClose}
      destroyOnHidden
      className="syslog-log-detail"
      extra={
        <Button
          loading={loading}
          onClick={() => setRevision((value) => value + 1)}
        >
          {t('source.refresh')}
        </Button>
      }
    >
      <div className={styles.stack}>
        <Alert
          type="info"
          showIcon
          title={t('unregistered.logsHint')}
          description={t('unregistered.previewHint')}
        />
        {error && (
          <Alert
            type="error"
            showIcon
            title={t('unregistered.logsFailed')}
            description={t(error)}
          />
        )}
        <Table<UnregisteredLogSummary>
          rowKey="sampleKey"
          columns={columns}
          dataSource={logs}
          loading={loading}
          size="small"
          scroll={{ x: 800 }}
          pagination={{ pageSize: 10, showSizeChanger: false }}
          locale={{
            emptyText: (
              <Empty
                image={Empty.PRESENTED_IMAGE_SIMPLE}
                description={t(
                  error ? 'unregistered.unavailable' : 'unregistered.noLogs',
                )}
              />
            ),
          }}
        />
        {selected && (
          <Card size="small" loading={sampleLoading} title={t('logs.original')}>
            {sampleError && (
              <Alert
                type="warning"
                showIcon
                title={t(sampleError)}
                action={
                  <Button onClick={() => setSampleRetry((value) => value + 1)}>
                    {t('logs.retry')}
                  </Button>
                }
              />
            )}
            {sample?.sampleKey === selected && (
              <>
                <Space wrap style={{ marginBottom: 12 }}>
                  <Typography.Text>
                    {formatTime(sample.receivedAt)}
                  </Typography.Text>
                  <Typography.Text type="secondary">
                    {sample.transport.toUpperCase()}
                  </Typography.Text>
                </Space>
                <EvidencePreview base64={sample.rawBytes} />
              </>
            )}
          </Card>
        )}
      </div>
    </Drawer>
  );
}
