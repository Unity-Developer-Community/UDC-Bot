import type { Stakes, TableSummary } from '@/protocol';

import { h } from '@/dom';
import { formatTokens } from '@/games/slots/slotsGame';

export interface TablesHandlers {
  onCreate: (bigBlind: number) => void;
  onSit: (tableId: number) => void;
  onWatch: (tableId: number) => void;
}

export interface TablesView {
  element: HTMLElement;
  /** `busy` disables the buttons while a call runs. */
  update: (tables: readonly TableSummary[], stakes: readonly Stakes[], busy: boolean) => void;
}

export function stakesLabel(stakes: Stakes): string {
  return `Blinds ${formatTokens(stakes.smallBlind)}/${formatTokens(stakes.bigBlind)}`;
}

function stakesHelp(stakes: Stakes): string {
  return `${stakesLabel(stakes)} (needs ${formatTokens(stakes.minBalance)} tokens to sit, up to ${formatTokens(stakes.maxCommit)} per hand)`;
}

/**
 * The open poker tables with a form to open one. The form stays in place while the rows are rebuilt on every change,
 * so a stakes choice survives the list updating live.
 */
export function renderTables(handlers: TablesHandlers): TablesView {
  const select = h('select', { 'class': 'bet-input stakes-select', 'data-testid': 'stakes', 'id': 'poker-stakes', 'name': 'stakes' });
  const createButton = h('button', { 'class': 'button', 'data-testid': 'create', 'type': 'submit' }, 'Open a table');
  const form = h('form', { class: 'bet' }, h('label', { for: 'poker-stakes' }, 'Stakes'), select, createButton);
  form.addEventListener('submit', (event) => {
    event.preventDefault();
    const bigBlind = Number(select.value);
    if (Number.isFinite(bigBlind) && bigBlind > 0)
      handlers.onCreate(bigBlind);
  });
  const rows = h('ul', { class: 'tables', role: 'list' });
  const empty = h('p', { class: 'message' }, 'No table is open. Open one and wait for others to join.');
  let shownStakes = '';

  const element = h('section', { class: 'tables-view' }, h('h2', {}, 'Open tables'), empty, rows, h('h2', {}, 'New table'), form);

  function update(tables: readonly TableSummary[], stakes: readonly Stakes[], busy: boolean): void {
    const key = JSON.stringify(stakes);
    if (key !== shownStakes) {
      shownStakes = key;
      const chosen = select.value;
      select.replaceChildren(...stakes.map(s => h('option', { value: String(s.bigBlind) }, stakesHelp(s))));
      if (chosen)
        select.value = chosen;
    }
    createButton.disabled = busy || stakes.length === 0;
    select.disabled = busy;
    empty.hidden = tables.length > 0;
    rows.replaceChildren(...tables.map((table) => {
      const full = table.seatsTaken >= table.maxSeats;
      const sit = h('button', { 'class': 'button small', 'data-action': 'sit', 'type': 'button' }, full ? 'Full' : 'Sit');
      sit.disabled = busy || full;
      sit.addEventListener('click', () => handlers.onSit(table.id));
      const watch = h('button', { 'class': 'button secondary small', 'data-action': 'watch', 'type': 'button' }, 'Watch');
      watch.disabled = busy;
      watch.addEventListener('click', () => handlers.onWatch(table.id));
      return h(
        'li',
        { 'class': 'table-row', 'data-table': String(table.id) },
        h('span', { class: 'table-info' }, `Table ${table.id} · ${stakesLabel(table.stakes)} · ${table.seatsTaken} of ${table.maxSeats} seats taken · ${table.handRunning ? 'hand running' : 'waiting for a hand'}`),
        h('span', { class: 'table-buttons' }, sit, watch),
      );
    }));
  }

  return { element, update };
}
