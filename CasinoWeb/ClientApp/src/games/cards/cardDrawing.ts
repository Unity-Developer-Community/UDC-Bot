import Phaser from 'phaser';

import type { Card } from '@/protocol';

import { isRedSuit, suitSymbol } from '@/games/cards/cardText';

/** A theme token of the page, so the canvas follows the light and dark themes like the rest of it. */
export function themeToken(name: string): string {
  return getComputedStyle(document.documentElement).getPropertyValue(name).trim();
}

export function colorNumber(css: string): number {
  return Phaser.Display.Color.HexStringToColor(css).color;
}

export function prefersReducedMotion(): boolean {
  return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}

export interface CardSize {
  height: number;
  width: number;
}

/**
 * The shapes of a card centered on (0, 0): its face, or its back when `card` is null. Card faces are pictures, like the
 * slot symbols, so they keep their colors in both themes; the suit symbol and the rank carry the meaning, not the color.
 */
export function drawCard(scene: Phaser.Scene, card: Card | null, { height, width }: CardSize): Phaser.GameObjects.GameObject[] {
  const outline = colorNumber(themeToken('--color-border'));
  const scale = width / 64;
  if (!card) {
    const back = scene.add.rectangle(0, 0, width, height, colorNumber(themeToken('--color-accent'))).setStrokeStyle(2, outline);
    const inner = scene.add.rectangle(0, 0, width - 12 * scale, height - 12 * scale).setStrokeStyle(2, 0xFFFFFF, 0.7);
    return [back, inner];
  }
  const color = isRedSuit(card.suit) ? '#c62828' : '#1f2328';
  const face = scene.add.rectangle(0, 0, width, height, 0xFFFFFF).setStrokeStyle(2, outline);
  const corner = scene.add.text(-width / 2 + 6 * scale, -height / 2 + 4 * scale, `${card.rank}\n${suitSymbol(card.suit)}`, {
    align: 'center',
    color,
    fontFamily: 'system-ui, sans-serif',
    fontSize: `${Math.round(15 * scale)}px`,
    fontStyle: 'bold',
    lineSpacing: -4,
  });
  const middle = scene.add.text(0, 8 * scale, suitSymbol(card.suit), { color, fontFamily: 'system-ui, sans-serif', fontSize: `${Math.round(34 * scale)}px` }).setOrigin(0.5);
  return [face, corner, middle];
}
