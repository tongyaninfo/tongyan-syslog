import { Form, Input } from 'antd';
import { useText } from '@/utils/i18n';

export function UsernameField({
  originalUsername,
}: {
  originalUsername?: string;
}) {
  const t = useText();
  return (
    <Form.Item
      name="username"
      label={t('users.username')}
      normalize={(value: string) => value.trim()}
      rules={[
        { required: true, message: t('login.usernameRequired') },
        {
          // 旧版允许 Unicode 名称，未改名时保留兼容，新建和改名遵循当前规则。
          validator: (_, value: string | undefined) =>
            !value ||
            value === originalUsername ||
            /^[A-Za-z0-9._-]{3,64}$/.test(value)
              ? Promise.resolve()
              : Promise.reject(new Error(t('users.usernameRule'))),
        },
      ]}
    >
      <Input maxLength={64} autoComplete="username" />
    </Form.Item>
  );
}

export function NewPasswordFields() {
  const t = useText();
  return (
    <>
      <Form.Item
        name="password"
        label={t('login.password')}
        extra={t('users.passwordRule')}
        rules={[
          { required: true, message: t('app.passwordRequired') },
          {
            validator: (_, value: string | undefined) =>
              !value ||
              (value.trim() && value.length >= 8 && value.length <= 1024)
                ? Promise.resolve()
                : Promise.reject(new Error(t('users.passwordRule'))),
          },
        ]}
      >
        <Input.Password maxLength={1024} autoComplete="new-password" />
      </Form.Item>
      <Form.Item
        name="confirmPassword"
        label={t('users.confirmPassword')}
        dependencies={['password']}
        rules={[
          { required: true, message: t('app.passwordRequired') },
          ({ getFieldValue }) => ({
            validator: (_, value) =>
              !value || value === getFieldValue('password')
                ? Promise.resolve()
                : Promise.reject(new Error(t('users.passwordMismatch'))),
          }),
        ]}
      >
        <Input.Password maxLength={1024} autoComplete="new-password" />
      </Form.Item>
    </>
  );
}
