import { describe, expect, it } from 'vitest';
import { App } from './App';
import { clock, provenanceLabel } from './api';
import { renderToStaticMarkup } from 'react-dom/server';
import { createElement } from 'react';
import { contextHref, resolveRoute, selectedTimeFromSearch } from './routes';
describe('Match experience', () => {
  it('renders the route shell and honest loading state without inventing match facts', () => {
    const markup = renderToStaticMarkup(createElement(App));
    expect(markup).toContain('Match data with source details');
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

const candidateStatusFixture = { matchId: null, isStreaming: false, currentSequence: null,
  currentClockValue: null, period: null, sourceSha256: null, sourceStatus: 'candidate_not_canonical',
  coordinateStatus: 'unverified', playerMetricsStatus: 'unvalidated' };
const candidateStatusResponse = { ok: true, json: async () => candidateStatusFixture };

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

describe('canonical provenance labels', () => {
  it('makes synthetic match provenance clear without inventing match facts', () => {
    expect(provenanceLabel('synthetic-approved')).toBe('Simulated EPL-context match');
    expect(provenanceLabel('owner-confirmed-synthetic')).toBe('Simulated EPL-context match');
    expect(provenanceLabel('official-source')).toBe('official-source');
  });

  it('shows the simulated label on canonical Home match cards', async () => {
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    window.history.replaceState(null, '', '/home');
    vi.stubGlobal('fetch', vi.fn(async () => ({
      ok: true,
      json: async () => ({items:[{
        matchId: 'synthetic-test-match', canonicalRevision: 3, asOfSeconds: 0,
        throughSequence: 0, phase: 'notstarted', homeScore: 0, awayScore: 0,
        provenance: 'synthetic-approved', algorithmVersion: 'test', sourceSha256: 'a'.repeat(64)
      }], nextMatchId: null, hasMore: false})
    })));
    const container = document.createElement('div');
    const root = createRoot(container);
    try {
      await act(async () => { root.render(createElement(App)); });
      expect(container.textContent).toContain('Simulated EPL-context match');
    } finally {
      await act(async () => root.unmount());
      vi.unstubAllGlobals();
      window.history.replaceState(null, '', '/');
    }
  });
});

import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { vi } from 'vitest';
it('loads API state and clears historical insight when replay moves before evidence', async () => {
  const fixture = {matchId: 'fixture', version: 'v1', seed: 42, durationSeconds: 3600, halfTimeSeconds: 1800, pitchLengthMetres: 120, pitchWidthMetres: 80, home: {id: 'h', name: 'Harbour FC', formation: '4-2-3-1', players: []}, away: {id: 'a', name: 'Ridge Athletic', formation: '4-2-3-1', players: []}};
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', '/demo/matches/fixture?matchId=fixture&atSeconds=0');
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    const seconds = Number(new URL(url, 'http://localhost').searchParams.get('atSeconds') ?? 0);
    const body = url.endsWith('/matches') ? [fixture] : url.includes('/state?') ? {clockSeconds: seconds, homeScore: seconds >= 3309 ? 1 : 0, awayScore: 0, status: 'Simulated replay', frame: {clockSeconds: seconds, players: [], ballX: 52.5, ballY: 34}} : url.includes('/insights?') && seconds >= 3309 ? [{id: 'press', title: 'Supported press', summary: 'Observed sequence', classification: 'derived'}] : [];
    return {ok: true, json: async () => body};
  }));
  const container = document.createElement('div');
  document.body.append(container);
  const root = createRoot(container);
  try {
    await act(async () => {root.render(createElement(App));});
    expect(container.textContent).toContain('Harbour FC');
    const slider = container.querySelector('input')!;
    expect(slider.max).toBe('3600');
    expect(container.querySelector('svg.pitch')?.getAttribute('viewBox')).toBe('-3 -3 126 86');
    expect(container.querySelectorAll('.pitch rect')[1].getAttribute('width')).toBe('16.5');
    expect(container.querySelectorAll('.pitch rect')[1].getAttribute('height')).toBe('40.32');
    expect(container.querySelectorAll('.pitch rect')[3].getAttribute('width')).toBe('5.5');
    expect(container.querySelectorAll('.pitch rect')[3].getAttribute('height')).toBe('18.32');
    expect(container.textContent).toContain('120 × 80 m');
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
    container.remove();
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  }
});

it('loads a canonical match using one exact selected time and revision without demo fallback', async () => {
  const matchId = '11111111-1111-1111-1111-111111111111';
  const observedRequests: string[] = [];
  const snapshot = {
    matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3309, asOfSeconds: 3309, throughSequence: 19,
    algorithmVersion: 'projection-v1', sourceSha256: 'a'.repeat(64), stateSha256: 'b'.repeat(64),
    provenance: 'synthetic-approved', period: 2, phase: 'secondhalf', homeScore: 1, awayScore: 0,
    possessionTeamId: null, homeActivePlayers: 11, awayActivePlayers: 11, players: []
  };
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', `/matches/${matchId}?matchId=${matchId}&atSeconds=3309&revision=4`);
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    observedRequests.push(url);
    const parsed = new URL(url, 'http://localhost');
    if (parsed.pathname.endsWith('/key-moments')) return {ok: false, status: 503, json: async () => ({status: 503, title: 'Unavailable', code: 'CANONICAL_KEY_MOMENTS_UNAVAILABLE', correlationId: 'test'})};
    const body = parsed.pathname.endsWith('/summary') ? snapshot
      : parsed.pathname.endsWith('/events') ? {matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3309, throughSequence: 19, items: [
        {eventId: 'event-1', matchId, sequence: 18, type: 'shot', clockSeconds: 3300, period: 2, occurredAt: '2026-01-01T00:55:00Z'},
        {eventId: 'event-2', matchId, sequence: 19, type: 'goal', clockSeconds: 3309, period: 2, occurredAt: '2026-01-01T00:55:09Z'}
      ], nextCursor: null, hasMore: false}
        : {matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3309, throughSequence: 19, items: [], nextCursor: null, hasMore: false};
    return {ok: true, json: async () => body};
  }));
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    expect(container.textContent).toContain('Match overview');
    expect(container.textContent).toContain('Simulated EPL-context match');
    expect(container.textContent).toContain('Canonical revision 4');
    expect(container.textContent).toContain('shot');
    expect(container.textContent).toContain('Goal');
    expect(container.textContent).toContain('Score event');
    expect(container.textContent).toContain('Canonical Key Moments are unavailable because no canonical projection is available.');
    expect(container.textContent).toContain('No observed tracking frame approved for display is available at 55:09. Positions are not estimated.');
    const canonicalRequests = observedRequests.filter(url => new URL(url, 'http://localhost').pathname.includes('/canonical/matches/'));
    expect(canonicalRequests).toHaveLength(5);
    for (const url of canonicalRequests) {
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

it('starts replay from live mode and keeps advancing on one historical revision', async () => {
  const matchId = '11111111-1111-1111-1111-111111111111';
  const observedUrls: string[] = [];
  vi.useFakeTimers();
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', `/matches/${matchId}?matchId=${matchId}&atSeconds=3309&mode=live`);
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    observedUrls.push(url);
    const parsed = new URL(url, 'http://localhost');
    if (parsed.pathname.endsWith('/key-moments'))
      return {ok: false, status: 503, json: async () => ({status: 503, title: 'Unavailable', code: 'CANONICAL_KEY_MOMENTS_UNAVAILABLE', correlationId: 'test'})};
    const atSeconds = Number(parsed.searchParams.get('atSeconds'));
    const snapshot = {matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: atSeconds === 5400 ? 5400 : atSeconds,
      asOfSeconds: 3309, throughSequence: 19, algorithmVersion: 'projection-v1', sourceSha256: 'a'.repeat(64), stateSha256: 'b'.repeat(64),
      provenance: 'synthetic-approved', period: 2, phase: 'secondhalf', homeScore: 1, awayScore: 0,
      possessionTeamId: null, homeActivePlayers: 11, awayActivePlayers: 11, players: []};
    const body = parsed.pathname.endsWith('/summary') ? snapshot
      : {matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: atSeconds, throughSequence: 19,
        items: [], nextCursor: null, hasMore: false};
    return {ok: true, json: async () => body};
  }));
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    expect(container.querySelector<HTMLInputElement>('#selected-time')?.value).toBe('3309');
    const play = container.querySelector<HTMLButtonElement>('.match-context-controls button')!;
    await act(async () => { play.click(); });
    for (let tick = 0; tick < 8; tick++)
      await act(async () => { await vi.advanceTimersByTimeAsync(1000); });
    expect(Number(container.querySelector<HTMLInputElement>('#selected-time')?.value)).toBeGreaterThanOrEqual(3311);
    expect(container.querySelector<HTMLButtonElement>('.match-context-controls button')?.getAttribute('aria-pressed')).toBe('true');
    expect(new URL(window.location.href).searchParams.get('mode')).toBeNull();
    expect(new URL(window.location.href).searchParams.get('revision')).toBe('4');
    expect(observedUrls.some(url => url.includes('/summary?atSeconds=3311&revision=4'))).toBe(true);
  } finally {
    await act(async () => root.unmount());
    vi.useRealTimers();
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  }
});

