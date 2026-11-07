import type { DailyInfo } from '@/protocol';

export interface Me {
  id: string;
  name: string;
  avatarUrl: string;
  balance: number | null;
  casinoEnabled: boolean;
  /** Absent while the casino is closed. */
  daily?: DailyInfo | null;
}

export type Session
  = | { kind: 'anonymous' }
    | { kind: 'member'; me: Me };

/** A 401 is the normal "not logged in" answer; anything else unexpected throws so the page can offer a retry. */
export async function fetchSession(fetchFn: typeof fetch = fetch): Promise<Session> {
  const response = await fetchFn('/api/me', { headers: { Accept: 'application/json' } });
  if (response.status === 401)
    return { kind: 'anonymous' };
  if (!response.ok)
    throw new Error(`GET /api/me answered ${response.status}`);
  return { kind: 'member', me: await response.json() as Me };
}

export async function logout(fetchFn: typeof fetch = fetch): Promise<void> {
  const response = await fetchFn('/api/logout', { method: 'POST' });
  if (!response.ok)
    throw new Error(`POST /api/logout answered ${response.status}`);
}
