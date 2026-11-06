import type { BlackjackApi, BlackjackStage, BlackjackViewState } from '@/games/blackjack/blackjackGame';
import type { BlackjackAction } from '@/protocol';

import { h } from '@/dom';
import { BlackjackGame } from '@/games/blackjack/blackjackGame';
import { cardLabel } from '@/games/cards/cardText';
import { formatTokens } from '@/games/slots/slotsGame';

export interface BlackjackScreen {
  element: HTMLElement;
  /** Raises the bet limit after the header claimed the daily reward. */
  addTokens: (tokens: number) => void;
  destroy: () => void;
}

export interface BlackjackScreenDeps {
  api: BlackjackApi;
  onBack: () => void;
  onBalance: (balance: number) => void;
  /** Starts the table drawing. Replaceable in tests, where there is no canvas. */
  mountStage?: (container: HTMLElement) => Promise<{ destroy: () => void; stage: BlackjackStage }>;
}

/** Loads Phaser only when blackjack is opened, so the lobby stays light. */
async function mountPhaserStage(container: HTMLElement): Promise<{ destroy: () => void; stage: BlackjackStage }> {
  const [{ default: Phaser }, { BlackjackScene }] = await Promise.all([import('phaser'), import('@/games/blackjack/blackjackScene')]);
  return new Promise((resolve) => {
    let game: { destroy: (removeCanvas: boolean) => void } | undefined;
    const scene = new BlackjackScene(stage => resolve({ destroy: () => game?.destroy(true), stage }));
    game = new Phaser.Game({
      audio: { noAudio: true },
      backgroundColor: getComputedStyle(document.documentElement).getPropertyValue('--color-surface').trim(),
      height: 380,
      parent: container,
      scale: { autoCenter: Phaser.Scale.CENTER_HORIZONTALLY, mode: Phaser.Scale.FIT },
      scene,
      type: Phaser.AUTO,
      width: 480,
    });
  });
}

const ACTIONS: { action: BlackjackAction; label: string }[] = [
  { action: 'hit', label: 'Hit' },
  { action: 'stand', label: 'Stand' },
  { action: 'doubleDown', label: 'Double down' },
];

function renderRules(): HTMLElement {
  return h(
    'details',
    { class: 'paytable' },
    h('summary', {}, 'Rules'),
    h('p', {}, 'Get closer to 21 than the dealer without going over. Number cards count their value, faces 10, an ace 11 or 1. You and the dealer get two cards; one of the dealer\'s stays face down until you finish.'),
    h(
      'ul',
      {},
      h('li', {}, 'Hit: take another card. Stand: keep your hand; the dealer then draws below 17, and on a soft 17.'),
      h('li', {}, 'Double down: bet as much again, take exactly one more card, then stand.'),
      h('li', {}, 'A win pays 2× your bet (a blackjack too), a tie gives your bet back, a bust or a loss keeps it.'),
    ),
    h('p', {}, 'Your bet leaves your balance when the cards are dealt. A hand left alone for a few minutes stands on its own and is settled.'),
  );
}

/**
 * The blackjack page: the table (a Phaser canvas) with the bet, deal and move controls around it as ordinary form
 * controls, which keep the keyboard and screen readers working, and both hands repeated in words. The controller owns the
 * behavior; this only shows its state.
 */
