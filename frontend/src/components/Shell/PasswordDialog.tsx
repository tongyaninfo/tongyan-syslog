import { Alert, Form, Input, Modal } from 'antd';
import { useRef, useState } from 'react';
import { ApiError, getErrorKey } from '@/requestErrorConfig';
import { changePassword } from '@/services/syslog/api';
import { useText } from '@/utils/i18n';
import {
  isSyntheticPreview,
  PREVIEW_NEW_PASSWORD,
  PREVIEW_PASSWORD,
} from './preview';

interface PasswordFields {
  currentPassword: string;
  newPassword: string;
  confirmPassword: string;
}

export default function PasswordDialog({
  onClose,
  onChanged,
}: {
  onClose: () => void;
  onChanged: () => Promise<void>;
}) {
  const t = useText();
  const [form] = Form.useForm<PasswordFields>();
  const [pending, setPending] = useState(false);
  const submitting = useRef(false);
  const [errorKey, setErrorKey] = useState<string>();

  async function submit({ currentPassword, newPassword }: PasswordFields) {
    if (submitting.current) return;
    submitting.current = true;
    setPending(true);
    setErrorKey(undefined);
    try {
      if (isSyntheticPreview()) {
        if (currentPassword !== PREVIEW_PASSWORD)
          throw new ApiError('current_password_invalid', 400);
        if (newPassword === currentPassword)
          throw new ApiError('password_unchanged', 400);
        await changePassword(PREVIEW_PASSWORD, PREVIEW_NEW_PASSWORD);
      } else {
        await changePassword(currentPassword, newPassword);
      }
      form.resetFields();
      await onChanged();
    } catch (error) {
      form.resetFields(['currentPassword']);
      setErrorKey(getErrorKey(error));
    } finally {
      submitting.current = false;
      setPending(false);
    }
  }

  return (
    <Modal
      open
      title={t('app.changePassword')}
      okText={t('app.save')}
      cancelText={t('app.cancel')}
      confirmLoading={pending}
      closable={!pending}
      keyboard={!pending}
      mask={{ closable: !pending }}
      cancelButtonProps={{ disabled: pending }}
      onOk={() => form.submit()}
      onCancel={() => {
        if (!submitting.current) onClose();
      }}
    >
      <p>
        {t(
          isSyntheticPreview() ? 'app.passwordPreviewHint' : 'app.passwordHint',
        )}
      </p>
      {errorKey && (
        <Alert
          className="syslog-password-error"
          type="error"
          showIcon
          title={t(errorKey)}
        />
      )}
      <Form
        form={form}
        layout="vertical"
        preserve={false}
        onFinish={submit}
        disabled={pending}
      >
        <Form.Item
          name="currentPassword"
          label={t('app.currentPassword')}
          rules={[{ required: true, message: t('app.passwordRequired') }]}
        >
          <Input.Password maxLength={1024} autoComplete="current-password" />
        </Form.Item>
        <Form.Item
          name="newPassword"
          label={t('app.newPassword')}
          dependencies={['currentPassword']}
          rules={[
            { required: true, message: t('app.passwordRequired') },
            ({ getFieldValue }) => ({
              validator: (_, value: string | undefined) => {
                if (!value) return Promise.resolve();
                // 与浏览器 maxLength 和服务端一致，按 UTF-16 长度检查。
                if (!value.trim() || value.length < 8 || value.length > 1024)
                  return Promise.reject(new Error(t('app.passwordLength')));
                if (value === getFieldValue('currentPassword'))
                  return Promise.reject(
                    new Error(t('error.passwordUnchanged')),
                  );
                return Promise.resolve();
              },
            }),
          ]}
        >
          <Input.Password maxLength={1024} autoComplete="new-password" />
        </Form.Item>
        <Form.Item
          name="confirmPassword"
          label={t('app.confirmPassword')}
          dependencies={['newPassword']}
          rules={[
            { required: true, message: t('app.passwordRequired') },
            ({ getFieldValue }) => ({
              validator: (_, value) =>
                !value || getFieldValue('newPassword') === value
                  ? Promise.resolve()
                  : Promise.reject(new Error(t('app.passwordMismatch'))),
            }),
          ]}
        >
          <Input.Password maxLength={1024} autoComplete="new-password" />
        </Form.Item>
      </Form>
    </Modal>
  );
}
