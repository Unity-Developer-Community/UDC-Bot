import { describe, expect, it } from 'vitest';

import type { Me, Session } from '@/api';

import { loginUrl, resolveScreen } from '@/routes';

const me: Me = { avatarUrl: 'a', balance: 5, casinoEnabled: true, id: '1', name: 'Ada' };
const member = (overrides: Partial<Me> = {}): Session => ({ kind: 'member', me: { ...me, ...overrides } });

describe('resolveScreen', () => {
  it('shows the login page to someone not logged in', () => {
    expect(resolveScreen('/', { kind: 'anonymous' })).toBe('login');
  });

  it('shows the lobby to a member', () => {
    expect(resolveScreen('/', member())).toBe('lobby');
  });

  it('shows the closed page when the casino is closed', () => {
    expect(resolveScreen('/', member({ casinoEnabled: false }))).toBe('closed');
  });

  it('shows the pages the server redirects to, whatever the session', () => {
    expect(resolveScreen('/access-denied', { kind: 'anonymous' })).toBe('access-denied');
    expect(resolveScreen('/login-failed', { kind: 'anonymous' })).toBe('login-failed');
  });
});

describe('loginUrl', () => {
  it('returns to the current path', () => {
    expect(loginUrl('/play')).toBe('/login?returnUrl=%2Fplay');
  });

  it('returns to the lobby from a failure page', () => {
    expect(loginUrl('/access-denied')).toBe('/login?returnUrl=%2F');
  });
});
