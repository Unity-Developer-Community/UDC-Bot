import { describe, expect, it, vi } from 'vitest';

import type { PokerApi } from '@/games/poker/pokerGame';
import type { TableSummary, TableUpdate } from '@/protocol';

import { CallRefusedError } from '@/casinoApi';
import { finishedView, hand, hubState, ok, options, seat, STAKES, view } from '@/games/poker/fixtures';
import { PokerGame, resultLines, secondsLeft, whoseTurn } from '@/games/poker/pokerGame';

const summary: TableSummary = { handRunning: false, id: 7, maxSeats: 6, seatsTaken: 1, stakes: STAKES };

function setup(overrides: Partial<PokerApi> = {}, now = () => 1_000) {
  const handlers: { tables: (t: TableSummary[]) => void; update: (u: TableUpdate) => void } = { tables: () => {}, update: () => {} };
  const unsubscribed = vi.fn();
  const api: PokerApi = {
    getState: vi.fn(async () => Promise.resolve(hubState())),
    onTablesChanged: (handler) => {
      handlers.tables = handler;
      return unsubscribed;
    },
    onTableUpdated: (handler) => {
      handlers.update = handler;
      return unsubscribed;
    },
    pokerAct: vi.fn(async () => Promise.resolve(ok(view({ turnSeat: 1 })))),
    tableAddAi: vi.fn(async () => Promise.resolve(ok(view()))),
    tableCreate: vi.fn(async () => Promise.resolve(ok(view()))),
    tableLeave: vi.fn(async () => Promise.resolve(ok(null))),
    tableRemoveAi: vi.fn(async () => Promise.resolve(ok(view()))),
    tableSit: vi.fn(async () => Promise.resolve(ok(view()))),
    tablesList: vi.fn(async () => Promise.resolve([summary])),
    tableUnwatch: vi.fn(async () => Promise.resolve()),
    tableWatch: vi.fn(async () => Promise.resolve(ok(view({ mySeat: null, turnSeat: null })))),
    ...overrides,
  };
  const stage = { show: vi.fn() };
  const onBalance = vi.fn();
  const game = new PokerGame({ api, hooks: { onBalance, onChange: () => {} }, now, stage });
  return { api, game, handlers, onBalance, stage, unsubscribed };
}