it('does not describe an unrelated key-moments 503 as an empty canonical projection', async () => {
  const matchId = '11111111-1111-1111-1111-111111111111';
  const snapshot = {
    matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3309, asOfSeconds: 3309, throughSequence: 19,
    algorithmVersion: 'projection-v1', sourceSha256: 'a'.repeat(64), stateSha256: 'b'.repeat(64),
    provenance: 'synthetic-approved', period: 2, phase: 'secondhalf', homeScore: 1, awayScore: 0,
    possessionTeamId: null, homeActivePlayers: 11, awayActivePlayers: 11, players: []
  };
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', `/matches/${matchId}?matchId=${matchId}&atSeconds=3309&revision=4`);
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    const parsed = new URL(url, 'http://localhost');
    if (parsed.pathname.endsWith('/key-moments')) return {ok: false, status: 503, json: async () => ({status: 503, title: 'Service offline', code: 'DEPENDENCY_UNAVAILABLE', correlationId: 'test'})};
    const body = parsed.pathname.endsWith('/summary') ? snapshot
      : parsed.pathname.endsWith('/events') ? {matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3309, throughSequence: 19, items: [], nextCursor: null, hasMore: false}
        : {matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3309, throughSequence: 19, items: [], nextCursor: null, hasMore: false};
    return {ok: true, json: async () => body};
  }));
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    expect(container.textContent).toContain('Canonical key moments could not be loaded.');
    expect(container.textContent).not.toContain('no canonical projection is available');
    expect(container.textContent).not.toContain('No persisted canonical key moments are available');
  } finally {
    await act(async () => root.unmount());
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  }
});

