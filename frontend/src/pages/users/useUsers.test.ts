import { act, renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '@/requestErrorConfig';
import { getUsers } from '@/services/syslog/api';
import type { UserPage, UserQuery } from '@/services/syslog/types';
import { useUsers } from './useUsers';

vi.mock('@/services/syslog/api', () => ({ getUsers: vi.fn() }));
const first: UserPage = { items: [], total: 23, page: 1, pageSize: 20 };
const query: UserQuery = { page: 1, pageSize: 20 };
beforeEach(() => vi.clearAllMocks());
describe('账号分页读取', () => {
  it('旧筛选响应不能覆盖新查询，筛选变化先清除旧计数', async () => {
    let finish!: (page: UserPage) => void;
    vi.mocked(getUsers).mockReturnValueOnce(
      new Promise((resolve) => {
        finish = resolve;
      }),
    );
    vi.mocked(getUsers).mockResolvedValueOnce({ ...first, total: 1 });
    const { result, rerender } = renderHook(({ input }) => useUsers(input), {
      initialProps: { input: query },
    });
    rerender({ input: { ...query, search: 'example' } });
    await waitFor(() => expect(result.current.data?.total).toBe(1));
    await act(async () => finish(first));
    expect(result.current.data?.total).toBe(1);
    expect(vi.mocked(getUsers).mock.calls[0][1]?.aborted).toBe(true);
  });
  it('首次读取失败不假报零账号，重试可恢复', async () => {
    vi.mocked(getUsers).mockRejectedValueOnce(
      new ApiError('service_unavailable', 503),
    );
    const { result } = renderHook(() => useUsers(query));
    await waitFor(() => expect(result.current.error).toBe('error.generic'));
    expect(result.current.data).toBeUndefined();
    vi.mocked(getUsers).mockResolvedValueOnce(first);
    await act(async () => result.current.reload());
    expect(result.current.data?.total).toBe(23);
    expect(result.current.error).toBeUndefined();
  });
});
