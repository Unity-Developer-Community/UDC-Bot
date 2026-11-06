import type { PokerApi, PokerStage, PokerViewState } from '@/games/poker/pokerGame';
import type { PokerActionKind, TableView } from '@/protocol';

import { h } from '@/dom';
import { cardLabel } from '@/games/cards/cardText';
import { PokerGame, resultLines, seatName, secondsLeft, TURN_SECONDS, whoseTurn } from '@/games/poker/pokerGame';
import { formatTokens } from '@/games/slots/slotsGame';
import { renderTables, stakesLabel } from '@/views/tables';

export interface PokerScreen {
  element: HTMLElement;
  destroy: () => void;
}

export interface PokerScreenDeps {
  api: PokerApi;
  onBack: () => void;
  onBalance: (balance: number) => void;
  /** Starts the table drawing. Replaceable in tests, where there is no canvas. */
  mountStage?: (container: HTMLElement) => Promise<{ destroy: () => void; stage: PokerStage }>;
  /** Milliseconds, as `Date.now`; replaceable in tests. */
  now?: () => number;
}

/** Loads Phaser only when a table is opened, so the lobby stays light. */
async function mountPhaserStage(container: HTMLElement): Promise<{ destroy: () => void; stage: PokerStage }> {
  const [{ default: Phaser }, { PokerScene }, { STAGE }] = await Promise.all([import('phaser'), import('@/games/poker/pokerScene'), import('@/games/poker/layout')]);
  return new Promise((resolve) => {
    let game: { destroy: (removeCanvas: boolean) => void } | undefined;
    const scene = new PokerScene(stage => resolve({ destroy: () => game?.destroy(true), stage }));
    game = new Phaser.Game({
      audio: { noAudio: true },
      backgroundColor: getComputedStyle(document.documentElement).getPropertyValue('--color-surface').trim(),
      height: STAGE.height,
      parent: container,
      scale: { autoCenter: Phaser.Scale.CENTER_HORIZONTALLY, mode: Phaser.Scale.FIT },
      scene,
      type: Phaser.AUTO,
      width: STAGE.width,
    });
  });
}

function renderRules(): HTMLElement {
  return h(
    'details',
    { class: 'paytable' },
    h('summary', {}, 'Rules'),
    h('p', {}, 'No-limit Texas Hold\'em for up to 6 players. Everyone gets two private cards; five shared cards come in three steps (the flop of three, then the turn and the river), with a round of betting before the flop and after each step. The best five cards out of your two and the board win the pot.'),
    h(
      'ul',
      {},
      h('li', {}, 'Blinds: two players post forced bets before the cards are dealt; the dealer button moves one seat each hand.'),
      h('li', {}, 'Bet and raise amounts are the total you have in the pot for that round. A raise is at least as big as the previous one.'),
      h('li', {}, 'Every blind, bet and call leaves your balance at once. You can put in at most 100 big blinds per hand, and you need 10 big blinds to sit.'),
      h('li', {}, 'You have 30 seconds per move: the server checks for you if it can, otherwise folds. Two missed moves in a row stand you up.'),
      h('li', {}, 'If you lose your connection, your seat is kept for 60 seconds; your moves still time out meanwhile.'),
    ),
  );
}

function seatFlags(view: TableView, seat: number): string[] {
  const flags: string[] = [];
  const taken = view.seats.find(s => s.seat === seat);
  const inHand = view.hand?.seats.find(s => s.seat === seat);
  if (view.turnSeat === seat)
    flags.push('to act');
  if (inHand?.folded)
    flags.push('folded');
  else if (inHand?.allIn)
    flags.push('all in');
  if (view.hand?.buttonSeat === seat)
    flags.push('dealer');
  if (view.hand?.smallBlindSeat === seat)
    flags.push('small blind');
  if (view.hand?.bigBlindSeat === seat)
    flags.push('big blind');
  if (taken?.isAi)
    flags.push('AI');
  if (taken?.away)
    flags.push('away');
  return flags;
}

