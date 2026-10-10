import { describe, expect, it } from 'vitest';
import { App } from './App';
import { clock } from './api';
import { renderToStaticMarkup } from 'react-dom/server';
import { createElement } from 'react';
import { contextHref, resolveRoute, selectedTimeFromSearch } from './routes';
describe('Match experience', () => {
  it('renders the route shell and honest loading state without inventing match facts', () => {
    const markup = renderToStaticMarkup(createElement(App));
    expect(markup).toContain('Canonical match data');
    expect(markup).toContain('Skip to main content');
    expect(markup).toContain('id="main-content"');
    expect(markup).toContain('aria-label="Main navigation"');
    expect(markup).toContain('Choose a match');
    expect(markup).toContain('Loading canonical match data');
    expect(markup).not.toContain('Arsenal');
  });
  it('formats selected time without losing seconds at period boundaries', () => {
    expect(clock(0)).toBe('00:00');
    expect(clock(2700)).toBe('45:00');
    expect(clock(3309)).toBe('55:09');
    expect(clock(5400)).toBe('90:00');
  });
});

describe('Information architecture routes', () => {
  it('resolves the seven product screens and replay deep link', () => {
    expect(resolveRoute('/welcome').kind).toBe('welcome');
    expect(resolveRoute('/home').kind).toBe('home');
    expect(resolveRoute('/matches/match-1').kind).toBe('matches');
    expect(resolveRoute('/teams/team-1').kind).toBe('teams');
    expect(resolveRoute('/players/player-1').kind).toBe('players');
    expect(resolveRoute('/insights/insight-1').kind).toBe('insights');
    expect(resolveRoute('/analyst').kind).toBe('analyst');
    expect(resolveRoute('/replay/match-1')).toEqual({ kind: 'matches', matchId: 'match-1', replay: true });
    expect(resolveRoute('/demo/matches/match-1').kind).toBe('demo-match');
    expect(resolveRoute('/not-a-page').kind).toBe('not-found');
    expect(resolveRoute('/matches/%E0%A4%A').kind).toBe('not-found');
  });

  it('keeps selected time bounded in route context links', () => {
    expect(contextHref('/matches/match-1', 'match-1', 3309)).toBe('/matches/match-1?matchId=match-1&atSeconds=3309');
    expect(contextHref('/home')).toBe('/home');
    expect(contextHref('/home', 'match-1', 0, 2)).toBe('/home?matchId=match-1&atSeconds=0&revision=2');
    expect(selectedTimeFromSearch('?atSeconds=5761')).toBe(0);
  });
});

import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { vi } from 'vitest';
it('loads API state and clears historical insight when replay moves before evidence', async () => {
  const fixture = {matchId: 'fixture', version: 'v1', seed: 42, durationSeconds: 5400, home: {id: 'h', name: 'Harbour FC', formation: '4-2-3-1', players: []}, away: {id: 'a', name: 'Ridge Athletic', formation: '4-2-3-1', players: []}};
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', '/demo/matches/fixture?matchId=fixture&atSeconds=0');
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    const seconds = Number(new URL(url, 'http://localhost').searchParams.get('atSeconds') ?? 0);
    const body = url.endsWith('/matches') ? [fixture] : url.includes('/state?') ? {clockSeconds: seconds, homeScore: seconds >= 3309 ? 1 : 0, awayScore: 0, status: 'Simulated replay', frame: {clockSeconds: seconds, players: [], ballX: 52.5, ballY: 34}} : url.includes('/insights?') && seconds >= 3309 ? [{id: 'press', title: 'Supported press', summary: 'Observed sequence', classification: 'derived'}] : [];
    return {ok: true, json: async () => body};
  }));
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => {root.render(createElement(App));});
    expect(container.textContent).toContain('Harbour FC');
    const slider = container.querySelector('input')!;
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(slider, '3309');
      slider.dispatchEvent(new Event('input', {bubbles: true}));
      slider.dispatchEvent(new Event('change', {bubbles: true}));
    });
    expect(container.textContent).toContain('Supported press');
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(slider, '0');
      slider.dispatchEvent(new Event('input', {bubbles: true}));
      slider.dispatchEvent(new Event('change', {bubbles: true}));
    });
    expect(container.textContent).not.toContain('Supported press');
  } finally {
    await act(async () => root.unmount());
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  }
});

