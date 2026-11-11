import { SearchOutlined } from '@ant-design/icons';
import { Empty, Input, Tag, Tree, Typography } from 'antd';
import { useMemo, useState } from 'react';
import type { McpAccessMode, McpPermission } from '@/services/syslog/mcp';
import { useText } from '@/utils/i18n';
import { allowedPermissions, mergeVisiblePermissions } from './common';
import styles from './styles.module.less';

export default function PermissionTree({
  permissions,
  value,
  onChange,
  mode,
  disabled = false,
}: {
  permissions: McpPermission[];
  value: string[];
  onChange?: (value: string[]) => void;
  mode: McpAccessMode;
  disabled?: boolean;
}) {
  const t = useText();
  const [search, setSearch] = useState('');
  const shown = useMemo(() => {
    const query = search.trim().toLocaleLowerCase();
    return permissions.filter((item) =>
      [item.name, item.module, item.title, item.description].some((part) =>
        part.toLocaleLowerCase().includes(query),
      ),
    );
  }, [permissions, search]);
  const modules = [...new Set(shown.map((item) => item.module))];
  const treeData = modules.map((module) => ({
    key: `module:${module}`,
    title: module,
    children: shown
      .filter((item) => item.module === module)
      .map((item) => ({
        key: item.name,
        disabled: mode === 'read' && item.isWrite,
        title: (
          <div>
            <span className={styles.treeTitle}>
              <span>{item.title}</span>
              <Tag color={item.isWrite ? 'gold' : 'blue'}>
                {t(item.isWrite ? 'mcp.write' : 'mcp.read')}
              </Tag>
              {item.isDestructive && (
                <Tag color="red">{t('mcp.destructive')}</Tag>
              )}
            </span>
            <span className={styles.treeHint}>{item.description}</span>
          </div>
        ),
      })),
  }));
  return (
    <div>
      <Input
        prefix={<SearchOutlined />}
        allowClear
        value={search}
        onChange={(event) => setSearch(event.target.value)}
        placeholder={t('mcp.searchPermissions')}
        aria-label={t('mcp.searchPermissions')}
        className={styles.notice}
      />
      <div className={styles.treeBox}>
        {shown.length ? (
          <Tree
            key={search}
            checkable
            selectable={false}
            defaultExpandAll
            disabled={disabled}
            checkedKeys={value}
            treeData={treeData}
            onCheck={(checked) => {
              const keys = Array.isArray(checked) ? checked : checked.checked;
              onChange?.(
                allowedPermissions(
                  mergeVisiblePermissions(
                    value,
                    keys.map(String),
                    shown.map((item) => item.name),
                  ),
                  permissions,
                  mode,
                ),
              );
            }}
          />
        ) : (
          <Empty
            image={Empty.PRESENTED_IMAGE_SIMPLE}
            description={t('mcp.noPermissions')}
          />
        )}
      </div>
      <div className={styles.treeFooter}>
        <Typography.Text type="secondary">
          {t('mcp.permissionCount', { count: value.length })}
        </Typography.Text>
        <Typography.Text type="secondary">
          {t(mode === 'read' ? 'mcp.readOnlyHint' : 'mcp.writeHint')}
        </Typography.Text>
      </div>
    </div>
  );
}
