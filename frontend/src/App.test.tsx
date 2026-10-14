import { describe, expect, it } from 'vitest';
import { App } from './App';
import { clock } from './api';
import { renderToStaticMarkup } from 'react-dom/server';
import { createElement } from 'react';
describe('Match experience', () => {
  it('labels simulation while loading without inventing score or evidence', () => {
    const markup = renderToStaticMarkup(createElement(App));
    expect(markup).toContain('Simulated matches');
    expect(markup).toContain('Skip to main content');
    expect(markup).toContain('id="main-content"');
    expect(markup).toContain('Finding a simulated match');
    expect(markup).toContain('Primary navigation');
    expect(markup).toContain('How is this match being played?');
    expect(markup).not.toContain('Arsenal');
  });
  it('formats selected time without losing seconds at period boundaries', () => {
    expect(clock(0)).toBe('00:00');
    expect(clock(2700)).toBe('45:00');
    expect(clock(3309)).toBe('55:09');
    expect(clock(5400)).toBe('90:00');
  });
});

import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { vi } from 'vitest';

it('keeps section navigation available when match data cannot be loaded', async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  const fetchMock = vi.fn(async () => { throw new Error('service unavailable'); });
  vi.stubGlobal('fetch', fetchMock);
  const container = document.createElement('div');
  document.body.appendChild(container);
  let root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    expect(container.textContent).toContain('Match data can’t be loaded right now');
    expect(container.textContent).not.toContain('service unavailable');
    const teamsLink = [...container.querySelectorAll('nav a')].find(link => link.textContent === 'Teams')!;
    await act(async () => { teamsLink.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true })); });
    expect(window.location.hash).toBe('#/teams');
    expect(container.querySelector('nav a[aria-current="page"]')?.textContent).toBe('Teams');
    expect(document.activeElement).toBe(container.querySelector('h1'));
    expect(container.textContent).toContain('Match data is not available yet');
    const skipLink = container.querySelector<HTMLAnchorElement>('.skip-link')!;
    await act(async () => { skipLink.click(); });
    expect(window.location.hash).toBe('#/teams');
    expect(document.activeElement?.id).toBe('main-content');
    expect(container.querySelector('nav a[aria-current="page"]')?.textContent).toBe('Teams');
    await act(async () => { root.unmount(); });
    root = createRoot(container);
    await act(async () => { root.render(createElement(App)); });
    expect(window.location.hash).toBe('#/teams');
    expect(container.querySelector('nav a[aria-current="page"]')?.textContent).toBe('Teams');
    const matchLink = [...container.querySelectorAll('nav a')].find(link => link.textContent === 'Match')!;
    await act(async () => { matchLink.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true })); });
    const retry = [...container.querySelectorAll('button')].find(button => button.textContent === 'Try again')!;
    await act(async () => { retry.click(); });
    expect(fetchMock.mock.calls.length).toBeGreaterThanOrEqual(2);
  } finally {
    await act(async () => root.unmount());
    container.remove();
    window.history.pushState({}, '', '/');
    vi.unstubAllGlobals();
  }
});

it('keeps an unknown route visible and keyboard-recoverable instead of presenting it as Match', async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  vi.stubGlobal('fetch', vi.fn(async () => { throw new Error('service unavailable'); }));
  window.history.pushState({}, '', '#/old-match-route');
  const container = document.createElement('div');
  document.body.appendChild(container);
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    expect(container.querySelector('h1')?.textContent).toBe('This page isn’t available');
    expect(container.textContent).toContain('This PitchMind page could not be found');
    expect(container.textContent).not.toContain('How is this match being played?');
    expect(container.querySelector('nav a[aria-current="page"]')).toBeNull();
    const homeLink = [...container.querySelectorAll('a')].find(link => link.textContent === 'Go to Home')!;
    await act(async () => { homeLink.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true })); });
    expect(window.location.hash).toBe('#/home');
    expect(container.querySelector('nav a[aria-current="page"]')?.textContent).toBe('Home');
    expect(document.activeElement).toBe(container.querySelector('h1'));

    window.history.pushState({}, '', '#/old-match-route');
    await act(async () => { window.dispatchEvent(new PopStateEvent('popstate')); });
    expect(container.querySelector('h1')?.textContent).toBe('This page isn’t available');
    expect(container.querySelector('nav a[aria-current="page"]')).toBeNull();

    window.history.pushState({}, '', '#/analyst?matchId=fixture');
    await act(async () => { window.dispatchEvent(new PopStateEvent('popstate')); });
    expect(container.querySelector('h1')?.textContent).toBe('What do you want to understand?');
    expect(container.querySelector('nav a[aria-current="page"]')?.textContent).toBe('Analyst');
  } finally {
    await act(async () => root.unmount());
    container.remove();
    window.history.pushState({}, '', '/');
    vi.unstubAllGlobals();
  }
});

