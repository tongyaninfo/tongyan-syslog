import { useCallback, useEffect, useRef, useState } from 'react';
import { getErrorKey } from '@/requestErrorConfig';
import { getSources } from '@/services/syslog/api';
import type { LogSource } from '@/services/syslog/types';

/** 来源集合用于本地筛选；刷新和保存同时发生时，旧请求不能覆盖新配置。 */
export function useSources() {
  const [sources, setSources] = useState<LogSource[]>([]);
  // 空数组只有在完整列表请求成功后才代表没有来源，首次失败仍是未知。
  const [hasLoaded, setHasLoaded] = useState(false);
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
      const result = await getSources(request.signal);
      if (generation.current === current && !request.signal.aborted) {
        setSources(result);
        setHasLoaded(true);
      }
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

  const saved = useCallback((source: LogSource) => {
    generation.current++;
    controller.current?.abort();
    setSources((previous) =>
      previous.some((item) => item.id === source.id)
        ? previous.map((item) => (item.id === source.id ? source : item))
        : [...previous, source],
    );
    setLoading(false);
    setError(undefined);
  }, []);

  return { sources, hasLoaded, loading, error, reload, saved };
}
