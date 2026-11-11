import { history, useModel } from '@umijs/max';
import { Alert, Button, Result } from 'antd';
import { type ReactNode, useEffect } from 'react';
import HelpDocs from '@/components/HelpDocs';
import { SESSION_EXPIRED_EVENT } from '@/services/syslog/api';
import { useText } from '@/utils/i18n';
import AccountMenu from './AccountMenu';
import { AppearanceProvider, useSystemAppearance } from './Appearance';
import LanguageMenu from './LanguageMenu';
import { isSyntheticPreview } from './preview';

export function HeaderActions() {
  return (
    <AppearanceProvider>
      <div className="syslog-header-actions">
        <HelpDocs />
        <LanguageMenu />
        <AccountMenu />
      </div>
    </AppearanceProvider>
  );
}

export function SessionRuntime({ children }: { children: ReactNode }) {
  const { initialState, setInitialState } = useModel('@@initialState');
  const t = useText();
  useSystemAppearance();
  useEffect(() => {
    const expired = () => {
      void setInitialState((state) =>
        state?.bootNotice
          ? state
          : { ...state, currentUser: undefined, bootError: 'error.session' },
      );
      if (history.location.pathname !== '/user/login')
        history.replace('/user/login');
    };
    window.addEventListener(SESSION_EXPIRED_EVENT, expired);
    return () => window.removeEventListener(SESSION_EXPIRED_EVENT, expired);
  }, [setInitialState]);
  if (!initialState?.currentUser) {
    return (
      <Result
        status="warning"
        title={t(initialState?.bootError ?? 'app.sessionLoading')}
        extra={
          <Button onClick={() => window.location.reload()}>
            {t('app.retry')}
          </Button>
        }
      />
    );
  }
  return <AppearanceProvider>{children}</AppearanceProvider>;
}

export function PreviewBanner() {
  const t = useText();
  // 仅合成预览服务注入该标记。生产构建不包含样本入口。
  return isSyntheticPreview() ? (
    <Alert
      className="preview-banner"
      type="warning"
      banner
      title={t('app.preview')}
    />
  ) : null;
}