it('retries the failed evidence or analyst action without reloading fixtures', async () => {
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  const fixture = {matchId: 'fixture', version: 'v1', seed: 42, durationSeconds: 5400, home: {id: 'h', name: 'Harbour FC', formation: '4-2-3-1', players: []}, away: {id: 'a', name: 'Ridge Athletic', formation: '4-2-3-1', players: []}};
  const insight = {id: 'press', matchId: 'fixture', clockSeconds: 0, title: 'Supported sequence', summary: 'Observed actions', classification: 'derived', detectorVersion: '1', eventIds: [], frameIds: []};
  let evidenceCalls = 0;
  let analystCalls = 0;
  let fixtureCalls = 0;
  const response = (body: unknown, ok = true) => ({ok, status: ok ? 200 : 503, headers: {get: () => null}, json: async () => body});
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const url = String(input);
    if (url.endsWith('/matches')) { fixtureCalls++; return response([fixture]); }
    if (url.includes('/state?')) return response({matchId: 'fixture', clockSeconds: 0, period: 1, status: 'Simulated replay', homeScore: 0, awayScore: 0, provenance: 'synthetic', frame: {id: 'frame', clockSeconds: 0, ballX: 52.5, ballY: 34, coordinateConvention: 'fixture', players: []}, lastSequence: 0});
    if (url.includes('/events?')) return response([]);
    if (url.includes('/insights?')) return response([insight]);
    if (url.includes('/evidence/')) {
      evidenceCalls++;
      return evidenceCalls === 1 ? response({title: 'Unavailable', code: 'MATCH_DATA_UNAVAILABLE'}, false) : response({insight, events: [], frames: [], coordinateConvention: 'fixture', conditions: ['Observed event sequence'], limitations: []});
    }
    if (url.endsWith('/analyst')) {
      analystCalls++;
      return analystCalls === 1 ? response({title: 'Unavailable', code: 'MATCH_DATA_UNAVAILABLE'}, false) : response({insightId: 'press', clockSeconds: 0, audience: 'fan', source: 'deterministic fallback', text: 'Explanation from the selected evidence.', eventIds: [], limitations: []});
    }
    throw new Error(`Unexpected request: ${url}`);
  });
  vi.stubGlobal('fetch', fetchMock);
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    const evidenceButton = [...container.querySelectorAll('button')].find(button => button.textContent?.includes('Show me why'))!;
    await act(async () => { evidenceButton.click(); });
    expect(container.textContent).toContain('This part of the match isn’t available right now.');
    await act(async () => { [...container.querySelectorAll('button')].find(button => button.textContent === 'Try again')!.click(); });
    expect(evidenceCalls).toBe(2);
    expect(container.textContent).toContain('Observed event sequence');
    const analystButton = [...container.querySelectorAll('button')].find(button => button.textContent?.includes('Explain this moment'))!;
    await act(async () => { analystButton.click(); });
    expect(container.textContent).toContain('This part of the match isn’t available right now.');
    await act(async () => { [...container.querySelectorAll('button')].find(button => button.textContent === 'Try again')!.click(); });
    expect(analystCalls).toBe(2);
    expect(container.textContent).toContain('Explanation from the selected evidence.');
    expect(fixtureCalls).toBe(1);
  } finally {
    await act(async () => root.unmount());
    window.history.pushState({}, '', '/');
    vi.unstubAllGlobals();
  }
});

it('loads API state and clears historical insight when replay moves before evidence', async () => {
  const fixture = {matchId: 'fixture', version: 'v1', seed: 42, durationSeconds: 5400, home: {id: 'h', name: 'Harbour FC', formation: '4-2-3-1', players: []}, away: {id: 'a', name: 'Ridge Athletic', formation: '4-2-3-1', players: []}};
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
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
  }
});