it('loads a canonical match using one exact selected time and revision without demo fallback', async () => {
  const matchId = '11111111-1111-1111-1111-111111111111';
  const observedRequests: string[] = [];
  const snapshot = {
    matchId, canonicalRevision: 4, selectedTimeSeconds: 3309, asOfSeconds: 3309, throughSequence: 19,
    algorithmVersion: 'projection-v1', sourceSha256: 'a'.repeat(64), stateSha256: 'b'.repeat(64),
    provenance: 'synthetic-approved', period: 2, phase: 'secondhalf', homeScore: 1, awayScore: 0,
    possessionTeamId: null, homeActivePlayers: 11, awayActivePlayers: 11, players: []
  };
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', `/matches/${matchId}?matchId=${matchId}&atSeconds=3309&revision=4`);
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    observedRequests.push(url);
    const parsed = new URL(url, 'http://localhost');
    if (parsed.pathname.endsWith('/key-moments')) return {ok: false, status: 503, json: async () => ({status: 503, title: 'Unavailable', code: 'DEPENDENCY_UNAVAILABLE', correlationId: 'test'})};
    const body = parsed.pathname.endsWith('/summary') ? snapshot
      : parsed.pathname.endsWith('/events') ? {matchId, canonicalRevision: 4, selectedTimeSeconds: 3309, throughSequence: 19, items: [
        {eventId: 'event-1', matchId, sequence: 18, type: 'shot', clockSeconds: 3300, period: 2, occurredAt: '2026-01-01T00:55:00Z'},
        {eventId: 'event-2', matchId, sequence: 19, type: 'goal', clockSeconds: 3309, period: 2, occurredAt: '2026-01-01T00:55:09Z'}
      ], nextCursor: null, hasMore: false}
        : {matchId, canonicalRevision: 4, selectedTimeSeconds: 3309, throughSequence: 19, items: [], nextCursor: null, hasMore: false};
    return {ok: true, json: async () => body};
  }));
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    expect(container.textContent).toContain('Match overview');
    expect(container.textContent).toContain('Canonical revision 4');
    expect(container.textContent).toContain('shot');
    expect(container.textContent).toContain('Goal');
    expect(container.textContent).toContain('Score event');
    expect(container.textContent).toContain('No persisted canonical key moments are available');
    expect(container.textContent).toContain('No observed tracking frame is recorded at 55:09. Positions are not estimated.');
    expect(observedRequests).toHaveLength(5);
    for (const url of observedRequests) {
      const parsed = new URL(url, 'http://localhost');
      expect(parsed.pathname).toContain(`/canonical/matches/${matchId}/`);
      expect(parsed.searchParams.get('atSeconds')).toBe('3309');
      expect(parsed.searchParams.get('revision')).toBe('4');
    }
    expect(new URL(observedRequests.find(url => url.includes('/key-moments'))!, 'http://localhost').searchParams.has('limit')).toBe(false);
  } finally {
    await act(async () => root.unmount());
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  }
});

