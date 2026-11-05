import { describe, expect, it } from 'vitest';

import type { BlackjackHand, Card } from '@/protocol';

import { cardLabel, EMPTY_TABLE, tableSteps } from '@/games/blackjack/table';

const c = (rank: string): Card => ({ rank, suit: 'spades' });

function hand(player: string[], dealer: string[], finished = false): BlackjackHand {
  return {
    actions: finished ? [] : ['hit', 'stand'],
    bet: 10,
    credited: 0,
    dealer: dealer.map(c),
    dealerValue: 0,
    finished,
    handId: 'h',
    net: 0,
    player: player.map(c),
    playerValue: 0,
    result: finished ? 'win' : null,
  };
}

describe('blackjack table', () => {
  it('opens with player, dealer, player, then the dealer card face down', () => {
    const { drawn, steps } = tableSteps(EMPTY_TABLE, hand(['10', '9'], ['7']));

    expect(steps).toEqual([
      { card: c('10'), kind: 'player' },
      { card: c('7'), kind: 'dealer' },
      { card: c('9'), kind: 'player' },
      { kind: 'hole' },
    ]);
    expect(drawn).toEqual({ dealer: 1, hole: true, player: 2 });
  });

  it('deals only the new card after a hit', () => {
    const start = tableSteps(EMPTY_TABLE, hand(['10', '2'], ['7'])).drawn;

    const { steps } = tableSteps(start, hand(['10', '2', '5'], ['7']));

    expect(steps).toEqual([{ card: c('5'), kind: 'player' }]);
  });

  it('turns the face-down card over, then draws for the dealer', () => {
    const start = tableSteps(EMPTY_TABLE, hand(['10', '6'], ['10'])).drawn;

    const { drawn, steps } = tableSteps(start, hand(['10', '6'], ['10', '5', '4'], true));

    expect(steps).toEqual([
      { card: c('5'), kind: 'reveal' },
      { card: c('4'), kind: 'dealer' },
    ]);
    expect(drawn).toEqual({ dealer: 3, hole: false, player: 2 });
  });

  it('plays a hand that ends on the deal straight through to the reveal', () => {
    const { steps } = tableSteps(EMPTY_TABLE, hand(['A', 'K'], ['10', '7'], true));

    expect(steps.map(s => s.kind)).toEqual(['player', 'dealer', 'player', 'hole', 'reveal']);
  });

  it('asks for nothing when the table already shows the hand', () => {
    const first = tableSteps(EMPTY_TABLE, hand(['10', '9'], ['7']));

    expect(tableSteps(first.drawn, hand(['10', '9'], ['7'])).steps).toEqual([]);
  });

  it('names a card with its rank and suit symbol', () => {
    expect(cardLabel({ rank: 'Q', suit: 'hearts' })).toBe('Q♥');
  });
});
