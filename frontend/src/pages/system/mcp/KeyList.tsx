import {
  CopyOutlined,
  PlusOutlined,
  ReloadOutlined,
  SearchOutlined,
} from '@ant-design/icons';
import {
  Alert,
  Button,
  Card,
  Drawer,
  Input,
  Modal,
  Space,
  Table,
  Tag,
  Typography,
} from 'antd';
import { useMemo, useRef, useState } from 'react';
import {
  deleteMcpKey,
  getMcpConnectionMaterial,
  getMcpKeys,
  type McpConnectionMaterial,
  type McpKey,
  type McpPermission,
  revokeMcpKey,
} from '@/services/syslog/mcp';
import { formatTime } from '@/utils/format';
import { useText } from '@/utils/i18n';
import ConnectionInstructions from './ConnectionInstructions';
import {
  buildConnectionInstructions,
  getMcpError,
  keyStatus,
  useMcpQuery,
} from './common';
import KeyEditor from './KeyEditor';
import PermissionTree from './PermissionTree';
import styles from './styles.module.less';

export function KeyStatusTag({ value }: { value: string }) {
  const t = useText();
  const known = ['active', 'expired', 'revoked', 'owner_disabled'].includes(
    value,
  );
  return (
    <Tag
      color={
        value === 'active' ? 'green' : value === 'revoked' ? 'red' : 'default'
      }
    >
      {known ? t(`mcp.keyStatus.${value}`) : value}
    </Tag>
  );
}

