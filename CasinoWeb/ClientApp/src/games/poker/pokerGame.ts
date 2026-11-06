import type {
  HubState,
  PokerActionKind,
  PokerRefusal,
  Stakes,
  TableCall,
  TableStatus,
  TableSummary,
  TableUpdate,
  TableView,
} from '@/protocol';

import { CallRefusedError } from '@/casinoApi';
import { cardLabel } from '@/games/cards/cardText';
import { formatTokens } from '@/games/slots/slotsGame';

/** What the controller needs from the table drawn on screen. */
export interface PokerStage {
  /** Draws the table as the viewer sees it; `null` takes everything off. */
  show: (view: TableView | null) => void;
}

/** The hub calls and pushes the controller uses; `CasinoApi` provides them. */
export interface PokerApi {
  getState: () => Promise<HubState>;
  onTablesChanged: (handler: (tables: TableSummary[]) => void) => () => void;
  onTableUpdated: (handler: (update: TableUpdate) => void) => () => void;
  pokerAct: (action: PokerActionKind, amount: number) => Promise<TableCall>;
  tableAddAi: () => Promise<TableCall>;
  tableCreate: (bigBlind: number) => Promise<TableCall>;
  tableLeave: () => Promise<TableCall>;
  tableRemoveAi: () => Promise<TableCall>;
  tableSit: (tableId: number, seat: number | null) => Promise<TableCall>;
  tablesList: () => Promise<TableSummary[]>;
  tableUnwatch: () => Promise<void>;
  tableWatch: (tableId: number) => Promise<TableCall>;
}

/** `list`: the open tables. `table`: seated at one or watching it. `closed`: the casino is closed and no table is held. */
export type PokerPhase = 'closed' | 'list' | 'loading' | 'table';

export interface PokerViewState {
  allowAiSeats: boolean;
  /** A call is running: the controls wait for its answer. */
  busy: boolean;
  message: string;
  /** Whether the message reports a refusal or a problem rather than the course of the game. */
  messageIsError: boolean;
  /** When the next deal happens, in the clock of `now`; `null` when it waits for players. */
  nextHandEndsAt: number | null;
  phase: PokerPhase;
  stakes: Stakes[];
  tables: TableSummary[];
  /** When the turn runs out, in the clock of `now`; `null` between hands. */
  turnEndsAt: number | null;
  view: TableView | null;
}

export interface PokerHooks {
  onBalance: (balance: number) => void;
  onChange: (state: Readonly<PokerViewState>) => void;
}

export interface PokerDeps {
  api: PokerApi;
  hooks: PokerHooks;
  /** Milliseconds, as `Date.now`; replaceable in tests. */
  stage: PokerStage;
  now?: () => number;
}

/** The turn timer of the server (`TableRules`), which the countdown bar is drawn against. */
export const TURN_SECONDS = 30;

export function secondsLeft(endsAt: number | null, now: number): number | null {
  return endsAt === null ? null : Math.max(0, Math.ceil((endsAt - now) / 1000));
}

export function statusText(status: TableStatus, refusal: PokerRefusal | null): string {
  switch (status) {
    case 'noSuchTable':
      return 'That table is gone.';
    case 'noSuchSeat':
      return 'That seat does not exist.';
    case 'seatTaken':
      return 'That seat is taken.';
    case 'tableFull':
      return 'That table is full.';
    case 'alreadySeated':
      return 'You already sit at a table. Leave it first.';
    case 'notSeated':
      return 'You do not sit at a table.';
    case 'belowMinimum':
      return 'Your balance is too low for these stakes: you need 10 big blinds to sit.';
    case 'tooManyTables':
      return 'Too many tables are open. Join one of them.';
    case 'badStakes':
      return 'Those stakes are not available.';
    case 'aiNotAllowed':
      return 'AI seats are not available here.';
    case 'noAiSeat':
      return 'There is no AI seat to remove.';
    case 'closed':
      return 'The casino is closed right now. You can only leave the table.';
    case 'refused':
      return refusalText(refusal);
    case 'failed':
    case 'ok':
      return 'Something went wrong. Please try again.';
  }
}

export function refusalText(refusal: PokerRefusal | null): string {
  switch (refusal) {
    case 'handOver':
      return 'That hand is over.';
    case 'notYourTurn':
      return 'It is not your turn.';
    case 'amountTooLow':
      return 'That amount is too low.';
    case 'amountTooHigh':
      return 'That amount is too high.';
    case 'insufficientBalance':
      return 'Your balance no longer covers that, so your stake was lowered to what is left. Choose again.';
    case 'notAllowed':
    case null:
      return 'That move is not possible right now.';
  }
}

export function seatName(view: TableView, seat: number): string {
  if (seat === view.mySeat)
    return 'You';
  return view.seats.find(s => s.seat === seat)?.name ?? `Seat ${seat + 1}`;
}

/** The seat that must act and whether it is the viewer's, or `null` between hands. */
export function whoseTurn(view: TableView): { mine: boolean; name: string; seat: number } | null {
  if (view.turnSeat === null)
    return null;
  return { mine: view.turnSeat === view.mySeat, name: seatName(view, view.turnSeat), seat: view.turnSeat };
}

