import type { HubConnection } from '@microsoft/signalr';

import { HubConnectionState } from '@microsoft/signalr';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import type { ConnectionStatus } from '@/connection';

import { CasinoConnection, RECONNECT_DELAYS_MS } from '@/connection';

class FakeHub {
  closed: () => void = () => {};
  failNextStart = false;
  reconnected: () => void = () => {};
  reconnecting: () => void = () => {};
  state = HubConnectionState.Disconnected;
  start = vi.fn(async () => {
    if (this.failNextStart) {
      this.failNextStart = false;
      throw new Error('negotiate failed');
    }
    this.state = HubConnectionState.Connected;
  });

  stop = vi.fn(async () => {
    this.state = HubConnectionState.Disconnected;
  });

  onclose(handler: () => void): void {
    this.closed = handler;
  }

  onreconnected(handler: () => void): void {
    this.reconnected = handler;
  }

  onreconnecting(handler: () => void): void {
    this.reconnecting = handler;
  }
}

function setup() {
  const hub = new FakeHub();
  const connection = new CasinoConnection(hub as unknown as HubConnection);
  const seen: ConnectionStatus[] = [];
  connection.onStatus(status => seen.push(status));
  return { connection, hub, seen };
}

describe('casinoConnection', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('goes from connecting to connected', async () => {
    const { connection, seen } = setup();

    await connection.start();

    expect(seen).toEqual(['connecting', 'connected']);
  });

  it('shows reconnecting while the library retries, then connected again', async () => {
    const { connection, hub, seen } = setup();
    await connection.start();

    hub.reconnecting();
    hub.reconnected();

    expect(seen.slice(2)).toEqual(['reconnecting', 'connected']);
  });

  it('reports offline when the first connect fails and tries again later', async () => {
    const { connection, hub, seen } = setup();
    hub.failNextStart = true;

    await connection.start();
    expect(connection.status).toBe('offline');

    await vi.advanceTimersByTimeAsync(RECONNECT_DELAYS_MS.at(-1)!);
    expect(hub.start).toHaveBeenCalledTimes(2);
    expect(seen.at(-1)).toBe('connected');
  });

  it('starts over after the library gives up', async () => {
    const { connection, hub } = setup();
    await connection.start();

    hub.state = HubConnectionState.Disconnected;
    hub.closed();
    expect(connection.status).toBe('offline');

    await vi.advanceTimersByTimeAsync(RECONNECT_DELAYS_MS.at(-1)!);
    expect(connection.status).toBe('connected');
  });

  it('does not start a second time while connected', async () => {
    const { connection, hub } = setup();
    await connection.start();

    await connection.start();

    expect(hub.start).toHaveBeenCalledTimes(1);
    expect(connection.status).toBe('connected');
  });

  it('stays stopped when stop arrives during a pending start', async () => {
    const { connection, hub } = setup();
    let fail: () => void = () => {};
    hub.start.mockImplementationOnce(async () => new Promise<void>((_, reject) => {
      fail = () => reject(new Error('stopped'));
    }));
    const starting = connection.start();

    const stopping = connection.stop();
    fail();
    await Promise.all([starting, stopping]);
    await vi.advanceTimersByTimeAsync(RECONNECT_DELAYS_MS.at(-1)! * 2);

    expect(hub.start).toHaveBeenCalledTimes(1);
    expect(connection.status).toBe('idle');
  });

  it('does not retry after stop', async () => {
    const { connection, hub } = setup();
    await connection.start();

    await connection.stop();
    hub.closed();
    await vi.advanceTimersByTimeAsync(RECONNECT_DELAYS_MS.at(-1)! * 2);

    expect(hub.start).toHaveBeenCalledTimes(1);
    expect(connection.status).toBe('idle');
  });
});
