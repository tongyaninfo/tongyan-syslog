import { useCallback, useEffect, useRef, useState } from 'react';
import { getErrorKey } from '@/requestErrorConfig';
import { getUsers } from '@/services/syslog/api';
import type { UserPage, UserQuery } from '@/services/syslog/types';

export function useUsers(query: UserQuery) {
  const [data, setData] = useState<UserPage>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  const request = useRef<AbortController | undefined>(undefined);
  const version = useRef(0);
  const reload = useCallback(async () => {
    request.current?.abort();
    const controller = new AbortController();
    request.current = controller;
    const current = ++version.current;
    setLoading(true);
    setError(undefined);
    try {
      const page = await getUsers(query, controller.signal);
      if (current === version.current && !controller.signal.aborted)
        setData(page);
    } catch (failure) {
      if (current === version.current && !controller.signal.aborted)
        setError(getErrorKey(failure));
    } finally {
      if (current === version.current && !controller.signal.aborted)
        setLoading(false);
    }
  }, [query]);
  useEffect(() => {
    // 筛选条件变化后清空旧页，避免把上一查询的账号总数显示为当前结果。
    setData(undefined);
    void reload();
    return () => {
      request.current?.abort();
      version.current++;
    };
  }, [reload]);
  return { data, loading, error, reload };
}
