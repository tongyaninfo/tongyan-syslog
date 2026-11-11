import { Alert, Form, InputNumber, Modal, Typography } from 'antd';
import { useEffect, useState } from 'react';
import {
  getLiveLogSettings,
  type LiveLogSettingsView,
  saveLiveLogSettings,
} from '@/services/syslog/liveLogs';
import { useText } from '@/utils/i18n';
import { getLiveLogError } from './helpers';

export default function SettingsModal({
  open,
  onClose,
  onSaved,
}: {
  open: boolean;
  onClose: () => void;
  onSaved: () => void;
}) {
  const t = useText();
  const [form] = Form.useForm<{ perDeviceLimit: number; maxMiB: number }>();
  const [current, setCurrent] = useState<LiveLogSettingsView | null>(null);
  const [error, setError] = useState<unknown>(null);
  const [busy, setBusy] = useState(false);
  const perDeviceLimit = Form.useWatch('perDeviceLimit', form);
  const maxMiB = Form.useWatch('maxMiB', form);
  useEffect(() => {
    if (!open) return;
    const controller = new AbortController();
    setCurrent(null);
    setError(null);
    setBusy(true);
    getLiveLogSettings(controller.signal)
      .then((value) => {
        if (controller.signal.aborted) return;
        setCurrent(value);
        form.setFieldsValue({
          perDeviceLimit: value.settings.perDeviceLimit,
          maxMiB: value.settings.maxBytes / 1048576,
        });
      })
      .catch((failure) => {
        if (!controller.signal.aborted) setError(failure);
      })
      .finally(() => {
        if (!controller.signal.aborted) setBusy(false);
      });
    return () => controller.abort();
  }, [open, form]);
  const save = async () => {
    if (!current) return;
    let values: { perDeviceLimit: number; maxMiB: number };
    try {
      values = await form.validateFields();
    } catch {
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await saveLiveLogSettings({
        version: current.settings.version,
        perDeviceLimit: values.perDeviceLimit,
        maxBytes: Math.round(values.maxMiB * 1048576),
      });
      onSaved();
      onClose();
    } catch (failure) {
      setError(failure);
    } finally {
      setBusy(false);
    }
  };
  return (
    <Modal
      open={open}
      title={t('live.settings')}
      onCancel={onClose}
      onOk={() => void save()}
      confirmLoading={busy}
      okButtonProps={{ disabled: !current }}
      okText={t('app.save')}
      cancelText={t('app.cancel')}
      destroyOnHidden
    >
      {error !== null && (
        <Alert
          type="error"
          showIcon
          title={t(getLiveLogError(error))}
          style={{ marginBottom: 16 }}
        />
      )}
      <Typography.Paragraph type="secondary">
        {t('live.settingsHint')}
      </Typography.Paragraph>
      <Form form={form} layout="vertical" disabled={busy || !current}>
        <Form.Item
          name="perDeviceLimit"
          label={t('live.perDeviceLimit')}
          rules={[
            {
              required: true,
              type: 'integer',
              min: 100,
              max: 50000,
              message: t('live.limitValidation'),
            },
          ]}
        >
          <InputNumber
            min={100}
            max={50000}
            precision={0}
            style={{ width: '100%' }}
          />
        </Form.Item>
        <Form.Item
          name="maxMiB"
          label={`${t('live.memoryLimit')} (MiB)`}
          rules={[
            {
              required: true,
              type: 'number',
              min: 16,
              max: 2048,
              message: t('live.memoryValidation'),
            },
          ]}
        >
          <InputNumber
            min={16}
            max={2048}
            precision={0}
            style={{ width: '100%' }}
          />
        </Form.Item>
      </Form>
      {current &&
        (perDeviceLimit < current.settings.perDeviceLimit ||
          maxMiB * 1048576 < current.settings.maxBytes) && (
          <Alert type="warning" showIcon title={t('live.shrinkHint')} />
        )}
    </Modal>
  );
}
