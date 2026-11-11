import { apiRequest } from './api';

export type SmtpSecurity = 'ssl' | 'starttls' | 'none';

export interface SmtpSettingsView {
  version: number;
  host: string;
  port: number;
  security: SmtpSecurity;
  username: string;
  senderAddress: string;
  senderName: string;
  subjectPrefix: string;
  hasPassword: boolean;
}

export type SmtpSettingsInput = Omit<SmtpSettingsView, 'hasPassword'> & {
  password?: string;
};

export type ReportFrequency = 'daily' | 'weekly' | 'monthly';

export interface ReportSubscriptionInput {
  version: number;
  name: string;
  enabled: boolean;
  frequency: ReportFrequency;
  sendTime: string;
  sourceIds: string[];
  recipients: string[];
}

export interface ReportSubscription extends ReportSubscriptionInput {
  id: string;
  nextSendAt: string | null;
}

export type DeliveryKind = 'test' | ReportFrequency;
export type DeliveryStatus =
  | 'queued'
  | 'sending'
  | 'accepted'
  | 'failed'
  | 'unknown';

export interface MailDeliverySummary {
  id: string;
  kind: DeliveryKind;
  status: DeliveryStatus;
  recipient: string;
  subject: string;
  periodStart?: string | null;
  periodEnd?: string | null;
  createdAt: string;
  updatedAt?: string | null;
  attempts: number;
  failureCode?: string | null;
  subscriptionName?: string | null;
}

export interface MailDeliveryDetail extends MailDeliverySummary {
  htmlBody?: string | null;
  textBody?: string | null;
}

export interface DeliveryQuery {
  kind?: DeliveryKind;
  status?: DeliveryStatus;
  recipient?: string;
  beforeId?: string;
  from?: string;
  to?: string;
}

export interface DeliveryPage {
  items: MailDeliverySummary[];
  nextBeforeId?: string | null;
}

const base = '/notifications';

export const getSmtpSettings = (signal?: AbortSignal) =>
  apiRequest<SmtpSettingsView>(`${base}/smtp`, { signal });

export const saveSmtpSettings = (body: SmtpSettingsInput) =>
  apiRequest<SmtpSettingsView>(`${base}/smtp`, { method: 'PUT', body });

export const sendTestMail = (recipient: string) =>
  apiRequest<{ id: string }>(`${base}/test`, {
    method: 'POST',
    body: { recipient },
  });

export const getReportSubscriptions = (signal?: AbortSignal) =>
  apiRequest<ReportSubscription[]>(`${base}/subscriptions`, { signal });

export const saveReportSubscription = (
  body: ReportSubscriptionInput,
  id?: string,
) =>
  apiRequest<ReportSubscription>(
    `${base}/subscriptions${id ? `/${encodeURIComponent(id)}` : ''}`,
    { method: id ? 'PUT' : 'POST', body },
  );

export const getMailDeliveries = (
  query: DeliveryQuery,
  signal?: AbortSignal,
) => {
  const params = new URLSearchParams();
  for (const [name, value] of Object.entries(query)) {
    if (value) params.set(name, value);
  }
  const search = params.toString();
  return apiRequest<DeliveryPage>(
    `${base}/deliveries${search ? `?${search}` : ''}`,
    { signal },
  );
};

export const getMailDelivery = (id: string, signal?: AbortSignal) =>
  apiRequest<MailDeliveryDetail>(
    `${base}/deliveries/${encodeURIComponent(id)}`,
    { signal },
  );
