import { describe, expect, it } from 'vitest';

import { CENTER, seatPosition } from '@/games/poker/layout';

describe('poker table layout', () => {
  it('puts the viewer at the bottom whatever their seat number', () => {
    for (const mine of [0, 3, 5]) {
      const position = seatPosition(mine, mine, 6);
      expect(position.x).toBeCloseTo(CENTER.x);
      expect(position.y).toBeGreaterThan(CENTER.y);
    }
  });

  it('puts the seat across from the viewer at the top and the next seat on the left, going clockwise', () => {
    expect(seatPosition(3, 0, 6).y).toBeLessThan(CENTER.y);
    expect(seatPosition(1, 0, 6).x).toBeLessThan(CENTER.x);
    expect(seatPosition(5, 0, 6).x).toBeGreaterThan(CENTER.x);
  });

  it('shows a watcher seat 0 at the bottom', () => {
    expect(seatPosition(0, null, 6)).toEqual(seatPosition(0, 0, 6));
  });
});
