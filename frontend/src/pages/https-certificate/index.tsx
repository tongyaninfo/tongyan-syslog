import { ReloadOutlined, UploadOutlined } from '@ant-design/icons';
import { PageContainer } from '@ant-design/pro-components';
import {
  Alert,
  Button,
  Card,
  Descriptions,
  Form,
  Input,
  Modal,
  Space,
  Typography,
} from 'antd';
import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError, getErrorKey } from '@/requestErrorConfig';
import {
  getHttpsCertificateSettings,
  uploadHttpsCertificate,
} from '@/services/syslog/api';
import type {
  HttpsCertificateSettings,
  HttpsCertificateSummary,
} from '@/services/syslog/types';
import { formatTime } from '@/utils/format';
import { useText } from '@/utils/i18n';
import styles from './styles.module.less';

const maxFileBytes = 256 * 1024;
const maxPasswordLength = 1024;
const maxPasswordFileBytes = 4 * 1024;
const passwordFileReadTimeoutMs = 5000;
const certificateErrors = new Map([
  ['https_certificate_invalid', 'httpsCertificate.invalid'],
  ['https_certificate_unavailable', 'httpsCertificate.unavailable'],
  ['service_unavailable', 'httpsCertificate.unavailable'],
  ['https_certificate_too_large', 'httpsCertificate.tooLarge'],
  [
    'https_certificate_managed_externally',
    'httpsCertificate.managedExternally',
  ],
]);

interface CertificateForm {
  password?: string;
}

function CertificateDetails({
  title,
  value,
  empty,
  loading,
}: {
  title: string;
  value?: HttpsCertificateSummary | null;
  empty: string;
  loading: boolean;
}) {
  const t = useText();
  return (
    <Card title={title} loading={loading}>
      {value ? (
        <Descriptions
          column={1}
          size="small"
          items={[
            {
              key: 'subject',
              label: t('httpsCertificate.subject'),
              children: value.subject || '—',
            },
            {
              key: 'fingerprint',
              label: t('httpsCertificate.fingerprint'),
              children: (
                <span className={styles.fingerprint}>{value.fingerprint}</span>
              ),
            },
            {
              key: 'notBefore',
              label: t('httpsCertificate.notBefore'),
              children: formatTime(value.notBefore),
            },
            {
              key: 'notAfter',
              label: t('httpsCertificate.notAfter'),
              children: formatTime(value.notAfter),
            },
          ]}
        />
      ) : (
        <Typography.Text type="secondary">{empty}</Typography.Text>
      )}
    </Card>
  );
}

