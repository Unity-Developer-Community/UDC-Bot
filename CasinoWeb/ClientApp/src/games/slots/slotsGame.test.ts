import { describe, expect, it, vi } from 'vitest';

import type { SlotsApi, SlotsStage } from '@/games/slots/slotsGame';
import type { HubState, SlotsHold, SlotsInfo, SlotsPlay } from '@/protocol';

import { CallRefusedError } from '@/casinoApi';
import { outcomeText, SlotsGame } from '@/games/slots/slotsGame';

const info: SlotsInfo = {
  maxBet: 100,
  minBet: 1,
  paytable: [],
  potTokens: 1000,
  reels: [['cherry', 'lemon'], ['cherry', 'lemon'], ['cherry', 'lemon']],
};

const state = (overrides: Partial<HubState> = {}): HubState => ({ balance: 500, casinoEnabled: true, slots: info, ...overrides });

function play(overrides: Partial<SlotsPlay> = {}): SlotsPlay {
  return {
    balance: 490,
    bet: 10,
    hold: null,
    net: -10,
    payout: 0,
    potTokens: 1001,
    result: { isJackpot: false, isRespin: false, multiplier: 0, stops: [0, 1, 0], symbols: ['cherry', 'lemon', 'cherry'] },
    status: 'ok',
    ...overrides,
  };
}

function setup(api: Partial<SlotsApi> = {}) {
  const calls: string[] = [];
  const stage: SlotsStage = {
    clearOutcome: vi.fn(() => calls.push('clear')),
    setHeld: vi.fn(),
    showOutcome: vi.fn(() => calls.push('outcome')),
    spinTo: vi.fn(async () => {
      calls.push('animate');
      return Promise.resolve();
    }),
  };
  const fullApi: SlotsApi = {
    getState: vi.fn(async () => Promise.resolve(state())),
    slotsRespin: vi.fn(async () => Promise.resolve(play())),
    slotsSpin: vi.fn(async () => Promise.resolve(play())),
    slotsToggleHold: vi.fn(async () => Promise.resolve<SlotsHold | null>(null)),
    ...api,
  };
  const onBalance = vi.fn(() => calls.push('balance'));
  const onChange = vi.fn();
  const game = new SlotsGame(fullApi, stage, { onBalance, onChange });
  return { api: fullApi, calls, game, onBalance, onChange, stage };
}

