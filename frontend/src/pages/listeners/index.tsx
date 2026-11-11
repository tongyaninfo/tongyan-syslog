import { ReloadOutlined, SaveOutlined } from '@ant-design/icons';
import { PageContainer } from '@ant-design/pro-components';
import {
  Alert,
  Button,
  Card,
  Form,
  Input,
  InputNumber,
  Modal,
  Select,
  Space,
  Switch,
  Table,
  Tag,
  Typography,
} from 'antd';
import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError, getErrorKey } from '@/requestErrorConfig';
import {
  getListenerSettings,
  saveListenerSettings,
  setListenerProtocol,
  setListenerReception,
} from '@/services/syslog/api';
import {
  type OverviewConnectionState,
  subscribeListenerRuntime,
} from '@/services/syslog/overviewStream';
import type {
  ListenerConfiguration,
  ListenerConnectionError,
  ListenerProtocol,
  ListenerRuntimeView,
  ListenerSettings,
  ListenerSettingsUpdate,
} from '@/services/syslog/types';
import { formatTime } from '@/utils/format';
import { useText } from '@/utils/i18n';
import styles from './styles.module.less';

const listenerFailureKeys: Record<string, string> = {
  listener_io_failed: 'listeners.failure.io',
  connection_io_failed: 'listeners.failure.connectionIo',
  tls_listener_start_failed: 'listeners.failure.tls',
  source_registry_unavailable: 'listeners.failure.sources',
  invalid_listener_configuration: 'listeners.invalid',
  listener_configuration_unavailable: 'listeners.failure.configuration',
  listener_forced_disabled: 'listeners.failure.forcedDisabled',
};
type PendingChange =
  | { kind: 'save'; input: ListenerSettingsUpdate }
  | { kind: 'reception'; enabled: boolean; version: string }
  | {
      kind: 'protocol';
      protocol: ListenerProtocol;
      enabled: boolean;
      version: string;
      masterEnabled: boolean;
    };

function isNewer(left: string, right: string) {
  // 序号是十进制字符串。按长度和字典序比较，避免转为 Number 丢失精度。
  return left.length === right.length
    ? left > right
    : left.length > right.length;
}

function editableParameters(configuration: ListenerConfiguration) {
  return configuration.listeners.map(
    ({ enabled: _, ...parameters }) => parameters,
  );
}

const protocolOrder: ListenerProtocol[] = ['udp', 'tcp', 'tls', 'relp'];

function orderSettings(data: ListenerSettings): ListenerSettings {
  const { saved } = data;
  // 部署默认的顺序可能与首次保存后的顺序不同。表单索引始终对应同一协议。
  const listeners = [...saved.configuration.listeners].sort(
    (left, right) =>
      protocolOrder.indexOf(left.protocol) -
      protocolOrder.indexOf(right.protocol),
  );
  return {
    ...data,
    saved: { ...saved, configuration: { ...saved.configuration, listeners } },
  };
}

function errorKey(error: unknown) {
  if (error instanceof ApiError) {
    if (error.code === 'version_conflict') return 'listeners.conflict';
    if (error.code === 'listener_migration_required')
      return 'listeners.migrationRequired';
    if (error.code === 'legacy_listener_configuration')
      return 'listeners.legacyConfiguration';
    if (error.code === 'listener_certificate_missing')
      return 'listeners.certificateRequired';
    if (error.code === 'listener_port_conflict')
      return 'listeners.portConflict';
    if (error.code === 'invalid_listener_configuration')
      return 'listeners.invalid';
    if (error.code === 'listener_forced_disabled')
      return 'listeners.failure.forcedDisabled';
  }
  return getErrorKey(error);
}

