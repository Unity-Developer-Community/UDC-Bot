import type { Session } from '@/api';
import type { CasinoConnection } from '@/connection';

import { fetchSession, logout } from '@/api';
import { CasinoApi } from '@/casinoApi';
import { renderBlackjackScreen } from '@/games/blackjack/blackjackPage';
import { renderSlotsScreen } from '@/games/slots/slotsPage';
import { loginUrl, resolveScreen } from '@/routes';
import { renderHeader } from '@/views/header';
import { renderLobby } from '@/views/lobby';
import { statusPage } from '@/views/pages';

export interface AppDeps {
  /** Started once the lobby is shown. Without one the games are not playable. */
  connection?: CasinoConnection;
  fetchFn?: typeof fetch;
  /** Called when the login no longer holds; a full page load of the login so no state of the old session stays. */
  pathname?: string;
  goToLogin?: () => void;
  /** Called after a logout to go back to the start; a full page load so no state of the old player stays. */
  restart?: () => void;
}

export async function startApp(root: HTMLElement, deps: AppDeps = {}): Promise<void> {
  const fetchFn = deps.fetchFn ?? fetch;
  const pathname = deps.pathname ?? window.location.pathname;
  const restart = deps.restart ?? (() => window.location.assign('/'));
  const goToLogin = deps.goToLogin ?? (() => window.location.assign(loginUrl(pathname)));
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
        connection.onSessionLost(goToLogin);
        void connection.start();
      }
      if (!session.me.casinoEnabled) {
        show(header.element, statusPage('Casino closed', 'The casino is closed right now. Come back later.'));
        return;
      }

      const api = connection ? new CasinoApi(connection) : undefined;
      const screens: Record<string, typeof renderSlotsScreen | typeof renderBlackjackScreen> = {
        blackjack: renderBlackjackScreen,
        slots: renderSlotsScreen,
      };
      let screen: { destroy: () => void } | undefined;
      function showLobby(): void {
        screen?.destroy();
        screen = undefined;
        show(header.element, renderLobby(new Set(api ? Object.keys(screens) : []), openGame));
      }
      function openGame(gameId: string): void {
        const render = screens[gameId];
        if (!render || !api)
          return;
        const opened = render({ api, onBack: showLobby, onBalance: header.setBalance });
        screen = opened;
        show(header.element, opened.element);
      }
      showLobby();
    }
  }
}
