import { ApiError, getErrorKey } from '@/requestErrorConfig';
import { getSession, getSetupStatus } from '@/services/syslog/api';
import type { CurrentUser, SetupStatus } from '@/services/syslog/types';

export interface StartupState {
  setupStatus?: SetupStatus;
  currentUser?: CurrentUser;
  bootError?: string;
}

/** 初始化读取失败必须保持未知状态，不能降级成允许创建管理员。 */
export async function loadStartupState(): Promise<StartupState> {
  let setupStatus: SetupStatus;
  try {
    setupStatus = await getSetupStatus();
    if (
      typeof setupStatus?.setupRequired !== 'boolean' ||
      typeof setupStatus?.canInitialize !== 'boolean'
    )
      throw new ApiError('invalid_response', 503);
  } catch (error) {
    return { bootError: getErrorKey(error) };
  }
  if (setupStatus.setupRequired) return { setupStatus };
  try {
    return { setupStatus, currentUser: await getSession() };
  } catch (error) {
    return {
      setupStatus,
      bootError:
        error instanceof ApiError && error.status === 401
          ? undefined
          : getErrorKey(error),
    };
  }
}

export function startupRedirect(
  state: StartupState,
  pathname: string,
): string | undefined {
  if (!state.setupStatus || state.setupStatus.setupRequired)
    return pathname === '/user/setup' ? undefined : '/user/setup';
  if (!state.currentUser)
    return pathname === '/user/login' ? undefined : '/user/login';
  if (pathname === '/user/setup' || pathname === '/user/login')
    return '/overview';
  return undefined;
}
