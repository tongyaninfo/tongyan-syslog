export interface ApiResult<T> {
  success: boolean;
  code: string;
  message: string;
  data: T;
}
export interface CurrentUser {
  id: string;
  username: string;
  role: string;
}
export interface SetupStatus {
  setupRequired: boolean;
  canInitialize: boolean;
}
export interface ManagedUser extends CurrentUser {
  enabled: boolean;
  createdAt: string;
  version: string;
}
export interface UserQuery {
  search?: string;
  enabled?: boolean;
  page: number;
  pageSize: number;
}
export interface UserPage {
  items: ManagedUser[];
  total: number;
  page: number;
  pageSize: number;
}
export interface UserInput {
  username: string;
  password: string;
}
export interface UserUpdate {
  username: string;
  enabled: boolean;
  expectedVersion: string;
}

export interface HttpsCertificateSummary {
  fingerprint: string;
  subject: string;
  notBefore: string;
  notAfter: string;
}

export interface HttpsCertificateSettings {
  current: HttpsCertificateSummary | null;
  pending: HttpsCertificateSummary | null;
  restartRequired: boolean;
  loadError: string | null;
  canUpload: boolean;
}

export interface LogSource {
  id: string;
  name: string;
  peerAddress: string;
  timeZoneId: string;
  retentionDays: number;
  enabled: boolean;
  ignoredSeverities?: number[] | null;
}
export type SourceInput = Omit<LogSource, 'id'>;

/** 未登记来源只保留有界内存样本，不具有正式日志事件或来源身份。 */
export interface UnregisteredDevice {
  peerAddress: string;
  firstReceivedAt: string;
  lastReceivedAt: string;
  transports: string[];
  received: number;
  sampleCount: number;
}
export interface UnregisteredBufferStatus {
  enabled: boolean;
  allowedNetworks: string[];
  deviceCount: number;
  sampleCount: number;
  bufferedBytes: number;
  maxBytes: number;
  maxDevices: number;
  received: number;
  evicted: number;
  rejected: number;
}
export interface UnregisteredDevices {
  items: UnregisteredDevice[];
  status: UnregisteredBufferStatus;
}
export interface UnregisteredLogSummary {
  sampleKey: string;
  receivedAt: string;
  transport: string;
  byteCount: number;
  preview: string;
}
export interface UnregisteredLogSample {
  sampleKey: string;
  receivedAt: string;
  transport: string;
  rawBytes: string;
}
export interface LogSearch {
  from: string;
  to: string;
  sourceId?: string;
  severity?: number;
  hostname?: string;
  appName?: string;
  keywords?: string;
  pageSize?: number;
  cursor?: string | null;
}
export interface LogEvent {
  eventId: string;
  batchId: string;
  sourceId: string;
  peerAddress: string;
  transport: string;
  receivedAt: string;
  eventTime: string | null;
  expiresAt: string;
  originalTimestamp: string;
  timeConfidence: string;
  hostname: string;
  appName: string;
  procId: string;
  msgId: string;
  structuredData: string;
  facility: number | null;
  severity: number | null;
  message: string;
  rawBytes: string;
  parseStatus: string;
  failureCode: string;
  parserVersion: string;
}
export interface LogSearchPage {
  items: LogEvent[];
  nextCursor: string | null;
  hasMore: boolean;
}
export interface IngestStatus {
  state: string;
  failureCode: string;
  listenerState: string;
  listenerFailureCode: string;
  received: number;
  receivedBytes: number;
  dropped: number;
  ignored?: number;
  persisted: number;
  parsed: number;
  parseFailed: number;
  parseUnknown: number;
  written: number;
  writeFailures: number;
  queuedBytes: number;
  spoolBytes: number;
  pendingBatches: number;
  lastReceiveUnixMilliseconds: number;
  lastWriteUnixMilliseconds: number;
}

export interface OverviewUpdate {
  status: IngestStatus;
  sources: { total: number; enabled: number } | null;
  sourceError: string | null;
  observedAt: string;
  databaseSizeBytes?: number | null;
  sourceTraffic?: {
    sourceId: string;
    received: number;
    written: number;
    ignored?: number;
  } | null;
}

export type ListenerProtocol = 'udp' | 'tcp' | 'tls' | 'relp';
export interface ListenerSetting {
  protocol: ListenerProtocol;
  enabled: boolean;
  address: string;
  port: number;
  framing: 'auto' | 'octet-counting' | 'newline';
  certificateId: string;
  frameTimeoutSeconds: number;
  idleTimeoutSeconds: number;
}
export interface ListenerConfiguration {
  enabled: boolean;
  listeners: ListenerSetting[];
}
export interface ListenerSettingsUpdate {
  version: string;
  configuration: ListenerConfiguration;
}
export interface ListenerConnectionError {
  protocol: ListenerProtocol;
  address: string;
  port: number;
  peerAddress: string;
  failureCode: string;
  failureMessage: string;
  occurredAt: string;
}
export interface ListenerRuntimeState {
  protocol: ListenerProtocol;
  address: string;
  port: number;
  state: string;
  failureCode: string;
  failureMessage?: string;
}

export interface ListenerRuntimeView {
  revision: string;
  saved: ListenerSettingsUpdate | null;
  running: ListenerConfiguration;
  restartRequired: boolean;
  states: ListenerRuntimeState[];
  connectionErrors: ListenerConnectionError[];
}

/** 保存与运行快照分别展示，不能将保存成功显示成端口已经监听。 */
export interface ListenerSettings extends ListenerRuntimeView {
  saved: ListenerSettingsUpdate;
  certificates: { id: string }[];
}

export interface ListenerRuntimeUpdate {
  runtime: ListenerRuntimeView;
  observedAt: string;
}