it('rejects events and insights that cross the selected-time sequence', async () => {
  const matchId = '11111111-1111-1111-1111-111111111111';
  const snapshot = {
    matchId, canonicalRevision: 4, selectedTimeSeconds: 3309, asOfSeconds: 3309, throughSequence: 19,
    algorithmVersion: 'projection-v1', sourceSha256: 'a'.repeat(64), stateSha256: 'b'.repeat(64),
    provenance: 'synthetic-approved', period: 2, phase: 'secondhalf', homeScore: 1, awayScore: 0,
    possessionTeamId: null, homeActivePlayers: 11, awayActivePlayers: 11, players: []
  };
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', `/matches/${matchId}?matchId=${matchId}&atSeconds=3309&revision=4`);
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    const parsed = new URL(url, 'http://localhost');
    if (parsed.pathname.endsWith('/key-moments')) return {ok: false, status: 503, json: async () => ({status: 503, title: 'Unavailable', code: 'DEPENDENCY_UNAVAILABLE', correlationId: 'test'})};
    const body = parsed.pathname.endsWith('/summary') ? snapshot
      : parsed.pathname.endsWith('/events') ? {matchId, canonicalRevision: 4, selectedTimeSeconds: 3309, throughSequence: 19, items: [{eventId: 'future-event', matchId, sequence: 20, type: 'goal', clockSeconds: 3310, period: 2, occurredAt: '2026-01-01T00:55:10Z'}], nextCursor: null, hasMore: false}
        : {matchId, canonicalRevision: 4, selectedTimeSeconds: 3309, throughSequence: 19, items: [{insightId: 'future-insight', matchId, canonicalRevision: 4, selectedTimeSeconds: 3309, throughSequence: 20, detectorVersion: 'test-v1', truthClass: 'interpretation', summary: 'A future insight', evidence: []}], nextCursor: null, hasMore: false};
    return {ok: true, json: async () => body};
  }));
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    expect(container.textContent).toContain('outside the selected match, time, revision, sequence or event cutoff');
    expect(container.textContent).not.toContain('future-event');
    expect(container.textContent).not.toContain('A future insight');
  } finally {
    await act(async () => root.unmount());
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  }
});

it('renders persisted home cards from canonical fields and keeps the match id in navigation', async () => {
  const firstId = '11111111-1111-1111-1111-111111111111';
  const secondId = '22222222-2222-2222-2222-222222222222';
  const card = (matchId: string, phase: string, homeScore: number, awayScore: number) => ({
    matchId, canonicalRevision: 3, asOfSeconds: 2700, throughSequence: 12, phase, homeScore, awayScore,
    provenance: 'synthetic-approved', algorithmVersion: 'projection-v1', sourceSha256: 'c'.repeat(64)
  });
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', '/home');
  vi.stubGlobal('fetch', vi.fn(async () => ({ ok: true, json: async () => ({ items: [card(firstId, 'secondhalf', 1, 0), card(secondId, 'fulltime', 1, 1)], nextMatchId: null, hasMore: false }) })));
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    expect(container.textContent).toContain('Synthetic match');
    expect(container.textContent).toContain('Home side 1–0 Away side');
    expect(container.textContent).toContain(firstId);
    expect(container.textContent).toContain('Second half');
    const firstLink = [...container.querySelectorAll<HTMLAnchorElement>('a')].find(link => link.href.includes(firstId));
    expect(firstLink?.getAttribute('href')).toBe(`/matches/${firstId}?matchId=${firstId}&atSeconds=2700&revision=3`);
    const phaseFilter = container.querySelector<HTMLSelectElement>('[aria-label="Filter matches by phase"]')!;
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value')!.set!.call(phaseFilter, 'secondhalf');
      phaseFilter.dispatchEvent(new Event('change', { bubbles: true }));
    });
    expect(container.textContent).toContain(firstId);
    expect(container.textContent).not.toContain(secondId);
    expect(window.location.search).toBe('?phase=secondhalf');
    expect(container.textContent).not.toContain('Arsenal');
  } finally {
    await act(async () => root.unmount());
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  }
});

