import { describe, expect, it, vi } from 'vitest';
import { act, createElement } from 'react';
import { createRoot } from 'react-dom/client';
import { renderToStaticMarkup } from 'react-dom/server';
import type { CanonicalTimelineBackfill, RealtimeCursorV1 } from './api';
import { selectHistoricalTime, type RealtimeTimelineState } from './realtime';
import { MatchRealtimeClient, type RealtimeConnection } from './realtimeClient';
import { RealtimeAuthProvider, RealtimeMatchUpdates } from './RealtimeMatchUpdates';

const matchId = '11111111-1111-4111-8111-111111111111';
const cursor = (lastSequence = 0): RealtimeCursorV1 => ({
  kind: 'matchUpdated', matchId, canonicalRevision: 2, lastSequence,
  serverUtc: '2026-10-11T12:00:00Z', schemaVersion: 1,
});

const initialState = (): RealtimeTimelineState => ({
  matchId, mode: 'LIVE', selectedTimeSeconds: 60, canonicalRevision: 2, throughSequence: 0,
  snapshot: { matchId, canonicalRevision: 2, selectedTimeSeconds: 5400, asOfSeconds: 60,
    throughSequence: 0, algorithmVersion: 'rules-v1', sourceSha256: 'a'.repeat(64),
    stateSha256: 'b'.repeat(64), provenance: 'synthetic', period: 1, phase: 'firsthalf',
    homeScore: 0, awayScore: 0, possessionTeamId: null, homeActivePlayers: 11,
    awayActivePlayers: 11, players: [] },
  events: [], nextCursor: null, hasMore: false, needsBackfill: false,
  hasNewMatchUpdate: false, reconnecting: false,
});

function backfill(atSeconds: number): CanonicalTimelineBackfill {
  const snapshot = { ...initialState().snapshot, selectedTimeSeconds: atSeconds, asOfSeconds: atSeconds };
  return { snapshot, events: { matchId, canonicalRevision: 2, selectedTimeSeconds: atSeconds,
    throughSequence: 0, items: [], nextCursor: null, hasMore: false },
  revisionChanged: false, replayRequired: false, requestedAfterSequence: -1 };
}

class FakeConnection implements RealtimeConnection {
  handlers = new Map<string, (...args: unknown[]) => void>();
  reconnected: (() => void) | undefined;
  calls: Array<[string, ...unknown[]]> = [];
  on(method: string, callback: (...args: unknown[]) => void) { this.handlers.set(method, callback); }
  onreconnecting() { /* not needed in this test */ }
  onreconnected(callback: () => void) { this.reconnected = callback; }
  onclose() { /* not needed in this test */ }
  async start() { /* connected */ }
  async stop() { /* stopped */ }
  async invoke(method: string, ...args: unknown[]) { this.calls.push([method, ...args]); }
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
