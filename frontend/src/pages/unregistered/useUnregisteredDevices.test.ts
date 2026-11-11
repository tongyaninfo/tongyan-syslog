import { act, renderHook, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { getUnregisteredDevices } from '@/services/syslog/api';
import type { UnregisteredDevices } from '@/services/syslog/types';
import { useUnregisteredDevices } from './useUnregisteredDevices';

vi.mock('@/services/syslog/api', () => ({ getUnregisteredDevices: vi.fn() }));

describe('未登记设备快照并发', () => {
  it('登记后触发的刷新不会被更早请求的迟到结果覆盖', async () => {
    let finishOld!: (value: UnregisteredDevices) => void;
    const pending = new Promise<UnregisteredDevices>((done) => {
      finishOld = done;
    });
    const fresh: UnregisteredDevices = {
      items: [],
      status: {
        enabled: true,
        allowedNetworks: [],
        deviceCount: 0,
        sampleCount: 0,
        bufferedBytes: 0,
        maxBytes: 67108864,
        maxDevices: 1000,
        received: 1,
        evicted: 0,
        rejected: 0,
      },
    };
    vi.mocked(getUnregisteredDevices)
      .mockReturnValueOnce(pending)
      .mockResolvedValueOnce(fresh);
    const { result } = renderHook(useUnregisteredDevices);
    const oldSignal = vi.mocked(getUnregisteredDevices).mock.calls[0][0];
    await act(async () => result.current.reload());
    expect(oldSignal?.aborted).toBe(true);
    await waitFor(() => expect(result.current.data).toBe(fresh));
    await act(async () =>
      finishOld({
        ...fresh,
        items: [
          {
            peerAddress: '192.0.2.25',
            firstReceivedAt: '2026-11-10T01:00:00Z',
            lastReceivedAt: '2026-11-10T01:00:00Z',
            transports: ['udp'],
            received: 1,
            sampleCount: 1,
          },
        ],
      }),
    );
    expect(result.current.data?.items).toEqual([]);
  });
});