describe('poker controller', () => {
  it('lists the open tables once loaded, with the stakes and the balance', async () => {
    const { game, onBalance } = setup();

    await game.load();

    expect(game.state.phase).toBe('list');
    expect(game.state.tables).toEqual([summary]);
    expect(game.state.stakes).toEqual([STAKES]);
    expect(onBalance).toHaveBeenCalledWith(500);
  });

  it('goes back to the table the member still sits at when it loads', async () => {
    const { game, stage } = setup({
      getState: async () => Promise.resolve(hubState({ poker: { allowAiSeats: true, stakes: [STAKES], table: view() } })),
    });

    await game.load();

    expect(game.state.phase).toBe('table');
    expect(game.state.view?.id).toBe(7);
    expect(game.state.allowAiSeats).toBe(true);
    expect(stage.show).toHaveBeenCalledWith(expect.objectContaining({ id: 7 }));
  });

  it('keeps the seated table, with a warning, while the casino is closed', async () => {
    const { game } = setup({
      getState: async () => Promise.resolve(hubState({ casinoEnabled: false, poker: { allowAiSeats: false, stakes: [], table: view() } })),
    });

    await game.load();

    expect(game.state.phase).toBe('table');
    expect(game.state.message).toContain('only leave');
  });

  it('is closed with no table when the casino is closed', async () => {
    const { game } = setup({ getState: async () => Promise.resolve(hubState({ casinoEnabled: false })) });

    await game.load();

    expect(game.state.phase).toBe('closed');
  });

  it('updates the list when the server pushes the open tables', async () => {
    const { game, handlers } = setup();
    await game.load();

    handlers.tables([{ ...summary, id: 9 }]);

    expect(game.state.tables.map(t => t.id)).toEqual([9]);
  });

  it('creates a table and shows it with the member seated', async () => {
    const { api, game, stage } = setup();
    await game.load();

    await game.create(2);

    expect(api.tableCreate).toHaveBeenCalledWith(2);
    expect(game.state.phase).toBe('table');
    expect(stage.show).toHaveBeenLastCalledWith(expect.objectContaining({ id: 7, mySeat: 0 }));
  });

  it('shows the reason a sit was refused and refreshes the list', async () => {
    const { api, game } = setup({ tableSit: async () => Promise.resolve({ refusal: null, status: 'tableFull', view: null }) });
    await game.load();
    vi.mocked(api.tablesList).mockClear();

    await game.sit(7);

    expect(game.state.phase).toBe('list');
    expect(game.state.message).toBe('That table is full.');
    expect(game.state.messageIsError).toBe(true);
    expect(api.tablesList).toHaveBeenCalledTimes(1);
  });

  it('watches a table and can sit at it afterwards', async () => {
    const { api, game } = setup();
    await game.load();

    await game.watch(7);
    expect(game.state.view?.mySeat).toBeNull();
    await game.sit(7, 3);

    expect(api.tableSit).toHaveBeenCalledWith(7, 3);
    expect(game.state.view?.mySeat).toBe(0);
  });

  it('does not sit at a second table while seated', async () => {
    const { api, game } = setup();
    await game.load();
    await game.create(2);

    await game.sit(8);

    expect(api.tableSit).not.toHaveBeenCalled();
  });

  it('takes the table push that arrives before the answer of a sit', async () => {
    let answer: (call: ReturnType<typeof ok>) => void = () => {};
    const { game, handlers } = setup({
      tableSit: async () => new Promise((resolve) => {
        answer = resolve;
      }),
    });
    await game.load();

    const sitting = game.sit(7);
    handlers.update({ tableId: 7, view: view({ turnSeat: 1 }) });
    answer(ok(view({ turnSeat: 0 })));
    await sitting;

    // The push is newer than the answer of the call that triggered it.
    expect(game.state.view?.turnSeat).toBe(1);
    expect(game.state.phase).toBe('table');
  });

  it('ignores the push of a table the member is not at', async () => {
    const { game, handlers } = setup();
    await game.load();

    handlers.update({ tableId: 3, view: view({ id: 3 }) });

    expect(game.state.view).toBeNull();
    expect(game.state.phase).toBe('list');
  });

  it('starts the turn countdown from the seconds the view carries', async () => {
    const { game, handlers } = setup({}, () => 10_000);
    await game.load();
    await game.create(2);

    handlers.update({ tableId: 7, view: view({ turnSecondsLeft: 12 }) });

    expect(secondsLeft(game.state.turnEndsAt, 10_000)).toBe(12);
    expect(secondsLeft(game.state.turnEndsAt, 15_500)).toBe(7);
    expect(secondsLeft(game.state.turnEndsAt, 99_000)).toBe(0);
    expect(secondsLeft(null, 10_000)).toBeNull();
  });

  it('returns to the list with a message when the table closes under the member', async () => {
    const { game, handlers } = setup();
    await game.load();
    await game.create(2);

    handlers.update({ tableId: 7, view: null });
    await vi.waitFor(() => expect(game.state.phase).toBe('list'));

    expect(game.state.view).toBeNull();
    expect(game.state.message).toContain('no longer at that table');
  });

  it('leaves a table, and stops watching a watched one', async () => {
    const { api, game } = setup();
    await game.load();
    await game.create(2);

    await game.leave();
    expect(api.tableLeave).toHaveBeenCalledTimes(1);
    expect(game.state.phase).toBe('list');
    expect(game.state.message).toBe('');

    await game.watch(7);
    await game.leave();
    expect(api.tableUnwatch).toHaveBeenCalledTimes(1);
    expect(game.state.phase).toBe('list');
  });

  it('sends an action only on the member\'s turn and only when it is offered', async () => {
    const { api, game } = setup();
    await game.load();
    await game.create(2);

    await game.act('check');
    expect(api.pokerAct).not.toHaveBeenCalled();

    await game.act('call');
    expect(api.pokerAct).toHaveBeenCalledWith('call', 0);
    expect(game.state.view?.turnSeat).toBe(1);

    await game.act('call');
    expect(api.pokerAct).toHaveBeenCalledTimes(1);
  });

  it('keeps a raise inside the limits and sends an all-in for the allowed total', async () => {
    const { api, game } = setup({ tableCreate: async () => Promise.resolve(ok(view())), pokerAct: vi.fn(async () => Promise.resolve(ok(view()))) });
    await game.load();
    await game.create(2);

    await game.act('raise', 1);
    await game.act('raise', 5000);
    await game.act('allIn');

    expect(vi.mocked(api.pokerAct).mock.calls).toEqual([['raise', 4], ['raise', 100], ['allIn', 100]]);
  });

  it('shows the refusal and the new options when the balance no longer covers a bet', async () => {
    const lowered = view({ hand: hand({ options: options({ actions: ['fold', 'allIn'], allInTo: 6, maxRaiseTo: 6 }) }) });
    const { game } = setup({ pokerAct: async () => Promise.resolve({ refusal: 'insufficientBalance', status: 'refused', view: lowered }) });
    await game.load();
    await game.create(2);

    await game.act('raise', 50);

    expect(game.state.message).toContain('balance no longer covers');
    expect(game.state.messageIsError).toBe(true);
    expect(game.state.view?.hand?.options?.allInTo).toBe(6);
    expect(game.state.phase).toBe('table');
  });

  it('adds and removes AI seats only where the server allows them', async () => {
    const off = setup();
    await off.game.load();
    await off.game.create(2);
    await off.game.addAi();
    expect(off.api.tableAddAi).not.toHaveBeenCalled();

    const on = setup({ getState: async () => Promise.resolve(hubState({ poker: { allowAiSeats: true, stakes: [STAKES], table: null } })) });
    await on.game.load();
    await on.game.create(2);
    await on.game.addAi();
    await on.game.removeAi();
    expect(on.api.tableAddAi).toHaveBeenCalledTimes(1);
    expect(on.api.tableRemoveAi).toHaveBeenCalledTimes(1);
  });

  it('asks for the balance again when a hand ends, since the views carry none', async () => {
    const { api, game, handlers, onBalance } = setup();
    await game.load();
    await game.create(2);
    vi.mocked(api.getState).mockClear();
    onBalance.mockClear();
    vi.mocked(api.getState).mockResolvedValue(hubState({ balance: 508 }));

    handlers.update({ tableId: 7, view: finishedView() });

    await vi.waitFor(() => expect(onBalance).toHaveBeenCalledWith(508));
  });

  it('turns a closed casino into a message at a table and into the closed screen on the list', async () => {
    const closed = new CallRefusedError('casino-closed');
    const seated = setup({ pokerAct: async () => Promise.reject(closed) });
    await seated.game.load();
    await seated.game.create(2);
    await seated.game.act('call');
    expect(seated.game.state.phase).toBe('table');
    expect(seated.game.state.message).toContain('only leave');

    const listed = setup({ tableCreate: async () => Promise.reject(closed) });
    await listed.game.load();
    await listed.game.create(2);
    expect(listed.game.state.phase).toBe('closed');
  });

  it('reports a dropped connection and stays usable', async () => {
    const { game } = setup({ tableCreate: async () => Promise.reject(new Error('offline')) });
    await game.load();

    await game.create(2);

    expect(game.state.phase).toBe('list');
    expect(game.state.busy).toBe(false);
    expect(game.state.messageIsError).toBe(true);
  });

  it('stops listening to the pushes when destroyed', async () => {
    const { game, unsubscribed } = setup();
    await game.load();

    game.destroy();

    expect(unsubscribed).toHaveBeenCalledTimes(2);
  });
});

