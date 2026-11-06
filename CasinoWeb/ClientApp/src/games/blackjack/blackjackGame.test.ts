import { describe, expect, it, vi } from 'vitest';

import type { BlackjackApi, BlackjackStage } from '@/games/blackjack/blackjackGame';
import type { BlackjackHand, BlackjackPlay, HubState } from '@/protocol';

import { CallRefusedError } from '@/casinoApi';
import { BlackjackGame, outcomeOf, outcomeText } from '@/games/blackjack/blackjackGame';

function hand(overrides: Partial<BlackjackHand> = {}): BlackjackHand {
  return {
    actions: ['hit', 'stand', 'doubleDown'],
    bet: 10,
    credited: 0,
    dealer: [{ rank: '10', suit: 'clubs' }],
    dealerValue: 10,
    finished: false,
    handId: 'h1',
    net: 0,
    player: [{ rank: '10', suit: 'spades' }, { rank: '9', suit: 'hearts' }],
    playerValue: 19,
    result: null,
    ...overrides,
  };
}

function won(overrides: Partial<BlackjackHand> = {}): BlackjackHand {
  return hand({
    actions: [],
    credited: 20,
    dealer: [{ rank: '10', suit: 'clubs' }, { rank: '7', suit: 'clubs' }],
    dealerValue: 17,
    finished: true,
    net: 10,
    result: 'win',
    ...overrides,
  });
}

function state(overrides: Partial<HubState> = {}): HubState {
  return {
    balance: 500,
    blackjack: { hand: null, minBet: 1 },
    casinoEnabled: true,
    slots: { maxBet: 100, minBet: 1, paytable: [], potTokens: 0, reels: [] },
    ...overrides,
  };
}

function setup(api: Partial<BlackjackApi> = {}) {
  const calls: string[] = [];
  const stage: BlackjackStage = {
    clear: vi.fn(() => calls.push('clear')),
    show: vi.fn(async () => {
      calls.push('show');
      return Promise.resolve();
    }),
    showOutcome: vi.fn(),
  };
  const fullApi: BlackjackApi = {
    blackjackAct: vi.fn(async () => Promise.resolve<BlackjackPlay>({ balance: 510, hand: won(), status: 'ok' })),
    blackjackDeal: vi.fn(async () => Promise.resolve<BlackjackPlay>({ balance: 490, hand: hand(), status: 'ok' })),
    getState: vi.fn(async () => Promise.resolve(state())),
    ...api,
  };
  const onBalance = vi.fn((balance: number) => calls.push(`balance ${balance}`));
  const game = new BlackjackGame(fullApi, stage, { onBalance, onChange: vi.fn() });
  return { api: fullApi, calls, game, onBalance, stage };
}

