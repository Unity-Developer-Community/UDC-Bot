import type { HubConnection } from '@microsoft/signalr';

import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';

export type ConnectionStatus = 'connected' | 'connecting' | 'idle' | 'offline' | 'reconnecting';

export const HUB_PATH = '/hub/casino';

/** Waits 0, 2, 10 then 30 seconds between automatic reconnect attempts, then gives up; the last delay is also the pause between later manual retries. */
export const RECONNECT_DELAYS_MS = [0, 2000, 10000, 30000];

/** The wiring of a SignalR connection to a status the page can show, kept apart from the page so it can be tested. */
export class CasinoConnection {
  private readonly listeners = new Set<(status: ConnectionStatus) => void>();
  private current: ConnectionStatus = 'idle';
  private retry: ReturnType<typeof setTimeout> | undefined;
  private stopped = false;

  private readonly hub: HubConnection;

  constructor(hub: HubConnection) {
    this.hub = hub;
    hub.onreconnecting(() => this.set('reconnecting'));
    hub.onreconnected(() => this.set('connected'));
    hub.onclose(() => {
      if (!this.stopped)
        this.scheduleStart();
    });
  }

  get status(): ConnectionStatus {
    return this.current;
  }

  onStatus(listener: (status: ConnectionStatus) => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  async start(): Promise<void> {
    // The hub refuses a second start while it is connecting, connected or reconnecting; asking is harmless.
    if (this.hub.state !== HubConnectionState.Disconnected)
      return;
    clearTimeout(this.retry);
    this.stopped = false;
    this.set('connecting');
    try {
      await this.hub.start();
      this.set('connected');
    }
    catch {
      // The first connect is not covered by the automatic reconnect, so it is retried here.
      this.scheduleStart();
    }
  }

  async stop(): Promise<void> {
    this.stopped = true;
    clearTimeout(this.retry);
    if (this.hub.state !== HubConnectionState.Disconnected)
      await this.hub.stop();
    this.set('idle');
  }

  /** Once the automatic reconnect gives up the connection is closed for good, so the page starts it again. */
  private scheduleStart(): void {
    // A stop that arrives while a start is pending makes that start fail; it must not bring the connection back.
    if (this.stopped)
      return;
    this.set('offline');
    clearTimeout(this.retry);
    this.retry = setTimeout(() => void this.start(), RECONNECT_DELAYS_MS.at(-1));
  }

  private set(status: ConnectionStatus): void {
    if (status === this.current)
      return;
    this.current = status;
    this.listeners.forEach(listener => listener(status));
  }
}

export function createCasinoConnection(): CasinoConnection {
  const hub = new HubConnectionBuilder()
    .withUrl(HUB_PATH)
    .withAutomaticReconnect(RECONNECT_DELAYS_MS)
    .configureLogging(LogLevel.Warning)
    .build();
  return new CasinoConnection(hub);
}
