import type { BlackjackAction, BlackjackPlay, DailyClaim, HubErrorCode, HubState, PokerActionKind, SlotsHold, SlotsPlay, TableCall, TableSummary, TableUpdate } from '@/protocol';

import { hubErrorCode } from '@/protocol';

/** A call the server refused on purpose, as opposed to a connection that failed. */
export class CallRefusedError extends Error {
  readonly code: HubErrorCode;

  constructor(code: HubErrorCode) {
    super(code);
    this.code = code;
  }
}

/** The part of the connection the games use; `CasinoConnection` provides it. */
export interface HubCaller {
  invoke: <T>(method: string, ...args: unknown[]) => Promise<T>;
  /** Listens to a message the server pushes; returns the function that stops listening. */
  on: <A extends unknown[]>(method: string, handler: (...args: A) => void) => () => void;
  reportSessionLost: () => void;
}

/** The hub methods the games call, with the server's refusals turned into `CallRefusedError`. */
export class CasinoApi {
  private readonly hub: HubCaller;

  constructor(hub: HubCaller) {
    this.hub = hub;
  }

  async blackjackAct(action: BlackjackAction): Promise<BlackjackPlay> {
    return this.call('BlackjackAct', action);
  }

  async blackjackDeal(bet: number): Promise<BlackjackPlay> {
    return this.call('BlackjackDeal', bet);
  }

  /** Claims the daily reward; `notYet` when the last claim, here or in Discord, is too recent. */
  async claimDailyReward(): Promise<DailyClaim> {
    return this.call('ClaimDailyReward');
  }

  async getState(): Promise<HubState> {
    return this.call('GetState');
  }

  /** Calls `handler` with the lobby rows each time a table opens, fills or closes. */
  onTablesChanged(handler: (tables: TableSummary[]) => void): () => void {
    return this.hub.on('TablesChanged', handler);
  }

  /** Calls `handler` with the member's own view each time the table they sit at or watch changes. */
  onTableUpdated(handler: (update: TableUpdate) => void): () => void {
    return this.hub.on('TableUpdated', handler);
  }

  /** Acts at the table; `amount` is the total of the player's bet in this street, for a bet or a raise. */
  async pokerAct(action: PokerActionKind, amount: number): Promise<TableCall> {
    return this.call('PokerAct', action, amount);
  }

  async slotsRespin(offerId: number): Promise<SlotsPlay> {
    return this.call('SlotsRespin', offerId);
  }

  async slotsSpin(bet: number): Promise<SlotsPlay> {
    return this.call('SlotsSpin', bet);
  }

  /** The held reels after the flip, or `null` when the offer is gone. */
  async slotsToggleHold(offerId: number, reel: number): Promise<SlotsHold | null> {
    return this.call('SlotsToggleHold', offerId, reel);
  }

  async tableAddAi(): Promise<TableCall> {
    return this.call('TableAddAi');
  }

  /** Opens a table with the stakes preset of this big blind and seats the member at it. */
  async tableCreate(bigBlind: number): Promise<TableCall> {
    return this.call('TableCreate', bigBlind);
  }

  async tableLeave(): Promise<TableCall> {
    return this.call('TableLeave');
  }

  async tableRemoveAi(): Promise<TableCall> {
    return this.call('TableRemoveAi');
  }

  /** Sits at the seat, or the first free one when `seat` is `null`. */
  async tableSit(tableId: number, seat: number | null): Promise<TableCall> {
    return this.call('TableSit', tableId, seat);
  }

  async tablesList(): Promise<TableSummary[]> {
    return this.call('TablesList');
  }

  async tableUnwatch(): Promise<void> {
    return this.call('TableUnwatch');
  }

  /** Follows a table with public information only. */
  async tableWatch(tableId: number): Promise<TableCall> {
    return this.call('TableWatch', tableId);
  }

  private async call<T>(method: string, ...args: unknown[]): Promise<T> {
    try {
      return await this.hub.invoke<T>(method, ...args);
    }
    catch (error) {
      const code = hubErrorCode(error);
      if (code === undefined)
        throw error;
      if (code === 'session-expired')
        this.hub.reportSessionLost();
      throw new CallRefusedError(code);
    }
  }
}
