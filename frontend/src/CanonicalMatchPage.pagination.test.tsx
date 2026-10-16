import { act, createElement } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { CanonicalMatchPage } from './CanonicalMatchPage';

const matchId = '11111111-1111-1111-1111-111111111111';
const sourceSha256 = 'a'.repeat(64);
const snapshot = {
  matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3308, asOfSeconds: 3308, throughSequence: 100,
  algorithmVersion: 'algorithm-v1', sourceSha256, stateSha256: 'b'.repeat(64), provenance: 'synthetic-approved',
  period: 2, phase: 'secondhalf', homeScore: 0, awayScore: 0, possessionTeamId: null,
  homeActivePlayers: 11, awayActivePlayers: 11, players: []
};

function event(sequence: number) {
  return { eventId: `event-${sequence}`, matchId, sequence, type: 'pass', clockSeconds: 3308, period: 2,
    occurredAt: '2026-01-01T00:55:08Z' };
}

function frame(sequence: number, frameId = `frame-${sequence}`, coordinateUnit = 'metres') {
  return {
    frameId, sequence, canonicalRevision: 4, projectionRevision: 4, period: 2, clockSeconds: 3308, coordinateVersion: 'coordinates-v1',
    sourceSha256, quality: {},
    frame: {
      schemaVersion: 1, matchId, frameId, sequence, canonicalRevision: 4,
      matchTime: { period: 2, elapsedSeconds: 3308, sourceClockValue: 3308, sourceClockUnit: 'seconds', sourceClockBasis: 'elapsed' },
      coordinateSystem: { lengthMetres: 105, widthMetres: 68, unit: coordinateUnit, axisOrigin: 'bottom_left', teamDirections: [{ teamId: 'home', direction: 'right' }] },
      ball: { x: 50, y: 30 }, players: [{ playerId: 'player-1', teamId: 'home', position: { x: 45, y: 30 } }],
      source: { providerId: 'synthetic', sourceMatchId: matchId, sourceRecordId: frameId, sourceSequence: sequence,
        sourceSchemaVersion: '1', sourceSha256, provenanceStatus: 'synthetic-approved', rightsReviewStatus: 'approved' }
    }
  };
}

function page(items: unknown[], nextCursor: string | null, hasMore: boolean, selectedTimeSeconds = 3308) {
  return { matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds, throughSequence: 100, items, nextCursor, hasMore };
}

function response(body: unknown) {
  return { ok: true, headers: new Headers(), json: async () => body } as Response;
}

async function settle() {
  await act(async () => { await new Promise(resolve => setTimeout(resolve, 0)); });
}

function renderReplay(hash: string) {
  window.history.replaceState(null, '', `/replay/${matchId}?matchId=${matchId}&atSeconds=3308&revision=4${hash}`);
  const container = document.createElement('div');
  document.body.appendChild(container);
  const root = createRoot(container);
  act(() => root.render(createElement(CanonicalMatchPage, { matchId, selectedSeconds: 3308, requestedRevision: 4, replay: true, initialMode: 'HISTORICAL' })));
  return { container, root };
}

afterEach(() => {
  vi.unstubAllGlobals();
  document.body.replaceChildren();
});

