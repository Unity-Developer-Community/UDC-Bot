import type { SlotsApi, SlotsStage, SlotsViewState } from '@/games/slots/slotsGame';
import type { SlotsInfo } from '@/protocol';

import { h } from '@/dom';
import { ART_LICENSE_URL } from '@/games/slots/art';
import { formatTokens, SlotsGame } from '@/games/slots/slotsGame';

export interface SlotsScreen {
  element: HTMLElement;
  destroy: () => void;
}

export interface SlotsScreenDeps {
  api: SlotsApi;
  onBack: () => void;
  onBalance: (balance: number) => void;
  /** Starts the machine drawing. Replaceable in tests, where there is no canvas. */
  mountStage?: (container: HTMLElement, info: SlotsInfo, onReelClick: (reel: number) => void) => Promise<{ destroy: () => void; stage: SlotsStage }>;
}

/** Loads Phaser only when the slots are opened, so the lobby stays light. */
async function mountPhaserStage(container: HTMLElement, info: SlotsInfo, onReelClick: (reel: number) => void): Promise<{ destroy: () => void; stage: SlotsStage }> {
  const [{ default: Phaser }, { SlotsScene }] = await Promise.all([import('phaser'), import('@/games/slots/slotsScene')]);
  return new Promise((resolve) => {
    let game: { destroy: (removeCanvas: boolean) => void } | undefined;
    const scene = new SlotsScene(info.reels, onReelClick, stage => resolve({ destroy: () => game?.destroy(true), stage }));
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

function symbolName(symbol: string): string {
  return symbol.charAt(0).toUpperCase() + symbol.slice(1);
}

function renderPaytable(info: SlotsInfo): HTMLElement {
  return h(
    'details',
    { class: 'paytable' },
    h('summary', {}, 'Paytable and rules'),
    h(
      'ul',
      {},
      ...info.paytable.map(line => h(
        'li',
        {},
        `${line.count}× ${symbolName(line.symbol)}${line.count < 3 ? ' + any' : ''} → `,
        line.isJackpot ? 'the jackpot (your bet ÷ 100 of the pot)' : `${line.multiplier}× your bet`,
      )),
    ),
    h('p', {}, `Three reels, one payline in the middle. Bet ${info.minBet}–${info.maxBet} tokens. "+ any" means exactly two of that symbol anywhere on the line; other pairs pay nothing. 5% of every bet feeds the jackpot, shared by the whole server and never below 1,000 tokens.`),
    h('p', {}, 'About 1 in 4 losing spins offers a hold: keep the reels you like and respin the others once, for free. The offer ends after 10 minutes or when you spin again.'),
    h('p', {}, 'Symbol art: ', h('a', { href: ART_LICENSE_URL ?? '#', rel: 'noopener', target: '_blank' }, 'Microsoft Fluent Emoji (MIT license)'), '.'),
  );
}

/**
 * The slots page: the machine (a Phaser canvas) with the bet, spin and hold controls around it as ordinary form controls,
 * which keep the keyboard and screen readers working. The controller owns the behavior; this only shows its state.
 */
export function renderSlotsScreen(deps: SlotsScreenDeps): SlotsScreen {
  const mountStage = deps.mountStage ?? mountPhaserStage;
  let destroyed = false;
  let destroyStage: () => void = () => {};

  // Controls work before the canvas is up; what they ask the machine to draw waits for it.
  let stageReady: (stage: SlotsStage) => void = () => {};
  const stagePromise = new Promise<SlotsStage>((resolve) => {
    stageReady = resolve;
  });
  // Leaving the screen ends an animation that is still running without finishing it. The spin itself is settled on the
  // server, so the controller must go on and report the balance instead of waiting for an animation that will never end.
  let leave: () => void = () => {};
  const left = new Promise<void>((resolve) => {
    leave = resolve;
  });
  const stage: SlotsStage = {
    clearOutcome: () => void stagePromise.then(s => s.clearOutcome()),
    setHeld: held => void stagePromise.then(s => s.setHeld(held)),
    showOutcome: outcome => void stagePromise.then(s => s.showOutcome(outcome)),
    spinTo: async (stops, held) => Promise.race([stagePromise.then(async s => s.spinTo(stops, held)), left]),
  };

  const game = new SlotsGame(deps.api, stage, { onBalance: deps.onBalance, onChange: state => render(state) });

  const backButton = h('button', { class: 'button secondary', type: 'button' }, '← Lobby');
  backButton.addEventListener('click', deps.onBack);

  const pot = h('span', { 'class': 'pot', 'data-testid': 'pot' }, '—');
  const stageBox = h('div', { class: 'slots-stage' });
  const betInput = h('input', { 'class': 'bet-input', 'data-testid': 'bet', 'id': 'slots-bet', 'inputmode': 'numeric', 'name': 'bet', 'type': 'number' });
  betInput.addEventListener('change', () => {
    game.setBet(Number(betInput.value));
    betInput.value = String(game.state.bet);
  });
  const presets = h('div', { class: 'presets' });
  const holdBox = h('div', { class: 'holds' });
  const spinButton = h('button', { 'class': 'button', 'data-testid': 'spin', 'type': 'submit' }, 'Spin');
  const respinButton = h('button', { 'class': 'button', 'data-testid': 'respin', 'type': 'button' }, 'Free respin');
  respinButton.addEventListener('click', () => void game.respin());
  const message = h('p', { 'class': 'message', 'data-testid': 'message', 'role': 'status' });
  const paytableBox = h('div', {});
  const loading = h('p', { class: 'message' }, 'Loading…');
  const retryButton = h('button', { class: 'button', type: 'button' }, 'Try again');
  retryButton.addEventListener('click', () => void start());

  const form = h(
    'form',
    { class: 'controls' },
    h('div', { class: 'bet' }, h('label', { for: 'slots-bet' }, 'Bet'), betInput, presets),
    holdBox,
    h('div', { class: 'spin-row' }, respinButton, spinButton),
  );
  form.addEventListener('submit', (event) => {
    event.preventDefault();
    game.setBet(Number(betInput.value));
    betInput.value = String(game.state.bet);
    void game.spin();
  });

  const element = h(
    'main',
    { class: 'game-screen' },
    h('div', { class: 'game-bar' }, backButton, h('h1', {}, 'Slots'), h('p', { class: 'pot-line' }, 'Jackpot: ', pot, ' tokens')),
    stageBox,
    loading,
    retryButton,
    form,
    message,
    paytableBox,
  );

  let presetsFor: SlotsInfo | null = null;
  let paytableFor: SlotsInfo | null = null;

  function render(state: Readonly<SlotsViewState>): void {
    const { info } = state;
    const busy = state.phase !== 'idle';
    loading.hidden = state.phase !== 'loading' || state.message !== '';
    retryButton.hidden = state.phase !== 'loading' || state.message === '';
    form.hidden = state.phase === 'loading' || state.phase === 'closed';
    stageBox.hidden = state.phase === 'closed';
    pot.textContent = formatTokens(state.potTokens);
    message.textContent = state.phase === 'closed' ? 'The casino is closed right now. Come back later.' : state.message;
    message.dataset.error = String(state.messageIsError);

    if (info) {
      betInput.min = String(info.minBet);
      betInput.max = String(info.maxBet);
      if (document.activeElement !== betInput)
        betInput.value = String(state.bet);
      if (presetsFor !== info) {
        presetsFor = info;
        const values = [...new Set([info.minBet, 10, 50, info.maxBet])].filter(v => v >= info.minBet && v <= info.maxBet);
        presets.replaceChildren(...values.map((value) => {
          const button = h('button', { class: 'button secondary small', type: 'button' }, String(value));
          button.addEventListener('click', () => {
            game.setBet(value);
            betInput.value = String(value);
          });
          return button;
        }));
      }
      if (paytableFor !== info) {
        paytableFor = info;
        paytableBox.replaceChildren(renderPaytable(info));
      }
    }

    betInput.disabled = busy || state.hold !== null;
    for (const button of presets.querySelectorAll('button'))
      button.disabled = busy || state.hold !== null;
    spinButton.disabled = busy;
    spinButton.textContent = state.hold ? `Spin again (${formatTokens(state.bet)})` : `Spin (${formatTokens(state.bet)})`;
    spinButton.classList.toggle('secondary', state.hold !== null);

    respinButton.hidden = state.hold === null;
    respinButton.disabled = busy;
    holdBox.hidden = state.hold === null;
    // The buttons are rebuilt on every change, so a keyboard user keeps their place.
    const focused = [...holdBox.children].indexOf(document.activeElement as Element);
    holdBox.replaceChildren(...(state.hold?.held ?? []).map((held, reel) => {
      // The label and aria-pressed change with the state, so it never rests on the style alone.
      const button = h('button', { 'aria-pressed': String(held), 'class': 'button secondary', 'type': 'button' }, held ? `🔒 Reel ${reel + 1}: Held` : `Reel ${reel + 1}: Hold`);
      button.disabled = busy;
      button.addEventListener('click', () => void game.toggleHold(reel));
      return button;
    }));
    if (focused >= 0)
      (holdBox.children[focused] as HTMLElement | undefined)?.focus();
  }

  let mounting = false;
  async function start(): Promise<void> {
    await game.load();
    const { info } = game.state;
    if (destroyed || mounting || !info || game.state.phase === 'closed')
      return;
    mounting = true;
    const mounted = await mountStage(stageBox, info, reel => void game.toggleHold(reel));
    if (destroyed) {
      mounted.destroy();
      return;
    }
    destroyStage = mounted.destroy;
    stageReady(mounted.stage);
  }

  render(game.state);
  void start();

  return {
    destroy() {
      destroyed = true;
      leave();
      destroyStage();
    },
    element,
  };
}
