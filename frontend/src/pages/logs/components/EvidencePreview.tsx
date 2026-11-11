import { useIntl } from '@umijs/max';
import { Alert, Tabs, Typography } from 'antd';
import { useMemo } from 'react';
import { formatCount } from '@/utils/format';
import { useText } from '@/utils/i18n';
import {
  decodeEvidence,
  HEX_PREVIEW_LIMIT,
  TEXT_PREVIEW_LIMIT,
  truncateText,
} from '../helpers';

export function LimitedText({ text }: { text: string }) {
  const t = useText();
  const { locale } = useIntl();
  return (
    <>
      {text.length > TEXT_PREVIEW_LIMIT && (
        <Alert
          className="syslog-evidence-notice"
          type="info"
          showIcon
          title={t('logs.messageTruncated', {
            count: formatCount(TEXT_PREVIEW_LIMIT, locale),
          })}
        />
      )}
      <pre className="syslog-evidence-text">
        {truncateText(text, TEXT_PREVIEW_LIMIT)}
      </pre>
    </>
  );
}

export default function EvidencePreview({ base64 }: { base64: string }) {
  const t = useText();
  const { locale } = useIntl();
  const evidence = useMemo(() => decodeEvidence(base64), [base64]);
  if (!evidence.valid) {
    return (
      <Alert
        type="error"
        showIcon
        title={t(
          evidence.error === 'tooLarge'
            ? 'logs.rawTooLarge'
            : 'logs.invalidBase64',
        )}
      />
    );
  }
  return (
    <section aria-label={t('logs.original')}>
      <Typography.Text type="secondary">
        {t('logs.originalSize', {
          count: formatCount(evidence.byteLength, locale),
        })}
      </Typography.Text>
      {evidence.invalidUtf8 && (
        <Alert
          className="syslog-evidence-notice"
          type="warning"
          showIcon
          title={t('logs.invalidUtf8')}
        />
      )}
      <Tabs
        items={[
          {
            key: 'text',
            label: t('logs.originalText'),
            children: (
              <>
                {evidence.textTruncated && (
                  <Alert
                    className="syslog-evidence-notice"
                    type="info"
                    showIcon
                    title={t('logs.textTruncated', {
                      count: formatCount(TEXT_PREVIEW_LIMIT, locale),
                    })}
                  />
                )}
                <pre className="syslog-evidence-text">
                  {evidence.text || t('logs.originalEmpty')}
                </pre>
              </>
            ),
          },
          {
            key: 'hex',
            label: t('logs.originalHex'),
            children: (
              <>
                {evidence.hexTruncated && (
                  <Alert
                    className="syslog-evidence-notice"
                    type="info"
                    showIcon
                    title={t('logs.hexTruncated', {
                      count: formatCount(HEX_PREVIEW_LIMIT, locale),
                    })}
                  />
                )}
                <pre className="syslog-evidence-hex">
                  {evidence.hex || t('logs.originalEmpty')}
                </pre>
              </>
            ),
          },
        ]}
      />
    </section>
  );
}
