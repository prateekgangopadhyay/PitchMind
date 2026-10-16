import { act, createElement } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, expect, it, vi } from 'vitest';
import { App } from './App';

const matchId = '11111111-1111-1111-1111-111111111111';
const snapshot = {
  matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3309, asOfSeconds: 3309, throughSequence: 19,
  algorithmVersion: 'projection-v1', sourceSha256: 'a'.repeat(64), stateSha256: 'b'.repeat(64),
  provenance: 'synthetic-approved', period: 2, phase: 'secondhalf', homeScore: 1, awayScore: 0,
  possessionTeamId: null, homeActivePlayers: 11, awayActivePlayers: 11, players: []
};
const insight = (id: string, sequence: number, summary: string) => ({
  insightId: id, matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3309,
  throughSequence: sequence, detectorVersion: 'test-v1', truthClass: 'interpretation', summary, evidence: []
});
const basePage = {matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3309, throughSequence: 19};

afterEach(() => {
  vi.unstubAllGlobals();
  window.history.replaceState(null, '', '/');
});

it('follows selected-time insight cursors and surfaces the latest persisted insight', async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', `/matches/${matchId}?matchId=${matchId}&atSeconds=3309&revision=4`);
  const insightRequests: URL[] = [];
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    const parsed = new URL(url, 'http://localhost');
    if (parsed.pathname.endsWith('/key-moments')) return {ok: false, status: 503, json: async () => ({status: 503, code: 'DEPENDENCY_UNAVAILABLE'})};
    if (parsed.pathname.endsWith('/summary')) return {ok: true, json: async () => snapshot};
    if (parsed.pathname.endsWith('/events')) return {ok: true, json: async () => ({...basePage, items: [], nextCursor: null, hasMore: false})};
    if (parsed.pathname.endsWith('/frames')) return {ok: true, json: async () => ({...basePage, items: [], nextCursor: null, hasMore: false})};
    if (parsed.pathname.endsWith('/insights')) {
      insightRequests.push(parsed);
      return {ok: true, json: async () => parsed.searchParams.get('cursor') === 'next'
        ? {...basePage, items: [insight('latest', 19, 'Latest saved explanation')], nextCursor: null, hasMore: false}
        : {...basePage, items: [insight('earlier', 12, 'Earlier saved explanation')], nextCursor: 'next', hasMore: true}};
    }
    throw new Error(`Unexpected request ${parsed.pathname}`);
  }));
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    const explanation = container.querySelector('.selected-time-explanation');
    expect(explanation?.textContent).toContain('Latest saved explanation');
    expect(explanation?.textContent).not.toContain('Earlier saved explanation');
    expect(insightRequests).toHaveLength(2);
    for (const request of insightRequests) {
      expect(request.searchParams.get('atSeconds')).toBe('3309');
      expect(request.searchParams.get('revision')).toBe('4');
      expect(request.searchParams.get('limit')).toBe('100');
    }
    expect(insightRequests[1].searchParams.get('cursor')).toBe('next');
  } finally {
    await act(async () => root.unmount());
  }
});

it('rejects selected-time insight cursor loops before rendering stale insight text', async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', `/matches/${matchId}?matchId=${matchId}&atSeconds=3309&revision=4`);
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    const parsed = new URL(url, 'http://localhost');
    if (parsed.pathname.endsWith('/key-moments')) return {ok: false, status: 503, json: async () => ({status: 503, code: 'DEPENDENCY_UNAVAILABLE'})};
    if (parsed.pathname.endsWith('/summary')) return {ok: true, json: async () => snapshot};
    if (parsed.pathname.endsWith('/events')) return {ok: true, json: async () => ({...basePage, items: [], nextCursor: null, hasMore: false})};
    if (parsed.pathname.endsWith('/frames')) return {ok: true, json: async () => ({...basePage, items: [], nextCursor: null, hasMore: false})};
    if (parsed.pathname.endsWith('/insights')) return {ok: true, json: async () => ({...basePage,
      items: [insight(`insight-${new URLSearchParams(parsed.search).get('cursor') ?? 'first'}`, 12, 'Must not be rendered')],
      nextCursor: 'repeat', hasMore: true})};
    throw new Error(`Unexpected request ${parsed.pathname}`);
  }));
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    expect(container.textContent).toContain('Canonical insight pagination did not advance.');
    expect(container.textContent).not.toContain('Must not be rendered');
  } finally {
    await act(async () => root.unmount());
  }
});