it('exposes observed positions as a screen-reader table and keeps event and time controls keyboard focusable', async () => {
  const matchId = '11111111-1111-1111-1111-111111111111';
  const snapshot = {
    matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3309, asOfSeconds: 3309, throughSequence: 19,
    algorithmVersion: 'projection-v1', sourceSha256: 'a'.repeat(64), stateSha256: 'b'.repeat(64),
    provenance: 'synthetic-approved', period: 2, phase: 'secondhalf', homeScore: 1, awayScore: 0,
    possessionTeamId: null, homeActivePlayers: 11, awayActivePlayers: 11, players: []
  };
  const observedFrame = {
    frameId: 'observed-frame', sequence: 19, canonicalRevision: 4, projectionRevision: 4, period: 2, clockSeconds: 3309, coordinateVersion: 'pitch-v1',
    sourceSha256: 'a'.repeat(64), quality: {coverage: 'observed'},
    frame: {
      schemaVersion: 1, matchId, frameId: 'observed-frame', sequence: 19, canonicalRevision: 4,
      matchTime: {period: 2, elapsedSeconds: 3309, sourceClockValue: 3309, sourceClockUnit: 'seconds', sourceClockBasis: 'match_elapsed'},
      coordinateSystem: {lengthMetres: 105, widthMetres: 68, unit: 'metres', axisOrigin: 'bottom_left', teamDirections: [{teamId: 'team-a', direction: 'right'}]},
      ball: {x: 52.5, y: 34}, players: [{playerId: 'player-a', teamId: 'team-a', position: {x: 10, y: 20}}],
      source: {providerId: 'test', sourceMatchId: 'test-match', sourceRecordId: 'observed-frame', sourceSequence: 1,
        sourceSchemaVersion: 'test-v1', sourceSha256: 'a'.repeat(64), provenanceStatus: 'approved-test', rightsReviewStatus: 'approved_for_use'}
    }
  };
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', `/matches/${matchId}?matchId=${matchId}&atSeconds=3309&revision=4`);
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    const parsed = new URL(url, 'http://localhost');
    if (parsed.pathname.endsWith('/key-moments')) return {ok: false, status: 503, json: async () => ({status: 503, title: 'Unavailable', code: 'CANONICAL_KEY_MOMENTS_UNAVAILABLE', correlationId: 'test'})};
    const body = parsed.pathname.endsWith('/summary') ? snapshot
      : parsed.pathname.endsWith('/events') ? {matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3309, throughSequence: 19, items: [
        {eventId: 'event-1', matchId, sequence: 18, type: 'shot', clockSeconds: 3300, period: 2, occurredAt: '2026-01-01T00:55:00Z'},
        {eventId: 'event-2', matchId, sequence: 19, type: 'goal', clockSeconds: 3309, period: 2, occurredAt: '2026-01-01T00:55:09Z'}
      ], nextCursor: null, hasMore: false}
        : parsed.pathname.endsWith('/frames') ? {matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3309, throughSequence: 19, items: [observedFrame], nextCursor: null, hasMore: false}
          : {matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3309, throughSequence: 19, items: [], nextCursor: null, hasMore: false};
    return {ok: true, json: async () => body};
  }));
  const container = document.createElement('div');
  document.body.appendChild(container);
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    const pitch = container.querySelector('svg[role="img"]');
    expect(pitch?.getAttribute('aria-label')).toContain('Observed player and ball positions at 55:09');
    expect(pitch?.getAttribute('aria-describedby')).toBe('pitch-description');
    expect(container.querySelector('table caption')?.textContent).toContain('Pitch view coordinates at 55:09');
    expect(container.querySelector('table tbody')?.textContent).toContain('player-a');
    expect(container.querySelector('table tbody')?.textContent).toContain('Ball');
    expect(container.querySelector('table tbody')?.textContent).toContain('52.50');
    expect(container.querySelector('table tbody')?.textContent).toContain('34.00');
    expect(container.querySelector('details')).toBeNull();
    const slider = container.querySelector<HTMLInputElement>('#selected-time')!;
    const eventButton = container.querySelector<HTMLButtonElement>('.canonical-event')!;
    slider.focus();
    expect(document.activeElement).toBe(slider);
    expect(slider.getAttribute('aria-valuetext')).toBe('55:09');
    eventButton.focus();
    expect(document.activeElement).toBe(eventButton);
    expect(eventButton.getAttribute('aria-label')).toBe('Select shot at 55:00, sequence 18');
  } finally {
    await act(async () => root.unmount());
    container.remove();
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  }
});

it('opens a frame evidence deep link on the exact observed frame when sequences are duplicated and focuses its nonvisual account', async () => {
  const matchId = '11111111-1111-1111-1111-111111111111';
  const snapshot = {
    matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3308, asOfSeconds: 3308, throughSequence: 20,
    algorithmVersion: 'projection-v1', sourceSha256: 'a'.repeat(64), stateSha256: 'b'.repeat(64),
    provenance: 'synthetic-approved', period: 2, phase: 'secondhalf', homeScore: 1, awayScore: 0,
    possessionTeamId: null, homeActivePlayers: 11, awayActivePlayers: 11, players: []
  };
  const frame = (sequence: number, frameId: string) => ({
    frameId, sequence, canonicalRevision: 4, projectionRevision: 4, period: 2, clockSeconds: 3308, coordinateVersion: 'pitch-v1',
    sourceSha256: 'a'.repeat(64), quality: { coverage: 'observed' },
    frame: {
      schemaVersion: 1, matchId, frameId, sequence, canonicalRevision: 4,
      matchTime: { period: 2, elapsedSeconds: 3308, sourceClockValue: 3308, sourceClockUnit: 'seconds', sourceClockBasis: 'match_elapsed' },
      coordinateSystem: { lengthMetres: 105, widthMetres: 68, unit: 'metres', axisOrigin: 'bottom_left', teamDirections: [{ teamId: 'team-a', direction: 'right' }] },
      ball: { x: 52.5, y: 34 }, players: [{ playerId: `player-${sequence}`, teamId: 'team-a', position: { x: sequence, y: 20 } }],
      source: { providerId: 'test', sourceMatchId: 'test-match', sourceRecordId: frameId, sourceSequence: sequence,
        sourceSchemaVersion: 'test-v1', sourceSha256: 'a'.repeat(64), provenanceStatus: 'approved-test', rightsReviewStatus: 'approved_for_use' }
    }
  });
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', `/replay/${matchId}?matchId=${matchId}&atSeconds=3308&revision=4&sourceRevision=4#canonical-frame-later-same-time-frame`);
  let requestedFrameId: string | null = null;
  let requestedSourceRevision: string | null = null;
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    const parsed = new URL(url, 'http://localhost');
    if (parsed.pathname.endsWith('/frames')) {
      requestedFrameId = parsed.searchParams.get('frameId');
      requestedSourceRevision = parsed.searchParams.get('sourceRevision');
    }
    if (parsed.pathname.endsWith('/key-moments')) return { ok: false, status: 503, json: async () => ({ status: 503, title: 'Unavailable', code: 'CANONICAL_KEY_MOMENTS_UNAVAILABLE', correlationId: 'test' }) };
    const body = parsed.pathname.endsWith('/summary') ? snapshot
      : parsed.pathname.endsWith('/events') ? { matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3308, throughSequence: 20, items: [], nextCursor: null, hasMore: false }
        : parsed.pathname.endsWith('/frames') ? { matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3308, throughSequence: 20, items: [frame(18, 'source-frame'), frame(18, 'later-same-time-frame')], nextCursor: null, hasMore: false }
          : { matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3308, throughSequence: 20, items: [], nextCursor: null, hasMore: false };
    return { ok: true, json: async () => body };
  }));
  const container = document.createElement('div');
  document.body.appendChild(container);
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    expect(container.textContent).toContain('Observed frame later-same-time-frame at 55:08');
    expect(container.textContent).not.toContain('Observed frame source-frame');
    expect(document.activeElement?.id).toBe('canonical-frame-later-same-time-frame');
    expect(requestedFrameId).toBe('later-same-time-frame');
    expect(requestedSourceRevision).toBe('4');
    expect(new URL(window.location.href).searchParams.get('sourceRevision')).toBe('4');
  } finally {
    await act(async () => root.unmount());
    container.remove();
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  }
});

