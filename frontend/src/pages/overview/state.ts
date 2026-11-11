export interface RuntimeStateView {
  label: string;
  color: 'default' | 'processing' | 'success' | 'warning' | 'error';
}

export function runtimeStateView(
  state?: string,
  failureCode?: string,
): RuntimeStateView {
  switch (state) {
    case 'running':
      return { label: 'overview.state.running', color: 'success' };
    case 'listening':
      return { label: 'overview.state.listening', color: 'success' };
    case 'starting':
      return { label: 'overview.state.starting', color: 'processing' };
    case 'disabled':
      return { label: 'overview.state.disabled', color: 'default' };
    case 'stopped':
      return { label: 'overview.state.stopped', color: 'default' };
    case 'failed':
      return { label: 'overview.state.failed', color: 'error' };
    case 'retrying':
      // 清空保护期间任务只复查状态，不会发送暂存批次。
      return {
        label:
          failureCode === 'log_clear_outcome_unknown'
            ? 'overview.state.writePaused'
            : 'overview.state.retrying',
        color: 'warning',
      };
    case 'reconciliation_required':
      return { label: 'overview.state.writePaused', color: 'warning' };
    default:
      return { label: 'overview.state.unknown', color: 'default' };
  }
}

const knownIssues = new Map([
  ['storage_reconciliation_required', 'overview.issue.writePaused'],
  ['storage_connection_failed', 'overview.issue.storageConnection'],
  ['storage_access_denied', 'overview.issue.storageAccess'],
  ['storage_schema_missing', 'logs.error.storageSchema'],
  ['storage_schema_incompatible', 'logs.error.storageSchemaIncompatible'],
  ['storage_timeout', 'overview.issue.storageTimeout'],
  ['storage_unavailable', 'overview.issue.storageUnavailable'],
  ['log_clear_outcome_unknown', 'overview.issue.clearPending'],
  ['spool_full', 'overview.issue.spoolFull'],
  ['storage_contract_rejected', 'overview.issue.contract'],
  ['spool_recovery_required', 'overview.issue.recovery'],
  ['spool_ack_cleanup_required', 'overview.issue.recovery'],
  ['spool_batch_exceeds_capacity', 'overview.issue.capacity'],
  ['ingest_recovery_required', 'overview.issue.recovery'],
  ['source_registry_unavailable', 'overview.issue.sources'],
  ['invalid_listener_configuration', 'overview.issue.listenerConfig'],
  ['tls_listener_start_failed', 'overview.issue.tls'],
  ['listener_io_failed', 'overview.issue.listener'],
]);

/** 只解释后端提供的任务状态，不根据累计量推断 ClickHouse 健康或设备在线状态。 */
export function runtimeIssueKey(
  code?: string,
  fallback = 'overview.issue.connection',
): string | undefined {
  if (!code) return undefined;
  return knownIssues.get(code) ?? fallback;
}
