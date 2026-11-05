import type { Session } from '@/api';
import type { CasinoConnection } from '@/connection';

import { fetchSession, logout } from '@/api';
import { loginUrl, resolveScreen } from '@/routes';
import { renderHeader } from '@/views/header';
import { renderLobby } from '@/views/lobby';
import { statusPage } from '@/views/pages';

export interface AppDeps {
  /** Started once the lobby is shown. Absent until the server has a hub to connect to. */
  connection?: CasinoConnection;
  fetchFn?: typeof fetch;
  pathname?: string;
  /** Called after a logout to go back to the start; a full page load so no state of the old player stays. */
  restart?: () => void;
}

export async function startApp(root: HTMLElement, deps: AppDeps = {}): Promise<void> {
  const fetchFn = deps.fetchFn ?? fetch;
  const pathname = deps.pathname ?? window.location.pathname;
  const restart = deps.restart ?? (() => window.location.assign('/'));
  const show = (...nodes: Node[]): void => root.replaceChildren(...nodes);

  const retry = (): void => void startApp(root, deps);
  const failure = (): void => show(statusPage(
    'Something went wrong',
    'The casino could not be reached. Check your connection and try again.',
    { label: 'Try again', onClick: retry },
  ));

  let session: Session;
  // Only the two failure pages need no session, so they never wait for the server.
  const needsSession = pathname !== '/access-denied' && pathname !== '/login-failed';
  try {
    session = needsSession ? await fetchSession(fetchFn) : { kind: 'anonymous' };
  }
  catch {
    failure();
    return;
  }

  const login = { href: loginUrl(pathname), label: 'Log in with Discord' };
  switch (resolveScreen(pathname, session)) {
    case 'access-denied':
      show(statusPage('Access denied', 'Only members of the Unity Developer Community Discord server can play.', { href: '/', label: 'Back' }));
      return;
    case 'login-failed':
      show(statusPage('Login failed', 'The Discord login did not complete.', { ...login, label: 'Try again' }));
      return;
    case 'login':
      show(statusPage('UDC Casino', 'Log in with your Discord account to play. Only members of the Unity Developer Community server get in.', login));
      return;
    case 'closed':
    case 'lobby': {
      if (session.kind !== 'member')
        return;
      const onLogout = (): void => {
        void logout(fetchFn).then(restart, failure);
      };
      const header = renderHeader(session.me, onLogout);
      const { connection } = deps;
      if (connection) {
        connection.onStatus(header.setStatus);
        header.setStatus(connection.status);
        void connection.start();
      }
      const closed = !session.me.casinoEnabled;
      show(
        header.element,
        closed
          ? statusPage('Casino closed', 'The casino is closed right now. Come back later.')
          : renderLobby(),
      );
    }
  }
}
