import { h } from '@/dom';

export interface Game {
  id: string;
  description: string;
  title: string;
}

export const GAMES: readonly Game[] = [
  { id: 'slots', title: 'Slots', description: 'Spin the reels, hold a symbol, win the jackpot.' },
  { id: 'blackjack', title: 'Blackjack', description: 'Beat the dealer to 21.' },
];

/** The game entries are not playable yet; each says so in text. */
export function renderLobby(): HTMLElement {
  return h(
    'main',
    { class: 'lobby' },
    h('h1', {}, 'Lobby'),
    h(
      'ul',
      { class: 'games' },
      ...GAMES.map(game => h(
        'li',
        { 'class': 'game', 'data-game': game.id },
        h('h2', {}, game.title),
        h('p', {}, game.description),
        h('button', { class: 'button', disabled: '', type: 'button' }, 'Coming soon'),
      )),
    ),
  );
}
