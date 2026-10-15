import {
  HubConnectionBuilder,
  LogLevel,
  type HubConnection,
} from '@microsoft/signalr';
import type {
  CanonicalTimelineBackfill,
  RealtimeCursorV1,
} from './api';
import {
  applyBackfillPage,
  buildTimelineBackfillPath,
  markRealtimeDisconnected,
  observeRealtimeCursor,
  type RealtimeTimelineState,
} from './realtime';

const MAX_BACKFILL_PAGES_PER_REFRESH = 100;

export interface RealtimeAccessTokenProvider {
  getAccessToken(): Promise<string | null>;
}

export type RealtimeStatus = 'connecting' | 'connected' | 'reconnecting' | 'disconnected' | 'unavailable' | 'fallback' | 'refresh-failed';

export interface RealtimeConnection {
  on(methodName: string, callback: (...args: unknown[]) => void): void;
  onreconnecting(callback: (error?: Error) => void): void;
  onreconnected(callback: (connectionId?: string) => void): void;
  onclose(callback: (error?: Error) => void): void;
  start(): Promise<void>;
  stop(): Promise<void>;
  invoke(methodName: string, ...args: unknown[]): Promise<unknown>;
}

export interface RealtimeClientOptions {
  matchId: string;
  authProvider: RealtimeAccessTokenProvider | null;
  getState(): RealtimeTimelineState;
  onStateChange(state: RealtimeTimelineState): void;
  onStatusChange(status: RealtimeStatus): void;
  fetcher?: typeof fetch;
  connectionFactory?: (url: string, accessTokenFactory: () => Promise<string>) => RealtimeConnection;
}

interface NegotiateResponse {
  transport: string;
  url: string;
  accessToken: string | null;
  cursor: RealtimeCursorV1;
  revisionChanged: boolean;
}

function createSignalRConnection(url: string, accessTokenFactory: () => Promise<string>): HubConnection {
  return new HubConnectionBuilder()
    .withUrl(url, { accessTokenFactory })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Error)
    .build();
}

function validCursor(value: unknown, matchId: string): value is RealtimeCursorV1 {
  if (!value || typeof value !== 'object') return false;
  const cursor = value as Partial<RealtimeCursorV1>;
  return cursor.kind === 'matchUpdated' && cursor.schemaVersion === 1 && cursor.matchId === matchId &&
    Number.isSafeInteger(cursor.canonicalRevision) && (cursor.canonicalRevision ?? 0) > 0 &&
    Number.isSafeInteger(cursor.lastSequence) && (cursor.lastSequence ?? -1) >= 0 &&
    typeof cursor.serverUtc === 'string' && Number.isFinite(Date.parse(cursor.serverUtc));
}

/** Authenticated browser adapter. SignalR messages only wake a durable canonical backfill. */
export class MatchRealtimeClient {
  private readonly fetcher: typeof fetch;
  private readonly connectionFactory: NonNullable<RealtimeClientOptions['connectionFactory']>;
  private connection: RealtimeConnection | null = null;
  private transportConnected = false;
  private subscriptionReady = false;
  private transportEpoch = 0;
  private started = false;
  private stopped = false;
  private lifecycleGeneration = 0;
  private lifecycleAbortController = new AbortController();
  private backfillQueue: Promise<void> = Promise.resolve();

  constructor(private readonly options: RealtimeClientOptions) {
    this.fetcher = options.fetcher ?? fetch;
    this.connectionFactory = options.connectionFactory ?? createSignalRConnection;
  }

