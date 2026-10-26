import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { TopMatchScoreboardAndSlider } from './TopMatchScoreboardAndSlider';

afterEach(() => {
  vi.unstubAllGlobals();
  window.history.replaceState(null, '', '/');
});

describe('TopMatchScoreboardAndSlider', () => {
  it('renders a neutral data-faithful scoreboard when no match is selected without injecting Arsenal or Manchester City', () => {
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    try {
      act(() => {
        root.render(
          <TopMatchScoreboardAndSlider
            pathname="/home"
            selectedSeconds={0}
            isLiveCandidate={false}
          />
        );
      });

      expect(container.textContent).not.toMatch(/Arsenal|Manchester City/);
      expect(container.textContent).toContain('00:00');
      expect(container.querySelector('#universal-match-slider')).not.toBeNull();
      // Ensure hardcoded stadium backdrop image is not present
      expect(container.querySelector('.stadium-backdrop-image')).toBeNull();
      expect(container.querySelector('img[src*="stadium-bg"]')).toBeNull();
    } finally {
      act(() => root.unmount());
      container.remove();
    }
  });

  it('renders active candidate match data (Manchester United vs Liverpool) with crests, dynamic scores, and current time', async () => {
    const matchId = 'manu-liv-source-candidate';
    const sourceSha256 = 'e'.repeat(64);

    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input), 'http://localhost');
      if (url.pathname.endsWith('/simulation/candidate/status')) {
        return Response.json({
          matchId, isStreaming: true, currentSequence: 10, currentClockValue: 854, period: 1, sourceSha256
        });
      }
      if (url.pathname.endsWith('/events')) {
        return Response.json({
          matchId, sourceSha256, hasMore: false,
          items: [
            {
              eventId: 'e-1', matchId, sequence: 1, clockSeconds: 0, period: 1, eventType: 'KickOff',
              rawPayload: { teamName: 'Manchester United' }
            },
            {
              eventId: 'e-2', matchId, sequence: 2, clockSeconds: 854, period: 1, eventType: 'Goal',
              rawPayload: { teamName: 'Manchester United', playerName: 'Bruno Fernandes' }
            },
            {
              eventId: 'e-3', matchId, sequence: 3, clockSeconds: 1653, period: 1, eventType: 'Goal',
              rawPayload: { teamName: 'Liverpool', playerName: 'Mohamed Salah' }
            }
          ]
        });
      }
      return Response.json({ status: 404 }, { status: 404 });
    }));

    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    try {
      await act(async () => {
        root.render(
          <TopMatchScoreboardAndSlider
            matchId={matchId}
            selectedSeconds={854}
            pathname="/home"
            isLiveCandidate={true}
          />
        );
        await new Promise(resolve => setTimeout(resolve, 40));
      });

      // Team names and crests
      expect(container.textContent).toContain('Manchester United');
      expect(container.textContent).toContain('Liverpool');
      expect(container.querySelector('img[src="/crests/manchester-united.svg"]')).not.toBeNull();
      expect(container.querySelector('img[src="/crests/liverpool.svg"]')).not.toBeNull();

      // Current time is 854s -> 14:14
      expect(container.textContent).toContain('14:14');

      // Score at 854s: Manchester United 1, Liverpool 0
      const homeScore = container.querySelector('.score-digit.home');
      const awayScore = container.querySelector('.score-digit.away');
      expect(homeScore?.textContent).toBe('1');
      expect(awayScore?.textContent).toBe('0');

      // Slider is mapped to elapsed time
      const slider = container.querySelector<HTMLInputElement>('#universal-match-slider');
      expect(slider).not.toBeNull();
      expect(slider?.value).toBe('854');

      // Quick links exist
      expect(container.querySelector('a[href*="/candidate-live"]')).not.toBeNull();
      expect(container.querySelector('a[href*="/analyst"]')).not.toBeNull();
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('updates location and fires locationchange event when scrubbing the slider', async () => {
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    let eventFired = false;
    const listener = () => { eventFired = true; };
    window.addEventListener('pitchmind:locationchange', listener);

    try {
      await act(async () => {
        root.render(
          <TopMatchScoreboardAndSlider
            matchId="manu-liv-source-candidate"
            selectedSeconds={300}
            pathname="/teams"
            isLiveCandidate={true}
          />
        );
      });

      const slider = container.querySelector<HTMLInputElement>('#universal-match-slider');
      expect(slider).not.toBeNull();

      await act(async () => {
        const nativeInputValueSetter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value')?.set;
        nativeInputValueSetter?.call(slider, '1200');
        slider!.dispatchEvent(new Event('input', { bubbles: true }));
        slider!.dispatchEvent(new Event('change', { bubbles: true }));
      });

      expect(eventFired).toBe(true);
      expect(window.location.search).toContain('atSeconds=1200');
    } finally {
      window.removeEventListener('pitchmind:locationchange', listener);
      await act(async () => root.unmount());
      container.remove();
    }
  });
});
