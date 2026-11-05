import type { HubConnection } from '@microsoft/signalr';

import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';

import { fetchSession } from '@/api';

export type ConnectionStatus = 'connected' | 'connecting' | 'idle' | 'offline' | 'reconnecting';

export const HUB_PATH = '/hub/casino';

/** Waits 0, 2, 10 then 30 seconds between automatic reconnect attempts, then gives up; the last delay is also the pause between later manual retries. */
export const RECONNECT_DELAYS_MS = [0, 2000, 10000, 30000];

/**
 * The wiring of a SignalR connection to a status the page can show, kept apart from the page so it can be tested.
 *
 * A login that no longer holds (cookie expired, or the player left the server) makes the server answer 401 to the
 * negotiation. The client library hides that status, so when the connection cannot be (re)established the session is
 * asked about instead, and a lost one is reported once through `onSessionLost`.
 */
export class CasinoConnection {
  private readonly listeners = new Set<(status: ConnectionStatus) => void>();
  private readonly lostListeners = new Set<() => void>();
  private current: ConnectionStatus = 'idle';
  private retry: ReturnType<typeof setTimeout> | undefined;
  private stopped = false;

  private readonly hub: HubConnection;
  private readonly isSessionLost: () => Promise<boolean>;
  private lost = false;

  constructor(hub: HubConnection, isSessionLost: () => Promise<boolean> = async () => Promise.resolve(false)) {
    this.hub = hub;
    this.isSessionLost = isSessionLost;
    hub.onreconnecting(() => {
      this.set('reconnecting');
      // The server drops the socket of a login it no longer accepts; there is no point in retrying that for a minute.
      void this.checkSession();
    });
    hub.onreconnected(() => this.set('connected'));
    hub.onclose(() => {
      if (!this.stopped)
        void this.retryUnlessSessionLost();
    });
  }

  get status(): ConnectionStatus {
    return this.current;
  }

  onStatus(listener: (status: ConnectionStatus) => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  /** Calls a hub method and returns its result. */
  async invoke<T>(method: string, ...args: unknown[]): Promise<T> {
    return this.hub.invoke<T>(method, ...args);
  }

  onSessionLost(listener: () => void): () => void {
    this.lostListeners.add(listener);
    return () => this.lostListeners.delete(listener);
  }

  /** For a caller that learned the login is gone (the hub said so): same effect as the connection finding out itself. */
  reportSessionLost(): void {
    if (this.lost)
      return;
    this.lost = true;
    this.stopped = true;
    clearTimeout(this.retry);
    void this.hub.stop().catch(() => {});
    this.set('idle');
    this.lostListeners.forEach(listener => listener());
  }

  async start(): Promise<void> {
    // The hub refuses a second start while it is connecting, connected or reconnecting; asking is harmless.
    if (this.lost || this.hub.state !== HubConnectionState.Disconnected)
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
      await this.retryUnlessSessionLost();
    }
  }

  async stop(): Promise<void> {
    this.stopped = true;
    clearTimeout(this.retry);
    if (this.hub.state !== HubConnectionState.Disconnected)
      await this.hub.stop();
    this.set('idle');
  }

  private async checkSession(): Promise<boolean> {
    const lost = await this.isSessionLost().catch(() => false);
    if (lost)
      this.reportSessionLost();
    return lost;
  }

  private async retryUnlessSessionLost(): Promise<void> {
    if (!await this.checkSession())
      this.scheduleStart();
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
  return new CasinoConnection(hub, async () => (await fetchSession()).kind === 'anonymous');
}
