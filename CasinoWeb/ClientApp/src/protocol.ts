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

/** The messages of the `HubException`s a refused call carries (`CasinoWeb/Hubs/HubErrors.cs`). */
export const HUB_ERRORS = ['casino-closed', 'session-expired', 'slow-down'] as const;
export type HubErrorCode = typeof HUB_ERRORS[number];

/** The server's reason for refusing a call, when the error is one of its refusals (the library wraps the message in a longer text). */
export function hubErrorCode(error: unknown): HubErrorCode | undefined {
  const message = error instanceof Error ? error.message : '';
  return HUB_ERRORS.find(code => message.includes(code));
}
