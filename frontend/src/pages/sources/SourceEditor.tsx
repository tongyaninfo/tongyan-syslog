import {
  Alert,
  AutoComplete,
  Button,
  Descriptions,
  Form,
  Input,
  InputNumber,
  Modal,
  Select,
  Space,
  Switch,
} from 'antd';
import { useEffect, useRef, useState } from 'react';
import { SeverityTag } from '@/components/LogTags';
import { ApiError, getErrorKey } from '@/requestErrorConfig';
import { createSource, getSources, updateSource } from '@/services/syslog/api';
import type { LogSource, SourceInput } from '@/services/syslog/types';
import { useText } from '@/utils/i18n';

interface SourceEditorProps {
  open: boolean;
  source?: LogSource;
  retentionOnly?: boolean;
  onCancel: () => void;
  onSaved: (source: LogSource) => void;
}

const zones = [
  'Asia/Shanghai',
  'UTC',
  'Asia/Tokyo',
  'Asia/Hong_Kong',
  'Europe/London',
  'America/New_York',
];
const defaultInput: SourceInput = {
  name: '',
  peerAddress: '',
  timeZoneId: 'Asia/Shanghai',
  retentionDays: 365,
  enabled: true,
};

function toInput(source: LogSource): SourceInput {
  return {
    name: source.name,
    peerAddress: source.peerAddress,
    timeZoneId: source.timeZoneId,
    retentionDays: source.retentionDays,
    enabled: source.enabled,
  };
}