  async start(): Promise<void> {
    if (this.started || this.stopped) return;
    this.started = true;
    const generation = ++this.lifecycleGeneration;
    this.lifecycleAbortController = new AbortController();
    let canonicalRefreshSucceeded = false;
    try {
      this.assertAuthBoundary();
      this.setStatus('connecting');
      await this.refreshFromCanonicalStore(generation);
      canonicalRefreshSucceeded = true;
      if (!this.isActive(generation)) return;
      const negotiation = await this.negotiate(generation);
      if (!this.isActive(generation) || !negotiation) return;
      const hubUrl = this.validateHubUrl(negotiation);
      if (!this.isActive(generation)) return;
      const connection = this.connectionFactory(hubUrl, () => this.requireAccessToken());
      this.connection = connection;
      this.transportConnected = false;
      this.subscriptionReady = false;
      this.transportEpoch = 0;
      connection.on('MatchUpdated', (...args) => this.handleNotification(args[0], generation, connection));
      connection.onreconnecting(() => {
        if (!this.isActive(generation)) return;
        this.transportEpoch++;
        this.transportConnected = false;
        this.subscriptionReady = false;
        this.markDisconnected();
        this.setStatus('reconnecting');
      });
      connection.onreconnected(() => {
        this.transportConnected = true;
        void this.resubscribeAndBackfill(generation, connection, this.transportEpoch);
      });
      connection.onclose(() => {
        this.transportEpoch++;
        this.transportConnected = false;
        this.subscriptionReady = false;
        if (this.isActive(generation)) {
          this.markDisconnected();
          this.setStatus('disconnected');
        }
      });
      await connection.start();
      if (!this.isActive(generation)) {
        if (this.connection === connection) this.connection = null;
        await connection.stop().catch(() => undefined);
        return;
      }
      this.transportConnected = true;
      await this.subscribeAndBackfill(negotiation.cursor, generation, connection);
    } catch {
      if (this.isActive(generation)) {
        this.setStatus(canonicalRefreshSucceeded ? 'fallback' : 'unavailable');
        await this.disposeConnection();
      }
    }
  }

  async retry(): Promise<void> {
    await this.stop();
    this.stopped = false;
    this.started = false;
    await this.start();
  }

  async stop(): Promise<void> {
    this.stopped = true;
    this.transportConnected = false;
    this.subscriptionReady = false;
    this.lifecycleGeneration++;
    this.lifecycleAbortController.abort();
    await this.disposeConnection();
    this.setStatus('disconnected');
  }

  /** Called when the host explicitly changes selected time or returns to LIVE. */
  async refresh(): Promise<void> {
    const generation = this.lifecycleGeneration;
    const epoch = this.transportEpoch;
    await this.refreshFromCanonicalStore(generation);
    if (!this.isActive(generation) || epoch !== this.transportEpoch ||
        !this.transportConnected || !this.subscriptionReady) return;
    const state = this.options.getState();
    if (!state.needsBackfill && !state.reconnecting) this.setStatus('connected');
  }

  /** Explicit durable HTTP fallback for viewers when SignalR is unavailable. */
  async refreshFallback(): Promise<void> {
    try {
      await this.refreshFromCanonicalStore();
      if (!this.stopped) {
        const state = this.options.getState();
        const liveReady = this.connection !== null && this.transportConnected && this.subscriptionReady &&
          !state.needsBackfill && !state.reconnecting;
        this.setStatus(liveReady ? 'connected' : 'fallback');
      }
    } catch (error) {
      if (!this.stopped) {
        this.markDisconnected();
        this.setStatus('refresh-failed');
      }
      throw error;
    }
  }

  private async requireAccessToken(): Promise<string> {
    this.assertAuthBoundary();
    const token = await this.options.authProvider!.getAccessToken();
    if (typeof token !== 'string' || token.trim().length === 0)
      throw new Error('Authenticated realtime access is unavailable.');
    return token;
  }

  private assertAuthBoundary(): void {
    if (!this.options.authProvider || typeof this.options.authProvider.getAccessToken !== 'function')
      throw new Error('An authenticated application provider is required for realtime access.');
  }

  private async negotiate(generation: number): Promise<NegotiateResponse | null> {
    const token = await this.requireAccessToken();
    if (!this.isActive(generation)) return null;
    const response = await this.fetcher('/api/v1/realtime/negotiate', {
      method: 'POST',
      headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' },
      body: JSON.stringify({ matchId: this.options.matchId, clientRevision: this.options.getState().canonicalRevision }),
      signal: this.lifecycleAbortController.signal,
    });
    if (!this.isActive(generation)) return null;
    if (!response.ok) throw new Error(`Realtime negotiation failed with ${response.status}.`);
    const result = await response.json() as NegotiateResponse;
    if (!this.isActive(generation)) return null;
    if (result.transport !== 'aspnetcore-signalr' || result.accessToken !== null || !validCursor(result.cursor, this.options.matchId))
      throw new Error('Realtime negotiation returned an unsupported contract.');
    return result;
  }

