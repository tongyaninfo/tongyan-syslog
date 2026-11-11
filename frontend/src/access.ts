import type { CurrentUser } from '@/services/syslog/types';

export default function access(
  state: { currentUser?: CurrentUser } | undefined,
) {
  return { admin: state?.currentUser?.role === 'admin' };
}
