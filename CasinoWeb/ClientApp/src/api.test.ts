import { describe, expect, it } from 'vitest';

import { fetchSession, logout } from '@/api';

function reply(status: number, body?: unknown): typeof fetch {
  return async () => Promise.resolve(new Response(body === undefined ? null : JSON.stringify(body), { status }));
}

describe('fetchSession', () => {
  it('reports a 401 as not logged in', async () => {
    expect(await fetchSession(reply(401))).toEqual({ kind: 'anonymous' });
  });

  it('returns the member', async () => {
    const me = { avatarUrl: 'a', balance: 10, casinoEnabled: true, id: '1', name: 'Ada' };

    expect(await fetchSession(reply(200, me))).toEqual({ kind: 'member', me });
  });

  it('throws on a server error so the page can offer a retry', async () => {
    await expect(fetchSession(reply(500))).rejects.toThrow('500');
  });
});

describe('logout', () => {
  it('throws when the server refuses', async () => {
    await expect(logout(reply(403))).rejects.toThrow('403');
  });

  it('resolves on 204', async () => {
    await expect(logout(reply(204))).resolves.toBeUndefined();
  });
});