/** The result of a finished hand in words, so nothing rests on the drawing alone. Empty while the hand runs. */
export function resultLines(view: TableView): string[] {
  const result = view.hand?.result;
  if (!result)
    return [];
  const lines: string[] = [];
  const shown = (seat: number): string | undefined => result.shown.find(s => s.seat === seat)?.description;
  for (const { amount, seat } of result.winnings.filter(w => w.amount > 0)) {
    const how = shown(seat);
    const subject = seat === view.mySeat ? 'You win' : `${seatName(view, seat)} wins`;
    let reason = '';
    if (how !== undefined)
      reason = ` with ${how}`;
    else if (!result.showdown)
      reason = ' (everyone else folded)';
    lines.push(`${subject} ${formatTokens(amount)} tokens${reason}.`);
  }
  for (const hand of result.shown) {
    if (!result.winnings.some(w => w.seat === hand.seat && w.amount > 0))
      lines.push(`${seatName(view, hand.seat)}: ${hand.holeCards.map(cardLabel).join(' ')} (${hand.description}).`);
  }
  const mine = view.hand?.seats.find(s => s.seat === view.mySeat);
  if (mine) {
    const net = (result.winnings.find(w => w.seat === mine.seat)?.amount ?? 0) - mine.committed;
    if (net > 0)
      lines.push(`Your result: +${formatTokens(net)} tokens.`);
    else if (net < 0)
      lines.push(`Your result: ${formatTokens(net)} tokens.`);
    else
      lines.push('Your result: even.');
  }
  return lines;
}

/**
 * Plays poker for the screen: sends the player's intents to the server, shows the table views the server pushes and keeps
 * the state the controls show. It holds no game rules: what the player may do comes with each view.
 */
export class PokerGame {
  private current: PokerViewState = {
    allowAiSeats: false,
    busy: false,
    message: '',
    messageIsError: false,
    nextHandEndsAt: null,
    phase: 'loading',
    stakes: [],
    tables: [],
    turnEndsAt: null,
    view: null,
  };

  private readonly api: PokerApi;
  private readonly hooks: PokerHooks;
  private readonly now: () => number;
  private readonly stage: PokerStage;
  private unsubscribe: (() => void)[] = [];
  /** Counts the table pushes, so an answer that was overtaken by a newer push does not replace it. */
  private pushes = 0;
  /** A sit, create or watch is running: the table push that comes before its answer is the table. */
  private joining = false;
  private leaving = false;

  constructor({ api, hooks, now = Date.now, stage }: PokerDeps) {
    this.api = api;
    this.hooks = hooks;
    this.now = now;
    this.stage = stage;
  }

  get state(): Readonly<PokerViewState> {
    return this.current;
  }

  async act(kind: PokerActionKind, amount = 0): Promise<void> {
    const { view } = this.current;
    const options = view?.hand?.options;
    if (!view || !options || view.turnSeat !== view.mySeat || !options.actions.includes(kind))
      return;
    let total = 0;
    if (kind === 'allIn')
      total = options.allInTo;
    else if (kind === 'bet' || kind === 'raise')
      total = Math.min(Math.max(Math.round(amount), options.minRaiseTo), options.maxRaiseTo);
    await this.callTable(async () => this.api.pokerAct(kind, total));
  }

  async addAi(): Promise<void> {
    if (!this.current.allowAiSeats || this.current.view?.mySeat == null)
      return;
    await this.callTable(async () => this.api.tableAddAi());
  }

  async create(bigBlind: number): Promise<void> {
    if (this.current.phase !== 'list')
      return;
    await this.join(async () => this.api.tableCreate(bigBlind));
  }

  destroy(): void {
    for (const stop of this.unsubscribe)
      stop();
    this.unsubscribe = [];
  }

  async leave(): Promise<void> {
    const { busy, view } = this.current;
    if (!view || busy)
      return;
    this.leaving = true;
    this.patch({ busy: true, message: '', messageIsError: false });
    try {
      if (view.mySeat === null)
        await this.api.tableUnwatch();
      else
        await this.api.tableLeave();
    }
    catch (error) {
      this.leaving = false;
      this.fail(error);
      return;
    }
    this.leaving = false;
    await this.backToList('');
  }

  async load(): Promise<void> {
    try {
      this.listen();
      const state = await this.api.getState();
      const { allowAiSeats, stakes, table } = state.poker;
      this.patch({ allowAiSeats, stakes });
      if (table) {
        this.setView(table);
        this.patch({
          message: state.casinoEnabled ? '' : 'The casino is closed right now. You can only leave the table.',
          messageIsError: !state.casinoEnabled,
          phase: 'table',
        });
        this.hooks.onBalance(state.balance);
        return;
      }
      if (!state.casinoEnabled) {
        this.patch({ phase: 'closed' });
        return;
      }
      this.hooks.onBalance(state.balance);
      await this.backToList('');
    }
    catch (error) {
      this.fail(error);
    }
  }

