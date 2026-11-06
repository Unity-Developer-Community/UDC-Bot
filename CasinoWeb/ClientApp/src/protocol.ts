/** The shapes the server's `CasinoHub` sends (`CasinoWeb/Hubs/HubModels.cs`). */

export type SlotSymbol = 'bell' | 'cherry' | 'diamond' | 'grape' | 'lemon' | 'seven' | 'star';

export interface PaytableLine {
  count: number;
  isJackpot: boolean;
  multiplier: number;
  symbol: SlotSymbol;
}

export interface SlotsInfo {
  maxBet: number;
  minBet: number;
  paytable: PaytableLine[];
  potTokens: number;
  /** The symbols of each reel strip in order; a result's stop is an index into its reel. */
  reels: SlotSymbol[][];
}

export interface HubState {
  balance: number;
  blackjack: BlackjackInfo;
  casinoEnabled: boolean;
  slots: SlotsInfo;
}

export interface SlotsHold {
  held: boolean[];
  offerId: number;
}

export interface SlotsResult {
  isJackpot: boolean;
  isRespin: boolean;
  multiplier: number;
  stops: number[];
  symbols: SlotSymbol[];
}

export type SlotsStatus = 'busy' | 'failed' | 'insufficientTokens' | 'invalidBet' | 'noHold' | 'ok';

/** Only `status` is meaningful unless it is `ok`. */
export interface SlotsPlay {
  balance: number;
  bet: number;
  hold: SlotsHold | null;
  net: number;
  payout: number;
  potTokens: number;
  result: SlotsResult | null;
  status: SlotsStatus;
}

export type CardSuit = 'clubs' | 'diamonds' | 'hearts' | 'spades';

export interface Card {
  /** `A`, `2`–`10`, `J`, `Q` or `K`. */
  rank: string;
  suit: CardSuit;
}

export type BlackjackAction = 'doubleDown' | 'hit' | 'stand';

export interface BlackjackHand {
  /** What the player can do now; `doubleDown` only when the balance covers a second bet. Empty once finished. */
  actions: BlackjackAction[];
  /** The tokens at stake, doubled by a double down. */
  bet: number;
  /** Tokens given back to the balance at the end: twice the bet for a win, the bet for a tie, 0 for a loss. */
  credited: number;
  /** The dealer's visible cards: only the first while the player acts, all once finished. */
  dealer: Card[];
  dealerValue: number;
  finished: boolean;
  handId: string;
  /** What the hand won or lost, as recorded in the history. */
  net: number;
  player: Card[];
  playerValue: number;
  result: 'loss' | 'tie' | 'win' | null;
}

export interface BlackjackInfo {
  /** The open hand, so a page closed mid-hand resumes it. */
  hand: BlackjackHand | null;
  minBet: number;
}

export type BlackjackStatus = 'failed' | 'handInProgress' | 'insufficientTokens' | 'invalidBet' | 'noHand' | 'notAllowed' | 'ok';

/** `hand` is set for `ok`, and for a refusal that leaves the hand open. */
export interface BlackjackPlay {
  balance: number;
  hand: BlackjackHand | null;
  status: BlackjackStatus;
}

/** The daily reward: its amount and the seconds until the next claim (0 when it can be claimed now). */
export interface DailyInfo {
  secondsUntilNext: number;
  tokens: number;
}

export type DailyClaimStatus = 'notYet' | 'ok';

/** `awarded` is 0 unless `ok`; `daily` always holds the seconds until the next claim. */
export interface DailyClaim {
  awarded: number;
  balance: number;
  daily: DailyInfo;
  status: DailyClaimStatus;
}

/** The messages of the `HubException`s a refused call carries (`CasinoWeb/Hubs/HubErrors.cs`). */
export const HUB_ERRORS = ['casino-closed', 'session-expired', 'slow-down'] as const;
export type HubErrorCode = typeof HUB_ERRORS[number];

/** The server's reason for refusing a call, when the error is one of its refusals (the library wraps the message in a longer text). */
export function hubErrorCode(error: unknown): HubErrorCode | undefined {
  const message = error instanceof Error ? error.message : '';
  return HUB_ERRORS.find(code => message.includes(code));
}
