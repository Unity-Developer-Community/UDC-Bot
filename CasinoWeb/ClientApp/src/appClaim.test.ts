import { describe, expect, it, vi } from 'vitest';

import type { Me } from '@/api';
import type { CasinoConnection } from '@/connection';
import type { DailyClaim } from '@/protocol';

import { startApp } from '@/app';

const addTokens = vi.fn();

// The real table needs a canvas; only what the page does with the open screen matters here.
vi.mock('@/games/blackjack/blackjackPage', () => ({
  renderBlackjackScreen: () => ({ addTokens, destroy: () => {}, element: document.createElement('div') }),
}));

const me: Me = { avatarUrl: 'https://cdn.example/a.png', balance: 500, casinoEnabled: true, daily: { secondsUntilNext: 0, tokens: 100 }, id: '1', name: 'Ada' };

async function openBlackjackAndClaim(claim: DailyClaim): Promise<HTMLElement> {
  const connection = {
    invoke: vi.fn(async () => Promise.resolve(claim)),
    onSessionLost: () => () => {},
    onStatus: () => () => {},
    start: async () => Promise.resolve(),
    status: 'idle',
  } as unknown as CasinoConnection;
  const root = document.createElement('div');
  await startApp(root, { connection, fetchFn: async () => Promise.resolve(new Response(JSON.stringify(me))), pathname: '/' });
  root.querySelector<HTMLButtonElement>('[data-game=blackjack] button')!.click();

  root.querySelector<HTMLButtonElement>('[data-testid=daily]')!.click();
  await vi.waitFor(() => expect(root.querySelector('[data-testid=toast]')?.textContent).not.toBe(''));
  return root;
}

describe('claiming the daily reward with a game open', () => {
  it('tells the open screen to raise its limit by the awarded tokens', async () => {
    addTokens.mockClear();

    await openBlackjackAndClaim({ awarded: 100, balance: 600, daily: { secondsUntilNext: 86_400, tokens: 100 }, status: 'ok' });

    expect(addTokens).toHaveBeenCalledExactlyOnceWith(100);
  });

  it('leaves the open screen alone when the reward was already claimed', async () => {
    addTokens.mockClear();

    await openBlackjackAndClaim({ awarded: 0, balance: 500, daily: { secondsUntilNext: 3600, tokens: 100 }, status: 'notYet' });

    expect(addTokens).not.toHaveBeenCalled();
  });
});