  private validateHubUrl(negotiation: NegotiateResponse): string {
    if (typeof window === 'undefined') throw new Error('Realtime requires a browser origin.');
    const url = new URL(negotiation.url, window.location.origin);
    if (url.origin !== window.location.origin || url.pathname !== '/hubs/matches' || url.search || url.hash)
      throw new Error('Realtime negotiation returned an untrusted hub URL.');
    return url.toString();
  }

  private async subscribeAndBackfill(cursor: RealtimeCursorV1, generation: number, connection: RealtimeConnection): Promise<void> {
    const epoch = this.transportEpoch;
    if (!this.isActive(generation) || this.connection !== connection) return;
    await connection.invoke('SubscribeToMatch', this.options.matchId);
    if (!this.isActive(generation) || this.connection !== connection ||
        epoch !== this.transportEpoch || !this.transportConnected) return;
    this.subscriptionReady = true;
    this.setStatus('connected');
    this.applyCursorWakeup(cursor);
    // Read after group join to close the initial snapshot/subscription race.
    await this.refreshFromCanonicalStore(generation);
  }

  private async resubscribeAndBackfill(generation: number, connection: RealtimeConnection, epoch: number): Promise<void> {
    if (!this.isActive(generation) || this.connection !== connection || epoch !== this.transportEpoch) return;
    this.setStatus('reconnecting');
    try {
      const negotiation = await this.negotiate(generation);
      if (!this.isActive(generation) || !negotiation || this.connection !== connection ||
          epoch !== this.transportEpoch || !this.transportConnected) return;
      await connection.invoke('SubscribeToMatch', this.options.matchId);
      if (!this.isActive(generation) || this.connection !== connection ||
          epoch !== this.transportEpoch || !this.transportConnected) return;
      this.applyCursorWakeup(negotiation.cursor);
      try {
        await this.refreshFromCanonicalStore(generation);
      } catch {
        if (this.isActive(generation)) {
          this.markDisconnected();
          this.setStatus('refresh-failed');
        }
        return;
      }
      if (this.isActive(generation) && this.connection === connection &&
          epoch === this.transportEpoch && this.transportConnected) {
        this.subscriptionReady = true;
        this.setStatus(!this.options.getState().needsBackfill ? 'connected' : 'disconnected');
      }
    } catch {
      if (epoch === this.transportEpoch) {
        this.subscriptionReady = false;
        if (this.isActive(generation)) this.setStatus('disconnected');
      }
    }
  }

  private handleNotification(value: unknown, generation: number, connection: RealtimeConnection): void {
    if (!this.isActive(generation) || this.connection !== connection) return;
    if (!validCursor(value, this.options.matchId)) return;
    const epoch = this.transportEpoch;
    this.applyCursorWakeup(value);
    // The payload is never treated as a match fact; it only wakes the durable reader.
    void this.refreshFromCanonicalStore(generation).then(() => {
      if (!this.isActive(generation) || this.connection !== connection || epoch !== this.transportEpoch ||
          !this.transportConnected || !this.subscriptionReady) return;
      const state = this.options.getState();
      if (!state.needsBackfill && !state.reconnecting) this.setStatus('connected');
    }).catch(() => {
      if (this.isActive(generation) && this.connection === connection) {
        this.markDisconnected();
        this.setStatus('refresh-failed');
      }
    });
  }

  private applyCursorWakeup(cursor: RealtimeCursorV1): void {
    const current = this.options.getState();
    const next = observeRealtimeCursor(current, cursor);
    if (next !== current) this.options.onStateChange(next);
  }

