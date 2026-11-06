import { afterEach, describe, expect, it, vi } from 'vitest';

import type { PokerApi, PokerStage } from '@/games/poker/pokerGame';
import type { TableUpdate } from '@/protocol';

import { finishedView, hand, hubState, ok, options, STAKES, view } from '@/games/poker/fixtures';
import { renderPokerScreen } from '@/games/poker/pokerPage';

function setup(overrides: Partial<PokerApi> = {}, now = () => 1_000) {
  const handlers: { update: (u: TableUpdate) => void } = { update: () => {} };
  const api: PokerApi = {
    getState: vi.fn(async () => Promise.resolve(hubState({ poker: { allowAiSeats: true, stakes: [STAKES, { ...STAKES, bigBlind: 10, smallBlind: 5 }], table: null } }))),
    onTablesChanged: () => () => {},
    onTableUpdated: (handler) => {
      handlers.update = handler;
      return () => {};
    },
    pokerAct: vi.fn(async () => Promise.resolve(ok(view({ turnSeat: 1 })))),
    tableAddAi: vi.fn(async () => Promise.resolve(ok(view()))),
    tableCreate: vi.fn(async () => Promise.resolve(ok(view()))),
    tableLeave: vi.fn(async () => Promise.resolve(ok(null))),
    tableRemoveAi: vi.fn(async () => Promise.resolve(ok(view()))),
    tableSit: vi.fn(async () => Promise.resolve(ok(view()))),
    tablesList: vi.fn(async () => Promise.resolve([{ handRunning: true, id: 7, maxSeats: 6, seatsTaken: 2, stakes: STAKES }, { handRunning: false, id: 8, maxSeats: 6, seatsTaken: 6, stakes: STAKES }])),
    tableUnwatch: vi.fn(async () => Promise.resolve()),
    tableWatch: vi.fn(async () => Promise.resolve(ok(view({ mySeat: null, turnSeat: null })))),
    ...overrides,
  };
  const stage: PokerStage = { show: vi.fn() };
  const mountStage = vi.fn(async () => Promise.resolve({ destroy: vi.fn(), stage }));
  const onBack = vi.fn();
  const screen = renderPokerScreen({ api, mountStage, now, onBack, onBalance: vi.fn() });
  // A form only submits while it is part of the document.
  document.body.append(screen.element);
  const $ = <T extends HTMLElement>(selector: string): T => screen.element.querySelector<T>(selector)!;
  return { $, api, handlers, mountStage, onBack, screen, stage };
}