export default function KeyList({
  permissions,
  permissionsReady,
}: {
  permissions: McpPermission[];
  permissionsReady: boolean;
}) {
  const t = useText();
  const { data, loading, error, reload } = useMcpQuery(getMcpKeys);
  const [search, setSearch] = useState('');
  const [creating, setCreating] = useState(false);
  const [viewing, setViewing] = useState<McpKey>();
  const [instructions, setInstructions] = useState<string>();
  const [manualCopy, setManualCopy] = useState(false);
  const [copied, setCopied] = useState(false);
  const [operation, setOperation] = useState<{
    kind: 'revoke' | 'delete';
    key: McpKey;
  }>();
  const [busy, setBusy] = useState(false);
  const [busyKey, setBusyKey] = useState<string>();
  const [operationError, setOperationError] = useState<string>();
  const locked = useRef(false);
  const rows = useMemo(() => {
    const query = search.trim().toLocaleLowerCase();
    return (data ?? []).filter((item) =>
      [item.name, item.prefix].some((text) =>
        text.toLocaleLowerCase().includes(query),
      ),
    );
  }, [data, search]);

  function showInstructions(material: McpConnectionMaterial) {
    // 地址在用户本次操作时取浏览器实际入口，服务端配置域名不参与覆盖。
    setManualCopy(false);
    setInstructions(
      buildConnectionInstructions(
        material,
        window.location.origin,
        t('mcp.installInstructions'),
      ),
    );
  }

  async function copyInstructions(row: McpKey) {
    if (locked.current) return;
    locked.current = true;
    setBusyKey(row.id);
    setOperationError(undefined);
    setCopied(false);
    try {
      const material = await getMcpConnectionMaterial(row.id);
      const text = buildConnectionInstructions(
        material,
        window.location.origin,
        t('mcp.installInstructions'),
      );
      try {
        if (!navigator.clipboard?.writeText)
          throw new Error('Clipboard unavailable');
        await navigator.clipboard.writeText(text);
        setCopied(true);
      } catch {
        // HTTP 或异步取凭据丢失剪贴板手势权限时，提供可全选的完整文本。
        setManualCopy(true);
        setInstructions(text);
      }
    } catch (failure) {
      setOperationError(getMcpError(failure));
    } finally {
      setBusyKey(undefined);
      locked.current = false;
    }
  }

  async function confirmOperation() {
    if (!operation || locked.current) return;
    locked.current = true;
    setBusy(true);
    setOperationError(undefined);
    try {
      if (operation.kind === 'revoke') await revokeMcpKey(operation.key.id);
      else await deleteMcpKey(operation.key.id);
      setOperation(undefined);
      reload();
    } catch (failure) {
      setOperationError(getMcpError(failure));
    } finally {
      locked.current = false;
      setBusy(false);
    }
  }

  return (
    <>
      <Card>
        <div className={styles.toolbar}>
          <Input
            className={styles.search}
            prefix={<SearchOutlined />}
            placeholder={t('mcp.searchKeys')}
            aria-label={t('mcp.searchKeys')}
            allowClear
            value={search}
            onChange={(event) => setSearch(event.target.value)}
          />
          <Space wrap>
            <Button
              icon={<ReloadOutlined />}
              loading={loading}
              onClick={reload}
            >
              {t('mcp.refresh')}
            </Button>
            <Button
              type="primary"
              icon={<PlusOutlined />}
              disabled={!permissionsReady || Boolean(error)}
              onClick={() => setCreating(true)}
            >
              {t('mcp.newKey')}
            </Button>
          </Space>
        </div>
        {error && (
          <Alert
            showIcon
            type="error"
            title={t(error)}
            className={styles.notice}
          />
        )}
        {operationError && !operation && (
          <Alert
            showIcon
            type="error"
            title={t(operationError)}
            className={styles.notice}
          />
        )}
        {copied && (
          <Alert
            showIcon
            type="success"
            title={t('mcp.copied')}
            className={styles.notice}
          />
        )}
        <Table<McpKey>
          size="small"
          rowKey="id"
          loading={loading}
          dataSource={rows}
          scroll={{ x: 1200 }}
          pagination={{
            defaultPageSize: 10,
            showSizeChanger: true,
            hideOnSinglePage: true,
          }}
          locale={{ emptyText: t(search ? 'mcp.noMatches' : 'mcp.noKeys') }}
          columns={[
            {
              title: t('mcp.keyName'),
              dataIndex: 'name',
              width: 210,
              render: (_, row) => (
                <div className={styles.name}>
                  <Typography.Text strong>{row.name}</Typography.Text>
                  <Typography.Text type="secondary" className={styles.mono}>
                    {row.prefix}
                  </Typography.Text>
                </div>
              ),
            },
            {
              title: t('mcp.accessMode'),
              dataIndex: 'accessMode',
              width: 110,
              render: (value) => (
                <Tag color={value === 'read' ? 'blue' : 'gold'}>
                  {t(value === 'read' ? 'mcp.readOnly' : 'mcp.readWrite')}
                </Tag>
              ),
            },
            {
              title: t('mcp.permissions'),
              width: 100,
              render: (_, row) => (
                <Button
                  type="link"
                  size="small"
                  onClick={() => setViewing(row)}
                >
                  {t('mcp.permissionCount', { count: row.permissions.length })}
                </Button>
              ),
            },
            {
              title: t('mcp.expiresAt'),
              width: 180,
              render: (_, row) =>
                row.expiresAt ? (
                  formatTime(row.expiresAt)
                ) : (
                  <Typography.Text type="secondary">
                    {t('mcp.permanent')}
                  </Typography.Text>
                ),
            },
            {
              title: t('mcp.status'),
              width: 100,
              render: (_, row) => <KeyStatusTag value={keyStatus(row)} />,
            },
            {
              title: t('mcp.lastUsed'),
              width: 180,
              render: (_, row) =>
                row.lastUsedAt
                  ? formatTime(row.lastUsedAt)
                  : t('mcp.neverUsed'),
            },
            {
              title: t('source.actions'),
              width: 280,
              fixed: 'right',
              render: (_, row) => (
                <Space size={2}>
                  <Button
                    type="link"
                    size="small"
                    icon={<CopyOutlined />}
                    loading={busyKey === row.id}
                    disabled={keyStatus(row) !== 'active' || busy}
                    onClick={() => void copyInstructions(row)}
                  >
                    {t('mcp.copyInstructions')}
                  </Button>
                  <Button
                    type="link"
                    size="small"
                    danger
                    disabled={busy || Boolean(busyKey)}
                    onClick={() => {
                      setOperationError(undefined);
                      setOperation({
                        kind: row.revokedAt ? 'delete' : 'revoke',
                        key: row,
                      });
                    }}
                  >
                    {t(row.revokedAt ? 'mcp.delete' : 'mcp.revoke')}
                  </Button>
                </Space>
              ),
            },
          ]}
        />
      </Card>
      {creating && (
        <KeyEditor
          permissions={permissions}
          onClose={() => setCreating(false)}
          onCreated={(material) => {
            setCreating(false);
            reload();
            showInstructions(material);
          }}
        />
      )}
      {viewing && (
        <Drawer
          open
          title={`${t('mcp.viewPermissions')} · ${viewing.name}`}
          size="min(640px, 100vw)"
          onClose={() => setViewing(undefined)}
        >
          <Alert
            showIcon
            type="info"
            title={t('mcp.immutable')}
            className={styles.notice}
          />
          <PermissionTree
            permissions={permissions.filter((item) =>
              viewing.permissions.includes(item.name),
            )}
            value={viewing.permissions}
            mode={viewing.accessMode}
            disabled
          />
        </Drawer>
      )}
      {instructions && (
        <ConnectionInstructions
          text={instructions}
          manual={manualCopy}
          onClose={() => setInstructions(undefined)}
        />
      )}
      <Modal
        open={Boolean(operation)}
        title={t(
          operation?.kind === 'delete'
            ? 'mcp.confirmDelete'
            : 'mcp.confirmRevoke',
        )}
        onCancel={() => !busy && setOperation(undefined)}
        confirmLoading={busy}
        onOk={() => void confirmOperation()}
        okText={t('mcp.confirm')}
        cancelText={t('app.cancel')}
        okButtonProps={{ danger: true }}
        cancelButtonProps={{ disabled: busy }}
        closable={!busy}
        mask={{ closable: !busy }}
      >
        {operation && (
          <div className={styles.stack}>
            <Typography.Text strong>{operation.key.name}</Typography.Text>
            <Typography.Paragraph>
              {t(
                operation.kind === 'delete'
                  ? 'mcp.deleteHint'
                  : 'mcp.revokeHint',
              )}
            </Typography.Paragraph>
            {operationError && (
              <Alert showIcon type="error" title={t(operationError)} />
            )}
          </div>
        )}
      </Modal>
    </>
  );
}