it('keeps selected-time scrubbing available and disables auto replay when reduced motion is preferred', async () => {
  const matchId = '11111111-1111-1111-1111-111111111111';
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  vi.stubGlobal('matchMedia', vi.fn((media: string) => ({
    matches: media.includes('prefers-reduced-motion'), media, onchange: null,
    addEventListener: vi.fn(), removeEventListener: vi.fn()
  })));
  window.history.replaceState(null, '', `/matches/${matchId}?matchId=${matchId}&atSeconds=3309&revision=4`);
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    const parsed = new URL(url, 'http://localhost');
    if (parsed.pathname.endsWith('/key-moments')) return {ok: false, status: 503, json: async () => ({status: 503, title: 'Unavailable', code: 'CANONICAL_KEY_MOMENTS_UNAVAILABLE', correlationId: 'test'})};
    const snapshot = {matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3309, asOfSeconds: 3309, throughSequence: 19,
      algorithmVersion: 'projection-v1', sourceSha256: 'a'.repeat(64), stateSha256: 'b'.repeat(64),
      provenance: 'synthetic-approved', period: 2, phase: 'secondhalf', homeScore: 1, awayScore: 0,
      possessionTeamId: null, homeActivePlayers: 11, awayActivePlayers: 11, players: []};
    const body = parsed.pathname.endsWith('/summary') ? snapshot
      : {matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3309, throughSequence: 19, items: [], nextCursor: null, hasMore: false};
    return {ok: true, json: async () => body};
  }));
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    expect(container.querySelector('#selected-time')).not.toBeNull();
    expect(container.textContent).toContain('Automatic replay is off under reduced motion');
    expect(container.querySelector('.match-context-controls button[aria-pressed]')).toBeNull();
  } finally {
    await act(async () => root.unmount());
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  }
});

it('rejects events and insights with a different match, revision or selected-time sequence', async () => {
  const matchId = '11111111-1111-1111-1111-111111111111';
  const snapshot = {
    matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3309, asOfSeconds: 3309, throughSequence: 19,
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
      : parsed.pathname.endsWith('/events') ? {matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3309, throughSequence: 19, items: [{eventId: 'foreign-event', matchId: '22222222-2222-2222-2222-222222222222', sequence: 18, type: 'goal', clockSeconds: 3300, period: 2, occurredAt: '2026-01-01T00:55:00Z'}], nextCursor: null, hasMore: false}
        : {matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3309, throughSequence: 19, items: [{insightId: 'foreign-insight', matchId, canonicalRevision: 3, projectionRevision: 4, selectedTimeSeconds: 3309, throughSequence: 19, detectorVersion: 'test-v1', truthClass: 'interpretation', summary: 'A mismatched revision insight', evidence: []}], nextCursor: null, hasMore: false};
    return {ok: true, json: async () => body};
  }));
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    expect(container.textContent).toContain('This match data does not match the selected moment.');
    expect(container.textContent).not.toContain('foreign-event');
    expect(container.textContent).not.toContain('A mismatched revision insight');
  } finally {
    await act(async () => root.unmount());
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  }
});

it.each(['event-match-id', 'insight-revision'] as const)('rejects a canonical row with a mismatched %s', async mismatch => {
  const matchId = '11111111-1111-1111-1111-111111111111';
  const snapshot = {
    matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3309, asOfSeconds: 3309, throughSequence: 19,
    algorithmVersion: 'projection-v1', sourceSha256: 'a'.repeat(64), stateSha256: 'b'.repeat(64),
    provenance: 'synthetic-approved', period: 2, phase: 'secondhalf', homeScore: 1, awayScore: 0,
    possessionTeamId: null, homeActivePlayers: 11, awayActivePlayers: 11, players: []
  };
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', `/matches/${matchId}?matchId=${matchId}&atSeconds=3309&revision=4`);
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    const parsed = new URL(url, 'http://localhost');
    if (parsed.pathname.endsWith('/key-moments')) return {ok: false, status: 503, json: async () => ({status: 503, code: 'DEPENDENCY_UNAVAILABLE'})};
    const event = {eventId: 'wrong-event', matchId: '22222222-2222-2222-2222-222222222222', sequence: 19,
      type: 'goal', clockSeconds: 3309, period: 2, occurredAt: '2026-01-01T00:55:09Z'};
    const insight = {insightId: 'wrong-revision', matchId, canonicalRevision: 3, projectionRevision: 4, selectedTimeSeconds: 3309,
      throughSequence: 19, sourceVersion: 'test-v1', provenance: 'synthetic-approved', sourceSha256: 'c'.repeat(64),
      detectorVersion: 'test-v1', truthClass: 'interpretation', summary: 'Must not be shown', evidence: []};
    const body = parsed.pathname.endsWith('/summary') ? snapshot
      : parsed.pathname.endsWith('/events')
        ? {matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3309, throughSequence: 19,
          items: mismatch === 'event-match-id' ? [event] : [], nextCursor: null, hasMore: false}
        : {matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3309, throughSequence: 19,
          items: mismatch === 'insight-revision' ? [insight] : [], nextCursor: null, hasMore: false};
    return {ok: true, json: async () => body};
  }));
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    expect(container.textContent).toContain('This match data does not match the selected moment.');
    expect(container.textContent).not.toContain('wrong-event');
    expect(container.textContent).not.toContain('Must not be shown');
  } finally {
    await act(async () => root.unmount());
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  }
});

