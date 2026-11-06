/** Reel geometry kept free of Phaser so it can be tested: a reel is a strip of symbols and a position along it. */

/** The strip index wrapped into 0..length-1, also for negative numbers. */
export function wrap(index: number, length: number): number {
  return ((index % length) + length) % length;
}

/**
 * Where a reel that is now at `from` (a position along the strip, in symbols) must travel to so that it comes to rest
 * with `stop` on the payline after at least `minTravel` symbols. It only ever moves forward, the direction a spinning reel rolls.
 */
export function landingPosition(from: number, stop: number, length: number, minTravel: number): number {
  const earliest = from + minTravel;
  const base = earliest - wrap(earliest, length) + stop;
  return base >= earliest ? base : base + length;
}

/** The symbols the window shows for a position: the rows above the payline, on it and below it. */
export function visibleRows<T>(strip: readonly T[], position: number, rowsAbove: number, rowsBelow: number): { offset: number; row: number; symbol: T }[] {
  const base = Math.floor(position);
  const offset = position - base;
  const rows: { offset: number; row: number; symbol: T }[] = [];
  for (let row = 0 - rowsAbove; row <= rowsBelow; row++) {
    const symbol = strip[wrap(base + row, strip.length)];
    if (symbol !== undefined)
      rows.push({ offset, row, symbol });
  }
  return rows;
}
