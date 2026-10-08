import { describe, expect, it } from 'vitest';
import { App } from './App';
import { clock } from './api';
import { renderToStaticMarkup } from 'react-dom/server';
import { createElement } from 'react';
describe('Match experience', () => {
  it('labels simulation while loading without inventing score or evidence', () => {
    const markup = renderToStaticMarkup(createElement(App));
    expect(markup).toContain('Simulated match');
    expect(markup).toContain('Loading simulated fixtures');
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
