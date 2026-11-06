import type { HubState, SlotsHold, SlotsInfo, SlotsPlay, SlotsStatus } from '@/protocol';

import { CallRefusedError } from '@/casinoApi';

/** What the controller needs from the machine drawn on screen. */
export interface SlotsStage {
  clearOutcome: () => void;
  setHeld: (held: readonly boolean[]) => void;
  /** Shows the payline of a finished spin: a win is highlighted, a loss is not. */
  showOutcome: (outcome: 'jackpot' | 'loss' | 'win') => void;
  /** Rolls the reels that are not held and settles each on its stop; resolves when all have stopped. */
  spinTo: (stops: readonly number[], held: readonly boolean[]) => Promise<void>;
}

/** The hub calls the controller makes; `CasinoApi` provides them. */
export interface SlotsApi {
  getState: () => Promise<HubState>;
  slotsRespin: (offerId: number) => Promise<SlotsPlay>;
  slotsSpin: (bet: number) => Promise<SlotsPlay>;
  slotsToggleHold: (offerId: number, reel: number) => Promise<SlotsHold | null>;
}

export type Phase = 'closed' | 'idle' | 'loading' | 'spinning';

export interface SlotsViewState {
  bet: number;
  hold: SlotsHold | null;
  info: SlotsInfo | null;
  message: string;
  /** Whether the message reports a refusal or a problem rather than the result of a spin. */
  messageIsError: boolean;
  phase: Phase;
  potTokens: number;
}

export interface SlotsHooks {
  onBalance: (balance: number) => void;
  onChange: (state: Readonly<SlotsViewState>) => void;
}

const DEFAULT_BET = 10;
const NO_HOLD = [false, false, false];

export function formatTokens(value: number): string {
  return value.toLocaleString('en-US');
}

export function refusalText(status: SlotsStatus, info: SlotsInfo | null): string {
  switch (status) {
    case 'busy':
      return 'Your previous spin is still being processed.';
    case 'insufficientTokens':
      return 'You do not have enough tokens for this bet.';
    case 'invalidBet':
      return info ? `Bet between ${info.minBet} and ${info.maxBet} tokens.` : 'That bet is not allowed.';
    case 'noHold':
      return 'This hold offer is no longer available.';
    case 'failed':
    case 'ok':
      return 'Something went wrong while playing the slots. Please try again.';
  }
}

/** Describes a finished spin in words, so the result never rests on the animation or a color alone. */
export function outcomeText(play: SlotsPlay): string {
  const result = play.result!;
  if (result.isJackpot)
    return `JACKPOT! You won ${formatTokens(play.payout)} tokens from the shared jackpot.`;
  if (result.multiplier > 0) {
    const what = result.isRespin ? 'Free respin win:' : 'You won';
    return `${what} ${formatTokens(play.payout)} tokens (${result.multiplier}× your bet of ${formatTokens(play.bet)}).`;
  }
  return result.isRespin ? 'The free respin won nothing.' : `No win. You lost ${formatTokens(play.bet)} tokens.`;
}

/**
 * Plays the slots for the screen: sends the player's intents to the server, animates what the server decided and keeps
 * the state the controls show. It holds no game rules, so the browser cannot disagree with the server about an outcome.
 */
export class SlotsGame {
  private current: SlotsViewState = {
    bet: DEFAULT_BET,
    hold: null,
    info: null,
    message: '',
    messageIsError: false,
    phase: 'loading',
    potTokens: 0,
  };

  private readonly api: SlotsApi;
  private readonly stage: SlotsStage;
  private readonly hooks: SlotsHooks;

  constructor(api: SlotsApi, stage: SlotsStage, hooks: SlotsHooks) {
    this.api = api;
    this.stage = stage;
    this.hooks = hooks;
  }

  get state(): Readonly<SlotsViewState> {
    return this.current;
  }

