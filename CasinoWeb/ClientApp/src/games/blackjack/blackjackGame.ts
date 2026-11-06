import type { BlackjackAction, BlackjackHand, BlackjackPlay, BlackjackStatus, HubState } from '@/protocol';

import { CallRefusedError } from '@/casinoApi';
import { formatTokens } from '@/games/slots/slotsGame';

/** What the controller needs from the table drawn on screen. */
export interface BlackjackStage {
  /** Takes every card off the table. */
  clear: () => void;
  /** Brings the table to `hand`, dealing the cards it does not show yet; resolves once they lie still. Without `animate` they appear at once. */
  show: (hand: BlackjackHand, animate: boolean) => Promise<void>;
  /** The result banner of a finished hand, or none. */
  showOutcome: (outcome: BlackjackOutcome | null) => void;
}

/** The hub calls the controller makes; `CasinoApi` provides them. */
export interface BlackjackApi {
  blackjackAct: (action: BlackjackAction) => Promise<BlackjackPlay>;
  blackjackDeal: (bet: number) => Promise<BlackjackPlay>;
  getState: () => Promise<HubState>;
}

/** `betting`: no hand is open, a new one can be dealt. `playing`: the player's turn. `busy`: a call or an animation is running. */
export type Phase = 'betting' | 'busy' | 'closed' | 'loading' | 'playing';

export type BlackjackOutcome = 'blackjack' | 'bust' | 'loss' | 'tie' | 'win';

export interface BlackjackViewState {
  /** The balance as last reported by the server, which caps the bet. */
  balance: number;
  bet: number;
  hand: BlackjackHand | null;
  message: string;
  /** Whether the message reports a refusal or a problem rather than the course of the hand. */
  messageIsError: boolean;
  minBet: number;
  phase: Phase;
}

export interface BlackjackHooks {
  onBalance: (balance: number) => void;
  onChange: (state: Readonly<BlackjackViewState>) => void;
}

const DEFAULT_BET = 10;

export function outcomeOf(hand: BlackjackHand): BlackjackOutcome | null {
  if (!hand.finished || hand.result === null)
    return null;
  if (hand.result === 'win' && hand.player.length === 2 && hand.playerValue === 21)
    return 'blackjack';
  if (hand.result === 'loss' && hand.playerValue > 21)
    return 'bust';
  return hand.result;
}

/** Describes a finished hand in words, so the result never rests on the table drawing alone. */
export function outcomeText(hand: BlackjackHand): string {
  const bet = formatTokens(hand.bet);
  const won = formatTokens(hand.net);
  switch (outcomeOf(hand)) {
    case 'blackjack':
      return `Blackjack! You win ${won} tokens.`;
    case 'bust':
      return `Bust with ${hand.playerValue}. You lose ${bet} tokens.`;
    case 'tie':
      return `Push: you and the dealer both have ${hand.playerValue}. Your bet of ${bet} tokens comes back.`;
    case 'win':
      return hand.dealerValue > 21
        ? `The dealer busts with ${hand.dealerValue}. You win ${won} tokens.`
        : `You win ${won} tokens: ${hand.playerValue} against the dealer's ${hand.dealerValue}.`;
    case 'loss':
      return `The dealer wins with ${hand.dealerValue} against your ${hand.playerValue}. You lose ${bet} tokens.`;
    case null:
      return '';
  }
}

export function refusalText(status: BlackjackStatus): string {
  switch (status) {
    case 'insufficientTokens':
      return 'You do not have enough tokens for this.';
    case 'invalidBet':
      return 'That bet is not allowed.';
    case 'noHand':
      return 'This hand is over: it was settled after a few minutes without action, or the casino restarted and gave the bet back. Deal a new one.';
    case 'notAllowed':
      return 'That move is not possible right now.';
    case 'handInProgress':
      return 'You already have a hand in progress: finish it first.';
    case 'failed':
    case 'ok':
      return 'Something went wrong while playing blackjack. Please try again.';
  }
}

function turnText(hand: BlackjackHand): string {
  return hand.actions.includes('doubleDown') ? 'Your move: hit, stand or double down.' : 'Your move: hit or stand.';
}

/**
 * Plays blackjack for the screen: sends the player's intents to the server, deals what the server decided and keeps the
 * state the controls show. It holds no game rules, so the browser cannot disagree with the server about a hand.
 */
export class BlackjackGame {
  private current: BlackjackViewState = {
    balance: 0,
    bet: DEFAULT_BET,
    hand: null,
    message: '',
    messageIsError: false,
    minBet: 1,
    phase: 'loading',
  };

  private readonly api: BlackjackApi;
  private readonly stage: BlackjackStage;
  private readonly hooks: BlackjackHooks;

  constructor(api: BlackjackApi, stage: BlackjackStage, hooks: BlackjackHooks) {
    this.api = api;
    this.stage = stage;
    this.hooks = hooks;
  }

  get state(): Readonly<BlackjackViewState> {
    return this.current;
  }

