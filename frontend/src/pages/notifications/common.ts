import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError, getErrorKey } from '@/requestErrorConfig';

const notificationErrors = new Map([
  ['notification_migration_required', 'notifications.migrationRequired'],
  ['smtp_not_configured', 'notifications.smtpNotConfigured'],
  ['smtp_password_required', 'notifications.passwordRequired'],
  ['smtp_auth_requires_tls', 'notifications.unencrypted'],
  ['smtp_secret_unavailable', 'notifications.passwordRequired'],
  ['smtp_authentication_failed', 'notifications.authenticationFailed'],
  ['smtp_temporary_rejection', 'notifications.providerRejected'],
  ['smtp_recipient_rejected', 'notifications.providerRejected'],
  ['smtp_rejected', 'notifications.providerRejected'],
  ['smtp_delivery_unknown', 'notifications.status.unknown'],
  ['smtp_tls_failed', 'notifications.tlsFailed'],
  ['smtp_timeout', 'notifications.smtpConnectionFailed'],
  ['smtp_connection_failed', 'notifications.smtpConnectionFailed'],
  ['report_content_unavailable', 'notifications.reportUnavailable'],
  ['report_data_unavailable', 'notifications.reportUnavailable'],
  ['report_subscription_changed', 'notifications.subscriptionChanged'],
  ['notification_queue_full', 'notifications.queueFull'],
  ['notification_subscription_limit', 'notifications.subscriptionLimit'],
  ['version_conflict', 'notifications.conflict'],
]);

export const getNotificationErrorKey = (code: string) =>
  notificationErrors.get(code);

export function getNotificationError(error: unknown) {
  return (
    (error instanceof ApiError && getNotificationErrorKey(error.code)) ||
    getErrorKey(error)
  );
}

export function useNotificationQuery<T>(
  read: (signal: AbortSignal) => Promise<T>,
) {
  const [data, setData] = useState<T>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  const pending = useRef<AbortController | undefined>(undefined);

  const reload = useCallback(() => {
    pending.current?.abort();
    const request = new AbortController();
    pending.current = request;
    setLoading(true);
    setError(undefined);
    void read(request.signal)
      .then((value) => {
        if (!request.signal.aborted) setData(value);
      })
      .catch((failure) => {
        if (!request.signal.aborted) setError(getNotificationError(failure));
      })
      .finally(() => {
        if (!request.signal.aborted) setLoading(false);
      });
  }, [read]);

  useEffect(() => {
    reload();
    return () => pending.current?.abort();
  }, [reload]);

  return {
    data,
    setData,
    loading,
    error,
    reload,
  };
}