export function renderBlackjackScreen(deps: BlackjackScreenDeps): BlackjackScreen {
  const mountStage = deps.mountStage ?? mountPhaserStage;
  let destroyed = false;
  let destroyStage: () => void = () => {};

  const stageBox = h('div', { class: 'slots-stage' });
  // The canvas starts with the first thing the controller asks it to draw, which comes once the state is loaded (and
  // never while the casino is closed). The controls work before it is up; what they ask it to draw waits for it.
  let mounted: Promise<BlackjackStage> | undefined;
  async function table(): Promise<BlackjackStage> {
    mounted ??= mountStage(stageBox).then((result) => {
      if (destroyed)
        result.destroy();
      else
        destroyStage = result.destroy;
      return result.stage;
    });
    return mounted;
  }
  // Leaving the screen ends an animation that is still running. The hand itself is settled on the server, so the
  // controller must go on and report the balance instead of waiting for an animation that will never end.
  let leave: () => void = () => {};
  const left = new Promise<void>((resolve) => {
    leave = resolve;
  });
  // A table that fails to load must not hold up the game: the hands are also shown in words, so play goes on without it.
  const ignore = (): void => {};
  const stage: BlackjackStage = {
    clear: () => void table().then(s => destroyed || s.clear()).catch(ignore),
    show: async (hand, animate) => Promise.race([table().then(async s => destroyed || s.show(hand, animate)).then(ignore, ignore), left]),
    showOutcome: outcome => void table().then(s => destroyed || s.showOutcome(outcome)).catch(ignore),
  };

  const game = new BlackjackGame(deps.api, stage, { onBalance: deps.onBalance, onChange: state => render(state) });

  const backButton = h('button', { class: 'button secondary', type: 'button' }, '← Lobby');
  backButton.addEventListener('click', deps.onBack);

  const betInput = h('input', { 'class': 'bet-input', 'data-testid': 'bet', 'id': 'blackjack-bet', 'inputmode': 'numeric', 'name': 'bet', 'type': 'number' });
  betInput.addEventListener('change', () => {
    game.setBet(Number(betInput.value));
    betInput.value = String(game.state.bet);
  });
  const presets = h('div', { class: 'presets' });
  for (const value of [10, 50, 100, 500]) {
    const button = h('button', { class: 'button secondary small', type: 'button' }, String(value));
    button.addEventListener('click', () => {
      game.setBet(value);
      betInput.value = String(game.state.bet);
    });
    presets.append(button);
  }
  const dealButton = h('button', { 'class': 'button', 'data-testid': 'deal', 'type': 'submit' }, 'Deal');
  const actionButtons = ACTIONS.map(({ action, label }) => {
    const button = h('button', { 'class': 'button', 'data-action': action, 'type': 'button' }, label);
    button.addEventListener('click', () => void game.act(action));
    return button;
  });
  const hands = h('div', { 'class': 'hands', 'data-testid': 'hands' });
  const message = h('p', { 'class': 'message', 'data-testid': 'message', 'role': 'status' });
  const loading = h('p', { class: 'message' }, 'Loading…');
  const retryButton = h('button', { class: 'button', type: 'button' }, 'Try again');
  retryButton.addEventListener('click', () => void start());

  const betRow = h('div', { class: 'bet' }, h('label', { for: 'blackjack-bet' }, 'Bet'), betInput, presets, dealButton);
  const actionRow = h('div', { class: 'spin-row' }, ...actionButtons);
  const form = h('form', { class: 'controls' }, betRow, actionRow);
  form.addEventListener('submit', (event) => {
    event.preventDefault();
    game.setBet(Number(betInput.value));
    betInput.value = String(game.state.bet);
    void game.deal();
  });

  const element = h(
    'main',
    { class: 'game-screen' },
    h('div', { class: 'game-bar' }, backButton, h('h1', {}, 'Blackjack')),
    stageBox,
    loading,
    retryButton,
    hands,
    form,
    message,
    h('div', {}, renderRules()),
  );

  function handLine(label: string, cards: string, value: string): HTMLElement {
    return h('p', {}, h('strong', {}, `${label}: `), cards, ` (${value})`);
  }

  function render(state: Readonly<BlackjackViewState>): void {
    const { hand, phase } = state;
    loading.hidden = phase !== 'loading' || state.message !== '';
    retryButton.hidden = phase !== 'loading' || state.message === '';
    form.hidden = phase === 'loading' || phase === 'closed';
    stageBox.hidden = phase === 'closed';
    message.textContent = phase === 'closed' ? 'The casino is closed right now. Come back later.' : state.message;
    message.dataset.error = String(state.messageIsError);

    betInput.min = String(state.minBet);
    if (document.activeElement !== betInput)
      betInput.value = String(state.bet);
    const betting = phase === 'betting';
    betInput.disabled = !betting;
    for (const button of presets.querySelectorAll('button'))
      button.disabled = !betting;
    dealButton.disabled = !betting;
    dealButton.textContent = hand?.finished ? `Deal again (${formatTokens(state.bet)})` : `Deal (${formatTokens(state.bet)})`;
    betRow.hidden = phase === 'playing';

    actionRow.hidden = !hand || hand.finished;
    for (const button of actionButtons) {
      const action = button.dataset.action as BlackjackAction;
      button.disabled = phase !== 'playing' || !hand?.actions.includes(action);
    }

    // The hands in words, so nothing rests on the drawing alone.
    if (!hand) {
      hands.replaceChildren();
      return;
    }
    const dealerCards = hand.dealer.map(cardLabel).join(' ') + (hand.finished ? '' : ' + a face-down card');
    hands.replaceChildren(
      h('p', {}, `Bet: ${formatTokens(hand.bet)} tokens`),
      handLine('Dealer', dealerCards, hand.finished ? String(hand.dealerValue) : `${hand.dealerValue} showing`),
      handLine('You', hand.player.map(cardLabel).join(' '), String(hand.playerValue)),
    );
  }

  async function start(): Promise<void> {
    await game.load();
  }

  render(game.state);
  void start();

  return {
    addTokens: tokens => game.addTokens(tokens),
    destroy() {
      destroyed = true;
      leave();
      destroyStage();
    },
    element,
  };
}
