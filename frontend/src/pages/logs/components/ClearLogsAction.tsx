import { DeleteOutlined } from '@ant-design/icons';
import { Alert, Button, Input, Modal, message, Space } from 'antd';
import { useEffect, useRef, useState } from 'react';
import { ApiError, getErrorKey } from '@/requestErrorConfig';
import { clearLogs, clearSourceLogs } from '@/services/syslog/api';
import type { LogSource } from '@/services/syslog/types';
import { useText } from '@/utils/i18n';

const confirmationWord = 'CLEAR';
const errorKeys = new Map([
  ['log_clear_confirmation_required', 'logs.clearConfirmation'],
  ['log_clear_in_progress', 'logs.clearInProgress'],
  ['log_clear_size_limit', 'logs.clearSizeLimit'],
  ['log_clear_outcome_unknown', 'logs.clearOutcomeUnknown'],
  ['request_timeout', 'logs.clearOutcomeUnknown'],
  ['storage_timeout', 'logs.clearOutcomeUnknown'],
  ['network_unavailable', 'logs.clearOutcomeUnknown'],
  ['invalid_response', 'logs.clearOutcomeUnknown'],
  ['storage_connection_failed', 'logs.error.storageConnection'],
  ['storage_schema_missing', 'logs.error.storageSchema'],
  ['storage_schema_incompatible', 'logs.error.storageSchemaIncompatible'],
  ['storage_access_denied', 'logs.clearStorageAccess'],
  ['source_not_found', 'error.notFound'],
]);

function clearErrorKey(error: unknown): string {
  if (error instanceof ApiError) {
    const specific = errorKeys.get(error.code);
    if (specific) return specific;
  }
  const fallback = getErrorKey(error);
  return fallback === 'error.generic' ? 'logs.clearFailed' : fallback;
}

export default function ClearLogsAction({
  onCleared,
}: {
  onCleared: () => void;
}) {
  const t = useText();
  const [open, setOpen] = useState(false);
  return (
    <>
      <Button
        danger
        icon={<DeleteOutlined aria-hidden />}
        onClick={() => setOpen(true)}
      >
        {t('logs.clearAction')}
      </Button>
      <ClearLogsDialog
        target={open ? { kind: 'all' } : undefined}
        onDismiss={() => setOpen(false)}
        onCleared={onCleared}
      />
    </>
  );
}

export function ClearLogsDialog({
  target,
  onDismiss,
  onCleared,
}: {
  target?: { kind: 'all' } | { kind: 'source'; source: LogSource };
  onDismiss: () => void;
  onCleared?: () => void;
}) {
  const t = useText();
  const [notice, contextHolder] = message.useMessage();
  const [confirmation, setConfirmation] = useState('');
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string>();
  const submitting = useRef(false);
  const mounted = useRef(true);

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  const source = target?.kind === 'source' ? target.source : undefined;

  function dismiss() {
    setConfirmation('');
    setError(undefined);
    onDismiss();
  }

  async function submit() {
    if (!target || confirmation !== confirmationWord || submitting.current)
      return;
    submitting.current = true;
    setPending(true);
    setError(undefined);
    try {
      if (source) await clearSourceLogs(source.id, confirmation);
      else await clearLogs(confirmation);
    } catch (failure) {
      if (!mounted.current) return;
      // 网络中断可能发生在清空完成之后。再次提交必须重新输入确认词。
      setConfirmation('');
      setError(clearErrorKey(failure));
      return;
    } finally {
      submitting.current = false;
      if (mounted.current) setPending(false);
    }
    if (!mounted.current) return;
    dismiss();
    void notice.success(
      source
        ? t('logs.clearSourceSucceeded', { name: source.name })
        : t('logs.clearSucceeded'),
    );
    onCleared?.();
  }

  return (
    <>
      {contextHolder}
      <Modal
        open={target !== undefined}
        title={t(source ? 'logs.clearSourceTitle' : 'logs.clearTitle')}
        okText={t('logs.clearSubmit')}
        cancelText={t('app.cancel')}
        okButtonProps={{
          danger: true,
          disabled: confirmation !== confirmationWord,
        }}
        cancelButtonProps={{ disabled: pending }}
        confirmLoading={pending}
        closable={!pending}
        keyboard={!pending}
        mask={{ closable: false }}
        onCancel={() => {
          if (!submitting.current) dismiss();
        }}
        onOk={submit}
      >
        <Space orientation="vertical" style={{ width: '100%' }}>
          <Alert
            type="warning"
            showIcon
            title={
              source
                ? t('logs.clearSourceScope', {
                    name: source.name,
                    address: source.peerAddress,
                  })
                : t('logs.clearScope')
            }
            description={t('logs.clearPendingLogs')}
          />
          <Input
            aria-label={t('logs.clearConfirmation')}
            placeholder={t('logs.clearConfirmation')}
            autoComplete="off"
            spellCheck={false}
            disabled={pending}
            value={confirmation}
            onChange={(event) => setConfirmation(event.target.value)}
          />
          {error && <Alert type="error" showIcon title={t(error)} />}
        </Space>
      </Modal>
    </>
  );
}