it.each(['frame-page-projection-revision', 'frame-item-projection-revision', 'nested-frame-canonical-revision'] as const)(
  'rejects tracking frames with a mismatched %s', async mismatch => {
    const matchId = '11111111-1111-1111-1111-111111111111';
    const snapshot = {
      matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3309, asOfSeconds: 3309, throughSequence: 19,
      algorithmVersion: 'projection-v1', sourceSha256: 'a'.repeat(64), stateSha256: 'b'.repeat(64),
      provenance: 'synthetic-approved', period: 2, phase: 'secondhalf', homeScore: 1, awayScore: 0,
      possessionTeamId: null, homeActivePlayers: 11, awayActivePlayers: 11, players: []
    };
    const sourceSha256 = 'a'.repeat(64);
    const frame = {
      frameId: 'frame-1', sequence: 19, canonicalRevision: 4, projectionRevision: mismatch === 'frame-item-projection-revision' ? 3 : 4,
      period: 2, clockSeconds: 3309, coordinateVersion: 'pitch-v1', sourceSha256, quality: {coverage: 'observed'},
      frame: {
        schemaVersion: 1, matchId, frameId: 'frame-1', sequence: 19,
        canonicalRevision: mismatch === 'nested-frame-canonical-revision' ? 3 : 4,
        matchTime: {period: 2, elapsedSeconds: 3309, sourceClockValue: 3309, sourceClockUnit: 'seconds', sourceClockBasis: 'match_elapsed'},
        coordinateSystem: {lengthMetres: 105, widthMetres: 68, unit: 'metres', axisOrigin: 'bottom_left', teamDirections: [{teamId: 'team-a', direction: 'right'}]},
        ball: {x: 52.5, y: 34}, players: [],
        source: {providerId: 'test', sourceMatchId: 'test-match', sourceRecordId: 'frame-1', sourceSequence: 1,
          sourceSchemaVersion: 'test-v1', sourceSha256, provenanceStatus: 'approved-test', rightsReviewStatus: 'approved_for_use'}
      }
    };
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    window.history.replaceState(null, '', `/matches/${matchId}?matchId=${matchId}&atSeconds=3309&revision=4`);
    vi.stubGlobal('fetch', vi.fn(async (url: string) => {
      const parsed = new URL(url, 'http://localhost');
      if (parsed.pathname.endsWith('/key-moments')) return {ok: false, status: 503, json: async () => ({status: 503, code: 'DEPENDENCY_UNAVAILABLE'})};
      const page = {matchId, canonicalRevision: 4, projectionRevision: mismatch === 'frame-page-projection-revision' ? 3 : 4,
        selectedTimeSeconds: 3309, throughSequence: 19, items: [frame], nextCursor: null, hasMore: false};
      const body = parsed.pathname.endsWith('/summary') ? snapshot
        : parsed.pathname.endsWith('/frames') ? page
          : {matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3309,
            throughSequence: 19, items: [], nextCursor: null, hasMore: false};
      return {ok: true, json: async () => body};
    }));
    const container = document.createElement('div');
    const root = createRoot(container);
    try {
      await act(async () => { root.render(createElement(App)); });
      expect(container.textContent).toContain('This match data does not match the selected moment.');
      expect(container.querySelector('svg[role="img"]')).toBeNull();
    } finally {
      await act(async () => root.unmount());
      vi.unstubAllGlobals();
      window.history.replaceState(null, '', '/');
    }
  }
);

it('renders persisted home cards from canonical fields and keeps the match id in navigation', async () => {
  const firstId = '11111111-1111-1111-1111-111111111111';
  const secondId = '22222222-2222-2222-2222-222222222222';
  const card = (matchId: string, phase: string, homeScore: number, awayScore: number) => ({
    matchId, canonicalRevision: 100, projectionRevision: 5, asOfSeconds: 2700, throughSequence: 12, phase, homeScore, awayScore,
    provenance: 'synthetic-approved', algorithmVersion: 'projection-v1', sourceSha256: 'c'.repeat(64)
  });
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', `/home?matchId=${firstId}&atSeconds=1200&revision=5`);
  vi.stubGlobal('fetch', vi.fn(async () => ({ ok: true, json: async () => ({ items: [card(firstId, 'secondhalf', 1, 0), card(secondId, 'fulltime', 1, 1)], nextMatchId: null, hasMore: false }) })));
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    expect(container.textContent).toContain('Simulated EPL-context match');
    expect(container.textContent).toContain('Home team 1–0 Away team');
    expect(container.textContent).toContain(firstId);
    expect(container.textContent).toContain('Second half');
    const firstLink = [...container.querySelectorAll<HTMLAnchorElement>('.match-card a.primary-link')].find(link => link.href.includes(firstId));
    expect(firstLink?.getAttribute('href')).toBe(`/matches/${firstId}?matchId=${firstId}&atSeconds=1200&revision=5`);
    const secondLink = [...container.querySelectorAll<HTMLAnchorElement>('.match-card a.primary-link')].find(link => link.href.includes(secondId));
    expect(secondLink?.getAttribute('href')).toBe(`/matches/${secondId}?matchId=${secondId}&atSeconds=2700&revision=5`);
    const phaseFilter = container.querySelector<HTMLSelectElement>('[aria-label="Filter matches by phase"]')!;
    await act(async () => {
      Object.getOwnPropertyDescriptor(HTMLSelectElement.prototype, 'value')!.set!.call(phaseFilter, 'secondhalf');
      phaseFilter.dispatchEvent(new Event('change', { bubbles: true }));
    });
    expect(container.textContent).toContain(firstId);
    expect(container.textContent).not.toContain(secondId);
    expect(window.location.search).toBe(`?matchId=${firstId}&atSeconds=1200&revision=5&phase=secondhalf`);
    expect(container.textContent).not.toContain('Arsenal');
  } finally {
    await act(async () => root.unmount());
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  }
});


