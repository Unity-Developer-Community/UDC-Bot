import type { BlackjackHand, Card } from '@/protocol';

/** What the table shows so far: how many of each hand's cards lie face up, and whether the dealer's second card lies face down. */
export interface Drawn {
  dealer: number;
  hole: boolean;
  player: number;
}

export const EMPTY_TABLE: Drawn = { dealer: 0, hole: false, player: 0 };

/** One card movement on the table. `hole` deals the dealer's second card face down; `reveal` turns it over. */
export type Step
  = | { card: Card; kind: 'dealer' | 'player' | 'reveal' }
    | { kind: 'hole' };

/**
 * The movements that take the table from what it shows to `hand`, in the order a dealer would make them. The opening
 * deal alternates player, dealer, player, dealer face down; after that new player cards come first, then the dealer turns
 * the face-down card over and draws.
 */
export function tableSteps(drawn: Drawn, hand: BlackjackHand): { drawn: Drawn; steps: Step[] } {
  const steps: Step[] = [];
  let { dealer, hole, player } = drawn;

  const [first, second] = hand.player;
  const [dealerFirst] = hand.dealer;
  if (player === 0 && dealer === 0 && !hole && first && second && dealerFirst) {
    steps.push({ card: first, kind: 'player' }, { card: dealerFirst, kind: 'dealer' }, { card: second, kind: 'player' }, { kind: 'hole' });
    player = 2;
    dealer = 1;
    hole = true;
  }

  for (const card of hand.player.slice(player))
    steps.push({ card, kind: 'player' });
  player = Math.max(player, hand.player.length);

  const hidden = hand.dealer[dealer];
  if (hole && hidden) {
    steps.push({ card: hidden, kind: 'reveal' });
    dealer++;
    hole = false;
  }
  for (const card of hand.dealer.slice(dealer))
    steps.push({ card, kind: 'dealer' });
  dealer = Math.max(dealer, hand.dealer.length);

  return { drawn: { dealer, hole, player }, steps };
}
