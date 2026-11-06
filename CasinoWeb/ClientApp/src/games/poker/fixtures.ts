import type { HubState, PokerHand, PokerOptions, PokerResult, PokerSeat, Stakes, TableCall, TableView } from '@/protocol';

/** Table views for the tests of the poker controller and page. */

export const STAKES: Stakes = { bigBlind: 2, maxCommit: 200, minBalance: 20, smallBlind: 1 };

export function seat(number: number, overrides: Partial<PokerSeat> = {}): PokerSeat {
  return { allIn: false, committed: 0, folded: false, holeCards: null, roundBet: 0, seat: number, ...overrides };
}

export function options(overrides: Partial<PokerOptions> = {}): PokerOptions {
  return { actions: ['fold', 'call', 'raise', 'allIn'], allInTo: 100, callAmount: 2, maxRaiseTo: 100, minRaiseTo: 4, ...overrides };
}

export function hand(overrides: Partial<PokerHand> = {}): PokerHand {
  return {
    bigBlindSeat: 1,
    board: [],
    buttonSeat: 0,
    currentBet: 2,
    options: null,
    pot: 3,
    pots: [{ amount: 3, eligibleSeats: [0, 1] }],
    result: null,
    seats: [
      seat(0, { committed: 1, holeCards: [{ rank: 'A', suit: 'spades' }, { rank: 'K', suit: 'hearts' }], roundBet: 1 }),
      seat(1, { committed: 2, roundBet: 2 }),
    ],
    smallBlindSeat: 0,
    street: 'preflop',
    ...overrides,
  };
}

/** Seat 0 is the viewer, seat 1 is Bob; a hand runs and it is the viewer's turn. */
export function view(overrides: Partial<TableView> = {}): TableView {
  return {
    hand: hand({ options: options() }),
    handRunning: true,
    id: 7,
    maxSeats: 6,
    mySeat: 0,
    nextHandSecondsLeft: null,
    seats: [
      { away: false, isAi: false, name: 'Ada', seat: 0 },
      { away: false, isAi: false, name: 'Bob', seat: 1 },
    ],
    stakes: STAKES,
    turnSeat: 0,
    turnSecondsLeft: 30,
    ...overrides,
  };
}

export function finishedResult(overrides: Partial<PokerResult> = {}): PokerResult {
  return {
    pots: [{ amount: 8, winnerSeats: [0] }],
    showdown: true,
    shown: [
      { bestFive: [], description: 'Pair of Aces', holeCards: [{ rank: 'A', suit: 'spades' }, { rank: 'K', suit: 'hearts' }], seat: 0 },
      { bestFive: [], description: 'High card King', holeCards: [{ rank: 'K', suit: 'clubs' }, { rank: '2', suit: 'clubs' }], seat: 1 },
    ],
    winnings: [{ amount: 8, seat: 0 }, { amount: 0, seat: 1 }],
    ...overrides,
  };
}

/** The viewer won 8 tokens at showdown after putting 4 in. */
export function finishedView(): TableView {
  return view({
    hand: hand({
      board: [{ rank: '2', suit: 'spades' }, { rank: '7', suit: 'diamonds' }, { rank: 'J', suit: 'clubs' }],
      options: null,
      pot: 8,
      result: finishedResult(),
      seats: [seat(0, { committed: 4 }), seat(1, { committed: 4 })],
      street: 'showdown',
    }),
    handRunning: false,
    nextHandSecondsLeft: 5,
    turnSecondsLeft: null,
    turnSeat: null,
  });
}

export function ok(tableView: TableView | null): TableCall {
  return { refusal: null, status: 'ok', view: tableView };
}

export function hubState(overrides: Partial<HubState> = {}): HubState {
  return {
    balance: 500,
    blackjack: { hand: null, minBet: 1 },
    casinoEnabled: true,
    poker: { allowAiSeats: false, stakes: [STAKES], table: null },
    slots: { maxBet: 100, minBet: 1, paytable: [], potTokens: 0, reels: [] },
    ...overrides,
  };
}
