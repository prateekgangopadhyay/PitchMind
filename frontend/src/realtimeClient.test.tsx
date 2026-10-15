import { describe, expect, it, vi } from 'vitest';
import { act, createElement } from 'react';
import { createRoot } from 'react-dom/client';
import { renderToStaticMarkup } from 'react-dom/server';
import type { CanonicalLedgerEvent, CanonicalTimelineBackfill, RealtimeCursorV1 } from './api';
import { selectHistoricalTime, type RealtimeTimelineState } from './realtime';
import { MatchRealtimeClient, type RealtimeConnection } from './realtimeClient';
import { RealtimeAuthProvider, RealtimeMatchUpdates } from './RealtimeMatchUpdates';

const matchId = '11111111-1111-4111-8111-111111111111';
const cursor = (lastSequence = 0): RealtimeCursorV1 => ({
  kind: 'matchUpdated', matchId, canonicalRevision: 2, lastSequence,
  serverUtc: '2026-10-11T12:00:00Z', schemaVersion: 1,
});

const initialState = (): RealtimeTimelineState => ({
  matchId, mode: 'LIVE', selectedTimeSeconds: 60, observedLiveRevision: 2, observedLiveSequence: 0,
  canonicalRevision: 2, throughSequence: 0,
  snapshot: { matchId, canonicalRevision: 2, projectionRevision: 2, selectedTimeSeconds: 5400, asOfSeconds: 60,
    throughSequence: 0, algorithmVersion: 'rules-v1', sourceSha256: 'a'.repeat(64),
    stateSha256: 'b'.repeat(64), provenance: 'synthetic', period: 1, phase: 'firsthalf',
    homeScore: 0, awayScore: 0, possessionTeamId: null, homeActivePlayers: 11,
    awayActivePlayers: 11, players: [] },
  events: [], nextCursor: null, hasMore: false, needsBackfill: false,
  hasNewMatchUpdate: false, reconnecting: false,
});

function backfill(atSeconds: number): CanonicalTimelineBackfill {
  const snapshot = { ...initialState().snapshot, selectedTimeSeconds: atSeconds, asOfSeconds: atSeconds };
  return { snapshot, events: { matchId, canonicalRevision: 2, projectionRevision: 2, selectedTimeSeconds: atSeconds,
    throughSequence: 0, items: [], nextCursor: null, hasMore: false },
  revisionChanged: false, replayRequired: false, requestedAfterSequence: -1 };
}

function event(sequence: number): CanonicalLedgerEvent {
  return { eventId: `event-${sequence}`, matchId, sequence, type: 'Pass', clockSeconds: 60,
    period: 1, occurredAt: '2026-10-11T12:00:00Z' };
}

function backfillPage(atSeconds: number, throughSequence: number, items: CanonicalLedgerEvent[],
  hasMore: boolean, nextCursor: string | null): CanonicalTimelineBackfill {
  const response = backfill(atSeconds);
  response.snapshot.throughSequence = throughSequence;
  response.events.throughSequence = throughSequence;
  response.events.items = items;
  response.events.hasMore = hasMore;
  response.events.nextCursor = nextCursor;
  return response;
}

class FakeConnection implements RealtimeConnection {
  handlers = new Map<string, (...args: unknown[]) => void>();
  reconnecting: (() => void) | undefined;
  reconnected: (() => void) | undefined;
  closed: (() => void) | undefined;
  calls: Array<[string, ...unknown[]]> = [];
  private invokeGate: Promise<void> | null = null;
  private releaseInvokeGate: (() => void) | undefined;
  on(method: string, callback: (...args: unknown[]) => void) { this.handlers.set(method, callback); }
  onreconnecting(callback: () => void) { this.reconnecting = callback; }
  onreconnected(callback: () => void) { this.reconnected = callback; }
  onclose(callback: () => void) { this.closed = callback; }
  async start() { /* connected */ }
  async stop() { /* stopped */ }
  async invoke(method: string, ...args: unknown[]) {
    this.calls.push([method, ...args]);
    const gate = this.invokeGate;
    if (gate) {
      this.invokeGate = null;
      await gate;
    }
  }
  close() { this.closed?.(); }
  beginReconnect() { this.reconnecting?.(); }
  holdNextInvoke() {
    this.invokeGate = new Promise(resolve => { this.releaseInvokeGate = resolve; });
  }
  releaseInvoke() { this.releaseInvokeGate?.(); }
}

