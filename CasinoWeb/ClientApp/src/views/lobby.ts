import { h } from '@/dom';

export interface Game {
  id: string;
  description: string;
  title: string;
}

export const GAMES: readonly Game[] = [
  { id: 'slots', title: 'Slots', description: 'Spin the reels, hold a symbol, win the jackpot.' },
  { id: 'blackjack', title: 'Blackjack', description: 'Beat the dealer to 21.' },
  { id: 'poker', title: 'Poker', description: 'Texas Hold\'em against other members at shared tables.' },
];

/**
 * The game entries. The games in `playable` open through `onOpen`; the others say in text that they are not available yet.
 */
export function renderLobby(playable: ReadonlySet<string>, onOpen: (gameId: string) => void): HTMLElement {
  return h(
    'main',
    { class: 'lobby' },
    h('h1', {}, 'Lobby'),
    h(
      'ul',
      { class: 'games' },
      ...GAMES.map((game) => {
        const open = playable.has(game.id);
        const button = h('button', { class: 'button', type: 'button', ...(open ? {} : { disabled: '' }) }, open ? 'Play' : 'Coming soon');
        if (open)
          button.addEventListener('click', () => onOpen(game.id));
        return h(
          'li',
          { 'class': 'game', 'data-game': game.id },
          h('h2', {}, game.title),
          h('p', {}, game.description),
          button,
        );
      }),
    ),
  );
}