describe('poker screen', () => {
  afterEach(() => document.body.replaceChildren());

  it('lists the open tables with their stakes, seats and state, and no table yet', async () => {
    const { $, screen } = setup();
    await vi.waitFor(() => expect(screen.element.querySelectorAll('.table-row')).toHaveLength(2));

    const rows = [...screen.element.querySelectorAll('.table-row .table-info')].map(e => e.textContent);
    expect(rows[0]).toBe('Table 7 · Blinds 1/2 · 2 of 6 seats taken · hand running');
    expect(rows[1]).toContain('waiting for a hand');
    expect($('.poker-table').hidden).toBe(true);
    expect($('[data-table="8"] [data-action=sit]').textContent).toBe('Full');
    expect($<HTMLButtonElement>('[data-table="8"] [data-action=sit]').disabled).toBe(true);
  });

  it('opens a table with the chosen stakes and shows it, without drawing before there is a table', async () => {
    const { $, api, mountStage, screen } = setup();
    await vi.waitFor(() => expect($<HTMLSelectElement>('[data-testid=stakes]').options).toHaveLength(2));
    expect(mountStage).not.toHaveBeenCalled();

    $<HTMLSelectElement>('[data-testid=stakes]').value = '10';
    $<HTMLButtonElement>('[data-testid=create]').click();
    await vi.waitFor(() => expect($('.poker-table').hidden).toBe(false));

    expect(api.tableCreate).toHaveBeenCalledWith(10);
    expect(screen.element.querySelector<HTMLElement>('.tables-view')!.hidden).toBe(true);
    expect($('[data-testid=info]').textContent).toBe('Table 7 · Blinds 1/2');
    expect(mountStage).toHaveBeenCalledTimes(1);
  });

  it('shows the seats, the board and the member\'s own cards in words, and whose turn it is', async () => {
    const { $, screen } = setup({ getState: async () => Promise.resolve(hubState({ poker: { allowAiSeats: false, stakes: [STAKES], table: view({ turnSeat: 1 }) } })) });
    await vi.waitFor(() => expect($('.poker-table').hidden).toBe(false));

    const seats = [...screen.element.querySelectorAll('[data-testid=seats] li')].map(e => e.textContent);
    expect(seats[0]).toBe('Seat 1: Ada (you) · dealer, small blind · bet 1 this round · 1 in the pot this hand · cards A♠ K♥');
    expect(seats[1]).toBe('Seat 2: Bob · to act, big blind · bet 2 this round · 2 in the pot this hand');
    expect(seats[2]).toBe('Seat 3: empty');
    expect($('[data-testid=turn]').textContent).toBe('Waiting for Bob: 30 s left');
    expect($('[data-testid=board]').textContent).toBe('Board: none yet · Pot: 3 tokens');
    expect($('.poker-controls').hidden).toBe(true);
  });

  it('offers the actions of the member\'s turn and sends them', async () => {
    const { $, api, screen } = setup({ getState: async () => Promise.resolve(hubState({ poker: { allowAiSeats: false, stakes: [STAKES], table: view() } })) });
    await vi.waitFor(() => expect($('.poker-controls').hidden).toBe(false));

    const visible = [...screen.element.querySelectorAll<HTMLButtonElement>('.poker-controls [data-action]')].filter(b => b.hidden === false).map(b => b.dataset.action);
    expect(visible).toEqual(['fold', 'call', 'allIn', 'raise']);
    expect($('[data-action=call]').textContent).toBe('Call 2');
    expect($('[data-action=allIn]').textContent).toBe('All in (100)');
    expect($('[data-testid=turn]').textContent).toBe('Your turn: 30 s left');
    expect($<HTMLInputElement>('[data-testid=raise]').value).toBe('4');

    $<HTMLInputElement>('[data-testid=raise]').value = '20';
    $<HTMLButtonElement>('[data-action=raise]').click();
    await vi.waitFor(() => expect(api.pokerAct).toHaveBeenCalledWith('raise', 20));
    $<HTMLButtonElement>('[data-action=call]').click();
  });

  it('labels the bet button when nobody has bet yet', async () => {
    const free = view({ hand: hand({ options: options({ actions: ['fold', 'check', 'bet', 'allIn'], callAmount: 0 }) }) });
    const { $, api } = setup({ getState: async () => Promise.resolve(hubState({ poker: { allowAiSeats: false, stakes: [STAKES], table: free } })) });
    await vi.waitFor(() => expect($('.poker-controls').hidden).toBe(false));

    expect($('[data-action=raise]').textContent).toBe('Bet');
    $<HTMLButtonElement>('[data-action=raise]').click();
    await vi.waitFor(() => expect(api.pokerAct).toHaveBeenCalledWith('bet', 4));
  });

  it('keeps what the player typed when another view arrives with the same limits', async () => {
    const { $, handlers } = setup({ getState: async () => Promise.resolve(hubState({ poker: { allowAiSeats: false, stakes: [STAKES], table: view() } })) });
    await vi.waitFor(() => expect($('.poker-controls').hidden).toBe(false));
    $<HTMLInputElement>('[data-testid=raise]').value = '30';

    handlers.update({ tableId: 7, view: view() });

    expect($<HTMLInputElement>('[data-testid=raise]').value).toBe('30');
  });

  it('counts the turn down every moment without a new view', async () => {
    let clock = 1_000;
    vi.useFakeTimers();
    try {
      const { $ } = setup({ getState: async () => Promise.resolve(hubState({ poker: { allowAiSeats: false, stakes: [STAKES], table: view() } })) }, () => clock);
      await vi.waitFor(() => expect($('[data-testid=turn]').textContent).toBe('Your turn: 30 s left'));

      clock += 10_000;
      await vi.advanceTimersByTimeAsync(300);

      expect($('[data-testid=turn]').textContent).toBe('Your turn: 20 s left');
      expect($<HTMLProgressElement>('progress').value).toBe(20);
    }
    finally {
      vi.useRealTimers();
    }
  });

  it('says the result of a finished hand and when the next one starts', async () => {
    const { $ } = setup({ getState: async () => Promise.resolve(hubState({ poker: { allowAiSeats: false, stakes: [STAKES], table: finishedView() } })) });
    await vi.waitFor(() => expect($('[data-testid=results] li')).not.toBeNull());

    const lines = [...document.querySelectorAll('[data-testid=results] li')].map(e => e.textContent);
    expect(lines[0]).toBe('You win 8 tokens with Pair of Aces.');
    expect($('[data-testid=turn]').textContent).toBe('Next hand in 5 s');
  });

  it('lets a watcher sit at an empty seat and offers no actions or AI seats', async () => {
    const watched = view({ hand: hand({ options: null }), mySeat: null, turnSeat: null });
    const { $, api, screen } = setup({ getState: async () => Promise.resolve(hubState({ poker: { allowAiSeats: true, stakes: [STAKES], table: watched } })) });
    await vi.waitFor(() => expect($('.poker-table').hidden).toBe(false));

    expect($('[data-testid=leave]').textContent).toBe('Stop watching');
    expect($('[data-testid=add-ai]').parentElement!.hidden).toBe(true);
    expect($('.poker-controls').hidden).toBe(true);
    const sit = screen.element.querySelectorAll<HTMLButtonElement>('[data-action=sit-seat]');
    sit[0]!.click();
    await vi.waitFor(() => expect(api.tableSit).toHaveBeenCalledWith(7, 2));
  });

  it('adds an AI seat, and leaves the table back to the list', async () => {
    const { $, api } = setup({ getState: async () => Promise.resolve(hubState({ poker: { allowAiSeats: true, stakes: [STAKES], table: view() } })) });
    await vi.waitFor(() => expect($('[data-testid=add-ai]').parentElement!.hidden).toBe(false));

    $<HTMLButtonElement>('[data-testid=add-ai]').click();
    await vi.waitFor(() => expect(api.tableAddAi).toHaveBeenCalledTimes(1));
    await vi.waitFor(() => expect($<HTMLButtonElement>('[data-testid=leave]').disabled).toBe(false));
    $<HTMLButtonElement>('[data-testid=leave]').click();

    await vi.waitFor(() => expect($('.tables-view').hidden).toBe(false));
    expect(api.tableLeave).toHaveBeenCalledTimes(1);
    expect($('.poker-table').hidden).toBe(true);
  });

  it('goes back to the lobby from the list', async () => {
    const { $, onBack } = setup();
    await vi.waitFor(() => expect($('.tables-view').hidden).toBe(false));

    $<HTMLButtonElement>('.game-bar button').click();

    expect(onBack).toHaveBeenCalledTimes(1);
  });

  it('says the casino is closed and offers no tables', async () => {
    const { $ } = setup({ getState: async () => Promise.resolve(hubState({ casinoEnabled: false })) });
    await vi.waitFor(() => expect($('[data-testid=message]').textContent).toContain('closed'));

    expect($('.tables-view').hidden).toBe(true);
  });

  it('offers a retry when the state cannot be loaded', async () => {
    const { $ } = setup({ getState: async () => Promise.reject(new Error('offline')) });
    await vi.waitFor(() => expect($('[data-testid=message]').textContent).toContain('connection dropped'));

    expect($('.message[data-testid=message]').dataset.error).toBe('true');
  });
});