it('preserves projection revision from an insight into selected-match navigation', async () => {
  const matchId = '11111111-1111-1111-1111-111111111111';
  const insight = {
    insightId: 'insight-press', matchId, canonicalRevision: 100, projectionRevision: 5,
    selectedTimeSeconds: 3309, throughSequence: 19, sourceVersion: 'test-v1', provenance: 'synthetic-approved',
    sourceSha256: 'c'.repeat(64), detectorVersion: 'test-v1', truthClass: 'interpretation',
    summary: 'Evidence-backed pattern.', evidence: [{kind: 'event', id: 'event-19', relation: 'supports'}]
  };
  const requested: string[] = [];
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', `/insights?matchId=${matchId}&atSeconds=3309&revision=5`);
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
    const url = new URL(String(input), 'http://localhost');
    requested.push(url.toString());
    return {ok: true, json: async () => ({matchId, canonicalRevision: 100, projectionRevision: 5,
      selectedTimeSeconds: 3309, throughSequence: 19, items: [insight], nextCursor: null, hasMore: false})};
  }));
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    const insightRequests = requested.filter(value => new URL(value).pathname.endsWith('/insights'));
    expect(insightRequests).toHaveLength(1);
    expect(new URL(insightRequests[0]).searchParams.get('revision')).toBe('5');
    const link = [...container.querySelectorAll<HTMLAnchorElement>('a')].find(anchor => anchor.textContent === 'Open selected match');
    expect(link?.getAttribute('href')).toBe(`/matches/${matchId}?matchId=${matchId}&atSeconds=3309&revision=5`);
  } finally {
    await act(async () => root.unmount());
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  }
});

it('shows a clear canonical-unavailable state and retries without demo substitution', async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', '/home');
  let homeAttempt = 0;
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    if (new URL(String(input), 'http://localhost').pathname.endsWith('/simulation/candidate/status')) return candidateStatusResponse;
    homeAttempt++;
    if (homeAttempt === 1) throw new Error('offline');
    return { ok: true, json: async () => ({ items: [], nextMatchId: null, hasMore: false }) };
  });
  vi.stubGlobal('fetch', fetchMock);
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    expect(container.textContent).toContain('No demo fixture has been substituted');
    const retry = [...container.querySelectorAll('button')].find(button => button.textContent?.includes('Try loading matches again'))!;
    await act(async () => { retry.click(); });
    expect(container.textContent).toContain('No matches are available yet.');
    expect(homeAttempt).toBe(2);
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
    if (new URL(url, 'http://localhost').pathname.endsWith('/simulation/candidate/status')) return candidateStatusResponse;
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
    const homeRequests = fetchMock.mock.calls.map(([url]) => url).filter(url => !new URL(url, 'http://localhost').pathname.endsWith('/simulation/candidate/status'));
    expect(homeRequests).toHaveLength(2);
    expect(homeRequests[1]).toContain(`afterMatchId=${firstId}`);
  } finally {
    await act(async () => root.unmount());
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  }
});

it('keeps a phase filter across cursor pages and does not claim the later phase is absent', async () => {
  const firstId = '11111111-1111-1111-1111-111111111111';
  const secondId = '22222222-2222-2222-2222-222222222222';
  const card = (matchId: string, phase: string) => ({
    matchId, canonicalRevision: 1, asOfSeconds: 1200, throughSequence: 3, phase, homeScore: 0, awayScore: 0,
    provenance: 'synthetic-approved', algorithmVersion: 'projection-v1', sourceSha256: 'e'.repeat(64)
  });
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', '/home?phase=secondhalf');
  const fetchMock = vi.fn(async (url: string) => {
    if (new URL(url, 'http://localhost').pathname.endsWith('/simulation/candidate/status')) return candidateStatusResponse;
    const secondPage = url.includes('afterMatchId=');
    return { ok: true, json: async () => ({
      items: [secondPage ? card(secondId, 'secondhalf') : card(firstId, 'firsthalf')],
      nextMatchId: secondPage ? null : firstId,
      hasMore: !secondPage
    }) };
  });
  vi.stubGlobal('fetch', fetchMock);
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    const phaseFilter = container.querySelector<HTMLSelectElement>('[aria-label="Filter matches by phase"]')!;
    expect(phaseFilter.value).toBe('secondhalf');
    expect(container.textContent).toContain('No match in this phase is in the current list.');
    expect(container.textContent).toContain('Choose “More matches” to check the next results.');
    expect(container.textContent).not.toContain('No matches are available in this phase.');
    await act(async () => { container.querySelector<HTMLButtonElement>('.load-more')!.click(); });
    expect(container.textContent).toContain(secondId);
    expect(container.textContent).not.toContain(firstId);
    expect(container.querySelector<HTMLSelectElement>('[aria-label="Filter matches by phase"]')!.value).toBe('secondhalf');
    expect(fetchMock.mock.calls.filter(([url]) => !new URL(url, 'http://localhost').pathname.endsWith('/simulation/candidate/status'))).toHaveLength(2);
  } finally {
    await act(async () => root.unmount());
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  }
});

it('aborts and ignores a pending cursor page when Home is refreshed', async () => {
  const firstId = '11111111-1111-1111-1111-111111111111';
  const refreshedId = '33333333-3333-3333-3333-333333333333';
  const staleId = '22222222-2222-2222-2222-222222222222';
  const card = (matchId: string) => ({
    matchId, canonicalRevision: 1, asOfSeconds: 1200, throughSequence: 3, phase: 'firsthalf', homeScore: 0, awayScore: 0,
    provenance: 'synthetic-approved', algorithmVersion: 'projection-v1', sourceSha256: 'e'.repeat(64)
  });
  let resolveCursor!: (response: { ok: boolean; json: () => Promise<unknown> }) => void;
  let cursorSignal: AbortSignal | undefined;
  let firstPageRequests = 0;
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', '/home');
  const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
    const url = String(input);
    if (url.includes('afterMatchId=')) {
      cursorSignal = init?.signal ?? undefined;
      return new Promise<{ ok: boolean; json: () => Promise<unknown> }>(resolve => { resolveCursor = resolve; });
    }
    firstPageRequests += 1;
    const match = firstPageRequests === 1 ? card(firstId) : card(refreshedId);
    return Promise.resolve({ ok: true, json: async () => ({ items: [match], nextMatchId: firstId, hasMore: true }) });
  });
  vi.stubGlobal('fetch', fetchMock);
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    await act(async () => { container.querySelector('.load-more')!.dispatchEvent(new MouseEvent('click', { bubbles: true })); });
    expect(cursorSignal?.aborted).toBe(false);

    await act(async () => { container.querySelector('.refresh-matches')!.dispatchEvent(new MouseEvent('click', { bubbles: true })); });
    expect(cursorSignal?.aborted).toBe(true);
    expect(container.textContent).toContain(refreshedId);

    await act(async () => {
      resolveCursor({ ok: true, json: async () => ({ items: [card(staleId)], nextMatchId: null, hasMore: false }) });
      await new Promise(resolve => setTimeout(resolve, 0));
    });
    expect(container.textContent).toContain(refreshedId);
    expect(container.textContent).not.toContain(staleId);
    expect(container.textContent).not.toContain(firstId);
  } finally {
    await act(async () => root.unmount());
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  }
});