export default function SourceEditor({
  open,
  source,
  retentionOnly = false,
  onCancel,
  onSaved,
}: SourceEditorProps) {
  const t = useText();
  const [form] = Form.useForm<SourceInput>();
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string>();
  const [pending, setPending] = useState<SourceInput>();
  const submitting = useRef(false);
  const mounted = useRef(true);

  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);

  useEffect(() => {
    if (!open) return;
    form.setFieldsValue({
      ...(source ? toInput(source) : defaultInput),
      ignoredSeverities: source?.ignoredSeverities ?? [],
    });
    setError(undefined);
    setPending(undefined);
  }, [form, open, source]);

  const commit = async (input: SourceInput) => {
    if (submitting.current) return;
    submitting.current = true;
    setSaving(true);
    setError(undefined);
    try {
      let submitted = input;
      if (retentionOnly && source) {
        // 保留设置只修改天数；读取最新来源，避免用旧页面快照覆盖名称、时区或接收开关。
        const latest = (await getSources()).find(
          (item) => item.id === source.id,
        );
        if (!latest || latest.retentionDays !== source.retentionDays) {
          if (mounted.current) {
            setError('source.sourceChanged');
            setPending(undefined);
          }
          return;
        }
        submitted = { ...toInput(latest), retentionDays: input.retentionDays };
      }
      const result = source
        ? await updateSource(source.id, submitted)
        : await createSource(submitted);
      if (mounted.current) onSaved(result);
    } catch (failure) {
      if (mounted.current) {
        setPending(undefined);
        setError(
          failure instanceof ApiError &&
            failure.code === 'source_filter_migration_required'
            ? 'source.ignoreMigrationRequired'
            : getErrorKey(failure),
        );
      }
    } finally {
      submitting.current = false;
      if (mounted.current) setSaving(false);
    }
  };

  const submit = async () => {
    if (submitting.current) return;
    try {
      const { ignoredSeverities, ...values } = await form.validateFields();
      const base = retentionOnly && source ? toInput(source) : values;
      const previousSeverities = source?.ignoredSeverities ?? [];
      const selectedSeverities = [...(ignoredSeverities ?? [])].sort(
        (left, right) => left - right,
      );
      // 未改忽略规则时省略该字段，避免保留期编辑或旧表单覆盖已有规则。
      const changedSeverities =
        !retentionOnly &&
        (previousSeverities.length !== selectedSeverities.length ||
          selectedSeverities.some(
            (value) => !previousSeverities.includes(value),
          ));
      const input: SourceInput = {
        ...base,
        name: base.name.trim(),
        peerAddress: base.peerAddress.trim(),
        timeZoneId: base.timeZoneId.trim(),
        retentionDays: values.retentionDays,
        ...(changedSeverities ? { ignoredSeverities: selectedSeverities } : {}),
      };
      if (
        changedSeverities ||
        (source && input.retentionDays !== source.retentionDays)
      ) {
        setPending(input);
        return;
      }
      await commit(input);
    } catch {
      // 表单校验错误由字段展示，不把校验对象当作服务故障。
    }
  };

  const pendingSeverities = pending?.ignoredSeverities;
  const pendingRetentionChange =
    pending && source && pending.retentionDays !== source.retentionDays;

  return (
    <>
      <Modal
        title={t(
          retentionOnly
            ? 'retention.edit'
            : source
              ? 'source.edit'
              : 'source.add',
        )}
        open={open}
        onCancel={() => {
          if (!saving) onCancel();
        }}
        onOk={() => void submit()}
        okText={t('source.save')}
        cancelText={t('source.cancel')}
        confirmLoading={saving}
        closable={!saving}
        keyboard={!saving}
        mask={{ closable: !saving }}
        width={560}
        destroyOnHidden
      >
        {retentionOnly && source && (
          <Descriptions
            size="small"
            column={1}
            style={{ marginBottom: 20 }}
            items={[
              {
                key: 'name',
                label: t('retention.source'),
                children: source.name,
              },
              {
                key: 'ip',
                label: t('source.address'),
                children: source.peerAddress,
              },
            ]}
          />
        )}
        {error && (
          <Alert
            type="error"
            showIcon
            title={t('source.saveFailed')}
            description={t(error)}
            style={{ marginBottom: 16 }}
          />
        )}
        <Form
          form={form}
          layout="vertical"
          preserve={false}
          disabled={saving || Boolean(pending)}
          onFinish={() => void submit()}
        >
          {!retentionOnly && (
            <>
              <Form.Item
                name="name"
                label={t('source.name')}
                rules={[
                  {
                    required: true,
                    whitespace: true,
                    message: t('source.required'),
                  },
                  { max: 128, message: t('source.nameMax') },
                ]}
              >
                <Input
                  maxLength={128}
                  placeholder={t('source.namePlaceholder')}
                  autoComplete="off"
                />
              </Form.Item>
              <Form.Item
                name="peerAddress"
                label={t('source.address')}
                rules={[
                  {
                    required: true,
                    whitespace: true,
                    message: t('source.required'),
                  },
                  {
                    pattern: /^[0-9a-fA-F:.%]+$/,
                    max: 64,
                    message: t('source.addressInvalid'),
                  },
                ]}
              >
                <Input
                  placeholder={t('source.addressPlaceholder')}
                  autoComplete="off"
                />
              </Form.Item>
              <Form.Item
                name="timeZoneId"
                label={t('source.timezone')}
                extra={t('source.timezoneHint')}
                rules={[
                  {
                    required: true,
                    whitespace: true,
                    message: t('source.required'),
                  },
                  { max: 128, message: t('source.zoneMax') },
                ]}
              >
                <AutoComplete
                  options={zones.map((zone) => ({ value: zone }))}
                  placeholder={t('source.timezonePlaceholder')}
                  filterOption={(input, option) =>
                    String(option?.value ?? '')
                      .toLowerCase()
                      .includes(input.toLowerCase())
                  }
                />
              </Form.Item>
              <Form.Item
                name="ignoredSeverities"
                label={t('source.ignoredSeverities')}
                extra={t('source.ignoreScope')}
              >
                <Select<number[]>
                  mode="multiple"
                  allowClear
                  showSearch={{ optionFilterProp: 'label' }}
                  placeholder={t('source.ignoreNone')}
                  options={Array.from({ length: 8 }, (_, value) => ({
                    value,
                    label: `${value} · ${t(`severity.${value}`)}`,
                  }))}
                />
              </Form.Item>
            </>
          )}
          <Form.Item
            name="retentionDays"
            label={t('source.retention')}
            extra={t('retention.hint')}
            rules={[
              { required: true, message: t('retention.range') },
              {
                type: 'integer',
                min: 1,
                max: 3650,
                message: t('retention.range'),
              },
            ]}
          >
            <InputNumber
              suffix={t('retention.unit')}
              style={{ width: '100%' }}
            />
          </Form.Item>
          <Space wrap style={{ marginBottom: 24 }}>
            {[365, 730, 1095].map((days, index) => (
              <Button
                key={days}
                size="small"
                disabled={saving || Boolean(pending)}
                onClick={() => form.setFieldValue('retentionDays', days)}
              >
                {t(`retention.year${index + 1}`)}
              </Button>
            ))}
          </Space>
          {!retentionOnly && (
            <Form.Item
              name="enabled"
              label={t('source.state')}
              valuePropName="checked"
              extra={t('source.stateHint')}
            >
              <Switch aria-label={t('source.enabledSwitch')} />
            </Form.Item>
          )}
        </Form>
      </Modal>
      <Modal
        title={t(
          pendingSeverities == null
            ? 'retention.confirmTitle'
            : 'source.ignoreConfirmTitle',
        )}
        open={Boolean(pending)}
        onOk={() => {
          if (pending) void commit(pending);
        }}
        onCancel={() => {
          if (!saving) setPending(undefined);
        }}
        okText={t('source.saveConfirmed')}
        cancelText={t('source.back')}
        confirmLoading={saving}
        closable={!saving}
        keyboard={!saving}
        mask={{ closable: !saving }}
        width={480}
      >
        {pendingRetentionChange && (
          <>
            <p>
              {t('retention.confirmDescription', {
                name: source.name,
                before: source.retentionDays,
                after: pending.retentionDays,
              })}
            </p>
            <Alert type="info" showIcon title={t('retention.confirmScope')} />
          </>
        )}
        {pendingSeverities != null && (
          <>
            <Descriptions
              size="small"
              column={1}
              style={{ margin: '12px 0' }}
              items={[
                {
                  key: 'ignoredSeverities',
                  label: t('source.ignoredSeverities'),
                  children: pendingSeverities.length ? (
                    <Space wrap>
                      {pendingSeverities.map((value) => (
                        <SeverityTag key={value} value={value} />
                      ))}
                    </Space>
                  ) : (
                    t('source.ignoreNone')
                  ),
                },
              ]}
            />
            <Alert type="info" showIcon title={t('source.ignoreScope')} />
          </>
        )}
      </Modal>
    </>
  );
}