describe('poker texts', () => {
  it('says whose turn it is', () => {
    expect(whoseTurn(view())).toEqual({ mine: true, name: 'You', seat: 0 });
    expect(whoseTurn(view({ turnSeat: 1 }))).toEqual({ mine: false, name: 'Bob', seat: 1 });
    expect(whoseTurn(view({ turnSeat: null }))).toBeNull();
  });

  it('reports the winner, the loser\'s hand and the member\'s own result at showdown', () => {
    expect(resultLines(finishedView())).toEqual([
      'You win 8 tokens with Pair of Aces.',
      'Bob: K♣ 2♣ (High card King).',
      'Your result: +4 tokens.',
    ]);
  });

  it('reports a win without showdown without showing cards', () => {
    const base = finishedView();
    const folded = view({
      hand: hand({
        options: null,
        result: { pots: [], showdown: false, shown: [], winnings: [{ amount: 3, seat: 1 }] },
        seats: [seat(0, { committed: 1, folded: true }), seat(1, { committed: 2 })],
        street: 'showdown',
      }),
      turnSeat: null,
    });

    expect(resultLines(folded)).toEqual(['Bob wins 3 tokens (everyone else folded).', 'Your result: -1 tokens.']);
    expect(resultLines(view())).toEqual([]);
    expect(base.hand?.result?.showdown).toBe(true);
  });
});
