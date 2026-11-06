import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import type { Me } from '@/api';
import type { DailyClaim } from '@/protocol';

import { CallRefusedError } from '@/casinoApi';
import { formatCountdown, renderHeader } from '@/views/header';

const me: Me = { avatarUrl: 'a', balance: 1000, casinoEnabled: true, daily: { secondsUntilNext: 0, tokens: 100 }, id: '1', name: 'Ada' };

function claim(overrides: Partial<DailyClaim> = {}): DailyClaim {
  return { awarded: 100, balance: 1100, daily: { secondsUntilNext: 86_400, tokens: 100 }, status: 'ok', ...overrides };
}

function button(root: HTMLElement): HTMLButtonElement {
  return root.querySelector<HTMLButtonElement>('[data-testid=daily]')!;
}

function toast(root: HTMLElement): HTMLElement {
  return root.querySelector<HTMLElement>('[data-testid=toast]')!;
}

function balanceText(root: HTMLElement): string | null | undefined {
  return root.querySelector('[data-testid=balance]')?.textContent;
}

async function click(root: HTMLElement): Promise<void> {
  button(root).click();
  await vi.advanceTimersByTimeAsync(0);
}

describe('formatCountdown', () => {
  it('shows hours and minutes, then minutes, then seconds', () => {
    expect(formatCountdown(5 * 3600 + 12 * 60)).toBe('5h 12m');
    expect(formatCountdown(12 * 60)).toBe('12m');
    expect(formatCountdown(60)).toBe('1m');
    expect(formatCountdown(59)).toBe('59s');
    expect(formatCountdown(45)).toBe('45s');
  });

  it('rounds minutes up so the text never reads 0m before the claim', () => {
    expect(formatCountdown(61)).toBe('2m');
    expect(formatCountdown(3601)).toBe('1h 1m');
  });
});

describe('renderHeader daily reward', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('shows an enabled claim button with the amount when the reward is available', () => {
    const { element } = renderHeader(me, vi.fn(), { onClaim: vi.fn() });

    expect(button(element).textContent).toBe('Claim 100 tokens');
    expect(button(element).disabled).toBe(false);
  });

  it('shows a disabled countdown that becomes the claim button at 0 without a reload', () => {
    const waiting = { ...me, daily: { secondsUntilNext: 62, tokens: 100 } };
    const { element } = renderHeader(waiting, vi.fn(), { onClaim: vi.fn() });
    expect(button(element).textContent).toBe('Next daily in 2m');
    expect(button(element).disabled).toBe(true);

    vi.advanceTimersByTime(3000);
    expect(button(element).textContent).toBe('Next daily in 59s');

    vi.advanceTimersByTime(59_000);
    expect(button(element).textContent).toBe('Claim 100 tokens');
    expect(button(element).disabled).toBe(false);
  });

  it('has no daily button without a claim callback or without daily information', () => {
    expect(renderHeader(me, vi.fn()).element.querySelector('[data-testid=daily]')).toBeNull();
    expect(renderHeader({ ...me, daily: null }, vi.fn(), { onClaim: vi.fn() }).element.querySelector('[data-testid=daily]')).toBeNull();
    expect(renderHeader({ ...me, daily: undefined }, vi.fn(), { onClaim: vi.fn() }).element.querySelector('[data-testid=daily]')).toBeNull();
  });

  it('adds the awarded tokens, switches to the countdown and toasts for about 4 seconds', async () => {
    const onClaim = vi.fn(async () => Promise.resolve(claim({ balance: 99_999 })));
    const { element } = renderHeader(me, vi.fn(), { onClaim });

    await click(element);

    expect(balanceText(element)).toBe('1,100 tokens');
    expect(button(element).textContent).toBe('Next daily in 24h 0m');
    expect(button(element).disabled).toBe(true);
    expect(toast(element).hidden).toBe(false);
    expect(toast(element).textContent).toBe('Daily reward: +100 tokens');

    vi.advanceTimersByTime(4100);
    expect(toast(element).hidden).toBe(true);
  });

  it('switches to the countdown on notYet, with the balance unchanged', async () => {
    const onClaim = vi.fn(async () => Promise.resolve(claim({ awarded: 0, daily: { secondsUntilNext: 5 * 3600 + 12 * 60, tokens: 100 }, status: 'notYet' })));
    const { element } = renderHeader(me, vi.fn(), { onClaim });

    await click(element);

    expect(balanceText(element)).toBe('1,000 tokens');
    expect(button(element).textContent).toBe('Next daily in 5h 12m');
    expect(toast(element).textContent).toBe('Already claimed. Next daily in 5h 12m');
  });

  it.each([
    [new CallRefusedError('slow-down'), 'Slow down: try again in a moment.'],
    [new CallRefusedError('casino-closed'), 'The casino is closed right now.'],
    [new Error('connection lost'), 'Could not claim the daily reward. Check your connection and try again.'],
  ])('keeps the button enabled and toasts a reason when the claim fails (%#)', async (error, text) => {
    const onClaim = vi.fn(async () => Promise.reject(error));
    const { element } = renderHeader(me, vi.fn(), { onClaim });

    await click(element);

    expect(button(element).disabled).toBe(false);
    expect(button(element).textContent).toBe('Claim 100 tokens');
    expect(balanceText(element)).toBe('1,000 tokens');
    expect(toast(element).textContent).toBe(text);
  });

  it('disables the button while a claim is running', async () => {
    let finish: (value: DailyClaim) => void = () => {};
    const onClaim = vi.fn(async () => new Promise<DailyClaim>((resolve) => {
      finish = resolve;
    }));
    const { element } = renderHeader(me, vi.fn(), { onClaim });

    await click(element);
    expect(button(element).disabled).toBe(true);
    button(element).click();
    expect(onClaim).toHaveBeenCalledTimes(1);

    finish(claim());
    await vi.advanceTimersByTimeAsync(0);
    expect(button(element).textContent).toBe('Next daily in 24h 0m');
  });
});
