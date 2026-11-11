import { PlusOutlined, ReloadOutlined } from '@ant-design/icons';
import { PageContainer } from '@ant-design/pro-components';
import { history, useModel } from '@umijs/max';
import {
  Alert,
  Button,
  Card,
  Empty,
  Input,
  Modal,
  message,
  Select,
  Space,
  Table,
  Tag,
  Typography,
} from 'antd';
import type { ColumnsType } from 'antd/es/table';
import { useRef, useState } from 'react';
import { getErrorKey } from '@/requestErrorConfig';
import { updateUser } from '@/services/syslog/api';
import type { ManagedUser, UserQuery } from '@/services/syslog/types';
import { formatTime } from '@/utils/format';
import { useText } from '@/utils/i18n';
import styles from './styles.module.less';
import UserEditor, { type UserEditorProps } from './UserEditor';
import { useUsers } from './useUsers';

export default function Users() {
  const t = useText();
  const { initialState, setInitialState } = useModel('@@initialState');
  const currentId = initialState?.currentUser?.id;
  const [query, setQuery] = useState<UserQuery>({ page: 1, pageSize: 20 });
  const [search, setSearch] = useState('');
  const { data, loading, error, reload } = useUsers(query);
  const [editor, setEditor] =
    useState<Pick<UserEditorProps, 'mode' | 'user'>>();
  const [stateTarget, setStateTarget] = useState<ManagedUser>();
  const [pending, setPending] = useState(false);
  const saving = useRef(false);
  const [saveError, setSaveError] = useState<string>();
  const [notice, contextHolder] = message.useMessage();

  async function saveState() {
    if (!stateTarget || saving.current) return;
    saving.current = true;
    setPending(true);
    setSaveError(undefined);
    try {
      await updateUser(stateTarget.id, {
        username: stateTarget.username,
        enabled: !stateTarget.enabled,
        expectedVersion: stateTarget.version,
      });
      setStateTarget(undefined);
      void notice.success(t('users.updated'));
      await reload();
    } catch (failure) {
      setSaveError(getErrorKey(failure));
    } finally {
      saving.current = false;
      setPending(false);
    }
  }

  const columns: ColumnsType<ManagedUser> = [
    {
      title: t('users.username'),
      dataIndex: 'username',
      width: 200,
      render: (value: string, user) => (
        <Space wrap>
          <span>{value}</span>
          {user.id === currentId && (
            <Tag color="blue">{t('users.current')}</Tag>
          )}
        </Space>
      ),
    },
    {
      title: t('users.role'),
      dataIndex: 'role',
      width: 90,
      render: () => t('users.admin'),
    },
    {
      title: t('users.filter'),
      dataIndex: 'enabled',
      width: 100,
      render: (value: boolean) => (
        <Tag color={value ? 'success' : 'default'}>
          {t(value ? 'users.enabled' : 'users.disabled')}
        </Tag>
      ),
    },
    {
      title: t('users.createdAt'),
      dataIndex: 'createdAt',
      width: 210,
      render: (value: string) => formatTime(value),
    },
    {
      title: t('users.actions'),
      key: 'actions',
      width: 250,
      render: (_, user) => (
        <Space wrap size={0}>
          <Button
            type="link"
            size="small"
            disabled={loading || !!error}
            onClick={() => setEditor({ mode: 'edit', user })}
          >
            {t('users.edit')}
          </Button>
          <Button
            type="link"
            size="small"
            disabled={loading || !!error || user.id === currentId}
            onClick={() => setEditor({ mode: 'password', user })}
          >
            {t('users.resetPassword')}
          </Button>
          <Button
            type="link"
            size="small"
            danger={user.enabled}
            disabled={loading || !!error || user.id === currentId}
            onClick={() => {
              setSaveError(undefined);
              setStateTarget(user);
            }}
          >
            {t(user.enabled ? 'users.disable' : 'users.enable')}
          </Button>
        </Space>
      ),
    },
  ];

  return (
    <PageContainer
      title={t('users.title')}
      content={t('users.description')}
      extra={[
        <Button
          key="refresh"
          icon={<ReloadOutlined />}
          loading={loading}
          onClick={() => void reload()}
        >
          {t('users.refresh')}
        </Button>,
        <Button
          key="add"
          type="primary"
          icon={<PlusOutlined />}
          onClick={() => setEditor({ mode: 'create' })}
        >
          {t('users.add')}
        </Button>,
      ]}
    >
      {contextHolder}
      <div className={styles.stack}>
        <Alert type="info" showIcon title={t('users.permissionHint')} />
        {error && (
          <Alert
            type="warning"
            showIcon
            title={t('users.loadFailed')}
            description={
              <>
                {t(error)}
                {data && <div>{t('users.stale')}</div>}
              </>
            }
          />
        )}
        <Card>
          <div className={styles.toolbar}>
            <Space wrap>
              <Input.Search
                value={search}
                maxLength={64}
                allowClear
                placeholder={t('users.search')}
                aria-label={t('users.search')}
                className={styles.search}
                onChange={(event) => {
                  setSearch(event.target.value);
                  if (!event.target.value)
                    setQuery((old) => ({ ...old, search: undefined, page: 1 }));
                }}
                onSearch={(value) =>
                  setQuery((old) => ({ ...old, search: value.trim(), page: 1 }))
                }
              />
              <Select
                value={
                  query.enabled === undefined ? 'all' : String(query.enabled)
                }
                aria-label={t('users.filter')}
                className={styles.filter}
                onChange={(value) =>
                  setQuery((old) => ({
                    ...old,
                    enabled: value === 'all' ? undefined : value === 'true',
                    page: 1,
                  }))
                }
                options={[
                  { value: 'all', label: t('users.all') },
                  { value: 'true', label: t('users.enabled') },
                  { value: 'false', label: t('users.disabled') },
                ]}
              />
            </Space>
            {data && (
              <Typography.Text type="secondary">
                {t('users.total', { total: data.total })}
              </Typography.Text>
            )}
          </div>
          <Table<ManagedUser>
            rowKey="id"
            size="small"
            columns={columns}
            dataSource={data?.items ?? []}
            loading={loading}
            scroll={{ x: 850 }}
            pagination={
              data
                ? {
                    current: data.page,
                    pageSize: data.pageSize,
                    total: data.total,
                    pageSizeOptions: [20, 50, 100],
                    showSizeChanger: true,
                    onChange: (page, pageSize) =>
                      setQuery((old) => ({ ...old, page, pageSize })),
                  }
                : false
            }
            locale={{
              emptyText: (
                <Empty
                  image={Empty.PRESENTED_IMAGE_SIMPLE}
                  description={t(data ? 'users.empty' : 'users.unavailable')}
                />
              ),
            }}
          />
        </Card>
        <Typography.Text type="secondary">
          {t('users.selfHint')}
        </Typography.Text>
      </div>
      {editor && (
        <UserEditor
          {...editor}
          onClose={() => setEditor(undefined)}
          onSaved={async (updated) => {
            const selfChanged =
              updated?.id === currentId &&
              editor.mode === 'edit' &&
              updated?.version !== editor.user?.version;
            setEditor(undefined);
            if (selfChanged && updated) {
              await setInitialState((old) => ({
                ...old,
                currentUser: undefined,
                bootError: undefined,
                bootNotice: 'users.selfChanged',
                loginUsername: updated.username,
              }));
              history.replace('/user/login');
              return;
            }
            void notice.success(
              t(
                editor.mode === 'create'
                  ? 'users.created'
                  : editor.mode === 'password'
                    ? 'users.passwordReset'
                    : 'users.updated',
              ),
            );
            await reload();
          }}
        />
      )}
      {stateTarget && (
        <Modal
          open
          title={t(stateTarget.enabled ? 'users.disableTitle' : 'users.enable')}
          okText={t('users.confirm')}
          cancelText={t('app.cancel')}
          confirmLoading={pending}
          okButtonProps={{ danger: stateTarget.enabled }}
          closable={!pending}
          keyboard={!pending}
          mask={{ closable: !pending }}
          cancelButtonProps={{ disabled: pending }}
          onOk={() => void saveState()}
          onCancel={() => {
            if (!saving.current) setStateTarget(undefined);
          }}
        >
          <p>
            {stateTarget.enabled
              ? t('users.disableHint', { username: stateTarget.username })
              : stateTarget.username}
          </p>
          {saveError && <Alert type="error" showIcon title={t(saveError)} />}
        </Modal>
      )}
    </PageContainer>
  );
}
