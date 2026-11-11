import { GlobalOutlined } from '@ant-design/icons';
import { Button, Dropdown, Tooltip } from 'antd';
import { supportedLanguages, useLanguage, useText } from '@/utils/i18n';

export default function LanguageMenu() {
  const { locale, setLanguage } = useLanguage();
  const t = useText();
  const label = t('app.language');
  return (
    <Tooltip title={label}>
      <Dropdown
        trigger={['click']}
        placement="bottomRight"
        menu={{
          items: supportedLanguages.map(({ value, label: name }) => ({
            key: value,
            label: name,
          })),
          selectable: true,
          selectedKeys: [locale],
          onClick: ({ key }) => setLanguage(key),
        }}
      >
        <Button
          type="text"
          aria-label={label}
          aria-haspopup="menu"
          icon={<GlobalOutlined />}
        />
      </Dropdown>
    </Tooltip>
  );
}
