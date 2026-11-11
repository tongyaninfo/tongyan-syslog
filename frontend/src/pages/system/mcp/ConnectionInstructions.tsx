import { CopyOutlined } from '@ant-design/icons';
import { Alert, Button, Input, Modal, Typography } from 'antd';
import { useRef, useState } from 'react';
import { useText } from '@/utils/i18n';
import styles from './styles.module.less';

export default function ConnectionInstructions({
  text,
  onClose,
  manual = false,
}: {
  text: string;
  onClose: () => void;
  manual?: boolean;
}) {
  const t = useText();
  const [result, setResult] = useState<'success' | 'manual' | undefined>(
    manual ? 'manual' : undefined,
  );
  const [copying, setCopying] = useState(false);
  const copyingRef = useRef(false);
  async function copy() {
    if (copyingRef.current) return;
    copyingRef.current = true;
    setCopying(true);
    try {
      if (!navigator.clipboard?.writeText)
        throw new Error('Clipboard unavailable');
      await navigator.clipboard.writeText(text);
      setResult('success');
    } catch {
      setResult('manual');
    } finally {
      copyingRef.current = false;
      setCopying(false);
    }
  }
  return (
    <Modal
      open
      title={t('mcp.connectionInstructions')}
      width={760}
      onCancel={onClose}
      footer={
        <>
          <Button onClick={onClose}>{t('mcp.close')}</Button>
          <Button
            type="primary"
            icon={<CopyOutlined />}
            loading={copying}
            onClick={() => void copy()}
          >
            {t('mcp.copy')}
          </Button>
        </>
      }
    >
      <div className={styles.stack}>
        <Typography.Text type="secondary">
          {t('mcp.instructionsHint')}
        </Typography.Text>
        {result && (
          <Alert
            showIcon
            type={result === 'success' ? 'success' : 'info'}
            title={t(result === 'success' ? 'mcp.copied' : 'mcp.copyManual')}
          />
        )}
        <Input.TextArea
          readOnly
          value={text}
          rows={15}
          className={styles.instructions}
          aria-label={t('mcp.connectionInstructions')}
          onFocus={(event) => event.currentTarget.select()}
        />
      </div>
    </Modal>
  );
}