  private refreshFromCanonicalStore(generation = this.lifecycleGeneration): Promise<void> {
    this.backfillQueue = this.backfillQueue.catch(() => undefined).then(async () => {
      if (!this.isActive(generation)) return;
      const context = this.options.getState();
      const sameContext = (state: RealtimeTimelineState) => state.matchId === context.matchId &&
        state.mode === context.mode &&
        (state.mode !== 'HISTORICAL' || state.selectedTimeSeconds === context.selectedTimeSeconds);
      const seenCursors = new Set<string>();
      let lastCursor: string | null = null;
      let stillBehind = false;
      let observedCursorRetryUsed = false;
      let previousResponseCursor: { revision: number; sequence: number } | null = null;

      for (let pageNumber = 0; pageNumber < MAX_BACKFILL_PAGES_PER_REFRESH; pageNumber++) {
        if (!this.isActive(generation)) return;
        const requestedState = this.options.getState();
        if (!sameContext(requestedState)) return;
        const requestedRevision = requestedState.canonicalRevision;
        const requestedThroughSequence = requestedState.throughSequence;
        const token = await this.requireAccessToken();
        if (!this.isActive(generation)) return;
        const response = await this.fetcher(`/api/v1/${buildTimelineBackfillPath(requestedState)}`, {
          headers: { Authorization: `Bearer ${token}` },
          signal: this.lifecycleAbortController.signal,
        });
        if (!this.isActive(generation)) return;
        if (!response.ok) throw new Error(`Canonical backfill failed with ${response.status}.`);
        const page = await response.json() as CanonicalTimelineBackfill;
        if (!this.isActive(generation)) return;
        const latest = this.options.getState();
        // A host-selected time/match change invalidates the in-flight page and all continuation pages.
        if (!sameContext(latest)) return;
        const next = applyBackfillPage(latest, page);
        if (!this.isActive(generation)) return;
        this.options.onStateChange(next);

        const cursor = page.events.nextCursor;
        if (!page.events.hasMore) {
          stillBehind = next.needsBackfill;
          const behindObservedLiveCursor = next.mode === 'LIVE' &&
            (next.canonicalRevision < next.observedLiveRevision ||
              (next.canonicalRevision === next.observedLiveRevision &&
                next.throughSequence < next.observedLiveSequence));
          if (stillBehind && behindObservedLiveCursor) {
            const cursorAdvanced = previousResponseCursor === null ||
              next.canonicalRevision > previousResponseCursor.revision ||
              (next.canonicalRevision === previousResponseCursor.revision &&
                next.throughSequence > previousResponseCursor.sequence);
            if (!observedCursorRetryUsed && cursorAdvanced) {
              // A live cursor can arrive while this request is pending. Fetch once
              // more so a consumed wake-up cannot leave a quiet client stale.
              observedCursorRetryUsed = true;
              previousResponseCursor = { revision: next.canonicalRevision, sequence: next.throughSequence };
              continue;
            }
            // The retry is bounded; preserve the behind marker and expose the
            // disconnected state instead of spinning on a stale canonical view.
            this.options.onStateChange({ ...next, needsBackfill: true, reconnecting: true });
            stillBehind = true;
          }
          break;
        }

        stillBehind = true;
        const sequenceAdvanced = next.canonicalRevision > requestedRevision ||
          next.throughSequence > requestedThroughSequence;
        if (!cursor || seenCursors.has(cursor) || cursor === lastCursor || !sequenceAdvanced) {
          this.options.onStateChange({ ...next, needsBackfill: true, reconnecting: true });
          break;
        }
        seenCursors.add(cursor);
        lastCursor = cursor;

        if (pageNumber === MAX_BACKFILL_PAGES_PER_REFRESH - 1)
          this.options.onStateChange({ ...next, needsBackfill: true, reconnecting: true });
      }

      if (stillBehind && this.isActive(generation) && this.connection)
        this.setStatus('disconnected');
    });
    return this.backfillQueue;
  }

  private markDisconnected(): void {
    const state = this.options.getState();
    this.options.onStateChange(markRealtimeDisconnected(state));
  }

  private setStatus(status: RealtimeStatus): void {
    this.options.onStatusChange(status);
  }

  private isActive(generation: number): boolean {
    return !this.stopped && generation === this.lifecycleGeneration;
  }

  private async disposeConnection(): Promise<void> {
    const existing = this.connection;
    this.connection = null;
    this.transportEpoch++;
    this.transportConnected = false;
    this.subscriptionReady = false;
    if (existing) await existing.stop().catch(() => undefined);
  }
}
