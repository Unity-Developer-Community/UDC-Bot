import type { Card, CardSuit } from '@/protocol';

const SUIT_SYMBOLS: Record<CardSuit, string> = { clubs: '♣', diamonds: '♦', hearts: '♥', spades: '♠' };

export function suitSymbol(suit: CardSuit): string {
  return SUIT_SYMBOLS[suit];
}

export function isRedSuit(suit: CardSuit): boolean {
  return suit === 'hearts' || suit === 'diamonds';
}

/** A short card name such as `10♠`, for the text that repeats the table in words. */
export function cardLabel(card: Card): string {
  return `${card.rank}${suitSymbol(card.suit)}`;
}