function canonicalSnapshot(matchId: string, selectedTimeSeconds: number, throughSequence: number,
  canonicalRevision = 4, projectionRevision = canonicalRevision) {
  return {
    matchId, canonicalRevision, projectionRevision, selectedTimeSeconds, asOfSeconds: selectedTimeSeconds, throughSequence,
    algorithmVersion: 'projection-v1', sourceSha256: 'a'.repeat(64), stateSha256: 'b'.repeat(64),
    provenance: 'synthetic-approved', period: 2, phase: 'secondhalf', homeScore: 1, awayScore: 0,
    possessionTeamId: null, homeActivePlayers: 11, awayActivePlayers: 11, players: []
  };
}

function canonicalPage(matchId: string, selectedTimeSeconds: number, throughSequence: number, items: unknown[],
  canonicalRevision = 4, projectionRevision = canonicalRevision) {
  return {matchId, canonicalRevision, projectionRevision, selectedTimeSeconds, throughSequence, items, nextCursor: null, hasMore: false};
}

it('keeps score and recorded events visible when optional insight service is unavailable', async () => {
  const matchId = '11111111-1111-1111-1111-111111111111';
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', `/matches/${matchId}?matchId=${matchId}&atSeconds=3309&revision=4`);
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
    const url = new URL(String(input), 'http://localhost');
    const unavailable = {ok: false, status: 503, json: async () => ({status: 503, title: 'Unavailable', code: 'DEPENDENCY_UNAVAILABLE', correlationId: 'test'})};
    if (url.pathname.endsWith('/insights') || url.pathname.endsWith('/frames') || url.pathname.endsWith('/key-moments')) return unavailable;
    if (url.pathname.endsWith('/summary')) return {ok: true, json: async () => canonicalSnapshot(matchId, 3309, 19)};
    const events = [{eventId: 'event-19', matchId, sequence: 19, type: 'goal', clockSeconds: 3309, period: 2, occurredAt: '2026-01-01T00:55:09Z'}];
    return {ok: true, json: async () => canonicalPage(matchId, 3309, 19, events)};
  }));
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    expect(container.textContent).toContain('Canonical revision 4');
    expect(container.textContent).toContain('Goal');
    expect(container.textContent).toContain('Saved insights service is temporarily unavailable.');
    expect(container.textContent).toContain('AI explanations are not connected; match facts remain available without them.');
  } finally {
    await act(async () => root.unmount());
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  }
});

it('uses projection revision for canonical page requests when revision axes diverge', async () => {
  const matchId = '11111111-1111-1111-1111-111111111111';
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', `/matches/${matchId}?matchId=${matchId}&atSeconds=3309&revision=5`);
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const url = new URL(String(input), 'http://localhost');
    const unavailable = {ok: false, status: 503, json: async () => ({status: 503, title: 'Unavailable', code: 'DEPENDENCY_UNAVAILABLE', correlationId: 'test'})};
    if (url.pathname.endsWith('/summary')) return {ok: true, json: async () => canonicalSnapshot(matchId, 3309, 19, 100, 5)};
    if (url.pathname.endsWith('/frames') || url.pathname.endsWith('/key-moments')) return unavailable;
    return {ok: true, json: async () => canonicalPage(matchId, 3309, 19, [], 100, 5)};
  });
  vi.stubGlobal('fetch', fetchMock);
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    expect(container.textContent).toContain('Canonical revision 100');
    const canonicalRequests = fetchMock.mock.calls.map(([input]) => new URL(String(input), 'http://localhost'))
      .filter(url => ['/summary', '/events', '/insights', '/frames', '/key-moments'].some(path => url.pathname.endsWith(path)));
    expect(canonicalRequests).toHaveLength(5);
    expect(canonicalRequests.every(url => url.searchParams.get('revision') === '5')).toBe(true);
  } finally {
    await act(async () => root.unmount());
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  }
});

