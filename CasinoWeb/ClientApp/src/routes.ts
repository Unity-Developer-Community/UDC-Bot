import type { Session } from '@/api';

export type Screen = 'access-denied' | 'closed' | 'lobby' | 'login' | 'login-failed';

/** The server redirects to these two paths after a failed login; every other address is the lobby or the login page. */
export function resolveScreen(pathname: string, session: Session): Screen {
  if (pathname === '/access-denied')
    return 'access-denied';
  if (pathname === '/login-failed')
    return 'login-failed';
  if (session.kind === 'anonymous')
    return 'login';
  return session.me.casinoEnabled ? 'lobby' : 'closed';
}

/** Where a login should send the player back to: the current path on this site, never another address. */
export function loginUrl(pathname: string): string {
  const returnUrl = pathname === '/access-denied' || pathname === '/login-failed' ? '/' : pathname;
  return `/login?returnUrl=${encodeURIComponent(returnUrl)}`;
}