it('shows a clear canonical-unavailable state and retries without demo substitution', async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', '/home');
  const fetchMock = vi.fn()
    .mockRejectedValueOnce(new Error('offline'))
    .mockResolvedValueOnce({ ok: true, json: async () => ({ items: [], nextMatchId: null, hasMore: false }) });
  vi.stubGlobal('fetch', fetchMock);
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    expect(container.textContent).toContain('No demo fixture has been substituted');
    await act(async () => { container.querySelector('button')!.click(); });
    expect(container.textContent).toContain('No canonical matches are available yet.');
    expect(fetchMock).toHaveBeenCalledTimes(2);
  } finally {
    await act(async () => root.unmount());
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  }
});

it('renders only an exact selected-time frame and exposes its nonvisual position table', async () => {
  const matchId = '11111111-1111-1111-1111-111111111111';
  const sourceSha256 = 'd'.repeat(64);
  const snapshot = {
    matchId, canonicalRevision: 4, selectedTimeSeconds: 3309, asOfSeconds: 3309, throughSequence: 19,
    algorithmVersion: 'projection-v1', sourceSha256: 'a'.repeat(64), stateSha256: 'b'.repeat(64),
    provenance: 'synthetic-approved', period: 2, phase: 'secondhalf', homeScore: 1, awayScore: 0,
    possessionTeamId: null, homeActivePlayers: 11, awayActivePlayers: 11, players: []
  };
  const frame = {
    schemaVersion: 1, matchId, frameId: 'frame-3309', sequence: 19,
    matchTime: {period: 2, elapsedSeconds: 3309, sourceClockValue: 3309, sourceClockUnit: 'seconds', sourceClockBasis: 'match_elapsed'},
    coordinateSystem: {lengthMetres: 105, widthMetres: 68, unit: 'normalized_0_1', axisOrigin: 'bottom_left', teamDirections: [{teamId: 'home', direction: 'left'}, {teamId: 'away', direction: 'right'}]},
    ball: {x: .25, y: .25},
    players: [{playerId: 'player-home-1', teamId: 'home', position: {x: .1, y: .2}}, {playerId: 'player-away-1', teamId: 'away', position: {x: .9, y: .8}}],
    source: {providerId: 'test-provider', sourceMatchId: matchId, sourceRecordId: 'source-frame', sourceSequence: 42, sourceSchemaVersion: 'test-v1', sourceSha256, provenanceStatus: 'owner_declared_synthetic', rightsReviewStatus: 'pending'}
  };
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', `/matches/${matchId}?matchId=${matchId}&atSeconds=3309&revision=4`);
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    const parsed = new URL(url, 'http://localhost');
    if (parsed.pathname.endsWith('/key-moments')) return {ok: false, status: 503, json: async () => ({status: 503, title: 'Unavailable', code: 'DEPENDENCY_UNAVAILABLE', correlationId: 'test'})};
    const page = {matchId, canonicalRevision: 4, selectedTimeSeconds: 3309, throughSequence: 19, items: [], nextCursor: null, hasMore: false};
    const body = parsed.pathname.endsWith('/summary') ? snapshot
      : parsed.pathname.endsWith('/events') ? {...page, items: [{eventId: 'event-19', matchId, sequence: 19, type: 'goal', clockSeconds: 3309, period: 2, occurredAt: '2026-01-01T00:55:09Z'}]}
        : parsed.pathname.endsWith('/frames') ? {...page, items: [{frameId: 'frame-3309', sequence: 19, period: 2, clockSeconds: 3309, coordinateVersion: 'pitch-v1', sourceSha256, quality: {coverage: 'observed'}, frame}]}
          : page;
    return {ok: true, json: async () => body};
  }));
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    const pitch = container.querySelector<SVGSVGElement>('.canonical-pitch')!;
    expect(pitch.getAttribute('aria-label')).toContain('55:09, period 2');
    expect(pitch.querySelector('.ball-marker')?.getAttribute('cx')).toBe('26.25');
    expect(pitch.querySelector('.ball-marker')?.getAttribute('cy')).toBe('51');
    expect(container.textContent).toContain('away attacks right in this view');
    expect(container.textContent).toContain('Sample gaps are not filled.');
    expect(container.textContent).toContain('player-home-1');
    expect(container.querySelector('table caption')?.textContent).toContain('55:09');
    expect(container.querySelector('details.position-table-details')?.hasAttribute('open')).toBe(true);
    expect(container.querySelector('table thead')?.textContent).toContain('Marker');
    const timelineEvent = container.querySelector<HTMLButtonElement>('[aria-label="Select Goal at 55:09, sequence 19"]');
    expect(timelineEvent).not.toBeNull();
    await act(async () => timelineEvent!.click());
    expect(new URL(window.location.href).searchParams.get('atSeconds')).toBe('3309');
    const play = [...container.querySelectorAll('button')].find(button => button.textContent === 'Play replay')!;
    await act(async () => play.click());
    expect(container.textContent).toContain('Pause replay');
    const pause = [...container.querySelectorAll('button')].find(button => button.textContent === 'Pause replay')!;
    await act(async () => pause.click());
    expect(container.textContent).toContain('Play replay');
  } finally {
    await act(async () => root.unmount());
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  }
});