export default function HttpsCertificatePage() {
  const t = useText();
  const [form] = Form.useForm<CertificateForm>();
  const fileInput = useRef<HTMLInputElement>(null);
  const passwordFileInput = useRef<HTMLInputElement>(null);
  const passwordFileRead = useRef<AbortController | undefined>(undefined);
  const readRequest = useRef<AbortController | undefined>(undefined);
  const uploadRequest = useRef<AbortController | undefined>(undefined);
  const [settings, setSettings] = useState<HttpsCertificateSettings>();
  const [loading, setLoading] = useState(true);
  const [readingError, setReadingError] = useState<string>();
  const [saving, setSaving] = useState(false);
  const [readingPasswordFile, setReadingPasswordFile] = useState(false);
  const [passwordFileError, setPasswordFileError] = useState(false);
  const [saveError, setSaveError] = useState<string>();
  const [saved, setSaved] = useState(false);
  const [selectedFile, setSelectedFile] = useState<File>();
  const [pendingFile, setPendingFile] = useState<File>();

  const reload = useCallback(async () => {
    readRequest.current?.abort();
    const request = new AbortController();
    readRequest.current = request;
    setLoading(true);
    setReadingError(undefined);
    setSaved(false);
    try {
      const result = await getHttpsCertificateSettings(request.signal);
      if (!request.signal.aborted) setSettings(result);
    } catch (error) {
      if (!request.signal.aborted) setReadingError(getErrorKey(error));
    } finally {
      if (!request.signal.aborted) setLoading(false);
    }
  }, []);

  useEffect(() => {
    void reload();
    return () => {
      readRequest.current?.abort();
      uploadRequest.current?.abort();
      passwordFileRead.current?.abort();
    };
  }, [reload]);

  const readPasswordFile = async (file: File) => {
    if (passwordFileRead.current || uploadRequest.current) return;
    if (
      !/\.txt$/i.test(file.name) ||
      file.size === 0 ||
      file.size > maxPasswordFileBytes
    ) {
      setPasswordFileError(true);
      return;
    }
    const request = new AbortController();
    passwordFileRead.current = request;
    setReadingPasswordFile(true);
    setPasswordFileError(false);
    let timeout: ReturnType<typeof setTimeout> | undefined;
    try {
      // 本地文件提供者异常时，恢复表单操作并忽略迟到的读取结果。
      const deadline = new Promise<never>((_, reject) => {
        timeout = setTimeout(
          () => reject(new Error()),
          passwordFileReadTimeoutMs,
        );
      });
      const bytes = await Promise.race([file.arrayBuffer(), deadline]);
      // UTF-8 解码会去掉 BOM。仅移除一个文件结尾换行，保留密码中的空格。
      const password = new TextDecoder('utf-8', { fatal: true })
        .decode(bytes)
        .replace(/(?:\r\n|[\r\n])$/, '');
      if (request.signal.aborted) return;
      if (
        !password ||
        password.length > maxPasswordLength ||
        /[\r\n\u2028\u2029]/.test(password)
      ) {
        setPasswordFileError(true);
        return;
      }
      form.setFieldValue('password', password);
      setSaveError(undefined);
    } catch {
      if (!request.signal.aborted) setPasswordFileError(true);
    } finally {
      clearTimeout(timeout);
      passwordFileRead.current = undefined;
      if (!request.signal.aborted) setReadingPasswordFile(false);
    }
  };

  const confirm = () => {
    if (passwordFileRead.current) return;
    setSaved(false);
    setSaveError(undefined);
    const file = selectedFile;
    if (!file || file.size === 0 || !/\.(pfx|p12)$/i.test(file.name)) {
      setSaveError('httpsCertificate.fileRequired');
      return;
    }
    if (file.size > maxFileBytes) {
      setSaveError('httpsCertificate.tooLarge');
      return;
    }
    setPendingFile(file);
  };

  const save = async () => {
    if (!pendingFile || uploadRequest.current || passwordFileRead.current)
      return;
    const request = new AbortController();
    uploadRequest.current = request;
    setSaving(true);
    try {
      const result = await uploadHttpsCertificate(
        pendingFile,
        form.getFieldValue('password') ?? '',
        request.signal,
      );
      if (request.signal.aborted) return;
      setSettings(result);
      setSaved(Boolean(result.current) && !result.restartRequired);
      setSelectedFile(undefined);
    } catch (error) {
      if (request.signal.aborted) return;
      const certificateError =
        error instanceof ApiError
          ? certificateErrors.get(error.code)
          : undefined;
      setSaveError(certificateError ?? getErrorKey(error));
    } finally {
      // 密码仅在当前上传请求期间使用，错误后须重新输入。
      if (!request.signal.aborted) {
        form.setFieldValue('password', '');
        setPasswordFileError(false);
        setSaving(false);
        setPendingFile(undefined);
      }
      uploadRequest.current = undefined;
    }
  };

  return (
    <PageContainer
      title={t('httpsCertificate.title')}
      extra={
        <Button
          icon={<ReloadOutlined />}
          loading={loading}
          disabled={saving || readingPasswordFile}
          onClick={() => void reload()}
        >
          {t('app.retry')}
        </Button>
      }
    >
      <Space orientation="vertical" size="middle" className={styles.page}>
        {readingError && (
          <Alert type="error" showIcon title={t(readingError)} />
        )}
        {settings?.loadError && (
          <Alert
            type="warning"
            showIcon
            title={t('httpsCertificate.loadFailed')}
            description={t(
              certificateErrors.get(settings.loadError) ??
                'httpsCertificate.unavailable',
            )}
          />
        )}
        {settings?.restartRequired && (
          <Alert
            type="info"
            showIcon
            title={t('httpsCertificate.pendingNotice')}
          />
        )}
        <div className={styles.certificates}>
          <CertificateDetails
            title={t('httpsCertificate.current')}
            value={settings?.current}
            empty={t('httpsCertificate.noCurrent')}
            loading={loading}
          />
          <CertificateDetails
            title={t('httpsCertificate.pending')}
            value={settings?.pending}
            empty={t('httpsCertificate.noPending')}
            loading={loading}
          />
        </div>
        {settings && !settings.canUpload && (
          <Alert
            type="info"
            showIcon
            title={t('httpsCertificate.managedExternally')}
          />
        )}
        {settings?.canUpload && (
          <Card title={t('httpsCertificate.upload')}>
            {saveError && <Alert type="error" showIcon title={t(saveError)} />}
            {saved && (
              <Alert
                type="success"
                showIcon
                title={t('httpsCertificate.saved')}
              />
            )}
            <Form
              form={form}
              className={styles.form}
              layout="vertical"
              disabled={loading || saving || readingPasswordFile}
              onFinish={confirm}
            >
              <Form.Item
                label={t('httpsCertificate.file')}
                htmlFor="https-certificate-file"
                extra={
                  <span id="https-certificate-file-hint">
                    {t('httpsCertificate.fileHint')}
                  </span>
                }
                required
              >
                <div className={styles.filePicker}>
                  {/* Ant Design 表单样式会覆盖原生 hidden 的显示规则。 */}
                  <input
                    data-testid="https-certificate-file-input"
                    ref={fileInput}
                    type="file"
                    accept=".pfx,.p12"
                    disabled={loading || saving}
                    hidden
                    style={{ display: 'none' }}
                    onChange={(event) => {
                      const input = event.currentTarget;
                      const file = input.files?.[0];
                      // 由表单持有所选文件，重置选择器后仍可再次选择同一文件。
                      input.value = '';
                      if (file) {
                        setSelectedFile(file);
                        setSaveError(undefined);
                        setSaved(false);
                      }
                    }}
                  />
                  <Button
                    id="https-certificate-file"
                    color="primary"
                    variant="outlined"
                    icon={<UploadOutlined aria-hidden />}
                    aria-label={t('httpsCertificate.chooseFile')}
                    aria-describedby="https-certificate-file-hint"
                    onClick={() => fileInput.current?.click()}
                  >
                    {t('httpsCertificate.chooseFile')}
                  </Button>
                  <Typography.Text
                    className={styles.fileName}
                    aria-live="polite"
                  >
                    {selectedFile?.name}
                  </Typography.Text>
                </div>
              </Form.Item>
              <Form.Item
                label={t('httpsCertificate.password')}
                htmlFor="https-certificate-password"
                validateStatus={passwordFileError ? 'error' : undefined}
                help={
                  passwordFileError
                    ? t('httpsCertificate.passwordFileInvalid')
                    : undefined
                }
              >
                <div className={styles.passwordPicker}>
                  <Form.Item name="password" noStyle>
                    <Input.Password
                      id="https-certificate-password"
                      autoComplete="off"
                      maxLength={maxPasswordLength}
                      onChange={() => setPasswordFileError(false)}
                    />
                  </Form.Item>
                  <input
                    data-testid="https-certificate-password-file"
                    ref={passwordFileInput}
                    type="file"
                    accept=".txt,text/plain"
                    disabled={loading || saving || readingPasswordFile}
                    hidden
                    style={{ display: 'none' }}
                    onChange={(event) => {
                      const input = event.currentTarget;
                      const file = input.files?.[0];
                      if (file) {
                        void readPasswordFile(file).finally(() => {
                          // 读取期间保留浏览器选择的文件，完成后允许再次选择同一文件。
                          input.value = '';
                        });
                      }
                    }}
                  />
                  <Button
                    loading={readingPasswordFile}
                    onClick={() => passwordFileInput.current?.click()}
                  >
                    {t('httpsCertificate.readPasswordFile')}
                  </Button>
                </div>
              </Form.Item>
              <Button
                htmlType="submit"
                type="primary"
                disabled={
                  loading ||
                  saving ||
                  readingPasswordFile ||
                  Boolean(readingError)
                }
              >
                {t('httpsCertificate.save')}
              </Button>
            </Form>
          </Card>
        )}
      </Space>
      <Modal
        open={Boolean(pendingFile)}
        title={t('httpsCertificate.confirmTitle')}
        okText={t('httpsCertificate.save')}
        cancelText={t('source.cancel')}
        confirmLoading={saving}
        closable={!saving}
        keyboard={!saving}
        mask={{ closable: false }}
        cancelButtonProps={{ disabled: saving }}
        onCancel={() => {
          if (!saving) setPendingFile(undefined);
        }}
        onOk={() => void save()}
      >
        <Typography.Paragraph>
          {t('httpsCertificate.confirmHint')}
        </Typography.Paragraph>
      </Modal>
    </PageContainer>
  );
}
