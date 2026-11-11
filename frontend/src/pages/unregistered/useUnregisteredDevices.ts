import { useCallback, useEffect, useRef, useState } from 'react';
import { getErrorKey } from '@/requestErrorConfig';
import { getUnregisteredDevices } from '@/services/syslog/api';
import type { UnregisteredDevices } from '@/services/syslog/types';

/** 每次刷新取消旧请求，避免登记后的新列表被旧快照覆盖。 */
export function useUnregisteredDevices() {
  const [data, setData] = useState<UnregisteredDevices>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  const controller = useRef<AbortController | undefined>(undefined);
  const generation = useRef(0);
  const reload = useCallback(async () => {
    const current = ++generation.current;
    controller.current?.abort();
    const request = new AbortController();
    controller.current = request;
    setLoading(true);
    setError(undefined);
    try {
      const result = await getUnregisteredDevices(request.signal);
      if (generation.current === current && !request.signal.aborted)
        setData(result);
    } catch (failure) {
      if (generation.current === current && !request.signal.aborted)
        setError(getErrorKey(failure));
    } finally {
      if (generation.current === current && !request.signal.aborted)
        setLoading(false);
    }
  }, []);
  useEffect(() => {
    void reload();
    return () => {
      generation.current++;
      controller.current?.abort();
    };
  }, [reload]);
  return { data, loading, error, reload };
}
