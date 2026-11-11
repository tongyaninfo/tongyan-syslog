import {
  Alert,
  Button,
  Card,
  Checkbox,
  Form,
  Input,
  InputNumber,
  Select,
  Space,
  Typography,
} from 'antd';
import { useEffect, useRef, useState } from 'react';
import {
  getSmtpSettings,
  saveSmtpSettings,
  sendTestMail,
} from '@/services/syslog/notifications';
import { useText } from '@/utils/i18n';
import { getNotificationError, useNotificationQuery } from './common';
import styles from './styles.module.less';

interface SettingsForm {
  host: string;
  port: number;
  useTls: boolean;
  mode: 'ssl' | 'starttls';
  username?: string;
  password?: string;
  senderAddress: string;
  senderName?: string;
  subjectPrefix?: string;
}

const defaultPrefix = '[通研 Syslog]';
const testSubject = 'SMTP 测试邮件';

export default function SmtpSettings({
  onViewDelivery,
}: {
  onViewDelivery: (id: string) => void;
}) {
  const t = useText();
  const [form] = Form.useForm<SettingsForm>();
  const { data, setData, loading, error, reload } =
    useNotificationQuery(getSmtpSettings);
  const useTls = Form.useWatch('useTls', form) ?? true;
  const subjectPrefix = Form.useWatch('subjectPrefix', form) ?? defaultPrefix;
  const settingsLocked = useRef(false);
  const testLocked = useRef(false);
  const [saving, setSaving] = useState(false);
  const [testing, setTesting] = useState(false);
  const [saveResult, setSaveResult] = useState<{
    success: boolean;
    key: string;
  }>();
  const [testError, setTestError] = useState<string>();
  const [testDelivery, setTestDelivery] = useState<string>();

  useEffect(() => {
    if (!data) return;
    form.setFieldsValue({
      host: data.host,
      port: data.port,
      useTls: data.security !== 'none',
      mode: data.security === 'starttls' ? 'starttls' : 'ssl',
      username: data.username,
      senderAddress: data.senderAddress,
      senderName: data.senderName,
      subjectPrefix: data.subjectPrefix,
      password: '',
    });
  }, [data, form]);

  async function save(values: SettingsForm) {
    if (!data || error || settingsLocked.current) return;
    settingsLocked.current = true;
    setSaving(true);
    setSaveResult(undefined);
    const username = values.useTls ? (values.username?.trim() ?? '') : '';
    try {
      const updated = await saveSmtpSettings({
        version: data.version,
        host: values.host.trim(),
        port: values.port,
        security: values.useTls ? values.mode : 'none',
        username,
        senderAddress: values.senderAddress.trim(),
        senderName: values.senderName?.trim() ?? '',
        subjectPrefix: values.subjectPrefix?.trim() ?? '',
        // 空值沿用原授权码。授权码只从当前表单写入，不由读取接口回显。
        ...(username && values.password ? { password: values.password } : {}),
      });
      setData(updated);
      setSaveResult({ success: true, key: 'notifications.smtpSaved' });
    } catch (failure) {
      setSaveResult({ success: false, key: getNotificationError(failure) });
    } finally {
      form.setFieldValue('password', '');
      setSaving(false);
      settingsLocked.current = false;
    }
  }

  async function send({ recipient }: { recipient: string }) {
    if (testLocked.current) return;
    testLocked.current = true;
    setTesting(true);
    setTestError(undefined);
    setTestDelivery(undefined);
    try {
      const result = await sendTestMail(recipient.trim());
      setTestDelivery(result.id);
    } catch (failure) {
      setTestError(getNotificationError(failure));
    } finally {
      setTesting(false);
      testLocked.current = false;
    }
  }

  const required = { required: true, message: t('error.input') };
  const requiredText = { ...required, whitespace: true };
  const subject = subjectPrefix.trim();
  return (
    <div className={styles.stack}>
      <Card
        title={t('notifications.smtp')}
        loading={loading && !data}
        extra={
          <Button
            disabled={saving}
            loading={loading}
            onClick={() => {
              setSaveResult(undefined);
              reload();
            }}
          >
            {t('app.retry')}
          </Button>
        }
      >
        {error && (
          <Alert
            type="error"
            showIcon
            title={t(error)}
            className={styles.notice}
          />
        )}
        {saveResult && (
          <Alert
            type={saveResult.success ? 'success' : 'error'}
            showIcon
            title={t(saveResult.key)}
            className={styles.notice}
          />
        )}
        {data && (
          <Form
            form={form}
            name="smtpSettings"
            layout="vertical"
            className={styles.form}
            initialValues={{
              useTls: true,
              mode: 'ssl',
              port: 465,
              subjectPrefix: defaultPrefix,
            }}
            onFinish={save}
            disabled={loading || saving || Boolean(error)}
          >
            <div className={styles.fields}>
              <Form.Item
                name="host"
                label={t('notifications.host')}
                rules={[requiredText]}
              >
                <Input
                  maxLength={253}
                  placeholder="smtp.example.com"
                  autoComplete="off"
                />
              </Form.Item>
              <Form.Item
                name="port"
                label={t('notifications.port')}
                rules={[required]}
              >
                <InputNumber
                  className={styles.fieldWidth}
                  min={1}
                  max={65535}
                  precision={0}
                />
              </Form.Item>
            </div>
            <Form.Item name="useTls" valuePropName="checked">
              <Checkbox>{t('notifications.useTls')}</Checkbox>
            </Form.Item>
            {useTls ? (
              <Form.Item
                name="mode"
                label={t('notifications.security')}
                extra={t('notifications.securityHint')}
                rules={[required]}
              >
                <Select
                  options={[
                    { value: 'ssl', label: 'SSL/TLS' },
                    { value: 'starttls', label: 'STARTTLS' },
                  ]}
                />
              </Form.Item>
            ) : (
              <Alert
                type="warning"
                showIcon
                title={t('notifications.unencrypted')}
                className={styles.notice}
              />
            )}
            <div className={styles.fields}>
              <Form.Item name="username" label={t('notifications.username')}>
                <Input maxLength={254} disabled={!useTls} autoComplete="off" />
              </Form.Item>
              <Form.Item
                name="password"
                label={t('notifications.password')}
                extra={t(
                  data.hasPassword
                    ? 'notifications.keepPassword'
                    : 'notifications.passwordHint',
                )}
              >
                <Input.Password
                  maxLength={1024}
                  disabled={!useTls}
                  autoComplete="new-password"
                />
              </Form.Item>
              <Form.Item
                name="senderAddress"
                label={t('notifications.senderAddress')}
                rules={[
                  required,
                  { type: 'email', message: t('notifications.invalidAddress') },
                ]}
              >
                <Input maxLength={254} inputMode="email" autoComplete="off" />
              </Form.Item>
              <Form.Item
                name="senderName"
                label={t('notifications.senderName')}
              >
                <Input maxLength={80} />
              </Form.Item>
            </div>
            <Form.Item
              name="subjectPrefix"
              label={t('notifications.subjectPrefix')}
              extra={t('notifications.subjectPrefixHint')}
            >
              <Input maxLength={80} />
            </Form.Item>
            <Alert
              className={styles.notice}
              type="info"
              title={t('notifications.subjectPreview')}
              description={`${subject ? `${subject} ` : ''}${testSubject}`}
            />
            <Button type="primary" htmlType="submit" loading={saving}>
              {t('app.save')}
            </Button>
          </Form>
        )}
      </Card>
      <Card title={t('notifications.testMail')}>
        <Typography.Paragraph type="secondary">
          {t('notifications.testHint')}
        </Typography.Paragraph>
        {testError && (
          <Alert
            className={styles.notice}
            type="error"
            showIcon
            title={t(testError)}
          />
        )}
        {testDelivery && (
          <Alert
            className={styles.notice}
            type="success"
            showIcon
            title={t('notifications.testQueued')}
            action={
              <Button size="small" onClick={() => onViewDelivery(testDelivery)}>
                {t('notifications.history')}
              </Button>
            }
          />
        )}
        <Form
          name="smtpTest"
          layout="vertical"
          className={styles.form}
          onFinish={send}
          disabled={
            loading ||
            saving ||
            testing ||
            Boolean(error) ||
            !data?.host ||
            !data.senderAddress
          }
        >
          <Form.Item
            name="recipient"
            label={t('notifications.recipient')}
            rules={[
              required,
              { type: 'email', message: t('notifications.invalidAddress') },
            ]}
          >
            <Input
              maxLength={254}
              inputMode="email"
              placeholder="user@example.com"
            />
          </Form.Item>
          <Space>
            <Button htmlType="submit" loading={testing}>
              {t('notifications.sendTest')}
            </Button>
          </Space>
        </Form>
      </Card>
    </div>
  );
}
