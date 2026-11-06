import { describe, expect, it } from 'vitest';

import { landingPosition, visibleRows, wrap } from '@/games/slots/reels';

describe('wrap', () => {
  it('keeps an index inside the strip, also below zero', () => {
    expect(wrap(7, 5)).toBe(2);
    expect(wrap(-1, 5)).toBe(4);
    expect(wrap(0, 5)).toBe(0);
  });
});

describe('landingPosition', () => {
  it('lands on the stop after at least the minimum travel', () => {
    const target = landingPosition(3, 1, 10, 20);

    expect(wrap(target, 10)).toBe(1);
    expect(target - 3).toBeGreaterThanOrEqual(20);
    expect(target - 3).toBeLessThan(20 + 10);
  });

  it('never moves backwards, even to a stop just behind the reel', () => {
    expect(landingPosition(5, 4, 10, 0)).toBe(14);
    expect(landingPosition(5, 5, 10, 0)).toBe(5);
  });

  it('starts from a position between two symbols', () => {
    const target = landingPosition(2.4, 0, 10, 10);

    expect(Number.isInteger(target)).toBe(true);
    expect(target).toBeGreaterThanOrEqual(12.4);
  });
});

describe('visibleRows', () => {
  const strip = ['a', 'b', 'c', 'd'];

  it('shows the stop on the payline with its neighbours above and below', () => {
    expect(visibleRows(strip, 2, 1, 1).map(r => r.symbol)).toEqual(['b', 'c', 'd']);
  });

  it('wraps around the ends of the strip', () => {
    expect(visibleRows(strip, 0, 1, 1).map(r => r.symbol)).toEqual(['d', 'a', 'b']);
    expect(visibleRows(strip, 3, 1, 1).map(r => r.symbol)).toEqual(['c', 'd', 'a']);
  });

  it('reports how far between two symbols the reel is', () => {
    expect(visibleRows(strip, 1.25, 0, 0)[0]).toEqual({ offset: 0.25, row: 0, symbol: 'b' });
  });
});
