import type { Me } from '@/api';
import type { ConnectionStatus } from '@/connection';
import type { DailyClaim } from '@/protocol';

import { CallRefusedError } from '@/casinoApi';
import { h } from '@/dom';

const STATUS_TEXT: Record<ConnectionStatus, string> = {
  connected: 'Connected',
  connecting: 'Connecting…',
  idle: '',
  offline: 'Offline',
  reconnecting: 'Reconnecting…',
};

// A symbol next to the text, so the state never rests on color alone.
const STATUS_ICON: Record<ConnectionStatus, string> = {
  connected: '●',
  connecting: '◌',
  idle: '',
  offline: '✕',
  reconnecting: '↻',
};

export function formatBalance(balance: number | null): string {
  return balance === null ? '—' : balance.toLocaleString('en-US');
}

/** The time left as shown on the button: `5h 12m`, `12m`, `45s`. Minutes round up, so the text never reads 0m before the claim. */
export function formatCountdown(seconds: number): string {
  if (seconds < 60)
    return `${Math.max(0, Math.ceil(seconds))}s`;
  const minutes = Math.ceil(seconds / 60);
  return minutes < 60 ? `${minutes}m` : `${Math.floor(minutes / 60)}h ${minutes % 60}m`;
}

const TOAST_MS = 4000;

function refusalToast(error: unknown): string {
  if (error instanceof CallRefusedError) {
    switch (error.code) {
      case 'casino-closed':
        return 'The casino is closed right now.';
      case 'session-expired':
        return 'Your login ended.';
      case 'slow-down':
        return 'Slow down: try again in a moment.';
    }
  }
  return 'Could not claim the daily reward. Check your connection and try again.';
}

export interface HeaderOptions {
  /** Claims the daily reward. Without one the header shows no daily button. */
  onClaim?: () => Promise<DailyClaim>;
  /** The clock of the countdown, in milliseconds. */
  now?: () => number;
}

export interface Header {
  element: HTMLElement;
  setBalance: (balance: number) => void;
  setStatus: (status: ConnectionStatus) => void;
}

export function renderHeader(me: Me, onLogout: () => void, options: HeaderOptions = {}): Header {
  const { onClaim } = options;
  const now = options.now ?? Date.now;
  const status = h('span', { 'class': 'status', 'data-status': 'idle', 'role': 'status' });
  status.hidden = true;

  const balance = h('span', { 'class': 'balance', 'data-testid': 'balance' }, me.balance === null ? formatBalance(null) : `${formatBalance(me.balance)} tokens`);

  let shownBalance = me.balance;

  // The server sends the seconds left and the deadline is set on this clock, so a wrong clock on the player's machine
  // cannot show a wrong countdown.
  let dailyTokens = me.daily?.tokens ?? 0;
  let deadline = now() + (me.daily?.secondsUntilNext ?? 0) * 1000;
  let ticker: ReturnType<typeof setInterval> | undefined;
  let claiming = false;
  const dailyButton = h('button', { 'class': 'button', 'data-testid': 'daily', 'type': 'button' });
  const hasDaily = Boolean(me.daily && onClaim);
  const toast = h('p', { 'class': 'toast', 'data-testid': 'toast', 'role': 'status' });
  toast.hidden = true;
  let toastTimer: ReturnType<typeof setTimeout> | undefined;

  function secondsLeft(): number {
    return Math.ceil((deadline - now()) / 1000);
  }

  function renderDaily(): void {
    const left = secondsLeft();
    const ready = left <= 0;
    dailyButton.textContent = ready ? `Claim ${formatBalance(dailyTokens)} tokens` : `Next daily in ${formatCountdown(left)}`;
    dailyButton.disabled = !ready || claiming;
    dailyButton.className = ready ? 'button' : 'button secondary';
    if (ready && ticker !== undefined) {
      clearInterval(ticker);
      ticker = undefined;
    }
  }

  function startCountdown(seconds: number): void {
    deadline = now() + seconds * 1000;
    if (ticker !== undefined)
      clearInterval(ticker);
    ticker = secondsLeft() > 0 ? setInterval(renderDaily, 1000) : undefined;
    renderDaily();
  }

  function showToast(text: string): void {
    clearTimeout(toastTimer);
    toast.textContent = text;
    toast.hidden = false;
    toastTimer = setTimeout(() => {
      toast.hidden = true;
      toast.textContent = '';
    }, TOAST_MS);
  }

  function setBalance(next: number): void {
    shownBalance = next;
    balance.textContent = `${formatBalance(next)} tokens`;
  }

  dailyButton.addEventListener('click', () => {
    if (!onClaim || claiming)
      return;
    claiming = true;
    renderDaily();
    void onClaim().then((claim) => {
      dailyTokens = claim.daily.tokens;
      // At least a second for notYet: a countdown of 0 would turn the button straight back into the claim button.
      startCountdown(claim.status === 'ok' ? claim.daily.secondsUntilNext : Math.max(1, claim.daily.secondsUntilNext));
      if (claim.status === 'ok') {
        // Added to what is shown instead of taking the returned balance: that one already holds the result of a hand or
        // a spin whose animation may still be running, which the games hold back until it ends.
        if (shownBalance !== null)
          setBalance(shownBalance + claim.awarded);
        showToast(`Daily reward: +${formatBalance(claim.awarded)} tokens`);
      }
      else {
        showToast(`Already claimed. Next daily in ${formatCountdown(secondsLeft())}`);
      }
    }, (error: unknown) => showToast(refusalToast(error))).finally(() => {
      claiming = false;
      renderDaily();
    });
  });
  if (hasDaily)
    startCountdown(me.daily?.secondsUntilNext ?? 0);

  const logoutButton = h('button', { class: 'button secondary', type: 'button' }, 'Log out');
  logoutButton.addEventListener('click', onLogout);

  const avatar = h('img', { alt: '', class: 'avatar', height: '40', src: me.avatarUrl, width: '40' });

  const element = h(
    'header',
    { class: 'header' },
    h('span', { class: 'brand' }, 'UDC Casino'),
    status,
    h(
      'div',
      { class: 'player' },
      avatar,
      h('span', { class: 'player-name' }, me.name),
      balance,
      ...(hasDaily ? [dailyButton] : []),
      logoutButton,
    ),
    toast,
  );

  return {
    element,
    setBalance,
    setStatus(next) {
      status.hidden = next === 'idle';
      status.dataset.status = next;
      status.textContent = `${STATUS_ICON[next]} ${STATUS_TEXT[next]}`;
    },
  };
}
