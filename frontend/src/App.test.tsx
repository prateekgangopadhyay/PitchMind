import { describe, expect, it } from 'vitest';
import { App } from './App';
import { clock, provenanceLabel } from './api';
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
    if (parsed.pathname.endsWith('/key-moments')) return {ok: false, status: 503, json: async () => ({status: 503, title: 'Unavailable', code: 'CANONICAL_KEY_MOMENTS_UNAVAILABLE', correlationId: 'test'})};
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
    expect(container.textContent).toContain('Simulated EPL-context match');
    expect(container.textContent).toContain('Canonical revision 4');
    expect(container.textContent).toContain('shot');
    expect(container.textContent).toContain('Goal');
    expect(container.textContent).toContain('Score event');
    expect(container.textContent).toContain('Canonical Key Moments are unavailable because no canonical projection is available.');
    expect(observedRequests).toHaveLength(4);
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

it('does not describe an unrelated key-moments 503 as an empty canonical projection', async () => {
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
    if (parsed.pathname.endsWith('/key-moments')) return {ok: false, status: 503, json: async () => ({status: 503, title: 'Service offline', code: 'DEPENDENCY_UNAVAILABLE', correlationId: 'test'})};
    const body = parsed.pathname.endsWith('/summary') ? snapshot
      : parsed.pathname.endsWith('/events') ? {matchId, canonicalRevision: 4, selectedTimeSeconds: 3309, throughSequence: 19, items: [], nextCursor: null, hasMore: false}
        : {matchId, canonicalRevision: 4, selectedTimeSeconds: 3309, throughSequence: 19, items: [], nextCursor: null, hasMore: false};
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

it('shows the latest persisted insight as the explanation for the exact selected time', async () => {
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
    if (parsed.pathname.endsWith('/key-moments')) return {ok: false, status: 503, json: async () => ({status: 503, title: 'Unavailable', code: 'CANONICAL_KEY_MOMENTS_UNAVAILABLE', correlationId: 'test'})};
    const body = parsed.pathname.endsWith('/summary') ? snapshot
      : parsed.pathname.endsWith('/events') ? {matchId, canonicalRevision: 4, selectedTimeSeconds: 3309, throughSequence: 19, items: [], nextCursor: null, hasMore: false}
        : {matchId, canonicalRevision: 4, selectedTimeSeconds: 3309, throughSequence: 19, items: [
          {insightId: 'earlier', matchId, canonicalRevision: 4, selectedTimeSeconds: 3309, throughSequence: 12, detectorVersion: 'press-v1', truthClass: 'derived', summary: 'Earlier saved insight', evidence: [{kind: 'event', id: 'event-12', relation: 'supports'}]},
          {insightId: 'latest', matchId, canonicalRevision: 4, selectedTimeSeconds: 3309, throughSequence: 19, detectorVersion: 'press-v2', truthClass: 'interpretation', summary: 'Persisted explanation at this point.', evidence: [{kind: 'event', id: 'event-19', relation: 'supports'}]}
        ], nextCursor: null, hasMore: false};
    return {ok: true, json: async () => body};
  }));
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    const explanation = container.querySelector<HTMLElement>('.selected-time-explanation');
    expect(explanation?.textContent).toContain('Explanation at 55:09');
    expect(explanation?.textContent).toContain('interpretation');
    expect(explanation?.textContent).toContain('Saved insight through sequence 19');
    expect(explanation?.textContent).toContain('Persisted explanation at this point.');
    expect(explanation?.querySelector('a')?.getAttribute('href'))
      .toBe(`/insights/latest?matchId=${matchId}&atSeconds=3309&revision=4`);
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

it.each([
  {
    name: 'an event with a different match id',
    event: (matchId: string) => ({eventId: 'wrong-match-event', matchId: matchId.replace('11111111', '22222222'), sequence: 18, type: 'shot', clockSeconds: 3300, period: 2, occurredAt: '2026-01-01T00:55:00Z'}),
    insight: undefined
  },
  {
    name: 'an insight with a different match id',
    event: undefined,
    insight: (matchId: string) => ({insightId: 'wrong-match-insight', matchId: matchId.replace('11111111', '22222222'), canonicalRevision: 4, selectedTimeSeconds: 3309, throughSequence: 19, detectorVersion: 'test-v1', truthClass: 'interpretation', summary: 'Wrong-match insight', evidence: []})
  },
  {
    name: 'an insight from a different canonical revision',
    event: undefined,
    insight: (matchId: string) => ({insightId: 'wrong-revision-insight', matchId, canonicalRevision: 3, selectedTimeSeconds: 3309, throughSequence: 19, detectorVersion: 'test-v1', truthClass: 'interpretation', summary: 'Wrong-revision insight', evidence: []})
  },
  {
    name: 'an insight beyond the projection watermark',
    event: undefined,
    insight: (matchId: string) => ({insightId: 'future-watermark-insight', matchId, canonicalRevision: 4, selectedTimeSeconds: 3309, throughSequence: 20, detectorVersion: 'test-v1', truthClass: 'interpretation', summary: 'Beyond-watermark insight', evidence: []})
  },
  {
    name: 'an insight with a null cutoff',
    event: undefined,
    insight: (matchId: string) => ({insightId: 'null-cutoff-insight', matchId, canonicalRevision: 4, selectedTimeSeconds: 3309, throughSequence: null as unknown as number, detectorVersion: 'test-v1', truthClass: 'interpretation', summary: 'Null-cutoff insight', evidence: []})
  },
  {
    name: 'an insight with a string cutoff',
    event: undefined,
    insight: (matchId: string) => ({insightId: 'string-cutoff-insight', matchId, canonicalRevision: 4, selectedTimeSeconds: 3309, throughSequence: '19' as unknown as number, detectorVersion: 'test-v1', truthClass: 'interpretation', summary: 'String-cutoff insight', evidence: []})
  },
  {
    name: 'an insight with a fractional cutoff',
    event: undefined,
    insight: (matchId: string) => ({insightId: 'fractional-cutoff-insight', matchId, canonicalRevision: 4, selectedTimeSeconds: 3309, throughSequence: 19.5, detectorVersion: 'test-v1', truthClass: 'interpretation', summary: 'Fractional-cutoff insight', evidence: []})
  },
  {
    name: 'all page watermarks null',
    event: undefined,
    insight: undefined,
    pageWatermarks: {snapshot: null as unknown as number, events: null as unknown as number, insights: null as unknown as number}
  },
  {
    name: 'a null snapshot watermark',
    event: undefined,
    insight: undefined,
    pageWatermarks: {snapshot: null as unknown as number, events: 19, insights: 19}
  },
  {
    name: 'a null event-page watermark',
    event: undefined,
    insight: undefined,
    pageWatermarks: {snapshot: 19, events: null as unknown as number, insights: 19}
  },
  {
    name: 'a null insight-page watermark',
    event: undefined,
    insight: undefined,
    pageWatermarks: {snapshot: 19, events: 19, insights: null as unknown as number}
  },
  {
    name: 'an event with a null sequence',
    event: (matchId: string) => ({eventId: 'invalid-event-sequence', matchId, sequence: null as unknown as number, type: 'shot', clockSeconds: 3300, period: 2, occurredAt: '2026-01-01T00:55:00Z'}),
    insight: undefined
  },
  {
    name: 'an event with a string sequence',
    event: (matchId: string) => ({eventId: 'invalid-event-sequence', matchId, sequence: '18' as unknown as number, type: 'shot', clockSeconds: 3300, period: 2, occurredAt: '2026-01-01T00:55:00Z'}),
    insight: undefined
  },
  {
    name: 'an event with a fractional sequence',
    event: (matchId: string) => ({eventId: 'invalid-event-sequence', matchId, sequence: 18.5, type: 'shot', clockSeconds: 3300, period: 2, occurredAt: '2026-01-01T00:55:00Z'}),
    insight: undefined
  },
  {
    name: 'an event with a negative sequence',
    event: (matchId: string) => ({eventId: 'invalid-event-sequence', matchId, sequence: -1, type: 'shot', clockSeconds: 3300, period: 2, occurredAt: '2026-01-01T00:55:00Z'}),
    insight: undefined
  },
  {
    name: 'an event with a null clock',
    event: (matchId: string) => ({eventId: 'invalid-event-clock', matchId, sequence: 18, type: 'shot', clockSeconds: null as unknown as number, period: 2, occurredAt: '2026-01-01T00:55:00Z'}),
    insight: undefined
  },
  {
    name: 'an event with a string clock',
    event: (matchId: string) => ({eventId: 'invalid-event-clock', matchId, sequence: 18, type: 'shot', clockSeconds: '3300' as unknown as number, period: 2, occurredAt: '2026-01-01T00:55:00Z'}),
    insight: undefined
  },
  {
    name: 'an event with a fractional clock',
    event: (matchId: string) => ({eventId: 'invalid-event-clock', matchId, sequence: 18, type: 'shot', clockSeconds: 3300.5, period: 2, occurredAt: '2026-01-01T00:55:00Z'}),
    insight: undefined
  },
  {
    name: 'an event with a negative clock',
    event: (matchId: string) => ({eventId: 'invalid-event-clock', matchId, sequence: 18, type: 'shot', clockSeconds: -1, period: 2, occurredAt: '2026-01-01T00:55:00Z'}),
    insight: undefined
  }
])('rejects $name before rendering', async ({event, insight, pageWatermarks}) => {
  const matchId = '11111111-1111-1111-1111-111111111111';
  const watermarks = pageWatermarks ?? {snapshot: 19, events: 19, insights: 19};
  const snapshot = {
    matchId, canonicalRevision: 4, selectedTimeSeconds: 3309, asOfSeconds: 3309, throughSequence: watermarks.snapshot,
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
      : parsed.pathname.endsWith('/events') ? {matchId, canonicalRevision: 4, selectedTimeSeconds: 3309, throughSequence: watermarks.events, items: event ? [event(matchId)] : [], nextCursor: null, hasMore: false}
        : {matchId, canonicalRevision: 4, selectedTimeSeconds: 3309, throughSequence: watermarks.insights, items: insight ? [insight(matchId)] : [], nextCursor: null, hasMore: false};
    return {ok: true, json: async () => body};
  }));
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    expect(container.textContent).toContain('outside the selected match, time, revision, sequence or event cutoff');
    expect(container.textContent).not.toContain('Wrong-match insight');
    expect(container.textContent).not.toContain('Wrong-revision insight');
    expect(container.textContent).not.toContain('Beyond-watermark insight');
    expect(container.textContent).not.toContain('Null-cutoff insight');
    expect(container.textContent).not.toContain('String-cutoff insight');
    expect(container.textContent).not.toContain('Fractional-cutoff insight');
    expect(container.textContent).not.toContain('wrong-match-event');
    expect(container.querySelector('.canonical-events')).toBeNull();
    expect(container.querySelector('.canonical-insights')).toBeNull();
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
    expect(container.textContent).toContain('Simulated EPL-context match');
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
