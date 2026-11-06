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
  poker: PokerInfo;
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

/** The big and small blind of a stakes preset a table is created with, and what it asks of a player. */
export interface Stakes {
  bigBlind: number;
  /** The most a player puts in during one hand. */
  maxCommit: number;
  /** The balance needed to sit and to be dealt into a hand. */
  minBalance: number;
  smallBlind: number;
}

export interface PokerInfo {
  allowAiSeats: boolean;
  stakes: Stakes[];
  /** The table the member sits at, as they see it, so a reload goes back to it. */
  table: TableView | null;
}

export interface TableSummary {
  id: number;
  handRunning: boolean;
  maxSeats: number;
  seatsTaken: number;
  stakes: Stakes;
}

/** A seat that is taken; the viewer's own is `TableView.mySeat`. */
export interface TableSeat {
  name: string;
  away: boolean;
  isAi: boolean;
  seat: number;
}

export type PokerStreet = 'flop' | 'preflop' | 'river' | 'showdown' | 'turn';

export type PokerActionKind = 'allIn' | 'bet' | 'call' | 'check' | 'fold' | 'raise';

export interface PokerSeat {
  allIn: boolean;
  /** What the seat put in during the whole hand. */
  committed: number;
  folded: boolean;
  /** The viewer's own cards, or those shown at showdown; `null` when hidden from this viewer. */
  holeCards: Card[] | null;
  /** What the seat put in during the current street. */
  roundBet: number;
  seat: number;
}

/** What the viewer may do; set only on their turn. The raise amounts are totals for the street. */
export interface PokerOptions {
  actions: PokerActionKind[];
  allInTo: number;
  callAmount: number;
  maxRaiseTo: number;
  /** 0 when neither a bet nor a raise is allowed. */
  minRaiseTo: number;
}

export interface PokerShownHand {
  bestFive: Card[];
  /** The hand in words, such as "Pair of Aces". */
  description: string;
  holeCards: Card[];
  seat: number;
}

export interface PokerResult {
  pots: { amount: number; winnerSeats: number[] }[];
  /** False when everyone else folded: nobody shows cards. */
  showdown: boolean;
  shown: PokerShownHand[];
  /** Per seat, the tokens credited back (0 for a loser). */
  winnings: { amount: number; seat: number }[];
}

export interface PokerHand {
  bigBlindSeat: number;
  board: Card[];
  buttonSeat: number;
  currentBet: number;
  options: PokerOptions | null;
  /** Everything in the middle, bets of the current street included. */
  pot: number;
  pots: { amount: number; eligibleSeats: number[] }[];
  result: PokerResult | null;
  seats: PokerSeat[];
  smallBlindSeat: number;
  /** `showdown` means the hand is over. */
  street: PokerStreet;
}

export interface TableView {
  id: number;
  hand: PokerHand | null;
  handRunning: boolean;
  maxSeats: number;
  /** Between hands, the seconds until the next deal; `null` when it waits for players. */
  nextHandSecondsLeft: number | null;
  /** The viewer's seat; `null` for a watcher. */
  mySeat: number | null;
  seats: TableSeat[];
  stakes: Stakes;
  /** The seat that must act; `null` between hands. */
  turnSeat: number | null;
  turnSecondsLeft: number | null;
}

export type TableStatus
  = | 'aiNotAllowed'
    | 'alreadySeated'
    | 'badStakes'
    | 'belowMinimum'
    | 'closed'
    | 'failed'
    | 'noAiSeat'
    | 'noSuchSeat'
    | 'noSuchTable'
    | 'notSeated'
    | 'ok'
    | 'refused'
    | 'seatTaken'
    | 'tableFull'
    | 'tooManyTables';

export type PokerRefusal = 'amountTooHigh' | 'amountTooLow' | 'handOver' | 'insufficientBalance' | 'notAllowed' | 'notYourTurn';

/** The answer to a table call: the table after it, from the caller's side. `refusal` is set only for `refused`. */
export interface TableCall {
  refusal: PokerRefusal | null;
  status: TableStatus;
  view: TableView | null;
}

/** Pushed as `TableUpdated`; `view` is `null` when the table closed or the member is no longer at it. */
export interface TableUpdate {
  tableId: number;
  view: TableView | null;
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
