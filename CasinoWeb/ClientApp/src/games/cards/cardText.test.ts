import { describe, expect, it } from 'vitest';

import { cardLabel, isRedSuit } from '@/games/cards/cardText';

describe('card text', () => {
  it('names a card with its rank and suit symbol', () => {
    expect(cardLabel({ rank: 'Q', suit: 'hearts' })).toBe('Q♥');
  });

  it('tells the red suits from the black ones', () => {
    expect(isRedSuit('diamonds')).toBe(true);
    expect(isRedSuit('spades')).toBe(false);
  });
});
