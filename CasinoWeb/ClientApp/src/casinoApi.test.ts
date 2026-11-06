import { describe, expect, it, vi } from 'vitest';

import type { HubCaller } from '@/casinoApi';

import { CallRefusedError, CasinoApi } from '@/casinoApi';

function setup(invoke: HubCaller['invoke'] = vi.fn(async () => Promise.resolve(undefined as never))) {
  const stop = vi.fn();
  const on = vi.fn(() => stop);
  const reportSessionLost = vi.fn();
  return { api: new CasinoApi({ invoke, on, reportSessionLost }), invoke, on, reportSessionLost, stop };
}

describe('casino API poker calls', () => {
  it('calls the table methods of the hub with their arguments', async () => {
    const { api, invoke } = setup();

    await api.tablesList();
    await api.tableCreate(10);
    await api.tableWatch(3);
    await api.tableSit(3, null);
    await api.tableAddAi();
    await api.tableRemoveAi();
    await api.pokerAct('raise', 40);
    await api.tableUnwatch();
    await api.tableLeave();

    expect(vi.mocked(invoke).mock.calls).toEqual([
      ['TablesList'],
      ['TableCreate', 10],
      ['TableWatch', 3],
      ['TableSit', 3, null],
      ['TableAddAi'],
      ['TableRemoveAi'],
      ['PokerAct', 'raise', 40],
      ['TableUnwatch'],
      ['TableLeave'],
    ]);
  });

  it('listens to the pushes under their hub names and returns the way to stop', () => {
    const { api, on, stop } = setup();
    const handler = vi.fn();

    const stopTables = api.onTablesChanged(handler);
    api.onTableUpdated(handler);

    expect(on).toHaveBeenCalledWith('TablesChanged', handler);
    expect(on).toHaveBeenCalledWith('TableUpdated', handler);
    stopTables();
    expect(stop).toHaveBeenCalledTimes(1);
  });

  it('turns a refusal of the hub into a CallRefusedError and reports a lost session', async () => {
    const { api, reportSessionLost } = setup(async () => Promise.reject(new Error('An unexpected error occurred invoking \'TableSit\': session-expired')));

    await expect(api.tableSit(1, null)).rejects.toBeInstanceOf(CallRefusedError);
    expect(reportSessionLost).toHaveBeenCalledTimes(1);
  });
});
