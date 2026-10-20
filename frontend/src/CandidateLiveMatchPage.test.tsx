import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, expect, it, vi } from 'vitest';
import { CandidateLiveMatchPage } from './CandidateLiveMatchPage';

afterEach(() => {
  vi.unstubAllGlobals();
  window.history.replaceState(null, '', '/');
});

it('loads both teams from the candidate PostgreSQL backfill and labels their positions as unverified', async () => {
  const matchId = 'b2c3d4e5-f6a7-8901-bcde-f12345678901';
  const sourceSha256 = 'a'.repeat(64);
  const status = { matchId, isStreaming: true, currentSequence: 1, currentClockValue: 0,
    period: 1, sourceSha256, sourceStatus: 'candidate_not_canonical', coordinateStatus: 'unverified', playerMetricsStatus: 'unvalidated' };
  const event = { eventId: 'source-event-1', matchId, sequence: 1, clockSeconds: 0, period: 1,
    eventType: 'KickOff', rawPayload: { ballX: 50, ballY: 50, playerPositions: [
      { playerName: 'Home keeper', teamName: 'Home FC', teamId: 'home', jerseyNumber: 1, x: 5, y: 50, speed: 0 },
      { playerName: 'Away forward', teamName: 'Away FC', teamId: 'away', jerseyNumber: 9, x: 95, y: 50, speed: 0 }
    ] } };
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', '/candidate-live');
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
    const url = new URL(String(input), 'http://localhost');
    if (url.pathname.endsWith('/simulation/candidate/status')) return { ok: true, json: async () => status };
    if (url.pathname.endsWith('/events')) return { ok: true, json: async () => ({ matchId, sourceSha256, items: [event], hasMore: false }) };
    return { ok: false, status: 503, json: async () => ({}) };
  }));
  const container = document.createElement('div');
  document.body.append(container);
  const root = createRoot(container);
  try {
    await act(async () => { root.render(<CandidateLiveMatchPage />); });
    expect(container.textContent).toContain('Home FC');
    expect(container.textContent).toContain('Away FC');
    expect(container.textContent).toContain('Home keeper');
    expect(container.textContent).toContain('Away forward');
    expect(container.textContent).toContain('source-event-1');
    expect(container.textContent).toContain('unverified');
    expect(container.querySelector('.candidate-source-scoreboard')?.getAttribute('aria-label')).toContain('Home FC 0, Away FC 0');
    expect([...container.querySelectorAll('.source-score-team strong')].map(node => node.textContent)).toEqual(['0', '0']);
    expect(container.querySelector('.candidate-pitch')?.getAttribute('aria-label')).toContain('orientation unverified');
    expect(container.querySelector('.candidate-ball-marker')).not.toBeNull();
    expect(Number(container.querySelector('.candidate-player.home')?.getAttribute('cx'))).toBeCloseTo(7.7);
  } finally {
    await act(async () => root.unmount());
    container.remove();
  }
});