  async act(action: BlackjackAction): Promise<void> {
    const { hand, phase } = this.current;
    if (phase !== 'playing' || !hand?.actions.includes(action))
      return;
    await this.play(async () => this.api.blackjackAct(action));
  }

  /** Raises the balance that caps the bet after a claim made elsewhere on the page; the header already shows it. */
  addTokens(tokens: number): void {
    this.patch({ balance: this.current.balance + tokens });
  }

  async deal(): Promise<void> {
    if (this.current.phase !== 'betting')
      return;
    const { bet } = this.current;
    this.stage.clear();
    this.stage.showOutcome(null);
    this.patch({ hand: null });
    await this.play(async () => this.api.blackjackDeal(bet));
  }

  async load(): Promise<void> {
    try {
      const state = await this.api.getState();
      if (!state.casinoEnabled) {
        this.patch({ phase: 'closed' });
        return;
      }
      this.hooks.onBalance(state.balance);
      const { hand, minBet } = state.blackjack;
      this.patch({ balance: state.balance, bet: this.clampBet(this.current.bet, minBet, state.balance), minBet });
      await this.resume(hand, '');
    }
    catch (error) {
      this.fail(error);
    }
  }

  setBet(value: number): void {
    if (this.current.phase === 'loading' || !Number.isFinite(value))
      return;
    this.patch({ bet: this.clampBet(value, this.current.minBet, this.current.balance) });
  }

  private clampBet(value: number, minBet: number, balance: number): number {
    return Math.min(Math.max(Math.round(value), minBet), Math.max(minBet, balance));
  }

  private fail(error: unknown): void {
    const { hand, phase } = this.current;
    // Back to a usable table when it was loaded, otherwise the screen keeps waiting for the state.
    const next: Phase = phase === 'loading' ? 'loading' : hand && !hand.finished ? 'playing' : 'betting';
    if (error instanceof CallRefusedError) {
      if (error.code === 'casino-closed') {
        this.patch({ phase: 'closed' });
        return;
      }
      // A lost login is reported by the connection itself, which takes the player to the login page.
      this.patch({
        message: error.code === 'slow-down' ? 'Slow down: too many actions at once.' : 'Your login ended.',
        messageIsError: true,
        phase: next,
      });
      return;
    }
    this.patch({ message: 'The connection dropped. Check it and try again.', messageIsError: true, phase: next });
  }

  private patch(changes: Partial<BlackjackViewState>): void {
    this.current = { ...this.current, ...changes };
    this.hooks.onChange(this.current);
  }

  private async play(call: () => Promise<BlackjackPlay>): Promise<void> {
    this.patch({ message: '', messageIsError: false, phase: 'busy' });

    let play: BlackjackPlay;
    try {
      play = await call();
    }
    catch (error) {
      this.fail(error);
      return;
    }

    switch (play.status) {
      case 'ok': {
        const hand = play.hand!;
        // While the cards are still moving the balance must not give the result away; a deal or a double down has
        // already taken the bet, which the player expects to see at once.
        if (!hand.finished)
          this.report(play.balance);
        await this.stage.show(hand, true);
        if (hand.finished)
          this.report(play.balance);
        this.settle(hand, '');
        return;
      }
      case 'handInProgress':
        this.report(play.balance);
        await this.resume(play.hand, refusalText(play.status), true);
        return;
      case 'insufficientTokens':
      case 'notAllowed': {
        const hand = play.hand ?? this.current.hand;
        if (play.hand)
          this.report(play.balance);
        this.patch({ hand, message: refusalText(play.status), messageIsError: true, phase: hand && !hand.finished ? 'playing' : 'betting' });
        return;
      }
      case 'noHand':
      case 'failed':
        // The server may have settled the hand or given it back: what it says now is the truth.
        await this.resync(refusalText(play.status));
        return;
      case 'invalidBet':
        this.patch({ message: refusalText(play.status), messageIsError: true, phase: 'betting' });
    }
  }

  private report(balance: number): void {
    this.hooks.onBalance(balance);
    this.patch({ balance, bet: this.clampBet(this.current.bet, this.current.minBet, balance) });
  }

  private async resume(hand: BlackjackHand | null, message: string, isError = false): Promise<void> {
    if (!hand) {
      this.stage.clear();
      this.stage.showOutcome(null);
      this.patch({ hand: null, message, messageIsError: isError, phase: 'betting' });
      return;
    }
    this.stage.clear();
    await this.stage.show(hand, false);
    this.settle(hand, message, isError);
  }

  private async resync(message: string): Promise<void> {
    try {
      const state = await this.api.getState();
      this.report(state.balance);
      await this.resume(state.blackjack.hand, message, true);
    }
    catch (error) {
      this.fail(error);
    }
  }

  private settle(hand: BlackjackHand, message: string, isError = false): void {
    if (hand.finished) {
      this.stage.showOutcome(outcomeOf(hand));
      this.patch({ hand, message: message || outcomeText(hand), messageIsError: isError, phase: 'betting' });
      return;
    }
    this.patch({ hand, message: message || turnText(hand), messageIsError: isError, phase: 'playing' });
  }
}
