import {
  ApiOutlined,
  PauseCircleOutlined,
  PlayCircleOutlined,
  ReloadOutlined,
} from '@ant-design/icons';
import { PageContainer } from '@ant-design/pro-components';
import { Alert, Button, Space, Tabs, Tag, Typography } from 'antd';
import { useEffect, useRef, useState } from 'react';
import {
  getMcpPermissions,
  getMcpService,
  setMcpEnabled,
} from '@/services/syslog/mcp';
import { useText } from '@/utils/i18n';
import CallHistory from './CallHistory';
import { getMcpError, mcpEndpoint, useMcpQuery } from './common';
import KeyList from './KeyList';
import styles from './styles.module.less';

export default function McpPage() {
  const t = useText();
  const service = useMcpQuery(getMcpService);
  const permissions = useMcpQuery(getMcpPermissions);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string>();
  const [pollError, setPollError] = useState<string>();
  const lock = useRef(false);
  const poll = useRef<AbortController | undefined>(undefined);

  useEffect(() => {
    const timer = setInterval(() => {
      if (document.visibilityState === 'hidden' || lock.current || poll.current)
        return;
      const request = new AbortController();
      poll.current = request;
      void getMcpService(request.signal)
        .then((next) => {
          if (!request.signal.aborted) {
            setPollError(undefined);
            service.setError(undefined);
            service.setData((previous) =>
              !previous ||
              BigInt(next.settings.version) >= BigInt(previous.settings.version)
                ? next
                : previous,
            );
          }
        })
        .catch((failure) => {
          if (!request.signal.aborted) setPollError(getMcpError(failure));
        })
        .finally(() => {
          if (poll.current === request) poll.current = undefined;
        });
    }, 3000);
    return () => {
      clearInterval(timer);
      poll.current?.abort();
    };
  }, [service.setData, service.setError]);

  async function toggle(enabled: boolean) {
    if (!service.data || service.error || lock.current) return;
    lock.current = true;
    poll.current?.abort();
    setSaving(true);
    setSaveError(undefined);
    try {
      const next = await setMcpEnabled(service.data.settings.version, enabled);
      service.setData(next);
    } catch (failure) {
      setSaveError(getMcpError(failure));
      service.reload();
    } finally {
      lock.current = false;
      setSaving(false);
    }
  }

  const state = pollError || service.error ? undefined : service.data?.state;
  const stateKey =
    state && ['running', 'stopped', 'stopping', 'faulted'].includes(state)
      ? `mcp.serviceState.${state}`
      : 'mcp.serviceState.unknown';
  return (
    <PageContainer
      title={t('mcp.title')}
      content={t('mcp.description')}
      extra={
        <Button
          icon={<ReloadOutlined />}
          loading={service.loading || permissions.loading}
          onClick={() => {
            service.reload();
            permissions.reload();
          }}
        >
          {t('mcp.refresh')}
        </Button>
      }
    >
      <div className={styles.hero}>
        <div className={styles.heroMain}>
          <div className={styles.heroIcon}>
            <ApiOutlined />
          </div>
          <div className={styles.heroContent}>
            <Space wrap>
              <Typography.Text strong>{t('mcp.service')}</Typography.Text>
              <Tag
                color={
                  state === 'running'
                    ? 'green'
                    : state === 'faulted'
                      ? 'red'
                      : 'default'
                }
              >
                {t(stateKey)}
              </Tag>
              <Typography.Text type="secondary">
                Streamable HTTP
              </Typography.Text>
            </Space>
            <span className={styles.endpoint}>
              {mcpEndpoint(window.location.origin)}
            </span>
            <Typography.Text type="secondary">
              {t('mcp.independentHint')}
            </Typography.Text>
          </div>
        </div>
        <div className={styles.metrics}>
          <div className={styles.metric}>
            <Typography.Text type="secondary">
              {t('mcp.toolCount')}
            </Typography.Text>
            <span className={styles.metricValue}>
              {service.data?.toolCount ?? '—'}
            </span>
          </div>
          <div className={styles.metric}>
            <Typography.Text type="secondary">
              {t('mcp.activeCalls')}
            </Typography.Text>
            <span className={styles.metricValue}>
              {service.data?.activeCalls ?? '—'}
            </span>
          </div>
          <Button
            type={service.data?.settings.enabled ? 'default' : 'primary'}
            danger={service.data?.settings.enabled}
            disabled={
              !service.data ||
              service.loading ||
              Boolean(service.error) ||
              Boolean(pollError)
            }
            loading={saving}
            icon={
              service.data?.settings.enabled ? (
                <PauseCircleOutlined />
              ) : (
                <PlayCircleOutlined />
              )
            }
            onClick={() => void toggle(!service.data?.settings.enabled)}
          >
            {t(service.data?.settings.enabled ? 'mcp.stop' : 'mcp.start')}
          </Button>
          {state === 'faulted' && service.data?.settings.enabled && (
            <Button
              type="primary"
              icon={<PlayCircleOutlined />}
              loading={saving}
              disabled={service.loading}
              onClick={() => void toggle(true)}
            >
              {t('mcp.retryStart')}
            </Button>
          )}
        </div>
      </div>
      {service.data?.recoveryError && (
        <Alert
          className={styles.notice}
          type="error"
          showIcon
          title={t('mcp.error.recovery')}
        />
      )}
      {(service.error || permissions.error || saveError || pollError) && (
        <Alert
          className={styles.notice}
          type="error"
          showIcon
          title={t(
            saveError ||
              service.error ||
              permissions.error ||
              pollError ||
              'error.generic',
          )}
        />
      )}
      <Tabs
        destroyOnHidden
        items={[
          {
            key: 'service',
            label: t('mcp.service'),
            children: (
              <KeyList
                permissions={permissions.data ?? []}
                permissionsReady={
                  Boolean(permissions.data) && !permissions.error
                }
              />
            ),
          },
          {
            key: 'calls',
            label: t('mcp.calls'),
            children: (
              <CallHistory
                service={service.data}
                permissions={permissions.data ?? []}
                onSettingsSaved={service.reload}
              />
            ),
          },
        ]}
      />
    </PageContainer>
  );
}
