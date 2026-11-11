import {
  ApiOutlined,
  FileSearchOutlined,
  LockOutlined,
  SafetyCertificateOutlined,
  UserOutlined,
} from '@ant-design/icons';
import { history, useModel } from '@umijs/max';
import { Alert, Button, Card, Form, Input, Typography } from 'antd';
import { useEffect, useState } from 'react';
import { PreviewBanner } from '@/components/Shell';
import {
  AppearanceProvider,
  useSystemAppearance,
} from '@/components/Shell/Appearance';
import LanguageMenu from '@/components/Shell/LanguageMenu';
import {
  isSyntheticPreview,
  PREVIEW_PASSWORD,
} from '@/components/Shell/preview';
import SiteFooter from '@/components/Shell/SiteFooter';
import { ApiError, getErrorKey } from '@/requestErrorConfig';
import { login } from '@/services/syslog/api';
import { useText } from '@/utils/i18n';
import './index.less';

export default function Login() {
  const { initialState, setInitialState } = useModel('@@initialState');
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string>();
  const [form] = Form.useForm<{ username: string; password: string }>();
  const t = useText();
  const errorKey = error ?? initialState?.bootError;
  const noticeKey = initialState?.bootNotice;
  const dark = initialState?.resolvedTheme === 'realDark';
  useSystemAppearance();
  // 等待共享会话状态完成渲染后跳转，避免布局守卫仍读到旧的未登录状态。
  useEffect(() => {
    if (initialState?.currentUser) history.replace('/overview');
  }, [initialState?.currentUser]);
  async function submit(input: { username: string; password: string }) {
    if (pending) return;
    setPending(true);
    setError(undefined);
    if (initialState?.bootError || noticeKey)
      void setInitialState((state) => ({
        ...state,
        bootError: undefined,
        bootNotice: undefined,
      }));
    try {
      if (
        isSyntheticPreview() &&
        (!/^[A-Za-z0-9._-]{3,64}$/.test(input.username.trim()) ||
          input.password !== PREVIEW_PASSWORD)
      )
        throw new ApiError('AUTH_FAILED', 401);
      const currentUser = await login(input.username.trim(), input.password);
      form.resetFields(['password']);
      await setInitialState((state) => ({
        ...state,
        currentUser,
        bootError: undefined,
        bootNotice: undefined,
        loginUsername: undefined,
      }));
    } catch (failure) {
      setError(getErrorKey(failure));
      form.resetFields(['password']);
    } finally {
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
          <section className="login-intro" aria-labelledby="login-heading">
            <span className="login-eyebrow">TONGYAN / SYSLOG</span>
            <Typography.Title id="login-heading">
              {t('login.hero')}
            </Typography.Title>
            <Typography.Paragraph className="login-description">
              {t('login.description')}
            </Typography.Paragraph>
            <div className="login-features">
              {[
                { key: 'collect', icon: <ApiOutlined /> },
                { key: 'search', icon: <FileSearchOutlined /> },
                { key: 'evidence', icon: <SafetyCertificateOutlined /> },
              ].map(({ key, icon }) => (
                <div className="login-feature" key={key}>
                  <span className="feature-icon">{icon}</span>
                  <div>
                    <strong>{t(`login.${key}`)}</strong>
                    <p>{t(`login.${key}Description`)}</p>
                  </div>
                </div>
              ))}
            </div>
          </section>
          <Card className="login-card">
            <div className="login-accent" />
            <Typography.Title level={2}>{t('login.welcome')}</Typography.Title>
            <Typography.Paragraph type="secondary">
              {t('login.subtitle')}
            </Typography.Paragraph>
            {noticeKey && !errorKey && (
              <Alert
                type="success"
                title={t(noticeKey)}
                showIcon
                className="login-error"
              />
            )}
            {errorKey && (
              <Alert
                type="error"
                title={t(errorKey)}
                showIcon
                className="login-error"
              />
            )}
            <Form
              form={form}
              layout="vertical"
              onFinish={submit}
              requiredMark={false}
              initialValues={{ username: initialState?.loginUsername }}
            >
              <Form.Item
                name="username"
                label={t('login.username')}
                rules={[
                  { required: true, message: t('login.usernameRequired') },
                ]}
              >
                <Input
                  prefix={<UserOutlined />}
                  size="large"
                  autoComplete="username"
                  maxLength={64}
                  disabled={pending}
                />
              </Form.Item>
              <Form.Item
                name="password"
                label={t('login.password')}
                rules={[
                  { required: true, message: t('login.passwordRequired') },
                ]}
              >
                <Input.Password
                  prefix={<LockOutlined />}
                  size="large"
                  autoComplete="current-password"
                  maxLength={1024}
                  disabled={pending}
                />
              </Form.Item>
              <Button
                type="primary"
                htmlType="submit"
                loading={pending}
                block
                size="large"
              >
                {t('login.submit')}
              </Button>
            </Form>
            <p className="login-account-hint">{t('setup.loginHint')}</p>
            <p className="login-security">
              <LockOutlined /> {t('login.sessionHint')}
            </p>
          </Card>
        </main>
        <SiteFooter />
      </div>
    </AppearanceProvider>
  );
}