const tick = async () => { await Promise.resolve(); await Promise.resolve(); await Promise.resolve(); };

describe('MatchRealtimeClient', () => {
  it('fails closed without the host auth provider', async () => {
    const fetcher = vi.fn<typeof fetch>();
    const status = vi.fn();
    const client = new MatchRealtimeClient({ matchId, authProvider: null, getState: initialState,
      onStateChange: vi.fn(), onStatusChange: status, fetcher });
    await client.start();
    expect(fetcher).not.toHaveBeenCalled();
    expect(status).toHaveBeenLastCalledWith('unavailable');
  });

  it('cancels pending startup when stopped during the first durable backfill', async () => {
    let resolveBackfill: ((response: Response) => void) | undefined;
    const fetcher = vi.fn<typeof fetch>(() => new Promise(resolve => { resolveBackfill = resolve; }));
    const connection = new FakeConnection();
    const connectionFactory = vi.fn(() => connection);
    const client = new MatchRealtimeClient({ matchId, authProvider: { getAccessToken: async () => 'token' },
      getState: initialState, onStateChange: vi.fn(), onStatusChange: vi.fn(), fetcher, connectionFactory });

    const startup = client.start();
    await vi.waitFor(() => expect(fetcher).toHaveBeenCalledTimes(1));
    await client.stop();
    resolveBackfill?.(Response.json({}));
    await startup;

    expect(connectionFactory).not.toHaveBeenCalled();
    expect(connection.calls).toEqual([]);
  });

  it('uses injected bearer auth, subscribes to the exact match, and backfills after cursor wake-ups', async () => {
    let state = initialState();
    const setState = vi.fn((next: RealtimeTimelineState) => { state = next; });
    const connection = new FakeConnection();
    let signalRAccessTokenFactory: (() => Promise<string>) | undefined;
    const fetcher = vi.fn<typeof fetch>(async (input, init) => {
      const url = String(input);
      if (url.endsWith('/realtime/negotiate')) {
        expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer injected-token');
        return Response.json({ transport: 'aspnetcore-signalr', url: '/hubs/matches', accessToken: null,
          cursor: cursor(), revisionChanged: false });
      }
      expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer injected-token');
      const at = Number(new URL(url, 'https://app.example').searchParams.get('atSeconds'));
      return Response.json(backfill(at));
    });
    const client = new MatchRealtimeClient({ matchId, authProvider: { getAccessToken: async () => 'injected-token' },
      getState: () => state, onStateChange: setState, onStatusChange: vi.fn(), fetcher,
      connectionFactory: vi.fn((_url, tokenFactory) => { signalRAccessTokenFactory = tokenFactory; return connection; }) });

    await client.start();
    await expect(signalRAccessTokenFactory?.()).resolves.toBe('injected-token');
    expect(connection.calls).toContainEqual(['SubscribeToMatch', matchId]);
    const beforeNotificationCalls = fetcher.mock.calls.length;
    connection.handlers.get('MatchUpdated')?.(cursor(1));
    await tick();
    expect(fetcher.mock.calls.length).toBeGreaterThan(beforeNotificationCalls);
    expect(state.throughSequence).toBe(0);
    expect(fetcher.mock.calls.some(([input]) => String(input).includes('timeline/backfill'))).toBe(true);
    await client.stop();
  });

  it('restores connected status when a later notification catches up after a failed backfill', async () => {
    let state = initialState();
    const setState = vi.fn((next: RealtimeTimelineState) => { state = next; });
    const connection = new FakeConnection();
    const status = vi.fn();
    let backfillCalls = 0;
    const fetcher = vi.fn<typeof fetch>(async input => {
      if (String(input).endsWith('/realtime/negotiate'))
        return Response.json({ transport: 'aspnetcore-signalr', url: '/hubs/matches', accessToken: null,
          cursor: cursor(), revisionChanged: false });
      backfillCalls++;
      if (backfillCalls === 3) return Response.json({}, { status: 503 });
      if (backfillCalls > 3) return Response.json(backfillPage(5400, 1, [event(1)], false, null));
      return Response.json(backfill(5400));
    });
    const client = new MatchRealtimeClient({ matchId, authProvider: { getAccessToken: async () => 'token' },
      getState: () => state, onStateChange: setState, onStatusChange: status, fetcher,
      connectionFactory: () => connection });

    await client.start();
    connection.handlers.get('MatchUpdated')?.(cursor(1));
    await vi.waitFor(() => expect(status).toHaveBeenLastCalledWith('disconnected'));
    expect(state.throughSequence).toBe(0);

    connection.handlers.get('MatchUpdated')?.(cursor(1));
    await vi.waitFor(() => expect(status).toHaveBeenLastCalledWith('connected'));
    expect(state.throughSequence).toBe(1);
    expect(state.needsBackfill).toBe(false);
    await client.stop();
  });

  it('restores connected status when a later selected-time refresh succeeds', async () => {
    let state = initialState();
    const setState = vi.fn((next: RealtimeTimelineState) => { state = next; });
    const connection = new FakeConnection();
    const status = vi.fn();
    let backfillCalls = 0;
    const fetcher = vi.fn<typeof fetch>(async input => {
      if (String(input).endsWith('/realtime/negotiate'))
        return Response.json({ transport: 'aspnetcore-signalr', url: '/hubs/matches', accessToken: null,
          cursor: cursor(), revisionChanged: false });
      backfillCalls++;
      if (backfillCalls === 3) return Response.json({}, { status: 503 });
      const at = Number(new URL(String(input), 'https://app.example').searchParams.get('atSeconds'));
      return Response.json(backfillCalls > 3
        ? backfillPage(at, 0, [event(0)], false, null)
        : backfill(at));
    });
    const client = new MatchRealtimeClient({ matchId, authProvider: { getAccessToken: async () => 'token' },
      getState: () => state, onStateChange: setState, onStatusChange: status, fetcher,
      connectionFactory: () => connection });

    await client.start();
    state = selectHistoricalTime(state, 45);
    await expect(client.refresh()).rejects.toThrow('Canonical backfill failed with 503.');
    status('disconnected');
    state = selectHistoricalTime(state, 90);
    await client.refresh();

    expect(state.snapshot.selectedTimeSeconds).toBe(90);
    expect(state.needsBackfill).toBe(false);
    expect(status).toHaveBeenLastCalledWith('connected');
    await client.stop();
  });

  it('keeps disconnected status when an in-flight backfill succeeds after the transport closes', async () => {
    let state = initialState();
    const setState = vi.fn((next: RealtimeTimelineState) => { state = next; });
    const connection = new FakeConnection();
    const status = vi.fn();
    let backfillCalls = 0;
    let resolveBackfill: ((response: Response) => void) | undefined;
    const fetcher = vi.fn<typeof fetch>(async input => {
      if (String(input).endsWith('/realtime/negotiate'))
        return Response.json({ transport: 'aspnetcore-signalr', url: '/hubs/matches', accessToken: null,
          cursor: cursor(), revisionChanged: false });
      backfillCalls++;
      if (backfillCalls === 3) return new Promise(resolve => { resolveBackfill = resolve; });
      return Response.json(backfillCalls > 3
        ? backfillPage(5400, 1, [event(1)], false, null)
        : backfill(5400));
    });
    const client = new MatchRealtimeClient({ matchId, authProvider: { getAccessToken: async () => 'token' },
      getState: () => state, onStateChange: setState, onStatusChange: status, fetcher,
      connectionFactory: () => connection });

    await client.start();
    connection.handlers.get('MatchUpdated')?.(cursor(1));
    await vi.waitFor(() => expect(backfillCalls).toBe(3));
    connection.close();
    expect(status).toHaveBeenLastCalledWith('disconnected');
    resolveBackfill?.(Response.json(backfillPage(5400, 1, [event(1)], false, null)));

    await vi.waitFor(() => expect(state.throughSequence).toBe(1));
    expect(status).toHaveBeenLastCalledWith('disconnected');
    await client.stop();
  });

  it('waits for match resubscription before restoring connected status after reconnect', async () => {
    let state = initialState();
    const setState = vi.fn((next: RealtimeTimelineState) => { state = next; });
    const connection = new FakeConnection();
    const status = vi.fn();
    let negotiateCalls = 0;
    let backfillCalls = 0;
    let resolvePendingBackfill: ((response: Response) => void) | undefined;
    let resolveReconnectNegotiation: ((response: Response) => void) | undefined;
    const fetcher = vi.fn<typeof fetch>(async input => {
      if (String(input).endsWith('/realtime/negotiate')) {
        negotiateCalls++;
        if (negotiateCalls === 2) return new Promise(resolve => { resolveReconnectNegotiation = resolve; });
        return Response.json({ transport: 'aspnetcore-signalr', url: '/hubs/matches', accessToken: null,
          cursor: cursor(), revisionChanged: false });
      }
      backfillCalls++;
      if (backfillCalls === 3) return new Promise(resolve => { resolvePendingBackfill = resolve; });
      return Response.json(backfillCalls > 3
        ? backfillPage(5400, 1, [event(1)], false, null)
        : backfill(5400));
    });
    const client = new MatchRealtimeClient({ matchId, authProvider: { getAccessToken: async () => 'token' },
      getState: () => state, onStateChange: setState, onStatusChange: status, fetcher,
      connectionFactory: () => connection });

    await client.start();
    connection.handlers.get('MatchUpdated')?.(cursor(1));
    await vi.waitFor(() => expect(backfillCalls).toBe(3));
    connection.beginReconnect();
    connection.reconnected?.();
    await vi.waitFor(() => expect(resolveReconnectNegotiation).toBeDefined());
    resolvePendingBackfill?.(Response.json(backfillPage(5400, 1, [event(1)], false, null)));
    await vi.waitFor(() => expect(state.throughSequence).toBe(1));

    expect(status).toHaveBeenLastCalledWith('reconnecting');
    resolveReconnectNegotiation?.(Response.json({ transport: 'aspnetcore-signalr', url: '/hubs/matches',
      accessToken: null, cursor: cursor(1), revisionChanged: false }));
    await vi.waitFor(() => expect(connection.calls.filter(call => call[0] === 'SubscribeToMatch')).toHaveLength(2));
    await vi.waitFor(() => expect(status).toHaveBeenLastCalledWith('connected'));
    await client.stop();
  });

  it('ignores an older reconnect backfill after a newer reconnect has begun', async () => {
    let state = initialState();
    const setState = vi.fn((next: RealtimeTimelineState) => { state = next; });
    const connection = new FakeConnection();
    const status = vi.fn();
    let negotiateCalls = 0;
    let backfillCalls = 0;
    let resolveFirstReconnectBackfill: ((response: Response) => void) | undefined;
    let resolveSecondNegotiation: ((response: Response) => void) | undefined;
    const fetcher = vi.fn<typeof fetch>(async input => {
      if (String(input).endsWith('/realtime/negotiate')) {
        negotiateCalls++;
        if (negotiateCalls === 3) return new Promise(resolve => { resolveSecondNegotiation = resolve; });
        return Response.json({ transport: 'aspnetcore-signalr', url: '/hubs/matches', accessToken: null,
          cursor: cursor(), revisionChanged: false });
      }
      backfillCalls++;
      if (backfillCalls === 3) return new Promise(resolve => { resolveFirstReconnectBackfill = resolve; });
      return Response.json(backfillCalls > 3
        ? backfillPage(5400, 1, [event(1)], false, null)
        : backfill(5400));
    });
    const client = new MatchRealtimeClient({ matchId, authProvider: { getAccessToken: async () => 'token' },
      getState: () => state, onStateChange: setState, onStatusChange: status, fetcher,
      connectionFactory: () => connection });

    await client.start();
    connection.beginReconnect();
    connection.reconnected?.();
    await vi.waitFor(() => expect(backfillCalls).toBe(3));

    connection.beginReconnect();
    connection.holdNextInvoke();
    connection.reconnected?.();
    await vi.waitFor(() => expect(resolveSecondNegotiation).toBeDefined());
    resolveSecondNegotiation?.(Response.json({ transport: 'aspnetcore-signalr', url: '/hubs/matches',
      accessToken: null, cursor: cursor(), revisionChanged: false }));
    await vi.waitFor(() => expect(connection.calls.filter(call => call[0] === 'SubscribeToMatch')).toHaveLength(3));
    resolveFirstReconnectBackfill?.(Response.json(backfillPage(5400, 1, [event(1)], false, null)));
    await vi.waitFor(() => expect(state.throughSequence).toBe(1));

    expect(status).toHaveBeenLastCalledWith('reconnecting');
    connection.releaseInvoke();
    await vi.waitFor(() => expect(status).toHaveBeenLastCalledWith('connected'));
    await client.stop();
  });

  it('re-negotiates, rejoins the exact match and preserves a historical timestamp after reconnect', async () => {
    let state = selectHistoricalTime(initialState(), 45);
    const setState = vi.fn((next: RealtimeTimelineState) => { state = next; });
    const connection = new FakeConnection();
    const fetcher = vi.fn<typeof fetch>(async input => {
      if (String(input).endsWith('/realtime/negotiate'))
        return Response.json({ transport: 'aspnetcore-signalr', url: '/hubs/matches', accessToken: null,
          cursor: cursor(3), revisionChanged: true });
      const at = Number(new URL(String(input), 'https://app.example').searchParams.get('atSeconds'));
      return Response.json(backfill(at));
    });
    const client = new MatchRealtimeClient({ matchId, authProvider: { getAccessToken: async () => 'token' },
      getState: () => state, onStateChange: setState, onStatusChange: vi.fn(), fetcher,
      connectionFactory: () => connection });

    await client.start();
    connection.reconnected?.();
    await vi.waitFor(() => expect(connection.calls.filter(call => call[0] === 'SubscribeToMatch')).toHaveLength(2));
    expect(connection.calls.filter(call => call[0] === 'SubscribeToMatch')).toHaveLength(2);
    expect(connection.calls.every(call => call[1] === matchId)).toBe(true);
    expect(state.mode).toBe('HISTORICAL');
    expect(state.selectedTimeSeconds).toBe(45);
    expect(state.hasNewMatchUpdate).toBe(true);
    expect(fetcher.mock.calls.some(([input]) => String(input).includes('atSeconds=45'))).toBe(true);
    await client.stop();
  });

  it('follows canonical backfill cursors across multiple pages until caught up', async () => {
    let state = initialState();
    const setState = vi.fn((next: RealtimeTimelineState) => { state = next; });
    const connection = new FakeConnection();
    const pageOne = Array.from({ length: 100 }, (_, index) => event(index + 1));
    const pageTwo = Array.from({ length: 50 }, (_, index) => event(index + 101));
    let pageCalls = 0;
    const fetcher = vi.fn<typeof fetch>(async input => {
      if (String(input).endsWith('/realtime/negotiate'))
        return Response.json({ transport: 'aspnetcore-signalr', url: '/hubs/matches', accessToken: null,
          cursor: cursor(150), revisionChanged: false });
      pageCalls++;
      if (pageCalls === 1) return Response.json(backfill(5400));
      if (pageCalls === 2) return Response.json(backfillPage(5400, 150, pageOne, true, 'page-2'));
      return Response.json(backfillPage(5400, 150, pageTwo, false, null));
    });
    const client = new MatchRealtimeClient({ matchId, authProvider: { getAccessToken: async () => 'token' },
      getState: () => state, onStateChange: setState, onStatusChange: vi.fn(), fetcher,
      connectionFactory: () => connection });

    await client.start();

    expect(pageCalls).toBe(3);
    expect(state.throughSequence).toBe(150);
    expect(state.events).toHaveLength(150);
    expect(state.needsBackfill).toBe(false);
    expect(fetcher.mock.calls[2]?.[0]).toContain('afterSequence=0');
    expect(fetcher.mock.calls[3]?.[0]).toContain('afterSequence=100');
    await client.stop();
  });

  it('stops a hasMore chain when the server makes no cursor progress', async () => {
    let state = initialState();
    const setState = vi.fn((next: RealtimeTimelineState) => { state = next; });
    const connection = new FakeConnection();
    const status = vi.fn();
    let pageCalls = 0;
    const fetcher = vi.fn<typeof fetch>(async input => {
      if (String(input).endsWith('/realtime/negotiate'))
        return Response.json({ transport: 'aspnetcore-signalr', url: '/hubs/matches', accessToken: null,
          cursor: cursor(2), revisionChanged: false });
      pageCalls++;
      return pageCalls === 1 ? Response.json(backfill(5400)) :
        Response.json(backfillPage(5400, 2, [], true, null));
    });
    const client = new MatchRealtimeClient({ matchId, authProvider: { getAccessToken: async () => 'token' },
      getState: () => state, onStateChange: setState, onStatusChange: status, fetcher,
      connectionFactory: () => connection });

    await client.start();

    expect(pageCalls).toBe(2);
    expect(state.needsBackfill).toBe(true);
    expect(state.reconnecting).toBe(true);
    expect(status).toHaveBeenLastCalledWith('disconnected');
    await client.stop();
  });

  it('refetches once when a live cursor arrives during a no-more page and catches up', async () => {
    let state = initialState();
    const setState = vi.fn((next: RealtimeTimelineState) => { state = next; });
    const connection = new FakeConnection();
    let resolveStalePage: ((response: Response) => void) | undefined;
    let pageCalls = 0;
    const fetcher = vi.fn<typeof fetch>(async input => {
      if (String(input).endsWith('/realtime/negotiate'))
        return Response.json({ transport: 'aspnetcore-signalr', url: '/hubs/matches', accessToken: null,
          cursor: cursor(), revisionChanged: false });
      pageCalls++;
      if (pageCalls < 3) return Response.json(backfill(5400));
      if (pageCalls === 3) return new Promise(resolve => { resolveStalePage = resolve; });
      return Response.json(backfillPage(5400, 1, [event(1)], false, null));
    });
    const client = new MatchRealtimeClient({ matchId, authProvider: { getAccessToken: async () => 'token' },
      getState: () => state, onStateChange: setState, onStatusChange: vi.fn(), fetcher,
      connectionFactory: () => connection });

    await client.start();
    connection.handlers.get('MatchUpdated')?.(cursor(1));
    await vi.waitFor(() => expect(pageCalls).toBe(3));
    resolveStalePage?.(Response.json(backfill(5400)));
    await vi.waitFor(() => expect(pageCalls).toBe(4));

    expect(String(fetcher.mock.calls[4]?.[0])).toContain('afterSequence=0');
    expect(state.throughSequence).toBe(1);
    expect(state.events).toEqual([event(1)]);
    expect(state.needsBackfill).toBe(false);
    await client.stop();
  });

  it('stops after the single observed-cursor retry returns the same stale cursor', async () => {
    let state = initialState();
    const setState = vi.fn((next: RealtimeTimelineState) => { state = next; });
    const connection = new FakeConnection();
    let resolveStalePage: ((response: Response) => void) | undefined;
    let pageCalls = 0;
    const status = vi.fn();
    const fetcher = vi.fn<typeof fetch>(async input => {
      if (String(input).endsWith('/realtime/negotiate'))
        return Response.json({ transport: 'aspnetcore-signalr', url: '/hubs/matches', accessToken: null,
          cursor: cursor(), revisionChanged: false });
      pageCalls++;
      if (pageCalls < 3) return Response.json(backfill(5400));
      if (pageCalls === 3) return new Promise(resolve => { resolveStalePage = resolve; });
      return Response.json(backfill(5400));
    });
    const client = new MatchRealtimeClient({ matchId, authProvider: { getAccessToken: async () => 'token' },
      getState: () => state, onStateChange: setState, onStatusChange: status, fetcher,
      connectionFactory: () => connection });

    await client.start();
    connection.handlers.get('MatchUpdated')?.(cursor(1));
    await vi.waitFor(() => expect(pageCalls).toBe(3));
    resolveStalePage?.(Response.json(backfill(5400)));
    await vi.waitFor(() => expect(pageCalls).toBe(4));

    await vi.waitFor(() => expect(status).toHaveBeenLastCalledWith('disconnected'));
    expect(pageCalls).toBe(4);
    expect(state.throughSequence).toBe(0);
    expect(state.needsBackfill).toBe(true);
    expect(state.reconnecting).toBe(true);
    await client.stop();
  });

  it('bounds a progressing catch-up chain and leaves the timeline marked behind', async () => {
    let state = initialState();
    const setState = vi.fn((next: RealtimeTimelineState) => { state = next; });
    const connection = new FakeConnection();
    let pageCalls = 0;
    const fetcher = vi.fn<typeof fetch>(async input => {
      if (String(input).endsWith('/realtime/negotiate'))
        return Response.json({ transport: 'aspnetcore-signalr', url: '/hubs/matches', accessToken: null,
          cursor: cursor(200), revisionChanged: false });
      pageCalls++;
      if (pageCalls === 1) return Response.json(backfill(5400));
      const sequence = pageCalls - 1;
      return Response.json(backfillPage(5400, 200, [event(sequence)], true, `page-${sequence}`));
    });
    const status = vi.fn();
    const client = new MatchRealtimeClient({ matchId, authProvider: { getAccessToken: async () => 'token' },
      getState: () => state, onStateChange: setState, onStatusChange: status, fetcher,
      connectionFactory: () => connection });

    await client.start();

    expect(pageCalls).toBe(101);
    expect(state.throughSequence).toBe(100);
    expect(state.needsBackfill).toBe(true);
    expect(state.reconnecting).toBe(true);
    expect(status).toHaveBeenLastCalledWith('disconnected');
    await client.stop();
  });

  it('does not skip a sequence gap when a page claims more events', async () => {
    let state = initialState();
    const setState = vi.fn((next: RealtimeTimelineState) => { state = next; });
    const connection = new FakeConnection();
    const fetcher = vi.fn<typeof fetch>(async input => {
      if (String(input).endsWith('/realtime/negotiate'))
        return Response.json({ transport: 'aspnetcore-signalr', url: '/hubs/matches', accessToken: null,
          cursor: cursor(2), revisionChanged: false });
      if (fetcher.mock.calls.length === 1) return Response.json(backfill(5400));
      return Response.json(backfillPage(5400, 2, [event(2)], true, 'gap-cursor'));
    });
    const client = new MatchRealtimeClient({ matchId, authProvider: { getAccessToken: async () => 'token' },
      getState: () => state, onStateChange: setState, onStatusChange: vi.fn(), fetcher,
      connectionFactory: () => connection });

    await client.start();

    expect(fetcher).toHaveBeenCalledTimes(3);
    expect(state.throughSequence).toBe(0);
    expect(state.events).toEqual([]);
    expect(state.needsBackfill).toBe(true);
    expect(state.reconnecting).toBe(true);
    await client.stop();
  });

  it('discards a pending page when selected context changes during catch-up', async () => {
    let state = initialState();
    const setState = vi.fn((next: RealtimeTimelineState) => { state = next; });
    const connection = new FakeConnection();
    let pageCalls = 0;
    const fetcher = vi.fn<typeof fetch>(async input => {
      if (String(input).endsWith('/realtime/negotiate'))
        return Response.json({ transport: 'aspnetcore-signalr', url: '/hubs/matches', accessToken: null,
          cursor: cursor(1), revisionChanged: false });
      pageCalls++;
      if (pageCalls === 1) return Response.json(backfill(5400));
      state = selectHistoricalTime(state, 45);
      return Response.json(backfillPage(5400, 1, [event(1)], false, null));
    });
    const client = new MatchRealtimeClient({ matchId, authProvider: { getAccessToken: async () => 'token' },
      getState: () => state, onStateChange: setState, onStatusChange: vi.fn(), fetcher,
      connectionFactory: () => connection });

    await client.start();

    expect(pageCalls).toBe(2);
    expect(state.mode).toBe('HISTORICAL');
    expect(state.selectedTimeSeconds).toBe(45);
    expect(state.events).toEqual([]);
    await client.stop();
  });

  it('aborts a continuation request chain when the client stops', async () => {
    let state = initialState();
    const setState = vi.fn((next: RealtimeTimelineState) => { state = next; });
    const connection = new FakeConnection();
    let resolveSecondPage: ((response: Response) => void) | undefined;
    let pageCalls = 0;
    const fetcher = vi.fn<typeof fetch>(async input => {
      if (String(input).endsWith('/realtime/negotiate'))
        return Response.json({ transport: 'aspnetcore-signalr', url: '/hubs/matches', accessToken: null,
          cursor: cursor(2), revisionChanged: false });
      pageCalls++;
      if (pageCalls === 1) return Response.json(backfill(5400));
      if (pageCalls === 2) return Response.json(backfillPage(5400, 2, [event(1)], true, 'page-2'));
      return new Promise(resolve => { resolveSecondPage = resolve; });
    });
    const client = new MatchRealtimeClient({ matchId, authProvider: { getAccessToken: async () => 'token' },
      getState: () => state, onStateChange: setState, onStatusChange: vi.fn(), fetcher,
      connectionFactory: () => connection });

    const startup = client.start();
    await vi.waitFor(() => expect(fetcher).toHaveBeenCalledTimes(4));
    await client.stop();
    resolveSecondPage?.(Response.json(backfillPage(5400, 2, [event(2)], false, null)));
    await startup;

    expect(pageCalls).toBe(3);
    expect(state.throughSequence).toBe(1);
  });
});