it('rejects a tracking frame whose selected-time metadata does not match the query', async () => {
  const matchId = '11111111-1111-1111-1111-111111111111';
  const snapshot = {
    matchId, canonicalRevision: 4, selectedTimeSeconds: 3309, asOfSeconds: 3309, throughSequence: 19,
    algorithmVersion: 'projection-v1', sourceSha256: 'a'.repeat(64), stateSha256: 'b'.repeat(64),
    provenance: 'synthetic-approved', period: 2, phase: 'secondhalf', homeScore: 1, awayScore: 0,
    possessionTeamId: null, homeActivePlayers: 11, awayActivePlayers: 11, players: []
  };
  const frame = {
    schemaVersion: 1, matchId, frameId: 'frame-3308', sequence: 19,
    matchTime: {period: 2, elapsedSeconds: 3308, sourceClockValue: 3308, sourceClockUnit: 'seconds', sourceClockBasis: 'match_elapsed'},
    coordinateSystem: {lengthMetres: 105, widthMetres: 68, unit: 'normalized_0_1', axisOrigin: 'bottom_left', teamDirections: [{teamId: 'home', direction: 'right'}]},
    ball: {x: .5, y: .5}, players: [{playerId: 'player-home-1', teamId: 'home', position: {x: .1, y: .2}}],
    source: {providerId: 'test-provider', sourceMatchId: matchId, sourceRecordId: 'source-frame', sourceSequence: 42, sourceSchemaVersion: 'test-v1', sourceSha256: 'd'.repeat(64), provenanceStatus: 'owner_declared_synthetic', rightsReviewStatus: 'pending'}
  };
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', `/matches/${matchId}?matchId=${matchId}&atSeconds=3309&revision=4`);
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    const parsed = new URL(url, 'http://localhost');
    if (parsed.pathname.endsWith('/key-moments')) return {ok: false, status: 503, json: async () => ({status: 503, title: 'Unavailable', code: 'DEPENDENCY_UNAVAILABLE', correlationId: 'test'})};
    const page = {matchId, canonicalRevision: 4, selectedTimeSeconds: 3309, throughSequence: 19, items: [], nextCursor: null, hasMore: false};
    const body = parsed.pathname.endsWith('/summary') ? snapshot
      : parsed.pathname.endsWith('/frames') ? {...page, items: [{frameId: 'frame-3308', sequence: 19, period: 2, clockSeconds: 3308, coordinateVersion: 'pitch-v1', sourceSha256: 'd'.repeat(64), quality: {}, frame}]}
        : page;
    return {ok: true, json: async () => body};
  }));
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    expect(container.textContent).toContain('outside the selected match, time, revision, sequence or event cutoff');
    expect(container.querySelector('.canonical-pitch')).toBeNull();
    expect(container.textContent).not.toContain('player-home-1');
  } finally {
    await act(async () => root.unmount());
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  }
});