describe('slotsGame', () => {
  it('loads the rules, the pot and the balance', async () => {
    const { game, onBalance } = setup();

    await game.load();

    expect(game.state).toMatchObject({ bet: 10, phase: 'idle', potTokens: 1000 });
    expect(game.state.info).toBe(info);
    expect(onBalance).toHaveBeenCalledWith(500);
  });

  it('is closed when the server says the casino is closed', async () => {
    const { game } = setup({ getState: async () => Promise.resolve(state({ casinoEnabled: false })) });

    await game.load();

    expect(game.state.phase).toBe('closed');
  });

  it('keeps the bet inside the limits', async () => {
    const { game } = setup();
    await game.load();

    game.setBet(5000);
    expect(game.state.bet).toBe(100);
    game.setBet(-3);
    expect(game.state.bet).toBe(1);
    game.setBet(7.6);
    expect(game.state.bet).toBe(8);
    game.setBet(Number.NaN);
    expect(game.state.bet).toBe(8);
  });

  it('animates what the server decided, then shows the balance and the result', async () => {
    const { api, calls, game, onBalance, stage } = setup();
    await game.load();
    game.setBet(10);
    calls.length = 0;

    await game.spin();

    expect(api.slotsSpin).toHaveBeenCalledWith(10);
    expect(stage.spinTo).toHaveBeenCalledWith([0, 1, 0], [false, false, false]);
    expect(calls).toEqual(['clear', 'animate', 'balance', 'outcome']);
    expect(onBalance).toHaveBeenLastCalledWith(490);
    expect(game.state).toMatchObject({ message: 'No win. You lost 10 tokens.', phase: 'idle', potTokens: 1001 });
    expect(stage.showOutcome).toHaveBeenCalledWith('loss');
  });

  it('reports a win in words and marks it on the stage', async () => {
    const win = play({ net: 10, payout: 20, result: { isJackpot: false, isRespin: false, multiplier: 2, stops: [0, 0, 1], symbols: ['cherry', 'cherry', 'lemon'] } });
    const { game, stage } = setup({ slotsSpin: async () => Promise.resolve(win) });
    await game.load();

    await game.spin();

    expect(game.state.message).toBe('You won 20 tokens (2× your bet of 10).');
    expect(stage.showOutcome).toHaveBeenCalledWith('win');
  });

  it('ignores a second spin while one is running', async () => {
    let finish: (value: SlotsPlay) => void = () => {};
    const slotsSpin = vi.fn(async () => new Promise<SlotsPlay>((resolve) => {
      finish = resolve;
    }));
    const { game } = setup({ slotsSpin });
    await game.load();

    const first = game.spin();
    await game.spin();
    finish(play());
    await first;

    expect(slotsSpin).toHaveBeenCalledTimes(1);
  });

  it('shows a refusal without animating or touching the balance', async () => {
    const { game, onBalance, stage } = setup({ slotsSpin: async () => Promise.resolve(play({ result: null, status: 'insufficientTokens' })) });
    await game.load();
    onBalance.mockClear();

    await game.spin();

    expect(game.state).toMatchObject({ message: 'You do not have enough tokens for this bet.', messageIsError: true, phase: 'idle' });
    expect(stage.spinTo).not.toHaveBeenCalled();
    expect(onBalance).not.toHaveBeenCalled();
  });

  it('offers the hold, toggles it on the server and spends it on a free respin of the other reels', async () => {
    const offer: SlotsHold = { held: [false, false, false], offerId: 7 };
    const { api, game, stage } = setup({
      slotsRespin: vi.fn(async () => Promise.resolve(play({ hold: null, net: 20, payout: 20, result: { isJackpot: false, isRespin: true, multiplier: 2, stops: [0, 0, 1], symbols: ['cherry', 'cherry', 'lemon'] } }))),
      slotsSpin: async () => Promise.resolve(play({ hold: offer })),
      slotsToggleHold: vi.fn(async () => Promise.resolve<SlotsHold | null>({ held: [true, false, false], offerId: 7 })),
    });
    await game.load();
    await game.spin();
    expect(game.state.hold).toEqual(offer);
    expect(game.state.message).toContain('free respin');

    await game.toggleHold(0);
    expect(api.slotsToggleHold).toHaveBeenCalledWith(7, 0);
    expect(stage.setHeld).toHaveBeenLastCalledWith([true, false, false]);

    await game.respin();
    expect(api.slotsRespin).toHaveBeenCalledWith(7);
    expect(stage.spinTo).toHaveBeenLastCalledWith([0, 0, 1], [true, false, false]);
    expect(game.state.hold).toBeNull();
    expect(game.state.message).toContain('Free respin win');
  });

  it('drops the offer when the server says it is gone', async () => {
    const { game } = setup({
      slotsSpin: async () => Promise.resolve(play({ hold: { held: [false, false, false], offerId: 7 } })),
      slotsToggleHold: async () => Promise.resolve(null),
    });
    await game.load();
    await game.spin();

    await game.toggleHold(1);

    expect(game.state.hold).toBeNull();
    expect(game.state.message).toBe('This hold offer is no longer available.');
  });

  it('keeps an open offer when a new bet is refused', async () => {
    const offer: SlotsHold = { held: [false, false, false], offerId: 7 };
    const spins = [play({ hold: offer }), play({ result: null, status: 'insufficientTokens' })];
    const { game } = setup({ slotsSpin: async () => Promise.resolve(spins.shift()!) });
    await game.load();
    await game.spin();

    await game.spin();

    expect(game.state.hold).toEqual(offer);
  });

  it('says to slow down when the server refuses too many actions, and stays usable', async () => {
    const { game } = setup({ slotsSpin: async () => Promise.reject(new CallRefusedError('slow-down')) });
    await game.load();

    await game.spin();

    expect(game.state).toMatchObject({ message: 'Slow down: too many actions at once.', phase: 'idle' });
  });

  it('closes when the casino closes while playing', async () => {
    const { game } = setup({ slotsSpin: async () => Promise.reject(new CallRefusedError('casino-closed')) });
    await game.load();

    await game.spin();

    expect(game.state.phase).toBe('closed');
  });

  it('survives a dropped connection', async () => {
    const { game } = setup({ slotsSpin: async () => Promise.reject(new Error('connection closed')) });
    await game.load();

    await game.spin();

    expect(game.state).toMatchObject({ messageIsError: true, phase: 'idle' });
    expect(game.state.message).toContain('connection dropped');
  });
});

describe('outcomeText', () => {
  it('names the jackpot', () => {
    const jackpot = play({ payout: 1234, result: { isJackpot: true, isRespin: false, multiplier: 0, stops: [0, 0, 0], symbols: ['diamond', 'diamond', 'diamond'] } });

    expect(outcomeText(jackpot)).toBe('JACKPOT! You won 1,234 tokens from the shared jackpot.');
  });
});
