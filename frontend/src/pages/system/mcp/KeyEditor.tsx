import {
  Alert,
  Button,
  DatePicker,
  Drawer,
  Form,
  Input,
  Radio,
  Space,
} from 'antd';
import dayjs, { type Dayjs } from 'dayjs';
import { useRef, useState } from 'react';
import {
  createMcpKey,
  type McpAccessMode,
  type McpConnectionMaterial,
  type McpPermission,
} from '@/services/syslog/mcp';
import { useText } from '@/utils/i18n';
import { allowedPermissions, getMcpError } from './common';
import PermissionTree from './PermissionTree';
import styles from './styles.module.less';

interface KeyForm {
  name: string;
  expiresAt?: Dayjs;
}

export default function KeyEditor({
  permissions,
  onClose,
  onCreated,
}: {
  permissions: McpPermission[];
  onClose: () => void;
  onCreated: (material: McpConnectionMaterial) => void;
}) {
  const t = useText();
  const [form] = Form.useForm<KeyForm>();
  const [mode, setMode] = useState<McpAccessMode>('read');
  const [selected, setSelected] = useState<string[]>([]);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string>();
  const locked = useRef(false);

  async function save(values: KeyForm) {
    if (locked.current) return;
    const granted = allowedPermissions(selected, permissions, mode);
    if (!granted.length) {
      setError('mcp.error.permissions');
      return;
    }
    locked.current = true;
    setSaving(true);
    setError(undefined);
    try {
      const result = await createMcpKey({
        name: values.name.trim(),
        accessMode: mode,
        permissions: granted,
        expiresAt: values.expiresAt
          ? dayjs
              .tz(
                values.expiresAt.format('YYYY-MM-DD HH:mm:ss'),
                'Asia/Shanghai',
              )
              .toISOString()
          : null,
      });
      onCreated({
        keyId: result.key.id,
        name: result.key.name,
        accessMode: result.key.accessMode,
        permissions: result.key.permissions,
        expiresAt: result.key.expiresAt,
        secret: result.secret,
      });
    } catch (failure) {
      setError(getMcpError(failure));
    } finally {
      setSaving(false);
      locked.current = false;
    }
  }

  return (
    <Drawer
      open
      title={t('mcp.newKey')}
      size="min(640px, 100vw)"
      onClose={() => !saving && onClose()}
      closable={!saving}
      mask={{ closable: !saving }}
      footer={
        <Space>
          <Button type="primary" loading={saving} onClick={() => form.submit()}>
            {t('mcp.create')}
          </Button>
          <Button disabled={saving} onClick={onClose}>
            {t('app.cancel')}
          </Button>
        </Space>
      }
    >
      <Form
        name="mcpKey"
        form={form}
        layout="vertical"
        onFinish={save}
        disabled={saving}
      >
        <Alert
          type="info"
          showIcon
          title={t('mcp.immutable')}
          className={styles.notice}
        />
        {error && (
          <Alert
            type="error"
            showIcon
            title={t(error)}
            className={styles.notice}
          />
        )}
        <Form.Item
          name="name"
          label={t('mcp.keyName')}
          rules={[
            {
              required: true,
              whitespace: true,
              message: t('mcp.nameRequired'),
            },
            { max: 128 },
          ]}
        >
          <Input
            maxLength={128}
            autoComplete="off"
            placeholder={t('mcp.namePlaceholder')}
          />
        </Form.Item>
        <Form.Item label={t('mcp.accessMode')} required>
          <Radio.Group
            value={mode}
            onChange={(event) => {
              const next = event.target.value as McpAccessMode;
              setMode(next);
              setSelected((previous) =>
                allowedPermissions(previous, permissions, next),
              );
            }}
            options={[
              { value: 'read', label: t('mcp.readOnly') },
              { value: 'read_write', label: t('mcp.readWrite') },
            ]}
          />
        </Form.Item>
        <Form.Item
          name="expiresAt"
          label={t('mcp.expiresAt')}
          extra={t('mcp.expiryHint')}
          rules={[
            {
              validator: (_, value?: Dayjs) =>
                !value ||
                dayjs
                  .tz(value.format('YYYY-MM-DD HH:mm:ss'), 'Asia/Shanghai')
                  .isAfter(dayjs())
                  ? Promise.resolve()
                  : Promise.reject(new Error(t('mcp.expiryInvalid'))),
            },
          ]}
        >
          <DatePicker
            showTime
            format="YYYY-MM-DD HH:mm:ss"
            className={styles.fieldWidth}
            placeholder={t('mcp.permanent')}
          />
        </Form.Item>
        <Form.Item label={t('mcp.permissions')} required>
          <PermissionTree
            permissions={permissions}
            value={selected}
            onChange={setSelected}
            mode={mode}
            disabled={saving}
          />
        </Form.Item>
      </Form>
    </Drawer>
  );
}
