import { describe, expect, it, vi } from 'vitest';

import type { Me } from '@/api';
import type { CasinoConnection, ConnectionStatus } from '@/connection';

import { startApp } from '@/app';

const me: Me = { avatarUrl: 'https://cdn.example/a.png', balance: 12345, casinoEnabled: true, id: '1', name: 'Ada <b>' };

function serve(status: number, body?: unknown): typeof fetch {
  return async () => Promise.resolve(new Response(body === undefined ? null : JSON.stringify(body), { status }));
}

async function render(deps: Parameters<typeof startApp>[1]) {
  const root = document.createElement('div');
  await startApp(root, deps);
  return root;
}

describe('startApp', () => {
  it('offers the Discord login to someone not logged in', async () => {
    const root = await render({ fetchFn: serve(401), pathname: '/' });

    expect(root.querySelector('a')?.getAttribute('href')).toBe('/login?returnUrl=%2F');
  });

  it('shows the player and the lobby entries to a member', async () => {
    const root = await render({ fetchFn: serve(200, me), pathname: '/' });

    expect(root.querySelector('.player-name')?.textContent).toBe('Ada <b>');
    expect(root.querySelector('[data-testid=balance]')?.textContent).toBe('12,345 tokens');
    expect(root.querySelector('.player-name b')).toBeNull();
    expect(root.querySelector('img.avatar')?.getAttribute('src')).toBe(me.avatarUrl);
    expect([...root.querySelectorAll('.game h2')].map(e => e.textContent)).toEqual(['Slots', 'Blackjack']);
  });

  it('shows the closed page, with the header, when the casino is closed', async () => {
    const root = await render({ fetchFn: serve(200, { ...me, balance: null, casinoEnabled: false }), pathname: '/' });

    expect(root.querySelector('main h1')?.textContent).toBe('Casino closed');
    expect(root.querySelector('.game')).toBeNull();
    expect(root.querySelector('.header')).not.toBeNull();
  });

  it('shows access denied without calling the server', async () => {
    const fetchFn = vi.fn(serve(500));

    const root = await render({ fetchFn, pathname: '/access-denied' });

    expect(root.querySelector('h1')?.textContent).toBe('Access denied');
    expect(fetchFn).not.toHaveBeenCalled();
  });

  it('shows login failed with a way to retry', async () => {
    const root = await render({ fetchFn: serve(500), pathname: '/login-failed' });

    expect(root.querySelector('h1')?.textContent).toBe('Login failed');
    expect(root.querySelector('a')?.getAttribute('href')).toBe('/login?returnUrl=%2F');
  });

  it('offers a retry when the server cannot be reached', async () => {
    const root = await render({ fetchFn: serve(500), pathname: '/' });

    expect(root.querySelector('h1')?.textContent).toBe('Something went wrong');
    expect(root.querySelector('button')?.textContent).toBe('Try again');
  });

  it('logs out and restarts', async () => {
    const restart = vi.fn();
    const fetchFn = vi.fn(async (input: RequestInfo | URL) =>
      Promise.resolve(String(input).endsWith('/logout') ? new Response(null, { status: 204 }) : new Response(JSON.stringify(me))),
    ) as unknown as typeof fetch;
    const root = await render({ fetchFn, pathname: '/', restart });

    root.querySelector<HTMLButtonElement>('.header button')!.click();
    await vi.waitFor(() => expect(restart).toHaveBeenCalled());

    expect(fetchFn).toHaveBeenCalledWith('/api/logout', { method: 'POST' });
  });

  it('shows the connection status in the header', async () => {
    let listener: (status: ConnectionStatus) => void = () => {};
    const start = vi.fn(async () => Promise.resolve());
    const connection = {
      status: 'idle',
      onStatus: (l: (status: ConnectionStatus) => void) => {
        listener = l;
        return () => {};
      },
      start,
    } as unknown as CasinoConnection;
    const root = await render({ connection, fetchFn: serve(200, me), pathname: '/' });
    const status = root.querySelector<HTMLElement>('.status')!;
    expect(status.hidden).toBe(true);

    listener('reconnecting');

    expect(status.hidden).toBe(false);
    expect(status.textContent).toContain('Reconnecting');
    expect(start).toHaveBeenCalled();
  });
});
