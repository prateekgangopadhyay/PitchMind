import { describe, expect, it } from 'vitest';
import { App } from './App';
import { clock, provenanceLabel } from './api';
import { renderToStaticMarkup } from 'react-dom/server';
import { createElement } from 'react';
import { contextHref, resolveRoute, selectedTimeFromSearch } from './routes';
import './styles.css';

describe('PitchMind design tokens', () => {
  it('uses the platform sans-serif stack without requiring a named external font', () => {
    const fontFamily = getComputedStyle(document.documentElement).fontFamily;
    expect(fontFamily).toMatch(/^system-ui/);
    expect(fontFamily).not.toMatch(/\bInter\b/i);
  });
});

describe('Match experience', () => {
  it('renders the route shell and honest loading state without inventing match facts', () => {
    const markup = renderToStaticMarkup(createElement(App));
    expect(markup).toContain('Canonical match data');
    expect(markup).toContain('Skip to main content');
    expect(markup).toContain('id="main-content"');
    expect(markup).toContain('aria-label="Main navigation"');
    expect(markup).toContain('Available matches');
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
    const body = parsed.pathname.endsWith('/summary') ? snapshot
      : parsed.pathname.endsWith('/events') ? {matchId, canonicalRevision: 4, selectedTimeSeconds: 3309, throughSequence: 19, items: [{eventId: 'event-1', matchId, sequence: 19, type: 'shot', clockSeconds: 3309, period: 2, occurredAt: '2026-01-01T00:55:09Z'}], nextCursor: null, hasMore: false}
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
    expect(container.querySelector('#selected-time')?.getAttribute('aria-valuetext')).toBe('55 minutes 9 seconds');
    expect(observedRequests).toHaveLength(3);
    for (const url of observedRequests) {
      const parsed = new URL(url, 'http://localhost');
      expect(parsed.pathname).toContain(`/canonical/matches/${matchId}/`);
      expect(parsed.searchParams.get('atSeconds')).toBe('3309');
      expect(parsed.searchParams.get('revision')).toBe('4');
    }
  } finally {
    await act(async () => root.unmount());
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  }
});