describe('blackjackGame', () => {
  it('loads the balance and waits for a bet', async () => {
    const { game, onBalance } = setup();

    await game.load();

    expect(game.state).toMatchObject({ balance: 500, bet: 10, hand: null, minBet: 1, phase: 'betting' });
    expect(onBalance).toHaveBeenCalledWith(500);
  });

  it('raises the bet limit when tokens are added, without reporting them back', async () => {
    const { game, onBalance } = setup({ getState: vi.fn(async () => Promise.resolve(state({ balance: 50 }))) });
    await game.load();
    game.setBet(500);
    expect(game.state.bet).toBe(50);
    onBalance.mockClear();

    game.addTokens(100);
    game.setBet(500);

    expect(game.state).toMatchObject({ balance: 150, bet: 150 });
    expect(onBalance).not.toHaveBeenCalled();
  });

  it('resumes a hand left open, without dealing it again', async () => {
    const open = hand();
    const { game, stage } = setup({ getState: async () => Promise.resolve(state({ blackjack: { hand: open, minBet: 1 } })) });

    await game.load();

    expect(game.state).toMatchObject({ hand: open, phase: 'playing' });
    expect(stage.show).toHaveBeenCalledWith(open, false);
  });

  it('is closed when the server says the casino is closed', async () => {
    const { game } = setup({ getState: async () => Promise.resolve(state({ casinoEnabled: false })) });

    await game.load();

    expect(game.state.phase).toBe('closed');
  });

  it('keeps the bet between the minimum and the balance', async () => {
    const { game } = setup();
    await game.load();

    game.setBet(5000);
    expect(game.state.bet).toBe(500);
    game.setBet(0);
    expect(game.state.bet).toBe(1);
    game.setBet(Number.NaN);
    expect(game.state.bet).toBe(1);
  });

  it('deals: shows the bet leaving the balance, then the cards, then waits for a move', async () => {
    const { api, calls, game } = setup();
    await game.load();
    calls.length = 0;

    await game.deal();

    expect(api.blackjackDeal).toHaveBeenCalledWith(10);
    expect(calls).toEqual(['clear', 'balance 490', 'show']);
    expect(game.state).toMatchObject({ message: 'Your move: hit, stand or double down.', phase: 'playing' });
  });

  it('shows the balance of a finished hand only once the cards have been dealt', async () => {
    const { calls, game, stage } = setup();
    await game.load();
    await game.deal();
    calls.length = 0;

    await game.act('stand');

    expect(calls).toEqual(['show', 'balance 510']);
    expect(game.state).toMatchObject({ message: 'You win 10 tokens: 19 against the dealer\'s 17.', phase: 'betting' });
    expect(stage.showOutcome).toHaveBeenLastCalledWith('win');
  });

  it('only sends a move the server offers', async () => {
    const { api, game } = setup({
      blackjackDeal: async () => Promise.resolve<BlackjackPlay>({ balance: 0, hand: hand({ actions: ['hit', 'stand'] }), status: 'ok' }),
    });
    await game.load();
    await game.deal();

    await game.act('doubleDown');

    expect(api.blackjackAct).not.toHaveBeenCalled();
    expect(game.state.message).toBe('Your move: hit or stand.');
  });

  it('keeps the hand open when a double down cannot be covered', async () => {
    const open = hand({ actions: ['hit', 'stand'] });
    const { game } = setup({ blackjackAct: async () => Promise.resolve<BlackjackPlay>({ balance: 5, hand: open, status: 'insufficientTokens' }) });
    await game.load();
    await game.deal();

    await game.act('doubleDown');

    expect(game.state).toMatchObject({ balance: 5, hand: open, messageIsError: true, phase: 'playing' });
  });

  it('refuses a deal above the balance and stays ready to bet', async () => {
    const { game } = setup({ blackjackDeal: async () => Promise.resolve<BlackjackPlay>({ balance: 0, hand: null, status: 'insufficientTokens' }) });
    await game.load();

    await game.deal();

    expect(game.state).toMatchObject({ hand: null, message: 'You do not have enough tokens for this.', phase: 'betting' });
  });

  it('asks the server again when the hand is gone, and shows what it says', async () => {
    const getState = vi.fn(async () => Promise.resolve(state()));
    const { game, onBalance } = setup({
      blackjackAct: async () => Promise.resolve<BlackjackPlay>({ balance: 0, hand: null, status: 'noHand' }),
      getState,
    });
    await game.load();
    await game.deal();
    getState.mockResolvedValueOnce(state({ balance: 520 }));

    await game.act('hit');

    expect(onBalance).toHaveBeenLastCalledWith(520);
    expect(game.state).toMatchObject({ hand: null, messageIsError: true, phase: 'betting' });
    expect(game.state.message).toContain('This hand is over');
  });

  it('shows the open hand when a deal finds one', async () => {
    const open = hand({ handId: 'older' });
    const { game } = setup({ blackjackDeal: async () => Promise.resolve<BlackjackPlay>({ balance: 400, hand: open, status: 'handInProgress' }) });
    await game.load();

    await game.deal();

    expect(game.state).toMatchObject({ hand: open, phase: 'playing' });
  });

  it('is usable again after a dropped connection, and closes on a closed casino', async () => {
    const blackjackDeal = vi.fn(async (): Promise<BlackjackPlay> => Promise.reject(new Error('socket closed')));
    const { game } = setup({ blackjackDeal });
    await game.load();

    await game.deal();
    expect(game.state).toMatchObject({ message: 'The connection dropped. Check it and try again.', phase: 'betting' });

    blackjackDeal.mockRejectedValueOnce(new CallRefusedError('casino-closed'));
    await game.deal();
    expect(game.state.phase).toBe('closed');
  });

  it('describes every ending in words', () => {
    expect(outcomeText(won({ player: [{ rank: 'A', suit: 'spades' }, { rank: 'K', suit: 'hearts' }], playerValue: 21 }))).toBe('Blackjack! You win 10 tokens.');
    expect(outcomeText(won({ dealerValue: 24 }))).toBe('The dealer busts with 24. You win 10 tokens.');
    expect(outcomeText(won({ credited: 0, net: -10, playerValue: 25, result: 'loss' }))).toBe('Bust with 25. You lose 10 tokens.');
    expect(outcomeText(won({ credited: 0, dealerValue: 20, net: -10, result: 'loss' }))).toBe('The dealer wins with 20 against your 19. You lose 10 tokens.');
    expect(outcomeText(won({ credited: 10, dealerValue: 19, net: 0, result: 'tie' }))).toBe('Push: you and the dealer both have 19. Your bet of 10 tokens comes back.');
    expect(outcomeOf(hand())).toBeNull();
  });
});
