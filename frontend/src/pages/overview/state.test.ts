import { describe, expect, it } from 'vitest';
import { runtimeIssueKey, runtimeStateView } from './state';

describe('overview write status', () => {
  it('shows automatic retry with the underlying write error', () => {
    expect(runtimeStateView('retrying', 'storage_timeout')).toEqual({
      label: 'overview.state.retrying',
      color: 'warning',
    });
    expect(runtimeIssueKey('storage_timeout')).toBe(
      'overview.issue.storageTimeout',
    );
    expect(runtimeIssueKey('storage_connection_failed')).toBe(
      'overview.issue.storageConnection',
    );
    expect(runtimeIssueKey('storage_access_denied')).toBe(
      'overview.issue.storageAccess',
    );
  });

  it('shows paused writes while the clear outcome is unknown', () => {
    expect(runtimeStateView('retrying', 'log_clear_outcome_unknown')).toEqual({
      label: 'overview.state.writePaused',
      color: 'warning',
    });
    expect(runtimeIssueKey('log_clear_outcome_unknown')).toBe(
      'overview.issue.clearPending',
    );
  });

  it('uses the caller fallback for an unknown error code', () => {
    expect(
      runtimeIssueKey('constructor', 'overview.issue.storageUnavailable'),
    ).toBe('overview.issue.storageUnavailable');
  });
});
