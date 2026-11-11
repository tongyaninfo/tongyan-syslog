import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '@/requestErrorConfig';
import { getSession, getSetupStatus } from '@/services/syslog/api';
import { loadStartupState, startupRedirect } from './startup';

vi.mock('@/services/syslog/api', () => ({
  getSession: vi.fn(),
  getSetupStatus: vi.fn(),
}));
const ready = { setupRequired: false, canInitialize: false };
const user = { id: '42', username: 'synthetic-admin', role: 'admin' };
beforeEach(() => vi.clearAllMocks());

describe('首次访问状态与路由守卫', () => {
  it('无账号时直接进入引导，不先请求需要账号的会话接口', async () => {
    vi.mocked(getSetupStatus).mockResolvedValue({
      setupRequired: true,
      canInitialize: true,
    });
    const state = await loadStartupState();
    expect(getSession).not.toHaveBeenCalled();
    expect(startupRedirect(state, '/logs')).toBe('/user/setup');
    expect(startupRedirect(state, '/user/setup')).toBeUndefined();
  });
  it('数据库或状态请求失败时没有初始化授权', async () => {
    vi.mocked(getSetupStatus).mockRejectedValue(
      new ApiError('service_unavailable', 503),
    );
    const state = await loadStartupState();
    expect(state.setupStatus).toBeUndefined();
    expect(state.bootError).toBe('error.generic');
    expect(getSession).not.toHaveBeenCalled();
    expect(startupRedirect(state, '/user/login')).toBe('/user/setup');
  });
  it('非合同状态不会被解释为已初始化', async () => {
    vi.mocked(getSetupStatus).mockResolvedValue({} as never);
    expect((await loadStartupState()).setupStatus).toBeUndefined();
    expect(getSession).not.toHaveBeenCalled();
  });
  it('初始化已完成但尚未登录时只进入登录', async () => {
    vi.mocked(getSetupStatus).mockResolvedValue(ready);
    vi.mocked(getSession).mockRejectedValue(
      new ApiError('authentication_required', 401),
    );
    const state = await loadStartupState();
    expect(state.bootError).toBeUndefined();
    expect(startupRedirect(state, '/user/setup')).toBe('/user/login');
    expect(startupRedirect(state, '/user/login')).toBeUndefined();
  });
  it('已有会话回到管理台且保留业务直达路径', async () => {
    vi.mocked(getSetupStatus).mockResolvedValue(ready);
    vi.mocked(getSession).mockResolvedValue(user);
    const state = await loadStartupState();
    expect(startupRedirect(state, '/user/setup')).toBe('/overview');
    expect(startupRedirect(state, '/user/login')).toBe('/overview');
    expect(startupRedirect(state, '/users')).toBeUndefined();
  });
  it('远程受限仍显示引导但不授予初始化权限', async () => {
    vi.mocked(getSetupStatus).mockResolvedValue({
      setupRequired: true,
      canInitialize: false,
    });
    const state = await loadStartupState();
    expect(state.setupStatus?.canInitialize).toBe(false);
    expect(startupRedirect(state, '/overview')).toBe('/user/setup');
  });
});
