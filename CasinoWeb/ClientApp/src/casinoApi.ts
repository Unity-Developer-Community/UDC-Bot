import type { BlackjackAction, BlackjackPlay, HubErrorCode, HubState, SlotsHold, SlotsPlay } from '@/protocol';

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

  async getState(): Promise<HubState> {
    return this.call('GetState');
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
