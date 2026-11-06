import { afterEach, describe, expect, it, vi } from 'vitest';

import type { SlotsApi, SlotsStage } from '@/games/slots/slotsGame';
import type { HubState, SlotsHold, SlotsInfo, SlotsPlay } from '@/protocol';

import { renderSlotsScreen } from '@/games/slots/slotsPage';

const info: SlotsInfo = {
  maxBet: 100,
  minBet: 1,
  paytable: [
    { count: 3, isJackpot: true, multiplier: 0, symbol: 'diamond' },
    { count: 3, isJackpot: false, multiplier: 100, symbol: 'seven' },
    { count: 2, isJackpot: false, multiplier: 2, symbol: 'cherry' },
  ],
  potTokens: 1234,
  reels: [['cherry', 'lemon'], ['cherry', 'lemon'], ['cherry', 'lemon']],
};

const loss: SlotsPlay = {
  balance: 490,
  bet: 10,
  hold: null,
  net: -10,
  payout: 0,
  potTokens: 1235,
  result: { isJackpot: false, isRespin: false, multiplier: 0, stops: [0, 1, 0], symbols: ['cherry', 'lemon', 'cherry'] },
  status: 'ok',
};

function fakeStage(): SlotsStage {
  return {
    clearOutcome: vi.fn(),
    setHeld: vi.fn(),
    showOutcome: vi.fn(),
    spinTo: vi.fn(async () => Promise.resolve()),
  };
}

function unusedApi(getState: SlotsApi['getState']): SlotsApi {
  return { getState, slotsRespin: vi.fn(), slotsSpin: vi.fn(), slotsToggleHold: vi.fn() };
}

async function setup(api: Partial<SlotsApi> = {}, spinTo?: SlotsStage['spinTo']) {
  const fullApi: SlotsApi = {
    getState: async () => Promise.resolve<HubState>({ balance: 500, blackjack: { hand: null, minBet: 1 }, casinoEnabled: true, slots: info }),
    slotsRespin: async () => Promise.resolve(loss),
    slotsSpin: vi.fn(async () => Promise.resolve(loss)),
    slotsToggleHold: async () => Promise.resolve<SlotsHold | null>(null),
    ...api,
  };
  const onBalance = vi.fn();
  const destroyStage = vi.fn();
  const screen = renderSlotsScreen({
    api: fullApi,
    mountStage: async () => Promise.resolve({ destroy: destroyStage, stage: { ...fakeStage(), ...(spinTo ? { spinTo } : {}) } }),
    onBack: vi.fn(),
    onBalance,
  });
  // A form only submits while it is part of the document.
  document.body.append(screen.element);
  await vi.waitFor(() => expect(screen.element.querySelector('.controls')?.hasAttribute('hidden')).toBe(false));
  const $ = <T extends HTMLElement>(selector: string): T => screen.element.querySelector<T>(selector)!;
  return { $, api: fullApi, destroyStage, onBalance, screen };
}

describe('slots screen', () => {
  afterEach(() => document.body.replaceChildren());

  it('shows the jackpot, the bet limits and the paytable once loaded', async () => {
    const { $ } = await setup();

    expect($('[data-testid=pot]').textContent).toBe('1,234');
    expect($<HTMLInputElement>('[data-testid=bet]').min).toBe('1');
    expect($<HTMLInputElement>('[data-testid=bet]').max).toBe('100');
    expect($<HTMLInputElement>('[data-testid=bet]').value).toBe('10');
    expect($('[data-testid=spin]').textContent).toBe('Spin (10)');
    expect($('.paytable').textContent).toContain('3× Diamond → the jackpot');
    expect($('.paytable').textContent).toContain('2× Cherry + any → 2× your bet');
  });

  it('spins with the typed bet and shows the result and the new balance', async () => {
    const { $, api, onBalance } = await setup();
    $<HTMLInputElement>('[data-testid=bet]').value = '25';

    $<HTMLButtonElement>('[data-testid=spin]').click();

    await vi.waitFor(() => expect($('[data-testid=message]').textContent).toBe('No win. You lost 10 tokens.'));
    expect(api.slotsSpin).toHaveBeenCalledWith(25);
    expect(onBalance).toHaveBeenLastCalledWith(490);
    expect($('[data-testid=pot]').textContent).toBe('1,235');
  });

  it('turns a hold offer into reel buttons and a free respin, and the buttons say their state in words', async () => {
    const offer: SlotsHold = { held: [false, false, false], offerId: 3 };
    const { $, screen } = await setup({
      slotsSpin: async () => Promise.resolve({ ...loss, hold: offer }),
      slotsToggleHold: async () => Promise.resolve({ held: [false, true, false], offerId: 3 }),
    });
    expect($('[data-testid=respin]').hidden).toBe(true);
    const holdButtons = (): HTMLButtonElement[] => [...screen.element.querySelectorAll<HTMLButtonElement>('.holds button')];

    $<HTMLButtonElement>('[data-testid=spin]').click();
    await vi.waitFor(() => expect($('[data-testid=respin]').hidden).toBe(false));
    expect(holdButtons().map(b => b.textContent)).toEqual(['Reel 1: Hold', 'Reel 2: Hold', 'Reel 3: Hold']);
    expect($('[data-testid=spin]').textContent).toBe('Spin again (10)');

    holdButtons()[1]!.click();
    await vi.waitFor(() => expect(holdButtons()[1]!.textContent).toBe('🔒 Reel 2: Held'));
    expect(holdButtons()[1]!.getAttribute('aria-pressed')).toBe('true');
  });

  it('says so when the casino is closed', async () => {
    const screen = renderSlotsScreen({
      api: unusedApi(async () => Promise.resolve({ balance: 0, blackjack: { hand: null, minBet: 1 }, casinoEnabled: false, slots: info })),
      onBack: vi.fn(),
      onBalance: vi.fn(),
    });

    await vi.waitFor(() => expect(screen.element.querySelector('[data-testid=message]')?.textContent).toContain('closed'));
    expect(screen.element.querySelector('.controls')?.hasAttribute('hidden')).toBe(true);
  });

  it('offers a retry when the first load fails, then loads', async () => {
    let fail = true;
    const screen = renderSlotsScreen({
      api: unusedApi(async () => (fail ? Promise.reject(new Error('not connected')) : Promise.resolve({ balance: 5, blackjack: { hand: null, minBet: 1 }, casinoEnabled: true, slots: info }))),
      mountStage: async () => Promise.resolve({ destroy: vi.fn(), stage: fakeStage() }),
      onBack: vi.fn(),
      onBalance: vi.fn(),
    });
    const retry = (): HTMLButtonElement => screen.element.querySelector<HTMLButtonElement>('main > button')!;
    await vi.waitFor(() => expect(retry().hidden).toBe(false));

    fail = false;
    retry().click();

    await vi.waitFor(() => expect(screen.element.querySelector('.controls')?.hasAttribute('hidden')).toBe(false));
    expect(retry().hidden).toBe(true);
  });

  it('still reports the balance of a spin when the screen is left before its animation ends', async () => {
    const { $, onBalance, screen } = await setup({}, async () => new Promise(() => {}));

    $<HTMLButtonElement>('[data-testid=spin]').click();
    await vi.waitFor(() => expect($<HTMLButtonElement>('[data-testid=spin]').disabled).toBe(true));
    screen.destroy();

    await vi.waitFor(() => expect(onBalance).toHaveBeenLastCalledWith(490));
  });

  it('removes the machine when the screen is left', async () => {
    const { destroyStage, screen } = await setup();

    screen.destroy();

    expect(destroyStage).toHaveBeenCalled();
  });
});
