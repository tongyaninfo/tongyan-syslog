import {
  DownOutlined,
  KeyOutlined,
  LogoutOutlined,
  SkinOutlined,
} from '@ant-design/icons';
import { history, useModel } from '@umijs/max';
import { App, Button, Dropdown } from 'antd';
import { useRef, useState } from 'react';
import { getErrorKey } from '@/requestErrorConfig';
import { logout } from '@/services/syslog/api';
import { useText } from '@/utils/i18n';
import { AppearanceDrawer } from './Appearance';
import PasswordDialog from './PasswordDialog';
import { isSyntheticPreview } from './preview';

export default function AccountMenu() {
  const { initialState, setInitialState } = useModel('@@initialState');
  const { message } = App.useApp();
  const t = useText();
  const [panel, setPanel] = useState<'theme' | 'password'>();
  const [pending, setPending] = useState(false);
  const signingOut = useRef(false);
  const username = initialState?.currentUser?.username;
  if (!username) return null;

  async function endSession(bootNotice?: string) {
    await setInitialState((state) => ({
      ...state,
      currentUser: undefined,
      bootError: undefined,
      bootNotice,
    }));
    history.replace('/user/login');
  }

  async function signOut() {
    if (signingOut.current) return;
    signingOut.current = true;
    setPending(true);
    try {
      await logout();
      await endSession();
    } catch (error) {
      message.error(t(getErrorKey(error)));
    } finally {
      signingOut.current = false;
      setPending(false);
    }
  }

  return (
    <>
      <Dropdown
        placement="bottomRight"
        trigger={['click']}
        menu={{
          items: [
            {
              key: 'theme',
              icon: <SkinOutlined />,
              label: t('app.appearance'),
            },
            {
              key: 'password',
              icon: <KeyOutlined />,
              label: t('app.changePassword'),
            },
            { type: 'divider' },
            {
              key: 'logout',
              icon: <LogoutOutlined />,
              label: t('app.logout'),
              disabled: pending,
            },
          ],
          onClick: ({ key }) => {
            if (key === 'theme' || key === 'password') setPanel(key);
            if (key === 'logout') void signOut();
          },
        }}
      >
        <Button
          className="syslog-account-trigger"
          type="text"
          loading={pending}
          aria-label={`${t('app.userMenu')} · ${username}`}
          aria-haspopup="menu"
        >
          <span className="syslog-account-name">{username}</span>
          <DownOutlined aria-hidden="true" />
        </Button>
      </Dropdown>
      <AppearanceDrawer
        open={panel === 'theme'}
        onClose={() => setPanel(undefined)}
      />
      {panel === 'password' && (
        <PasswordDialog
          onClose={() => setPanel(undefined)}
          onChanged={() =>
            endSession(
              isSyntheticPreview()
                ? 'app.passwordPreviewChanged'
                : 'app.passwordChanged',
            )
          }
        />
      )}
    </>
  );
}
