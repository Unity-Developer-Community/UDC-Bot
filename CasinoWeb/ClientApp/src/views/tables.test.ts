import { describe, expect, it, vi } from 'vitest';

import type { Stakes, TableSummary } from '@/protocol';

import { renderTables } from '@/views/tables';

const small: Stakes = { bigBlind: 2, maxCommit: 200, minBalance: 20, smallBlind: 1 };
const big: Stakes = { bigBlind: 50, maxCommit: 5000, minBalance: 500, smallBlind: 25 };
const row = (id: number, seatsTaken: number, handRunning = false): TableSummary => ({ handRunning, id, maxSeats: 6, seatsTaken, stakes: small });

function setup() {
  const handlers = { onCreate: vi.fn(), onSit: vi.fn(), onWatch: vi.fn() };
  const view = renderTables(handlers);
  document.body.append(view.element);
  return { handlers, view };
}

describe('tables view', () => {
  it('says so when no table is open and lists the stakes to open one with', () => {
    const { view } = setup();

    view.update([], [small, big], false);

    expect(view.element.querySelector<HTMLElement>('.message')!.hidden).toBe(false);
    expect([...view.element.querySelectorAll('option')].map(o => o.textContent)).toEqual([
      'Blinds 1/2 (needs 20 tokens to sit, up to 200 per hand)',
      'Blinds 25/50 (needs 500 tokens to sit, up to 5,000 per hand)',
    ]);
  });

  it('opens a table with the chosen stakes', () => {
    const { handlers, view } = setup();
    view.update([], [small, big], false);

    view.element.querySelector('select')!.value = '50';
    view.element.querySelector('form')!.requestSubmit();

    expect(handlers.onCreate).toHaveBeenCalledWith(50);
  });

  it('keeps the chosen stakes when the rows update', () => {
    const { view } = setup();
    view.update([], [small, big], false);
    view.element.querySelector('select')!.value = '50';

    view.update([row(1, 2)], [small, big], false);

    expect(view.element.querySelector('select')!.value).toBe('50');
  });

  it('sits or watches by table and refuses a seat at a full table', () => {
    const { handlers, view } = setup();
    view.update([row(1, 2, true), row(2, 6)], [small], false);

    view.element.querySelector<HTMLButtonElement>('[data-table="1"] [data-action=sit]')!.click();
    view.element.querySelector<HTMLButtonElement>('[data-table="2"] [data-action=watch]')!.click();
    const full = view.element.querySelector<HTMLButtonElement>('[data-table="2"] [data-action=sit]')!;

    expect(handlers.onSit).toHaveBeenCalledWith(1);
    expect(handlers.onWatch).toHaveBeenCalledWith(2);
    expect(full.disabled).toBe(true);
    expect(full.textContent).toBe('Full');
    expect(view.element.querySelector('[data-table="1"] .table-info')!.textContent).toContain('hand running');
  });

  it('disables every button while a call runs', () => {
    const { view } = setup();

    view.update([row(1, 2)], [small], true);

    expect([...view.element.querySelectorAll('button')].every(b => b.disabled)).toBe(true);
  });
});