/**
 * The poker page: the open tables and, once seated or watching, the table (a Phaser canvas) with the actions around it as
 * ordinary form controls, which keep the keyboard and screen readers working, and the seats, cards and result repeated in
 * words. The controller owns the behavior; this only shows its state.
 */
export function renderPokerScreen(deps: PokerScreenDeps): PokerScreen {
  const mountStage = deps.mountStage ?? mountPhaserStage;
  const now = deps.now ?? Date.now;
  let destroyed = false;
  let destroyStage: () => void = () => {};

  const stageBox = h('div', { class: 'poker-stage' });
  // The canvas starts with the first table to draw. The controls work before it is up; what they ask it to draw waits for it.
  let mounted: Promise<PokerStage> | undefined;
  async function table(): Promise<PokerStage> {
    mounted ??= mountStage(stageBox).then((result) => {
      if (destroyed)
        result.destroy();
      else
        destroyStage = result.destroy;
      return result.stage;
    });
    return mounted;
  }
  // A table that fails to load must not hold up the game: everything is also shown in words, so play goes on without it.
  const ignore = (): void => {};
  const stage: PokerStage = {
    show: (view) => {
      if (view || mounted)
        void table().then(s => destroyed || s.show(view)).catch(ignore);
    },
  };

  const game = new PokerGame({ api: deps.api, hooks: { onBalance: deps.onBalance, onChange: state => render(state) }, now, stage });

  const backButton = h('button', { class: 'button secondary', type: 'button' }, '← Lobby');
  backButton.addEventListener('click', deps.onBack);
  const leaveButton = h('button', { 'class': 'button secondary', 'data-testid': 'leave', 'type': 'button' }, 'Leave table');
  leaveButton.addEventListener('click', () => void game.leave());

  const tables = renderTables({
    onCreate: bigBlind => void game.create(bigBlind),
    onSit: id => void game.sit(id),
    onWatch: id => void game.watch(id),
  });

  const loading = h('p', { class: 'message' }, 'Loading…');
  const retryButton = h('button', { class: 'button', type: 'button' }, 'Try again');
  retryButton.addEventListener('click', () => void game.load());
  const message = h('p', { 'class': 'message', 'data-testid': 'message', 'role': 'status' });

  // The table.
  const info = h('p', { 'data-testid': 'info' });
  const turn = h('p', { 'class': 'turn', 'data-testid': 'turn', 'role': 'status' });
  const turnBar = h('progress', { 'aria-hidden': 'true', 'max': String(TURN_SECONDS), 'value': '0' });
  const board = h('p', { 'data-testid': 'board' });
  const seats = h('ul', { 'class': 'poker-seats', 'data-testid': 'seats' });
  const results = h('ul', { 'class': 'results', 'data-testid': 'results', 'role': 'status' });

  const actionButton = (action: PokerActionKind, label: string): HTMLButtonElement => {
    const button = h('button', { 'class': 'button', 'data-action': action, 'type': 'button' }, label);
    button.addEventListener('click', () => void game.act(action));
    return button;
  };
  const foldButton = actionButton('fold', 'Fold');
  const checkButton = actionButton('check', 'Check');
  const callButton = actionButton('call', 'Call');
  const allInButton = actionButton('allIn', 'All in');
  const raiseInput = h('input', { 'aria-label': 'Amount', 'class': 'bet-input', 'data-testid': 'raise', 'inputmode': 'numeric', 'name': 'raise', 'type': 'number' });
  const raiseButton = h('button', { 'class': 'button', 'data-action': 'raise', 'type': 'submit' }, 'Raise to');
  const minButton = h('button', { class: 'button secondary small', type: 'button' }, 'Min');
  const maxButton = h('button', { class: 'button secondary small', type: 'button' }, 'Max');
  const raiseForm = h('form', { class: 'bet' }, raiseInput, minButton, maxButton, raiseButton);
  minButton.addEventListener('click', () => {
    raiseInput.value = raiseInput.min;
  });
  maxButton.addEventListener('click', () => {
    raiseInput.value = raiseInput.max;
  });
  raiseForm.addEventListener('submit', (event) => {
    event.preventDefault();
    const options = game.state.view?.hand?.options;
    if (!options)
      return;
    void game.act(options.actions.includes('bet') ? 'bet' : 'raise', Number(raiseInput.value));
  });
  const controls = h('div', { class: 'poker-controls' }, h('div', { class: 'spin-row' }, foldButton, checkButton, callButton, allInButton), raiseForm);

  const addAiButton = h('button', { 'class': 'button secondary small', 'data-testid': 'add-ai', 'type': 'button' }, 'Add AI seat');
  addAiButton.addEventListener('click', () => void game.addAi());
  const removeAiButton = h('button', { 'class': 'button secondary small', 'data-testid': 'remove-ai', 'type': 'button' }, 'Remove AI seat');
  removeAiButton.addEventListener('click', () => void game.removeAi());
  const aiRow = h('div', { class: 'presets' }, addAiButton, removeAiButton);

  controls.hidden = true;
  aiRow.hidden = true;

  const tableSection = h('section', { class: 'poker-table' }, info, stageBox, turn, turnBar, board, seats, controls, aiRow, results);

  const element = h(
    'main',
    { class: 'game-screen poker-screen' },
    h('div', { class: 'game-bar' }, backButton, leaveButton, h('h1', {}, 'Poker')),
    loading,
    retryButton,
    tables.element,
    tableSection,
    message,
    h('div', {}, renderRules()),
  );

  let raiseKey = '';

  function renderControls(state: Readonly<PokerViewState>, view: TableView): void {
    const options = view.hand?.options;
    controls.hidden = options === null || options === undefined || view.turnSeat !== view.mySeat;
    if (controls.hidden || !options)
      return;
    const allowed = (action: PokerActionKind): boolean => options.actions.includes(action);
    for (const [button, action] of [[foldButton, 'fold'], [checkButton, 'check'], [callButton, 'call'], [allInButton, 'allIn']] as const) {
      button.hidden = !allowed(action);
      button.disabled = state.busy;
    }
    callButton.textContent = `Call ${formatTokens(options.callAmount)}`;
    allInButton.textContent = `All in (${formatTokens(options.allInTo)})`;
    const canRaise = allowed('bet') || allowed('raise');
    raiseForm.hidden = !canRaise;
    raiseButton.textContent = allowed('bet') ? 'Bet' : 'Raise to';
    raiseButton.disabled = state.busy;
    raiseInput.min = String(options.minRaiseTo);
    raiseInput.max = String(options.maxRaiseTo);
    // A new turn or new limits start from the smallest amount; a view pushed meanwhile does not undo what the player typed.
    const key = `${view.id}:${options.minRaiseTo}:${options.maxRaiseTo}`;
    if (key !== raiseKey) {
      raiseKey = key;
      raiseInput.value = String(options.minRaiseTo);
    }
  }

  function renderSeats(view: TableView): void {
    const items: HTMLElement[] = [];
    for (let seat = 0; seat < view.maxSeats; seat++) {
      const taken = view.seats.find(s => s.seat === seat);
      const inHand = view.hand?.seats.find(s => s.seat === seat);
      if (!taken) {
        const item = h('li', { 'data-seat': String(seat) }, h('strong', {}, `Seat ${seat + 1}: `), 'empty');
        if (view.mySeat === null) {
          const sit = h('button', { 'class': 'button small', 'data-action': 'sit-seat', 'type': 'button' }, 'Sit here');
          sit.disabled = game.state.busy;
          sit.addEventListener('click', () => void game.sit(view.id, seat));
          item.append(' ', sit);
        }
        items.push(item);
        continue;
      }
      const parts = [seat === view.mySeat ? `${taken.name} (you)` : taken.name];
      const flags = seatFlags(view, seat);
      if (flags.length > 0)
        parts.push(flags.join(', '));
      if (inHand && inHand.roundBet > 0)
        parts.push(`bet ${formatTokens(inHand.roundBet)} this round`);
      if (inHand && inHand.committed > 0)
        parts.push(`${formatTokens(inHand.committed)} in the pot this hand`);
      if (inHand?.holeCards)
        parts.push(`cards ${inHand.holeCards.map(cardLabel).join(' ')}`);
      items.push(h('li', { 'data-seat': String(seat) }, h('strong', {}, `Seat ${seat + 1}: `), parts.join(' · ')));
    }
    seats.replaceChildren(...items);
  }

  function renderTable(state: Readonly<PokerViewState>, view: TableView): void {
    info.textContent = `Table ${view.id} · ${stakesLabel(view.stakes)}${view.mySeat === null ? ' · you are watching' : ''}`;
    const { hand } = view;
    board.textContent = hand
      ? `Board: ${hand.board.length > 0 ? hand.board.map(cardLabel).join(' ') : 'none yet'} · Pot: ${formatTokens(hand.pot)} tokens`
      : '';
    renderSeats(view);
    renderControls(state, view);
    const lines = resultLines(view);
    results.replaceChildren(...lines.map(line => h('li', {}, line)));
    const watcher = view.mySeat === null;
    leaveButton.textContent = watcher ? 'Stop watching' : 'Leave table';
    aiRow.hidden = !state.allowAiSeats || watcher;
    addAiButton.disabled = state.busy;
    removeAiButton.disabled = state.busy;
    updateClock();
  }

  /** The countdown text and bar, refreshed every moment without rebuilding the table. */
  function updateClock(): void {
    const { nextHandEndsAt, turnEndsAt, view } = game.state;
    if (!view) {
      turn.textContent = '';
      turnBar.hidden = true;
      return;
    }
    const whose = whoseTurn(view);
    const turnSeconds = secondsLeft(turnEndsAt, now());
    const nextSeconds = secondsLeft(nextHandEndsAt, now());
    turnBar.hidden = !(whose && turnSeconds !== null);
    if (whose && turnSeconds !== null) {
      turn.textContent = whose.mine ? `Your turn: ${turnSeconds} s left` : `Waiting for ${seatName(view, whose.seat)}: ${turnSeconds} s left`;
      turnBar.value = turnSeconds;
    }
    else if (nextSeconds !== null) {
      turn.textContent = `Next hand in ${nextSeconds} s`;
    }
    else if (view.handRunning) {
      turn.textContent = 'Hand in progress';
    }
    else {
      turn.textContent = `Waiting for players: at least two seated players with ${formatTokens(view.stakes.minBalance)} tokens each.`;
    }
  }

  function render(state: Readonly<PokerViewState>): void {
    const { phase, view } = state;
    const atTable = phase === 'table' && view !== null;
    loading.hidden = phase !== 'loading' || state.message !== '';
    retryButton.hidden = phase !== 'loading' || state.message === '';
    tables.element.hidden = phase !== 'list';
    tableSection.hidden = !atTable;
    backButton.hidden = atTable;
    leaveButton.hidden = !atTable;
    leaveButton.disabled = state.busy;
    message.textContent = phase === 'closed' ? 'The casino is closed right now. Come back later.' : state.message;
    message.dataset.error = String(state.messageIsError);
    if (phase === 'list')
      tables.update(state.tables, state.stakes, state.busy);
    if (atTable)
      renderTable(state, view);
  }

  const clock = setInterval(updateClock, 250);
  render(game.state);
  void game.load();

  return {
    destroy() {
      destroyed = true;
      clearInterval(clock);
      game.destroy();
      destroyStage();
    },
    element,
  };
}
