import { afterEach, describe, expect, it, vi } from 'vitest';

import type { BlackjackApi, BlackjackStage } from '@/games/blackjack/blackjackGame';
import type { BlackjackHand, BlackjackPlay, HubState } from '@/protocol';

import { renderBlackjackScreen } from '@/games/blackjack/blackjackPage';

const open: BlackjackHand = {
  actions: ['hit', 'stand'],
  bet: 10,
  credited: 0,
  dealer: [{ rank: 'K', suit: 'clubs' }],
  dealerValue: 10,
  finished: false,
  handId: 'h1',
  net: 0,
  player: [{ rank: '10', suit: 'spades' }, { rank: '9', suit: 'hearts' }],
  playerValue: 19,
  result: null,
};

function state(overrides: Partial<HubState> = {}): HubState {
  return {
    balance: 500,
    blackjack: { hand: null, minBet: 1 },
    casinoEnabled: true,
    poker: { allowAiSeats: false, stakes: [], table: null },
    slots: { maxBet: 100, minBet: 1, paytable: [], potTokens: 0, reels: [] },
    ...overrides,
  };
}

function fakeStage(): BlackjackStage {
  return { clear: vi.fn(), show: vi.fn(async () => Promise.resolve()), showOutcome: vi.fn() };
}

async function setup(api: Partial<BlackjackApi> = {}, failTable = false) {
  const fullApi: BlackjackApi = {
    blackjackAct: vi.fn(async () => Promise.resolve<BlackjackPlay>({ balance: 510, hand: { ...open, actions: [], credited: 20, dealer: [...open.dealer, { rank: '7', suit: 'clubs' }], dealerValue: 17, finished: true, net: 10, result: 'win' }, status: 'ok' })),
    blackjackDeal: vi.fn(async () => Promise.resolve<BlackjackPlay>({ balance: 490, hand: open, status: 'ok' })),
    getState: async () => Promise.resolve(state()),
    ...api,
  };
  const mountStage = vi.fn(async () => (failTable ? Promise.reject(new Error('no WebGL')) : Promise.resolve({ destroy: vi.fn(), stage: fakeStage() })));
  const screen = renderBlackjackScreen({ api: fullApi, mountStage, onBack: vi.fn(), onBalance: vi.fn() });
  // A form only submits while it is part of the document.
  document.body.append(screen.element);
  const $ = <T extends HTMLElement>(selector: string): T => screen.element.querySelector<T>(selector)!;
  return { $, api: fullApi, mountStage, screen };
}

const action = (root: HTMLElement, name: string): HTMLButtonElement => root.querySelector<HTMLButtonElement>(`[data-action=${name}]`)!;

describe('blackjack screen', () => {
  afterEach(() => document.body.replaceChildren());

  it('offers the bet and the deal once loaded, and no moves yet', async () => {
    const { $, screen } = await setup();
    await vi.waitFor(() => expect($('.controls').hidden).toBe(false));

    expect($<HTMLInputElement>('[data-testid=bet]').value).toBe('10');
    expect($('[data-testid=deal]').textContent).toBe('Deal (10)');
    expect(action(screen.element, 'hit').closest<HTMLElement>('.spin-row')!.hidden).toBe(true);
  });

  it('deals, repeats both hands in words and enables only the moves the server offers', async () => {
    const { $, api, screen } = await setup();
    await vi.waitFor(() => expect($('.controls').hidden).toBe(false));

    $<HTMLButtonElement>('[data-testid=deal]').click();
    await vi.waitFor(() => expect(action(screen.element, 'hit').disabled).toBe(false));

    expect(api.blackjackDeal).toHaveBeenCalledWith(10);
    expect($('[data-testid=hands]').textContent).toContain('Dealer: K♣ + a face-down card (10 showing)');
    expect($('[data-testid=hands]').textContent).toContain('You: 10♠ 9♥ (19)');
    expect(action(screen.element, 'stand').disabled).toBe(false);
    expect(action(screen.element, 'doubleDown').disabled).toBe(true);
    expect($('.bet').hidden).toBe(true);
  });

  it('shows the result in words and offers the next deal', async () => {
    const { $, screen } = await setup();
    await vi.waitFor(() => expect($('.controls').hidden).toBe(false));
    $<HTMLButtonElement>('[data-testid=deal]').click();
    await vi.waitFor(() => expect(action(screen.element, 'stand').disabled).toBe(false));

    action(screen.element, 'stand').click();

    await vi.waitFor(() => expect($('[data-testid=message]').textContent).toBe('You win 10 tokens: 19 against the dealer\'s 17.'));
    expect($('[data-testid=hands]').textContent).toContain('Dealer: K♣ 7♣ (17)');
    expect($('[data-testid=deal]').textContent).toBe('Deal again (10)');
    expect($<HTMLButtonElement>('[data-testid=deal]').disabled).toBe(false);
  });

  it('keeps playing in words when the table cannot be drawn', async () => {
    const { $, screen } = await setup({}, true);
    await vi.waitFor(() => expect($('.controls').hidden).toBe(false));

    $<HTMLButtonElement>('[data-testid=deal]').click();

    await vi.waitFor(() => expect(action(screen.element, 'hit').disabled).toBe(false));
    expect($('[data-testid=hands]').textContent).toContain('You: 10♠ 9♥ (19)');
  });

  it('does not start the table while the casino is closed', async () => {
    const { $, mountStage } = await setup({ getState: async () => Promise.resolve(state({ casinoEnabled: false })) });

    await vi.waitFor(() => expect($('[data-testid=message]').textContent).toContain('closed'));
    expect(mountStage).not.toHaveBeenCalled();
    expect($('.controls').hidden).toBe(true);
  });
});