it('follows the canonical fixtures cursor when more persisted matches exist', async () => {
  const firstId = '11111111-1111-1111-1111-111111111111';
  const secondId = '22222222-2222-2222-2222-222222222222';
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', '/home');
  const fetchMock = vi.fn(async (url: string) => {
    const firstPage = !url.includes('afterMatchId=');
    const matchId = firstPage ? firstId : secondId;
    return { ok: true, json: async () => ({
      items: [{ matchId, canonicalRevision: 1, asOfSeconds: 1200, throughSequence: 3, phase: 'firsthalf', homeScore: 0, awayScore: 0, provenance: 'synthetic-approved', algorithmVersion: 'projection-v1', sourceSha256: 'e'.repeat(64) }],
      nextMatchId: firstPage ? firstId : null,
      hasMore: firstPage
    }) };
  });
  vi.stubGlobal('fetch', fetchMock);
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    expect(container.textContent).toContain(firstId);
    await act(async () => { container.querySelector('.load-more')!.dispatchEvent(new MouseEvent('click', { bubbles: true })); });
    expect(container.textContent).toContain(secondId);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(fetchMock.mock.calls[1]?.[0]).toContain(`afterMatchId=${firstId}`);
  } finally {
    await act(async () => root.unmount());
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  }
});

it('opens a match-context evidence drawer and navigates to the cited event time', async () => {
  const matchId = '11111111-1111-1111-1111-111111111111';
  const insight = {
    insightId: 'insight-press', matchId, canonicalRevision: 4, selectedTimeSeconds: 3309,
    throughSequence: 19, detectorVersion: 'pattern-v1', truthClass: 'interpretation',
    summary: 'A persisted sequence is classified as a pressing pattern.',
    evidence: [{kind: 'event', id: 'event-18', relation: 'supports'}]
  };
  const evidenceRequestUrls: string[] = [];
  const snapshot = {
    matchId, canonicalRevision: 4, selectedTimeSeconds: 3309, asOfSeconds: 3309, throughSequence: 19,
    algorithmVersion: 'projection-v1', sourceSha256: 'a'.repeat(64), stateSha256: 'b'.repeat(64),
    provenance: 'synthetic-approved', period: 2, phase: 'secondhalf', homeScore: 1, awayScore: 0,
    possessionTeamId: null, homeActivePlayers: 11, awayActivePlayers: 11, players: []
  };
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', `/matches/${matchId}?matchId=${matchId}&atSeconds=3309&revision=4`);
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    const parsed = new URL(url, 'http://localhost');
    if (parsed.pathname.endsWith('/key-moments') || parsed.pathname.endsWith('/frames'))
      return {ok: false, status: 503, json: async () => ({status: 503, title: 'Unavailable', code: 'DEPENDENCY_UNAVAILABLE', correlationId: 'test'})};
    if (parsed.pathname.endsWith('/evidence/insight-press')) {
      evidenceRequestUrls.push(url);
      const item = {kind: 'event', id: 'event-18', relation: 'supports', sequence: 18, clockSeconds: 3300, period: 2,
        payload: {eventId: 'event-18', matchId, sequence: 18, type: 'pass', clockSeconds: 3300, period: 2, occurredAt: '2026-01-01T00:55:00Z'}};
      return {ok: true, json: async () => ({insight, items: [item]})};
    }
    const page = {matchId, canonicalRevision: 4, selectedTimeSeconds: 3309, throughSequence: 19, nextCursor: null, hasMore: false};
    const body = parsed.pathname.endsWith('/summary') ? snapshot
      : parsed.pathname.endsWith('/events') ? {...page, items: [
        {eventId: 'event-18', matchId, sequence: 18, type: 'pass', clockSeconds: 3300, period: 2, occurredAt: '2026-01-01T00:55:00Z'},
        {eventId: 'event-19', matchId, sequence: 19, type: 'goal', clockSeconds: 3309, period: 2, occurredAt: '2026-01-01T00:55:09Z'}
      ]} : {...page, items: [insight]};
    return {ok: true, json: async () => body};
  }));
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    expect(container.textContent).toContain(insight.summary);
    await act(async () => { [...container.querySelectorAll('button')].find(button => button.textContent === 'Why this insight?')!.click(); });
    expect(evidenceRequestUrls).toHaveLength(1);
    const evidenceUrl = new URL(evidenceRequestUrls[0]!, 'http://localhost');
    expect(evidenceUrl.searchParams.get('atSeconds')).toBe('3309');
    expect(evidenceUrl.searchParams.get('revision')).toBe('4');
    expect(container.textContent).toContain('event-18');
    expect(container.textContent).toContain('supports');
    expect(container.textContent).toContain('55:00 · period 2 · sequence 18');
    expect(container.textContent).toContain('This view does not add intent or causality.');
    await act(async () => { [...container.querySelectorAll('button')].find(button => button.textContent === 'View event in timeline')!.click(); });
    expect(new URL(window.location.href).searchParams.get('atSeconds')).toBe('3300');
  } finally {
    await act(async () => root.unmount());
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  }
});