export default function ListenersPage() {
  const t = useText();
  const [form] = Form.useForm<ListenerConfiguration>();
  const [data, setData] = useState<ListenerSettings>();
  const [live, setLive] = useState<ListenerRuntimeView>();
  const [connection, setConnection] =
    useState<OverviewConnectionState>('connecting');
  const [connectionAttempt, setConnectionAttempt] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  const [changeError, setChangeError] = useState<string>();
  const [saved, setSaved] = useState(false);
  const [changing, setChanging] = useState(false);
  const [pending, setPending] = useState<PendingChange>();
  const editor = useRef<ListenerSettingsUpdate | undefined>(undefined);
  const generation = useRef(0);
  const controller = useRef<AbortController | undefined>(undefined);
  const submitting = useRef(false);
  const mounted = useRef(true);

  const reload = useCallback(async () => {
    const current = ++generation.current;
    controller.current?.abort();
    const request = new AbortController();
    controller.current = request;
    setLoading(true);
    setError(undefined);
    setSaved(false);
    try {
      const result = orderSettings(await getListenerSettings(request.signal));
      if (current !== generation.current || request.signal.aborted) return;
      setData(result);
      editor.current = result.saved;
      form.setFieldsValue(result.saved.configuration);
    } catch (failure) {
      if (current === generation.current && !request.signal.aborted)
        setError(errorKey(failure));
    } finally {
      if (current === generation.current && !request.signal.aborted)
        setLoading(false);
    }
  }, [form]);

  useEffect(() => {
    mounted.current = true;
    void reload();
    return () => {
      mounted.current = false;
      generation.current++;
      controller.current?.abort();
    };
  }, [reload]);

  // biome-ignore lint/correctness/useExhaustiveDependencies: 手动重连需要结束旧订阅并建立新连接。
  useEffect(
    () =>
      subscribeListenerRuntime({
        snapshot: ({ runtime }) => setLive(runtime),
        state: setConnection,
        expired: () => setConnection('disconnected'),
      }),
    [connectionAttempt],
  );

  const refresh = () => {
    if (connection === 'disconnected')
      setConnectionAttempt((value) => value + 1);
    void reload();
  };

  const applyChange = async () => {
    if (!pending || submitting.current) return;
    submitting.current = true;
    generation.current++;
    setChanging(true);
    setChangeError(undefined);
    try {
      const response =
        pending.kind === 'save'
          ? await saveListenerSettings(pending.input)
          : pending.kind === 'protocol'
            ? await setListenerProtocol(
                pending.protocol,
                pending.enabled,
                pending.version,
              )
            : await setListenerReception(pending.enabled, pending.version);
      if (!mounted.current) return;
      const result = orderSettings(response);
      setData(result);
      setPending(undefined);
      if (pending.kind === 'save') {
        editor.current = result.saved;
        form.setFieldsValue(result.saved.configuration);
        setSaved(true);
      } else {
        // 启停不改编辑区。只有已保存参数未变时，才更新表单的保存版本与启停值。
        if (
          editor.current &&
          JSON.stringify(editableParameters(editor.current.configuration)) ===
            JSON.stringify(editableParameters(result.saved.configuration))
        )
          editor.current = result.saved;
        setSaved(false);
      }
    } catch (failure) {
      // 保留表单及服务端最后确认的运行状态，错误只显示本地化提示。
      if (mounted.current) setChangeError(errorKey(failure));
    } finally {
      submitting.current = false;
      if (mounted.current) setChanging(false);
    }
  };

  const receptionChange = pending?.kind === 'reception' ? pending : undefined;
  const protocolChange = pending?.kind === 'protocol' ? pending : undefined;
  const saveChange = pending?.kind === 'save' ? pending.input : undefined;
  const runtime =
    live && (!data || isNewer(live.revision, data.revision)) ? live : data;
  const desired = runtime?.saved;
  const masterEnabled = runtime?.running.enabled ?? false;
  const actionDisabled = loading || changing || Boolean(error || pending);
  const receptionLabel = (enabled: boolean) => (
    <Typography.Text type={enabled ? 'success' : 'danger'}>
      {t(enabled ? 'source.enabled' : 'source.disabled')}
    </Typography.Text>
  );

  const stateLabel = (state: string) =>
    t(
      ['disabled', 'starting', 'listening', 'failed', 'stopped'].includes(state)
        ? `listeners.state.${state}`
        : 'listeners.state.unknown',
    );

  return (
    <PageContainer
      title={t('listeners.title')}
      content={t('listeners.description')}
      extra={[
        <Button
          key="refresh"
          icon={<ReloadOutlined aria-hidden />}
          loading={loading}
          disabled={changing || Boolean(pending)}
          onClick={refresh}
        >
          {t('listeners.reload')}
        </Button>,
      ]}
    >
      <div className={styles.stack}>
        <Alert
          type="info"
          showIcon
          title={t('listeners.restartHint')}
          description={t('listeners.boundary')}
        />
        {error && (
          <Alert
            type="error"
            showIcon
            title={t('listeners.loadFailed')}
            description={t(error)}
            action={<Button onClick={refresh}>{t('source.retry')}</Button>}
          />
        )}
        {saved && (
          <Alert
            type="success"
            showIcon
            title={t('listeners.saved')}
            description={t('listeners.restartHint')}
          />
        )}
        {data && runtime && (
          <>
            <Card
              title={t('listeners.running')}
              size="small"
              extra={
                <Tag color={runtime.restartRequired ? 'warning' : 'default'}>
                  {t(
                    runtime.restartRequired
                      ? 'listeners.pending'
                      : 'listeners.current',
                  )}
                </Tag>
              }
            >
              <div className={styles.master}>
                <Space wrap>
                  <Typography.Text strong>
                    {t('listeners.master')}
                  </Typography.Text>
                  <Switch
                    aria-label={t('listeners.master')}
                    checked={masterEnabled}
                    loading={changing && Boolean(receptionChange)}
                    disabled={actionDisabled || !desired}
                    onChange={(enabled) => {
                      if (!desired) return;
                      setChangeError(undefined);
                      setPending({
                        kind: 'reception',
                        enabled,
                        version: desired.version,
                      });
                    }}
                  />
                  {receptionLabel(masterEnabled)}
                </Space>
                <Typography.Text type="secondary">
                  {t('listeners.masterHint')}
                </Typography.Text>
                {(connection === 'reconnecting' ||
                  connection === 'disconnected') && (
                  <Typography.Text type="secondary" role="status">
                    {t('listeners.streamInterrupted')}
                  </Typography.Text>
                )}
              </div>
              <Table
                size="small"
                pagination={false}
                rowKey={(row) => `${row.protocol}-${row.address}-${row.port}`}
                dataSource={runtime.states}
                scroll={{ x: 640 }}
                columns={[
                  {
                    title: t('listeners.protocol'),
                    dataIndex: 'protocol',
                    render: (value: string) => value.toUpperCase(),
                  },
                  { title: t('listeners.address'), dataIndex: 'address' },
                  { title: t('listeners.port'), dataIndex: 'port' },
                  {
                    title: t('listeners.enabled'),
                    key: 'enabled',
                    render: (_, { protocol }) => (
                      <Switch
                        size="small"
                        aria-label={`${protocol.toUpperCase()} ${t('listeners.enabled')}`}
                        checked={
                          desired?.configuration.listeners.some(
                            (listener) =>
                              listener.protocol === protocol &&
                              listener.enabled,
                          ) ?? false
                        }
                        loading={
                          changing && protocolChange?.protocol === protocol
                        }
                        disabled={actionDisabled || !desired}
                        onChange={(enabled) => {
                          if (!desired) return;
                          setChangeError(undefined);
                          setPending({
                            kind: 'protocol',
                            protocol,
                            enabled,
                            version: desired.version,
                            masterEnabled,
                          });
                        }}
                      />
                    ),
                  },
                  {
                    title: t('listeners.state'),
                    dataIndex: 'state',
                    render: (value: string) => (
                      <Tag
                        color={
                          value === 'listening'
                            ? 'success'
                            : value === 'failed'
                              ? 'error'
                              : 'default'
                        }
                      >
                        {stateLabel(value)}
                      </Tag>
                    ),
                  },
                  {
                    title: t('listeners.failure'),
                    dataIndex: 'failureCode',
                    render: (value: string, row) =>
                      value
                        ? row.failureMessage ||
                          t(
                            listenerFailureKeys[value] ??
                              'listeners.listenerFailed',
                          )
                        : '—',
                  },
                ]}
              />
              {Boolean(runtime.connectionErrors?.length) && (
                <div className={styles.connectionErrors}>
                  <Typography.Title level={5}>
                    {t('listeners.connectionErrors')}
                  </Typography.Title>
                  <Table<ListenerConnectionError>
                    size="small"
                    pagination={{ pageSize: 5, hideOnSinglePage: true }}
                    rowKey={(row) =>
                      `${row.protocol}|${row.address}|${row.port}|${row.peerAddress}`
                    }
                    dataSource={runtime.connectionErrors}
                    scroll={{ x: 620 }}
                    columns={[
                      {
                        title: t('listeners.clientAddress'),
                        dataIndex: 'peerAddress',
                      },
                      {
                        title: t('listeners.protocol'),
                        dataIndex: 'protocol',
                        render: (value: string) => value.toUpperCase(),
                      },
                      {
                        title: t('listeners.errorOccurredAt'),
                        dataIndex: 'occurredAt',
                        render: (value: string) => formatTime(value),
                      },
                      {
                        title: t('listeners.failure'),
                        dataIndex: 'failureMessage',
                        render: (value: string, row) =>
                          value ||
                          t(
                            listenerFailureKeys[row.failureCode] ??
                              'listeners.listenerFailed',
                          ),
                      },
                    ]}
                  />
                </div>
              )}
            </Card>
            <Form<ListenerConfiguration>
              form={form}
              layout="vertical"
              disabled={actionDisabled}
              onFinish={(configuration) => {
                const current = editor.current;
                if (!current) return;
                setChangeError(undefined);
                setSaved(false);
                // 固定提交已确认的快照，确认对话框打开期间不读取变化中的表单。
                setPending({
                  kind: 'save',
                  input: {
                    version: current.version,
                    configuration: {
                      enabled: current.configuration.enabled,
                      listeners: configuration.listeners.map((listener) => ({
                        ...listener,
                        enabled: current.configuration.listeners.some(
                          (saved) =>
                            saved.protocol === listener.protocol &&
                            saved.enabled,
                        ),
                        address: listener.address.trim(),
                        certificateId: listener.certificateId ?? '',
                      })),
                    },
                  },
                });
              }}
            >
              <div className={styles.stack}>
                <Typography.Title level={5} className={styles.editorTitle}>
                  {t('listeners.savedConfiguration')}
                </Typography.Title>
                <div className={styles.grid}>
                  {data.saved.configuration.listeners.map((listener, index) => (
                    <Card
                      key={listener.protocol}
                      title={listener.protocol.toUpperCase()}
                      size="small"
                    >
                      <Form.Item name={['listeners', index, 'protocol']} hidden>
                        <Input />
                      </Form.Item>
                      <div className={styles.fields}>
                        <Form.Item
                          name={['listeners', index, 'address']}
                          label={t('listeners.address')}
                          rules={[
                            {
                              required: true,
                              whitespace: true,
                              message: t('listeners.addressRequired'),
                            },
                          ]}
                        >
                          <Input
                            maxLength={64}
                            aria-label={`${listener.protocol.toUpperCase()} ${t('listeners.address')}`}
                          />
                        </Form.Item>
                        <Form.Item
                          name={['listeners', index, 'port']}
                          label={t('listeners.port')}
                          rules={[
                            {
                              required: true,
                              type: 'integer',
                              min: 1,
                              max: 65535,
                              message: t('listeners.portRequired'),
                            },
                          ]}
                        >
                          <InputNumber
                            min={1}
                            max={65535}
                            precision={0}
                            className={styles.wide}
                            aria-label={`${listener.protocol.toUpperCase()} ${t('listeners.port')}`}
                          />
                        </Form.Item>
                      </div>
                      <Form.Item
                        name={['listeners', index, 'framing']}
                        hidden={
                          listener.protocol === 'udp' ||
                          listener.protocol === 'relp'
                        }
                        label={t('listeners.framing')}
                      >
                        <Select
                          aria-label={`${listener.protocol.toUpperCase()} ${t('listeners.framing')}`}
                          options={[
                            {
                              value: 'auto',
                              label: t('listeners.auto'),
                            },
                            {
                              value: 'octet-counting',
                              label: t('listeners.octet'),
                            },
                            { value: 'newline', label: t('listeners.newline') },
                          ]}
                        />
                      </Form.Item>
                      <Form.Item
                        name={['listeners', index, 'certificateId']}
                        hidden={listener.protocol !== 'tls'}
                        label={t('listeners.certificate')}
                        rules={[
                          {
                            validator: (_, value: string) =>
                              listener.protocol !== 'tls' ||
                              !editor.current?.configuration.listeners.some(
                                (saved) =>
                                  saved.protocol === 'tls' && saved.enabled,
                              ) ||
                              (value &&
                                data.certificates.some(
                                  ({ id }) => id === value,
                                ))
                                ? Promise.resolve()
                                : Promise.reject(
                                    new Error(
                                      t('listeners.certificateRequired'),
                                    ),
                                  ),
                          },
                        ]}
                      >
                        <Select
                          allowClear
                          options={data.certificates.map(({ id }) => ({
                            value: id,
                            label:
                              id === 'system-self-signed'
                                ? t('httpsCertificate.selfSigned')
                                : id === 'system-uploaded'
                                  ? t('httpsCertificate.uploaded')
                                  : id,
                          }))}
                        />
                      </Form.Item>
                      {listener.protocol === 'tls' &&
                        data.certificates.length === 0 && (
                          <Typography.Paragraph type="secondary">
                            {t('listeners.noCertificates')}
                          </Typography.Paragraph>
                        )}
                      {listener.protocol !== 'udp' && (
                        <div className={styles.fields}>
                          <Form.Item
                            name={['listeners', index, 'frameTimeoutSeconds']}
                            label={t('listeners.frameTimeout')}
                            rules={[
                              {
                                required: true,
                                type: 'integer',
                                min: 1,
                                max: 300,
                                message: t('listeners.timeoutRequired'),
                              },
                            ]}
                          >
                            <InputNumber
                              min={1}
                              max={300}
                              precision={0}
                              className={styles.wide}
                            />
                          </Form.Item>
                          <Form.Item
                            name={['listeners', index, 'idleTimeoutSeconds']}
                            label={t('listeners.idleTimeout')}
                            rules={[
                              {
                                required: true,
                                type: 'integer',
                                min: 1,
                                max: 86400,
                                message: t('listeners.timeoutRequired'),
                              },
                            ]}
                          >
                            <InputNumber
                              min={1}
                              max={86400}
                              precision={0}
                              className={styles.wide}
                            />
                          </Form.Item>
                        </div>
                      )}
                      {listener.protocol === 'udp' && (
                        <>
                          <Form.Item
                            name={['listeners', index, 'frameTimeoutSeconds']}
                            hidden
                          >
                            <InputNumber />
                          </Form.Item>
                          <Form.Item
                            name={['listeners', index, 'idleTimeoutSeconds']}
                            hidden
                          >
                            <InputNumber />
                          </Form.Item>
                        </>
                      )}
                      {listener.protocol === 'relp' && (
                        <Typography.Paragraph type="secondary">
                          {t('listeners.relpHint')}
                        </Typography.Paragraph>
                      )}
                    </Card>
                  ))}
                </div>
                <Space>
                  <Button
                    htmlType="submit"
                    type="primary"
                    icon={<SaveOutlined aria-hidden />}
                    loading={changing && Boolean(saveChange)}
                  >
                    {t('listeners.save')}
                  </Button>
                  <Typography.Text type="secondary">
                    {t('listeners.restartHint')}
                  </Typography.Text>
                </Space>
              </div>
            </Form>
          </>
        )}
        <Modal
          open={Boolean(pending)}
          title={
            protocolChange
              ? `${protocolChange.protocol.toUpperCase()} · ${t(protocolChange.enabled ? 'listeners.protocolEnable' : 'listeners.protocolDisable')}`
              : t(
                  receptionChange
                    ? receptionChange.enabled
                      ? 'listeners.confirmEnable'
                      : 'listeners.confirmDisable'
                    : 'listeners.confirmTitle',
                )
          }
          okText={t(
            receptionChange || protocolChange
              ? 'listeners.confirmSwitch'
              : 'listeners.confirmSave',
          )}
          cancelText={t('source.cancel')}
          confirmLoading={changing}
          closable={!changing}
          keyboard={!changing}
          mask={{ closable: false }}
          cancelButtonProps={{ disabled: changing }}
          onCancel={() => {
            if (!changing) setPending(undefined);
          }}
          onOk={() => void applyChange()}
        >
          <Typography.Paragraph>
            {t(
              protocolChange
                ? !protocolChange.masterEnabled
                  ? 'listeners.protocolSavedHint'
                  : protocolChange.enabled
                    ? 'listeners.protocolEnableHint'
                    : 'listeners.protocolDisableHint'
                : receptionChange
                  ? receptionChange.enabled
                    ? 'listeners.enableHint'
                    : 'listeners.disableHint'
                  : 'listeners.confirmHint',
            )}
          </Typography.Paragraph>
          {saveChange && (
            <>
              <Typography.Paragraph>
                {t('listeners.master')}{' '}
                {receptionLabel(saveChange.configuration.enabled)}
              </Typography.Paragraph>
              <ul className={styles.summary}>
                {saveChange.configuration.listeners.map((listener) => (
                  <li key={listener.protocol}>
                    <Typography.Text
                      type={listener.enabled ? 'success' : 'danger'}
                    >
                      {listener.protocol.toUpperCase()} · {listener.address}:
                      {listener.port} ·{' '}
                      {t(
                        listener.enabled ? 'source.enabled' : 'source.disabled',
                      )}
                    </Typography.Text>
                  </li>
                ))}
              </ul>
            </>
          )}
          {changeError && (
            <Alert
              type="error"
              showIcon
              title={t(
                receptionChange || protocolChange
                  ? 'listeners.switchFailed'
                  : 'listeners.saveFailed',
              )}
              description={t(changeError)}
            />
          )}
        </Modal>
      </div>
    </PageContainer>
  );
}
