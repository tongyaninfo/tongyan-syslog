import { SafetyCertificateOutlined } from '@ant-design/icons';
import { history, useModel } from '@umijs/max';
import { Alert, Button, Card, Form, Typography } from 'antd';
import { useEffect, useRef, useState } from 'react';
import { PreviewBanner } from '@/components/Shell';
import {
  AppearanceProvider,
  useSystemAppearance,
} from '@/components/Shell/Appearance';
import LanguageMenu from '@/components/Shell/LanguageMenu';
import {
  isSyntheticPreview,
  PREVIEW_NEW_PASSWORD,
} from '@/components/Shell/preview';
import SiteFooter from '@/components/Shell/SiteFooter';
import { NewPasswordFields, UsernameField } from '@/components/UserCredentials';
import { ApiError, getErrorKey } from '@/requestErrorConfig';
import { getSetupStatus, initializeAdmin } from '@/services/syslog/api';
import type { UserInput } from '@/services/syslog/types';
import { useText } from '@/utils/i18n';
import '../login/index.less';

export default function Setup() {
  const { initialState, setInitialState } = useModel('@@initialState');
  const [pending, setPending] = useState(false);
  const submitting = useRef(false);
  const [error, setError] = useState<string>();
  const [form] = Form.useForm<UserInput & { confirmPassword: string }>();
  const t = useText();
  const dark = initialState?.resolvedTheme === 'realDark';
  const status = initialState?.setupStatus;
  useSystemAppearance();
  useEffect(() => {
    if (status && !status.setupRequired)
      history.replace(initialState?.currentUser ? '/overview' : '/user/login');
  }, [status, initialState?.currentUser]);

  async function submit(input: UserInput) {
    if (submitting.current || !status?.setupRequired || !status.canInitialize)
      return;
    submitting.current = true;
    setPending(true);
    setError(undefined);
    try {
      const username = input.username.trim();
      await initializeAdmin({
        username,
        password: isSyntheticPreview() ? PREVIEW_NEW_PASSWORD : input.password,
      });
      form.resetFields();
      await setInitialState((state) => ({
        ...state,
        currentUser: undefined,
        setupStatus: { setupRequired: false, canInitialize: false },
        bootError: undefined,
        loginUsername: username,
        bootNotice: isSyntheticPreview()
          ? 'setup.previewCompleted'
          : 'setup.completed',
      }));
    } catch (failure) {
      setError(getErrorKey(failure));
      form.resetFields(['password', 'confirmPassword']);
      // 并发初始化已完成时重新读取真实状态，不能仍保留可提交的表单。
      if (
        failure instanceof ApiError &&
        failure.code === 'setup_already_completed'
      ) {
        try {
          const setupStatus = await getSetupStatus();
          await setInitialState((state) => ({ ...state, setupStatus }));
        } catch (refreshFailure) {
          setError(getErrorKey(refreshFailure));
        }
      }
    } finally {
      submitting.current = false;
      setPending(false);
    }
  }

  return (
    <AppearanceProvider>
      <div className="login-page" data-theme={dark ? 'dark' : 'light'}>
        <PreviewBanner />
        <header className="login-brand">
          <img
            src={
              dark
                ? '/assets/brand/tyinfo-logo-transparent.png'
                : '/assets/brand/tyinfo-logo-horizontal.png'
            }
            alt={t('app.brand')}
          />
          <span>SYSLOG</span>
          <div className="login-language">
            <LanguageMenu />
          </div>
        </header>
        <main className="login-content">
          <section className="login-intro" aria-labelledby="setup-heading">
            <span className="login-eyebrow">TONGYAN / SYSLOG</span>
            <Typography.Title id="setup-heading">
              {t('setup.title')}
            </Typography.Title>
            <Typography.Paragraph className="login-description">
              {t('setup.description')}
            </Typography.Paragraph>
            <div className="login-feature">
              <span className="feature-icon">
                <SafetyCertificateOutlined />
              </span>
              <p>{t('users.permissionHint')}</p>
            </div>
          </section>
          <Card className="login-card">
            <div className="login-accent" />
            <Typography.Title level={2}>
              {t('setup.cardTitle')}
            </Typography.Title>
            {!status ? (
              <>
                <Alert
                  type="error"
                  showIcon
                  title={t('setup.unavailable')}
                  description={t('setup.unavailableHint')}
                />
                <Button
                  className="login-error"
                  onClick={() => window.location.reload()}
                >
                  {t('app.retry')}
                </Button>
              </>
            ) : !status.canInitialize ? (
              <>
                <Alert
                  type="warning"
                  showIcon
                  title={t('setup.restricted')}
                  description={t('setup.restrictedHint')}
                />
                <Button
                  className="login-error"
                  onClick={() => window.location.reload()}
                >
                  {t('app.retry')}
                </Button>
              </>
            ) : (
              <>
                <Typography.Paragraph type="secondary">
                  {t(
                    isSyntheticPreview()
                      ? 'setup.previewHint'
                      : 'setup.cardHint',
                  )}
                </Typography.Paragraph>
                {error && (
                  <Alert
                    className="login-error"
                    type="error"
                    showIcon
                    title={t(error)}
                  />
                )}
                <Form
                  form={form}
                  layout="vertical"
                  requiredMark={false}
                  disabled={pending}
                  preserve={false}
                  onFinish={submit}
                >
                  <UsernameField />
                  <NewPasswordFields />
                  <Button
                    type="primary"
                    htmlType="submit"
                    loading={pending}
                    block
                    size="large"
                  >
                    {t('setup.submit')}
                  </Button>
                </Form>
              </>
            )}
          </Card>
        </main>
        <SiteFooter />
      </div>
    </AppearanceProvider>
  );
}