it('changes explanation detail for Fan and Analyst without changing persisted facts', async () => {
  const matchId = '11111111-1111-1111-1111-111111111111';
  const insight = {
    insightId: 'insight-press', matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3309,
    throughSequence: 19, sourceVersion: 'source-v1', provenance: 'synthetic-approved', sourceSha256: 'c'.repeat(64),
    detectorVersion: 'pattern-v1', truthClass: 'interpretation', summary: 'A stored pattern is classified as an interpretation.',
    conditions: {sampleSize: 11, observed: true}, limitations: ['Sparse frame coverage'],
    evidence: [{kind: 'event', id: 'event-18', relation: 'supports'}]
  };
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', `/matches/${matchId}?matchId=${matchId}&atSeconds=3309&revision=4`);
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const url = new URL(String(input), 'http://localhost');
    const unavailable = {ok: false, status: 503, json: async () => ({status: 503, title: 'Unavailable', code: 'DEPENDENCY_UNAVAILABLE', correlationId: 'test'})};
    if (url.pathname.endsWith('/frames') || url.pathname.endsWith('/key-moments')) return unavailable;
    if (url.pathname.endsWith('/summary')) return {ok: true, json: async () => canonicalSnapshot(matchId, 3309, 19)};
    if (url.pathname.endsWith('/insights')) return {ok: true, json: async () => canonicalPage(matchId, 3309, 19, [insight])};
    if (url.pathname.endsWith('/events')) return {ok: true, json: async () => canonicalPage(matchId, 3309, 19, [])};
    return {ok: false, status: 404, json: async () => ({})};
  });
  vi.stubGlobal('fetch', fetchMock);
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    const summary = container.querySelector('.canonical-insights > li > p')?.textContent;
    expect(summary).toBe(insight.summary);
    expect(container.textContent).not.toContain('sampleSize');
    const analystButton = [...container.querySelectorAll<HTMLButtonElement>('.audience-mode button')].find(button => button.textContent === 'Analyst')!;
    await act(async () => { analystButton.click(); });
    expect(analystButton.getAttribute('aria-pressed')).toBe('true');
    expect(container.querySelector('.canonical-insights > li > p')?.textContent).toBe(summary);
    expect(container.textContent).toContain('Detector pattern-v1');
    expect(container.textContent).toContain('sampleSize');
    expect(container.textContent).toContain('Sparse frame coverage');
    const fanButton = [...container.querySelectorAll<HTMLButtonElement>('.audience-mode button')].find(button => button.textContent === 'Fan')!;
    await act(async () => { fanButton.click(); });
    expect(fanButton.getAttribute('aria-pressed')).toBe('true');
    expect(container.querySelector('.canonical-insights > li > p')?.textContent).toBe(summary);
    expect(container.querySelector('.analyst-insight-detail')).toBeNull();
    expect(fetchMock.mock.calls.some(([input]) => new URL(String(input), 'http://localhost').pathname.endsWith('/analyst'))).toBe(false);
  } finally {
    await act(async () => root.unmount());
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  }
});

it('restores focus to the cited event when following evidence into replay', async () => {
  const matchId = '11111111-1111-1111-1111-111111111111';
  const insight = {
    insightId: 'insight-press', matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3300,
    throughSequence: 18, sourceVersion: 'source-v1', provenance: 'synthetic-approved', sourceSha256: 'c'.repeat(64),
    detectorVersion: 'pattern-v1', truthClass: 'interpretation', summary: 'Stored insight.', conditions: {}, limitations: null,
    evidence: [{kind: 'event', id: 'event-18', relation: 'supports'}]
  };
  const event = {eventId: 'event-18', matchId, sequence: 18, type: 'pass', clockSeconds: 3300, period: 2, occurredAt: '2026-01-01T00:55:00Z'};
  const evidence = {insight, items: [{kind: 'event', id: 'event-18', relation: 'supports', sourceCanonicalRevision: 4, sequence: 18, clockSeconds: 3300, period: 2, payload: event}]};
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', `/matches/${matchId}?matchId=${matchId}&atSeconds=3300&revision=4`);
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
    const url = new URL(String(input), 'http://localhost');
    const unavailable = {ok: false, status: 503, json: async () => ({status: 503, title: 'Unavailable', code: 'DEPENDENCY_UNAVAILABLE', correlationId: 'test'})};
    if (url.pathname.endsWith('/frames') || url.pathname.endsWith('/key-moments')) return unavailable;
    if (url.pathname.endsWith('/summary')) return {ok: true, json: async () => canonicalSnapshot(matchId, 3300, 18)};
    if (url.pathname.endsWith('/events')) return {ok: true, json: async () => canonicalPage(matchId, 3300, 18, [event])};
    if (url.pathname.endsWith('/insights')) return {ok: true, json: async () => canonicalPage(matchId, 3300, 18, [insight])};
    if (url.pathname.endsWith('/evidence/insight-press')) return {ok: true, json: async () => evidence};
    return {ok: false, status: 404, json: async () => ({})};
  }));
  const container = document.createElement('div');
  document.body.append(container);
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    const disclosure = container.querySelector<HTMLDetailsElement>('.canonical-evidence-disclosure')!;
    await act(async () => { disclosure.open = true; disclosure.dispatchEvent(new Event('toggle')); });
    const citationLink = [...container.querySelectorAll<HTMLAnchorElement>('.canonical-evidence-content a')]
      .find(link => link.textContent?.includes('event-18'))!;
    expect(citationLink.getAttribute('href')).toContain('#canonical-event-18');
    const target = new URL(citationLink.href);
    await act(async () => {
      window.history.replaceState(null, '', `${target.pathname}${target.search}${target.hash}`);
      window.dispatchEvent(new PopStateEvent('popstate'));
    });
    expect((document.activeElement as HTMLElement).id).toBe('canonical-event-18');
  } finally {
    await act(async () => root.unmount());
    container.remove();
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  }
});

it('offers retry after required canonical summary failure without substituting demo data', async () => {
  const matchId = '11111111-1111-1111-1111-111111111111';
  let summaryRequests = 0;
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', `/matches/${matchId}?matchId=${matchId}&atSeconds=3309&revision=4`);
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
    const url = new URL(String(input), 'http://localhost');
    if (url.pathname.endsWith('/summary') && ++summaryRequests === 1)
      return {ok: false, status: 503, json: async () => ({status: 503, title: 'Unavailable', code: 'DEPENDENCY_UNAVAILABLE', correlationId: 'test'})};
    const unavailable = {ok: false, status: 503, json: async () => ({status: 503, title: 'Unavailable', code: 'DEPENDENCY_UNAVAILABLE', correlationId: 'test'})};
    if (url.pathname.endsWith('/frames') || url.pathname.endsWith('/key-moments')) return unavailable;
    if (url.pathname.endsWith('/summary')) return {ok: true, json: async () => canonicalSnapshot(matchId, 3309, 19)};
    return {ok: true, json: async () => canonicalPage(matchId, 3309, 19, [])};
  }));
  const container = document.createElement('div');
  document.body.append(container);
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    expect(container.textContent).toContain('Match data service is temporarily unavailable.');
    await act(async () => { container.querySelector<HTMLButtonElement>('#canonical-match-retry')!.click(); });
    expect(container.textContent).toContain('Canonical revision 4');
    expect(summaryRequests).toBe(2);
    expect(document.activeElement?.id).toBe('selected-time');
  } finally {
    await act(async () => root.unmount());
    container.remove();
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  }
});