  async load(): Promise<void> {
    try {
      const state = await this.api.getState();
      if (!state.casinoEnabled) {
        this.patch({ phase: 'closed' });
        return;
      }
      this.hooks.onBalance(state.balance);
      this.patch({
        message: '',
        messageIsError: false,
        bet: Math.min(Math.max(DEFAULT_BET, state.slots.minBet), state.slots.maxBet),
        info: state.slots,
        phase: 'idle',
        potTokens: state.slots.potTokens,
      });
    }
    catch (error) {
      this.fail(error);
    }
  }

  async respin(): Promise<void> {
    const { hold, phase } = this.current;
    if (phase !== 'idle' || !hold)
      return;
    await this.play(async () => this.api.slotsRespin(hold.offerId), hold.held);
  }

  setBet(value: number): void {
    const info = this.current.info;
    if (!info || !Number.isFinite(value))
      return;
    this.patch({ bet: Math.min(Math.max(Math.round(value), info.minBet), info.maxBet) });
  }

  async spin(): Promise<void> {
    if (this.current.phase !== 'idle')
      return;
    const { bet } = this.current;
    await this.play(async () => this.api.slotsSpin(bet), NO_HOLD);
  }

  async toggleHold(reel: number): Promise<void> {
    const { hold, phase } = this.current;
    if (phase !== 'idle' || !hold)
      return;
    try {
      const next = await this.api.slotsToggleHold(hold.offerId, reel);
      if (next) {
        this.stage.setHeld(next.held);
        this.patch({ hold: next });
      }
      else {
        this.stage.setHeld(NO_HOLD);
        this.patch({ hold: null, message: refusalText('noHold', this.current.info), messageIsError: true });
      }
    }
    catch (error) {
      this.fail(error);
    }
  }

  private fail(error: unknown): void {
    // Back to a usable machine when it was already loaded, otherwise the screen keeps waiting for the state.
    const phase = this.current.info ? 'idle' : 'loading';
    if (error instanceof CallRefusedError) {
      if (error.code === 'casino-closed') {
        this.patch({ phase: 'closed' });
        return;
      }
      // A lost login is reported by the connection itself, which takes the player to the login page.
      this.patch({
        message: error.code === 'slow-down' ? 'Slow down: too many actions at once.' : 'Your login ended.',
        messageIsError: true,
        phase,
      });
      return;
    }
    this.patch({ message: 'The connection dropped. Check it and try again.', messageIsError: true, phase });
  }

  private patch(changes: Partial<SlotsViewState>): void {
    this.current = { ...this.current, ...changes };
    this.hooks.onChange(this.current);
  }

  private async play(call: () => Promise<SlotsPlay>, held: readonly boolean[]): Promise<void> {
    this.patch({ message: '', messageIsError: false, phase: 'spinning' });
    this.stage.clearOutcome();

    let play: SlotsPlay;
    try {
      play = await call();
    }
    catch (error) {
      this.fail(error);
      return;
    }

    if (play.status !== 'ok' || !play.result) {
      // Only a gone offer ends the offer; a refused bet leaves it open to spend.
      const hold = play.status === 'noHold' ? null : this.current.hold;
      if (!hold)
        this.stage.setHeld(NO_HOLD);
      this.patch({ hold, message: refusalText(play.status, this.current.info), messageIsError: true, phase: 'idle' });
      return;
    }

    this.stage.setHeld(NO_HOLD);
    await this.stage.spinTo(play.result.stops, held);

    const text = outcomeText(play);
    this.hooks.onBalance(play.balance);
    this.patch({
      hold: play.hold,
      message: play.hold ? `${text} Hold the reels you want to keep, then take a free respin.` : text,
      messageIsError: false,
      phase: 'idle',
      potTokens: play.potTokens,
    });
    this.stage.showOutcome(play.result.isJackpot ? 'jackpot' : play.result.multiplier > 0 ? 'win' : 'loss');
  }
}