it('renders an accessible fail-closed host state and keeps historical time in the update notice', () => {
  const state = { ...selectHistoricalTime(initialState(), 45), hasNewMatchUpdate: true };
  const markup = renderToStaticMarkup(createElement(RealtimeMatchUpdates, { state, onStateChange: vi.fn() }));
  expect(markup).toContain('Live updates unavailable. Sign in and retry.');
  expect(markup).toContain('role="status" aria-live="polite"');
  expect(markup).toContain('Your selected time stays at 00:45.');
  expect(markup).toContain('Return to live');
});

it('does not restart SignalR when host state rerenders with a fresh token callback', async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  const start = vi.spyOn(MatchRealtimeClient.prototype, 'start').mockResolvedValue();
  const container = document.createElement('div');
  const root = createRoot(container);
  let state = initialState();
  const render = () => createElement(RealtimeAuthProvider, {
    getAccessToken: async () => 'host-token',
    children: createElement(RealtimeMatchUpdates, { state, onStateChange: vi.fn() }),
  });

  try {
    await act(async () => root.render(render()));
    expect(start).toHaveBeenCalledTimes(1);
    state = { ...state, hasNewMatchUpdate: true };
    await act(async () => root.render(render()));
    expect(start).toHaveBeenCalledTimes(1);
  } finally {
    await act(async () => root.unmount());
    start.mockRestore();
    vi.unstubAllGlobals();
  }
});

it('shows disconnected retry state when selected-time refresh fails', async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  const start = vi.spyOn(MatchRealtimeClient.prototype, 'start').mockResolvedValue();
  const refresh = vi.spyOn(MatchRealtimeClient.prototype, 'refresh').mockRejectedValue(new Error('offline'));
  const container = document.createElement('div');
  const root = createRoot(container);
  const onStateChange = vi.fn();
  let state = initialState();
  const render = () => createElement(RealtimeAuthProvider, {
    getAccessToken: async () => 'host-token',
    children: createElement(RealtimeMatchUpdates, { state, onStateChange }),
  });

  try {
    await act(async () => root.render(render()));
    state = selectHistoricalTime(state, 45);
    await act(async () => root.render(render()));
    await vi.waitFor(() => expect(container.textContent).toContain('Match updates disconnected'));
    expect(container.textContent).toContain('Retry live connection');
    expect(onStateChange).toHaveBeenCalledWith(expect.objectContaining({ needsBackfill: true, reconnecting: true }));
    expect(refresh).toHaveBeenCalledTimes(1);
  } finally {
    await act(async () => root.unmount());
    start.mockRestore();
    refresh.mockRestore();
    vi.unstubAllGlobals();
  }
});