  async refreshTables(): Promise<void> {
    try {
      this.patch({ tables: await this.api.tablesList() });
    }
    catch (error) {
      this.fail(error);
    }
  }

  async removeAi(): Promise<void> {
    if (!this.current.allowAiSeats || this.current.view?.mySeat == null)
      return;
    await this.callTable(async () => this.api.tableRemoveAi());
  }

  /** Sits at a table from the list, or from the table being watched. */
  async sit(tableId: number, seat: number | null = null): Promise<void> {
    const { phase, view } = this.current;
    if (phase === 'table' ? view?.mySeat !== null : phase !== 'list')
      return;
    await this.join(async () => this.api.tableSit(tableId, seat));
  }

  async watch(tableId: number): Promise<void> {
    if (this.current.phase !== 'list')
      return;
    await this.join(async () => this.api.tableWatch(tableId));
  }

  private answer(answer: TableCall, pushesBefore: number): void {
    // A push that came in the meantime is newer than the answer of the call that triggered it.
    if (answer.view && this.pushes === pushesBefore)
      this.setView(answer.view);
    const ok = answer.status === 'ok';
    this.patch({
      busy: false,
      message: ok ? '' : statusText(answer.status, answer.refusal),
      messageIsError: !ok,
      phase: this.current.view ? 'table' : this.current.phase,
    });
  }

  private async backToList(message: string): Promise<void> {
    this.setView(null);
    this.patch({ busy: false, message, messageIsError: false, phase: 'list' });
    await this.refreshTables();
    this.refreshBalance();
  }

  /** Runs a call at the table the viewer is at and shows the answer. */
  private async callTable(call: () => Promise<TableCall>): Promise<void> {
    if (this.current.busy)
      return;
    this.patch({ busy: true, message: '', messageIsError: false });
    const pushesBefore = this.pushes;
    let answer: TableCall;
    try {
      answer = await call();
    }
    catch (error) {
      this.fail(error);
      return;
    }
    this.answer(answer, pushesBefore);
  }

  private fail(error: unknown): void {
    this.joining = false;
    const { phase, view } = this.current;
    // Back to a usable screen when it was loaded, otherwise the screen keeps waiting for the state.
    const next: PokerPhase = phase === 'loading' ? 'loading' : view ? 'table' : 'list';
    if (error instanceof CallRefusedError) {
      if (error.code === 'casino-closed') {
        this.patch(view
          ? { busy: false, message: 'The casino is closed right now. You can only leave the table.', messageIsError: true, phase: 'table' }
          : { busy: false, phase: 'closed' });
        return;
      }
      // A lost login is reported by the connection itself, which takes the player to the login page.
      this.patch({
        busy: false,
        message: error.code === 'slow-down' ? 'Slow down: too many actions at once.' : 'Your login ended.',
        messageIsError: true,
        phase: next,
      });
      return;
    }
    this.patch({ busy: false, message: 'The connection dropped. Check it and try again.', messageIsError: true, phase: next });
  }

  private async join(call: () => Promise<TableCall>): Promise<void> {
    if (this.current.busy)
      return;
    this.joining = true;
    this.patch({ busy: true, message: '', messageIsError: false });
    const pushesBefore = this.pushes;
    let answer: TableCall;
    try {
      answer = await call();
    }
    catch (error) {
      this.fail(error);
      return;
    }
    this.joining = false;
    this.answer(answer, pushesBefore);
    // The list the player chose from may be out of date.
    if (answer.status === 'ok')
      this.refreshBalance();
    else if (!answer.view)
      await this.refreshTables();
  }

  private listen(): void {
    if (this.unsubscribe.length > 0)
      return;
    this.unsubscribe = [
      this.api.onTablesChanged(tables => this.patch({ tables })),
      this.api.onTableUpdated(update => this.onUpdate(update)),
    ];
  }

  private onUpdate({ tableId, view }: TableUpdate): void {
    const here = this.current.view;
    if (view) {
      if (here?.id !== tableId && !this.joining)
        return;
      this.pushes++;
      this.setView(view);
      this.patch({ phase: 'table' });
      return;
    }
    if (here?.id !== tableId || this.leaving)
      return;
    this.pushes++;
    void this.backToList('You are no longer at that table: it closed, or you were stood up after missed turns.');
  }

  private patch(changes: Partial<PokerViewState>): void {
    this.current = { ...this.current, ...changes };
    this.hooks.onChange(this.current);
  }

  /** The blinds and bets leave the balance during the hand and the winnings come back at its end; the views carry no balance, so it is asked for. */
  private refreshBalance(): void {
    this.api.getState().then(state => this.hooks.onBalance(state.balance), () => {});
  }

  private setView(view: TableView | null): void {
    const before = this.current.view;
    const now = this.now();
    this.stage.show(view);
    this.patch({
      nextHandEndsAt: view?.nextHandSecondsLeft == null ? null : now + view.nextHandSecondsLeft * 1000,
      turnEndsAt: view?.turnSecondsLeft == null ? null : now + view.turnSecondsLeft * 1000,
      view,
    });
    if (view?.hand?.street === 'showdown' && before?.hand?.street !== 'showdown')
      this.refreshBalance();
  }
}
