import type { Me } from '@/api';
import type { ConnectionStatus } from '@/connection';

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

export interface Header {
  element: HTMLElement;
  setBalance: (balance: number) => void;
  setStatus: (status: ConnectionStatus) => void;
}

export function renderHeader(me: Me, onLogout: () => void): Header {
  const status = h('span', { 'class': 'status', 'data-status': 'idle', 'role': 'status' });
  status.hidden = true;

  const balance = h('span', { 'class': 'balance', 'data-testid': 'balance' }, me.balance === null ? formatBalance(null) : `${formatBalance(me.balance)} tokens`);

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
      logoutButton,
    ),
  );

  return {
    element,
    setBalance(next) {
      balance.textContent = `${formatBalance(next)} tokens`;
    },
    setStatus(next) {
      status.hidden = next === 'idle';
      status.dataset.status = next;
      status.textContent = `${STATUS_ICON[next]} ${STATUS_TEXT[next]}`;
    },
  };
}