it('does not show a source claim when evidence references do not resolve exactly', async () => {
  const matchId = '11111111-1111-1111-1111-111111111111';
  const insight = {
    insightId: 'insight-press', matchId, canonicalRevision: 4, selectedTimeSeconds: 3309,
    throughSequence: 19, detectorVersion: 'pattern-v1', truthClass: 'interpretation',
    summary: 'A persisted sequence is classified as a pressing pattern.',
    evidence: [{kind: 'event', id: 'event-18', relation: 'supports'}]
  };
  const snapshot = {
    matchId, canonicalRevision: 4, selectedTimeSeconds: 3309, asOfSeconds: 3309, throughSequence: 19,
    algorithmVersion: 'projection-v1', sourceSha256: 'a'.repeat(64), stateSha256: 'b'.repeat(64),
    provenance: 'synthetic-approved', period: 2, phase: 'secondhalf', homeScore: 1, awayScore: 0,
    possessionTeamId: null, homeActivePlayers: 11, awayActivePlayers: 11, players: []
  };
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', `/matches/${matchId}?matchId=${matchId}&atSeconds=3309&revision=4`);
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    const parsed = new URL(url, 'http://localhost');
    if (parsed.pathname.endsWith('/key-moments') || parsed.pathname.endsWith('/frames'))
      return {ok: false, status: 503, json: async () => ({status: 503, title: 'Unavailable', code: 'DEPENDENCY_UNAVAILABLE', correlationId: 'test'})};
    if (parsed.pathname.endsWith('/evidence/insight-press'))
      return {ok: true, json: async () => ({insight, items: [{kind: 'event', id: 'future-event', relation: 'supports', sequence: 20, clockSeconds: 3310, period: 2,
        payload: {eventId: 'future-event', matchId, sequence: 20, clockSeconds: 3310, period: 2}}]})};
    const page = {matchId, canonicalRevision: 4, selectedTimeSeconds: 3309, throughSequence: 19, nextCursor: null, hasMore: false};
    const body = parsed.pathname.endsWith('/summary') ? snapshot
      : parsed.pathname.endsWith('/insights') ? {...page, items: [insight]} : {...page, items: []};
    return {ok: true, json: async () => body};
  }));
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    await act(async () => { [...container.querySelectorAll('button')].find(button => button.textContent === 'Why this insight?')!.click(); });
    expect(container.textContent).toContain('The evidence response does not match this insight and selected match time. No source claims are shown.');
    expect(container.textContent).not.toContain('future-event');
  } finally {
    await act(async () => root.unmount());
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  }
});