describe('canonical replay deep-link pagination', () => {
  it('follows event cursors to focus an event outside the first page with the same match, time and revision', async () => {
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input), 'http://localhost');
      if (url.pathname.endsWith('/summary')) return response(snapshot);
      if (url.pathname.endsWith('/insights')) return response(page([], null, false));
      if (url.pathname.endsWith('/key-moments')) return response({});
      if (url.pathname.endsWith('/frames')) return response(page([frame(12)], null, false));
      if (url.pathname.endsWith('/events') && !url.searchParams.has('cursor')) return response(page([event(1)], 'event-cursor-2', true));
      if (url.pathname.endsWith('/events')) return response(page([event(80)], null, false));
      throw new Error(`Unexpected request ${url}`);
    });
    vi.stubGlobal('fetch', fetchMock);
    const { container, root } = renderReplay('#canonical-event-80');
    try {
      await settle();
      expect(container.querySelector('#canonical-event-80')).not.toBeNull();
      expect(document.activeElement?.id).toBe('canonical-event-80');
      const cursorRequest = fetchMock.mock.calls.map(([input]) => new URL(String(input), 'http://localhost'))
        .find(url => url.pathname.endsWith('/events') && url.searchParams.has('cursor'));
      expect(cursorRequest?.searchParams.get('cursor')).toBe('event-cursor-2');
      expect(cursorRequest?.searchParams.get('atSeconds')).toBe('3308');
      expect(cursorRequest?.searchParams.get('revision')).toBe('4');
      expect(cursorRequest?.searchParams.get('limit')).toBe('50');
    } finally {
      act(() => root.unmount());
    }
  });

  // Exact-frame lookup and frame anchors are already covered by the current-main evidence tests.

  it('reports a partial search instead of claiming a source is absent when a continuation request fails', async () => {
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input), 'http://localhost');
      if (url.pathname.endsWith('/summary')) return response(snapshot);
      if (url.pathname.endsWith('/insights')) return response(page([], null, false));
      if (url.pathname.endsWith('/key-moments')) return response({});
      if (url.pathname.endsWith('/frames')) return response(page([], null, false));
      if (url.pathname.endsWith('/events') && !url.searchParams.has('cursor')) return response(page([event(1)], 'event-cursor-2', true));
      return { ok: false, status: 503, headers: new Headers(), json: async () => ({ code: 'TEMPORARY_FAILURE' }) } as Response;
    });
    vi.stubGlobal('fetch', fetchMock);
    const { container, root } = renderReplay('#canonical-event-80');
    try {
      await settle();
      expect(container.textContent).toContain('Some event pages could not be loaded. The linked event was not verified.');
      expect(container.textContent).not.toContain('The linked source is not present');
      expect(container.querySelector('#canonical-event-80')).toBeNull();
    } finally {
      act(() => root.unmount());
    }
  });

  it('aborts pending linked-source pages when selected time changes and when the replay unmounts', async () => {
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    const pending: Array<{ signal?: AbortSignal; resolve: (value: Response) => void }> = [];
    const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(String(input), 'http://localhost');
      if (url.pathname.endsWith('/summary')) return Promise.resolve(response({ ...snapshot, selectedTimeSeconds: Number(url.searchParams.get('atSeconds')) }));
      if (url.pathname.endsWith('/insights')) return Promise.resolve(response(page([], null, false, Number(url.searchParams.get('atSeconds') ?? 3308))));
      if (url.pathname.endsWith('/key-moments')) return Promise.resolve(response({}));
      if (url.pathname.endsWith('/frames')) return Promise.resolve(response(page([], null, false, Number(url.searchParams.get('atSeconds') ?? 3308))));
      if (url.pathname.endsWith('/events') && !url.searchParams.has('cursor')) return Promise.resolve(response(page([event(1)], `cursor-${url.searchParams.get('atSeconds')}`, true, Number(url.searchParams.get('atSeconds')))));
      return new Promise<Response>(resolve => pending.push({ signal: init?.signal ?? undefined, resolve }));
    });
    vi.stubGlobal('fetch', fetchMock);
    const { container, root } = renderReplay('#canonical-event-80');
    try {
      await settle();
      expect(pending).toHaveLength(1);
      expect(pending[0].signal?.aborted).toBe(false);
      await act(async () => root.render(createElement(CanonicalMatchPage, {
        matchId, selectedSeconds: 3309, requestedRevision: 4, replay: true, initialMode: 'HISTORICAL'
      })));
      expect(pending[0].signal?.aborted).toBe(true);
      pending[0].resolve(response(page([event(80)], null, false)));
      await settle();
      expect(pending).toHaveLength(2);
      expect(pending[1].signal?.aborted).toBe(false);
    } finally {
      act(() => root.unmount());
      expect(pending.at(-1)?.signal?.aborted).toBe(true);
      pending.at(-1)?.resolve(response(page([event(80)], null, false)));
      await settle();
      expect(container.querySelector('#canonical-event-80')).toBeNull();
    }
  });
});
