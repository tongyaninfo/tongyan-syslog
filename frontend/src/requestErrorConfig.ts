import common from '@/locales/zh-CN/common';
import userMessages from '@/locales/zh-CN/users';

const messages = { ...common, ...userMessages };

export class ApiError extends Error {
  constructor(
    public readonly code: string,
    public readonly status: number,
  ) {
    super(code);
    this.name = 'ApiError';
  }
}

const keys = new Map<string, keyof typeof messages>([
  ['AUTH_FAILED', 'error.auth'],
  ['authentication_required', 'error.session'],
  ['INVALID_INPUT', 'error.input'],
  ['invalid_request', 'error.input'],
  ['invalid_query', 'error.query'],
  ['unsupported_search_token', 'error.token'],
  ['source_forbidden', 'error.permission'],
  ['FORBIDDEN', 'error.permission'],
  ['forbidden', 'error.permission'],
  ['NOT_FOUND', 'error.notFound'],
  ['not_found', 'error.notFound'],
  ['CONFLICT', 'error.conflict'],
  ['csrf_rejected', 'error.csrf'],
  ['https_required', 'error.https'],
  ['rate_limited', 'error.rate'],
  ['storage_timeout', 'error.timeout'],
  ['request_timeout', 'error.timeout'],
  ['storage_reconciliation_required', 'error.reconcile'],
  ['network_unavailable', 'error.network'],
  ['current_password_invalid', 'error.currentPassword'],
  ['password_policy', 'error.passwordInvalid'],
  ['password_unchanged', 'error.passwordUnchanged'],
  ['username_invalid', 'users.usernameRule'],
  ['username_conflict', 'error.usernameConflict'],
  ['version_conflict', 'error.userVersion'],
  ['user_not_found', 'error.notFound'],
  ['last_admin_required', 'error.lastAdmin'],
  ['self_disable_forbidden', 'error.selfDisable'],
  ['self_reset_forbidden', 'error.selfReset'],
  ['setup_already_completed', 'error.setupCompleted'],
  ['setup_local_required', 'error.setupLocal'],
]);

// 只保存消息键，切换语言后旧错误也能更新。未知响应正文不会进入页面。
export function getErrorKey(error: unknown): keyof typeof messages {
  if (!(error instanceof ApiError)) return 'error.generic';
  return keys.get(error.code) ?? 'error.generic';
}

export function getErrorMessage(error: unknown): string {
  return messages[getErrorKey(error)];
}
