import { Alert, Form, Modal } from 'antd';
import { useRef, useState } from 'react';
import {
  isSyntheticPreview,
  PREVIEW_NEW_PASSWORD,
} from '@/components/Shell/preview';
import { NewPasswordFields, UsernameField } from '@/components/UserCredentials';
import { getErrorKey } from '@/requestErrorConfig';
import {
  createUser,
  resetUserPassword,
  updateUser,
} from '@/services/syslog/api';
import type { ManagedUser, UserInput } from '@/services/syslog/types';
import { useText } from '@/utils/i18n';

export interface UserEditorProps {
  mode: 'create' | 'edit' | 'password';
  user?: ManagedUser;
  onClose: () => void;
  onSaved: (user?: ManagedUser) => void | Promise<void>;
}

export default function UserEditor({
  mode,
  user,
  onClose,
  onSaved,
}: UserEditorProps) {
  const t = useText();
  const [form] = Form.useForm<UserInput & { confirmPassword: string }>();
  const [pending, setPending] = useState(false);
  const submitting = useRef(false);
  const [error, setError] = useState<string>();
  const [confirming, setConfirming] = useState(false);

  async function save(input: UserInput) {
    if (submitting.current) return;
    if (mode === 'password' && !confirming) {
      setConfirming(true);
      return;
    }
    submitting.current = true;
    setPending(true);
    setError(undefined);
    try {
      // 合成预览不传输用户填写的口令，仅发送公开的固定样本。
      const password = isSyntheticPreview()
        ? PREVIEW_NEW_PASSWORD
        : input.password;
      let updated: ManagedUser | undefined;
      if (mode === 'password' && user)
        await resetUserPassword(user.id, password, user.version);
      else if (mode === 'edit' && user)
        updated = await updateUser(user.id, {
          username: input.username.trim(),
          enabled: user.enabled,
          expectedVersion: user.version,
        });
      else
        updated = await createUser({
          username: input.username.trim(),
          password,
        });
      form.resetFields();
      await onSaved(updated);
    } catch (failure) {
      setError(getErrorKey(failure));
      setConfirming(false);
    } finally {
      submitting.current = false;
      setPending(false);
    }
  }

  return (
    <Modal
      open
      title={t(
        mode === 'create'
          ? 'users.add'
          : mode === 'edit'
            ? 'users.edit'
            : confirming
              ? 'users.resetTitle'
              : 'users.resetPassword',
      )}
      okText={t(confirming ? 'users.confirm' : 'app.save')}
      cancelText={t(confirming ? 'users.back' : 'app.cancel')}
      confirmLoading={pending}
      closable={!pending}
      keyboard={!pending}
      mask={{ closable: !pending }}
      cancelButtonProps={{ disabled: pending }}
      onOk={() => form.submit()}
      onCancel={() => {
        if (!submitting.current) {
          if (confirming) setConfirming(false);
          else onClose();
        }
      }}
    >
      <p>
        {t(
          mode === 'edit'
            ? 'users.editHint'
            : mode === 'password'
              ? 'users.resetHint'
              : 'users.permissionHint',
          { username: user?.username ?? '' },
        )}
      </p>
      {isSyntheticPreview() && mode !== 'edit' && (
        <Alert type="info" showIcon title={t('users.previewHint')} />
      )}
      {error && (
        <Alert
          className="syslog-password-error"
          type="error"
          showIcon
          title={t('users.saveFailed')}
          description={t(error)}
        />
      )}
      <Form
        form={form}
        layout="vertical"
        preserve={false}
        initialValues={{ username: user?.username }}
        onFinish={save}
        disabled={pending || confirming}
        style={{ marginTop: 20 }}
      >
        {mode !== 'password' && (
          <UsernameField
            originalUsername={mode === 'edit' ? user?.username : undefined}
          />
        )}
        {mode !== 'edit' && <NewPasswordFields />}
      </Form>
    </Modal>
  );
}
