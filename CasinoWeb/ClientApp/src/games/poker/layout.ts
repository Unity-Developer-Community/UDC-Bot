export interface Point {
  x: number;
  y: number;
}

export const STAGE = { height: 440, width: 640 };
export const CENTER: Point = { x: STAGE.width / 2, y: 215 };
const RADIUS = { x: 250, y: 150 };

/**
 * Where a seat sits around the oval table. The viewer's own seat is at the bottom, the others follow clockwise from
 * it, so everyone sees the table from their own chair; a watcher sees seat 0 at the bottom.
 */
export function seatPosition(seat: number, mySeat: number | null, maxSeats: number): Point {
  const offset = ((seat - (mySeat ?? 0)) % maxSeats + maxSeats) % maxSeats;
  // Screen y grows downward, so 90 degrees is the bottom of the oval; the angle grows clockwise on screen.
  const angle = Math.PI / 2 + offset * 2 * Math.PI / maxSeats;
  return { x: CENTER.x + RADIUS.x * Math.cos(angle), y: CENTER.y + RADIUS.y * Math.sin(angle) };
}
