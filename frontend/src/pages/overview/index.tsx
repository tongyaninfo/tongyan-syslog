import {
  CloudDownloadOutlined,
  DatabaseOutlined,
  HddOutlined,
  ReloadOutlined,
  WarningOutlined,
} from '@ant-design/icons';
import { PageContainer } from '@ant-design/pro-components';
import { useIntl } from '@umijs/max';
import {
  Alert,
  Button,
  Card,
  Select,
  Space,
  Switch,
  Tag,
  Typography,
  theme,
} from 'antd';
import type { ReactNode } from 'react';
import { formatBytes, formatCount, formatTime } from '@/utils/format';
import { useText } from '@/utils/i18n';
import { useSources } from '../sources/useSources';
import RateChart from './RateChart';
import { runtimeIssueKey, runtimeStateView } from './state';
import styles from './styles.module.less';
import { useOverview } from './useOverview';

export default function OverviewPage() {
  const t = useText();
  const { locale } = useIntl();
  const { token } = theme.useToken();
  const view = useOverview();
  const sourceChoices = useSources();
  const { status, sources } = view;
  const ingestState = runtimeStateView(status?.state, status?.failureCode);
  const listenerState = runtimeStateView(status?.listenerState);
  const retrying = ingestState.label === 'overview.state.retrying';
  const ingestIssue = runtimeIssueKey(
    status?.failureCode,
    retrying ? 'overview.issue.storageUnavailable' : undefined,
  );
  const listenerIssue = runtimeIssueKey(status?.listenerFailureCode);
  const enabledSources = sources?.enabled;
  const connectionLabel = `overview.live.${view.connectionState}`;
  const connectionColor =
    view.connectionState === 'connected'
      ? 'success'
      : view.connectionState === 'disconnected'
        ? 'error'
        : view.connectionState === 'paused'
          ? 'default'
          : 'processing';
  const connectionHint =
    view.connectionState === 'paused'
      ? 'overview.live.pausedHint'
      : view.connectionState === 'connecting'
        ? 'overview.live.waitingHint'
        : 'overview.live.disconnectedHint';
  const value = (count?: number) => formatCount(count, locale);
  const lastWrite = status
    ? status.lastWriteUnixMilliseconds > 0
      ? formatTime(status.lastWriteUnixMilliseconds)
      : t('overview.noWrite')
    : '—';

  const metric = (
    key: string,
    title: string,
    count: string,
    hint: string,
    icon: ReactNode,
    warning = false,
  ) => (
    <Card className={styles.metricCard} key={key}>
      <div className={styles.metricLabel}>
        <span
          style={{ color: warning ? token.colorWarning : token.colorPrimary }}
          aria-hidden
        >
          {icon}
        </span>
        <Typography.Text type="secondary">{title}</Typography.Text>
      </div>
      <span
        className={styles.metricValue}
        style={{ color: warning ? token.colorWarning : token.colorTextHeading }}
      >
        {count}
      </span>
      <Typography.Text type="secondary">{hint}</Typography.Text>
    </Card>
  );

  return (
    <PageContainer
      title={t('overview.title')}
      content={t('overview.description')}
      extra={[
        <div className={styles.toolbar} key="controls">
          <Tag color={connectionColor} role="status">
            {t(connectionLabel)}
          </Tag>
          <Space>
            <Switch
              checked={view.autoRefresh}
              onChange={view.setAutoRefresh}
              size="small"
              aria-label={t('overview.autoRefresh')}
            />
            <Typography.Text type="secondary">
              {t('overview.autoRefresh')}
            </Typography.Text>
          </Space>
          <Button
            icon={<ReloadOutlined />}
            loading={view.loading}
            onClick={() => {
              view.refresh();
              void sourceChoices.reload();
            }}
          >
            {t('overview.refresh')}
          </Button>
        </div>,
      ]}
    >
      <section
        className={styles.content}
        aria-label={t('overview.title')}
        aria-busy={view.loading}
      >
        {view.connectionState !== 'connected' && (
          <Alert
            type={
              view.connectionState === 'disconnected' ||
              view.connectionState === 'reconnecting'
                ? 'warning'
                : 'info'
            }
            showIcon
            title={t(connectionLabel)}
            description={t(connectionHint)}
          />
        )}
        {view.statusError && (
          <Alert
            type="error"
            showIcon
            title={t('overview.statusError')}
            description={t(view.statusError)}
          />
        )}
        {view.sourceError && (
          <Alert
            type="error"
            showIcon
            title={t('overview.sourceError')}
            description={t(view.sourceError)}
          />
        )}
        {(ingestIssue || retrying) && (
          <Alert
            type="warning"
            showIcon
            title={t(
              retrying ? 'overview.retryTitle' : 'overview.runtimeIssue',
            )}
            description={
              <>
                {ingestIssue && <div>{t(ingestIssue)}</div>}
                {retrying && <div>{t('overview.retryHint')}</div>}
              </>
            }
          />
        )}
        {listenerIssue && (
          <Alert
            type="warning"
            showIcon
            title={t('overview.listenerIssue')}
            description={t(listenerIssue)}
          />
        )}

        <div className={styles.metrics}>
          {metric(
            'received',
            t('overview.received'),
            value(status?.received),
            t('overview.receivedHint', {
              bytes: formatBytes(status?.receivedBytes, locale),
            }),
            <CloudDownloadOutlined />,
          )}
          {metric(
            'written',
            t('overview.written'),
            value(status?.written),
            t('overview.writtenHint'),
            <DatabaseOutlined />,
          )}
          {metric(
            'dropped',
            t('overview.dropped'),
            value(status?.dropped),
            t('overview.droppedHint'),
            <WarningOutlined />,
            (status?.dropped ?? 0) > 0,
          )}
          {metric(
            'sources',
            t('overview.sources'),
            value(sources?.total),
            t('overview.sourcesHint', { enabled: value(enabledSources) }),
            <HddOutlined />,
          )}
        </div>

        <RateChart
          history={view.rateHistory}
          live={view.connectionState === 'connected'}
          sourceFilter={
            <Select<string>
              aria-label={t('overview.rate.sourceFilter')}
              className={styles.rateSourceFilter}
              size="small"
              popupMatchSelectWidth={220}
              loading={sourceChoices.loading}
              status={sourceChoices.error ? 'warning' : undefined}
              value={view.rateSourceId ?? ''}
              showSearch={{ optionFilterProp: 'label' }}
              onChange={(id) => view.setRateSourceId(id || undefined)}
              options={[
                { value: '', label: t('overview.rate.allSources') },
                ...sourceChoices.sources.map((source) => ({
                  value: source.id,
                  label: `${source.name} (${source.peerAddress})`,
                })),
                ...(sourceChoices.error
                  ? [
                      {
                        value: 'source-load-error',
                        label: t('overview.sourceError'),
                        disabled: true,
                      },
                    ]
                  : []),
              ]}
            />
          }
        />

        {sources?.total === 0 && (
          <Alert
            type="info"
            showIcon
            title={t('overview.emptyTitle')}
            description={t('overview.emptyDescription')}
            action={
              <Button href="/sources">{t('overview.manageSources')}</Button>
            }
          />
        )}

        <div className={styles.detailGrid}>
          <Card title={t('overview.pipeline')}>
            <Space wrap size={[16, 12]}>
              <Space>
                <Typography.Text type="secondary">
                  {t('overview.listener')}
                </Typography.Text>
                <Tag color={listenerState.color}>{t(listenerState.label)}</Tag>
              </Space>
              <Space>
                <Typography.Text type="secondary">
                  {t('overview.ingest')}
                </Typography.Text>
                <Tag color={ingestState.color}>{t(ingestState.label)}</Tag>
              </Space>
            </Space>
            <dl className={styles.fields}>
              <div>
                <dt>
                  <Typography.Text type="secondary">
                    {t('overview.parsed')}
                  </Typography.Text>
                </dt>
                <dd>{value(status?.parsed)}</dd>
              </div>
              <div>
                <dt>
                  <Typography.Text type="secondary">
                    {t('overview.parseFailed')}
                  </Typography.Text>
                </dt>
                <dd>{value(status?.parseFailed)}</dd>
              </div>
              <div>
                <dt>
                  <Typography.Text type="secondary">
                    {t('overview.parseUnknown')}
                  </Typography.Text>
                </dt>
                <dd>{value(status?.parseUnknown)}</dd>
              </div>
              <div>
                <dt>
                  <Typography.Text type="secondary">
                    {t('overview.pendingBatches')}
                  </Typography.Text>
                </dt>
                <dd>{value(status?.pendingBatches)}</dd>
              </div>
              <div className={styles.lastWrite}>
                <dt>
                  <Typography.Text type="secondary">
                    {t('overview.lastWrite')}
                  </Typography.Text>
                </dt>
                <dd>{lastWrite}</dd>
              </div>
            </dl>
          </Card>
          <Card title={t('overview.buffer')}>
            <Typography.Text type="secondary">
              {t('overview.bufferHint')}
            </Typography.Text>
            <dl className={styles.fields}>
              <div>
                <dt>
                  <Typography.Text type="secondary">
                    {t('overview.queueBytes')}
                  </Typography.Text>
                </dt>
                <dd>{formatBytes(status?.queuedBytes, locale)}</dd>
              </div>
              <div>
                <dt>
                  <Typography.Text type="secondary">
                    {t('overview.spoolBytes')}
                  </Typography.Text>
                </dt>
                <dd>{formatBytes(status?.spoolBytes, locale)}</dd>
              </div>
              <div>
                <dt>
                  <Typography.Text type="secondary">
                    {t('overview.databaseSize')}
                  </Typography.Text>
                </dt>
                <dd>{formatBytes(view.databaseSizeBytes, locale)}</dd>
              </div>
              <div>
                <dt>
                  <Typography.Text type="secondary">
                    {t('overview.writeFailures')}
                  </Typography.Text>
                </dt>
                <dd>{value(status?.writeFailures)}</dd>
              </div>
            </dl>
          </Card>
        </div>
        <Typography.Paragraph type="secondary" className={styles.note}>
          {t('overview.counterHint')}
          {view.refreshedAt && (
            <>
              <br />
              {t('overview.lastReceive', {
                time: formatTime(status?.lastReceiveUnixMilliseconds),
              })}
              <br />
              {t('overview.refreshedAt', {
                time: formatTime(view.refreshedAt),
              })}
            </>
          )}
        </Typography.Paragraph>
      </section>
    </PageContainer>
  );
}
