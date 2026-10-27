import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { TacticalPitchView } from './TacticalPitchView';
import { MatchSelectionPage } from './MatchContextPages';

afterEach(() => {
  vi.unstubAllGlobals();
  window.history.replaceState(null, '', '/');
});

describe('Match Cinema Fullscreen & Tactical Pitch Enhancement', () => {
  it('renders tactical passes on the pitch with directional vectors and toggle control', async () => {
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    const testPlayers = [
      { id: 'p1', teamId: 'Manchester United', name: 'Bruno Fernandes', jerseyNumber: 8, x: 50, y: 35 },
      { id: 'p2', teamId: 'Manchester United', name: 'Marcus Rashford', jerseyNumber: 10, x: 75, y: 25 },
      { id: 'p3', teamId: 'Liverpool', name: 'Virgil van Dijk', jerseyNumber: 4, x: 68, y: 32 }
    ];

    const testEvents = [
      {
        type: 'Pass',
        sequence: 12,
        clockSeconds: 240,
        teamName: 'Manchester United',
        playerName: 'Bruno Fernandes',
        receiverName: 'Marcus Rashford',
        pitchX: 50,
        pitchY: 35,
        endX: 75,
        endY: 25,
        action: 'Progressive Key Pass'
      }
    ];

    try {
      await act(async () => {
        root.render(
          <TacticalPitchView
            players={testPlayers}
            homeTeam={{ id: 'Manchester United', name: 'Manchester United', activePlayers: 2 }}
            awayTeam={{ id: 'Liverpool', name: 'Liverpool', activePlayers: 1 }}
            seconds={245}
            availableEvents={testEvents}
          />
        );
      });

      // Passes layer is rendered on the pitch
      const passLayer = container.querySelector('.tactical-passes-layer');
      expect(passLayer).not.toBeNull();
      const passVectors = container.querySelectorAll('.tactical-pass-vector');
      expect(passVectors.length).toBeGreaterThanOrEqual(1);
      expect(container.textContent).toContain('Fernandes → Rashford');

      // 3D Player bubbles with shadows and kit discs
      const homePlayer = container.querySelector('.tactical-player.home-team');
      expect(homePlayer).not.toBeNull();
      expect(homePlayer?.querySelector('ellipse')).not.toBeNull(); // Turf ground contact shadow
      expect(homePlayer?.querySelector('circle[fill*="kit-gradient"]')).not.toBeNull(); // 3D kit gradient
      expect(homePlayer?.textContent).toContain('8'); // Jersey number

      // Toggle Passes button
      const togglePassBtn = Array.from(container.querySelectorAll('.pitch-tool-btn')).find(b =>
        b.textContent?.includes('Passes')
      );
      expect(togglePassBtn).toBeDefined();

      // Click to hide passes
      await act(async () => {
        togglePassBtn?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      });
      expect(container.querySelector('.tactical-passes-layer')).toBeNull();

      // Click again to show passes
      await act(async () => {
        togglePassBtn?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      });
      expect(container.querySelector('.tactical-passes-layer')).not.toBeNull();
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('displays the professional tooltip HUD with player photo avatar when hovering a player', async () => {
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    const testPlayers = [
      { id: 'p1', teamId: 'Manchester United', name: 'Bruno Fernandes', jerseyNumber: 8, x: 62, y: 34 },
      { id: 'p2', teamId: 'Liverpool', name: 'Mohamed Salah', jerseyNumber: 11, x: 72, y: 18 }
    ];

    try {
      await act(async () => {
        root.render(
          <TacticalPitchView
            players={testPlayers}
            homeTeam={{ id: 'Manchester United', name: 'Manchester United', activePlayers: 1 }}
            awayTeam={{ id: 'Liverpool', name: 'Liverpool', activePlayers: 1 }}
            seconds={150}
          />
        );
      });

      // Initially no tooltip card is open
      expect(container.querySelector('.tactical-player-tooltip-card')).toBeNull();

      // Hover on Bruno Fernandes using mouseover / focus
      const brunoGroup = container.querySelector('.tactical-player.home-team');
      expect(brunoGroup).not.toBeNull();

      await act(async () => {
        brunoGroup?.dispatchEvent(new MouseEvent('mouseover', { bubbles: true }));
      });

      // Tooltip card with player profile and avatar is displayed
      const tooltip = container.querySelector('.tactical-player-tooltip-card');
      expect(tooltip).not.toBeNull();
      expect(tooltip?.textContent).toContain('Bruno Fernandes');
      expect(tooltip?.textContent).toContain('#8');

      // Avatar image with profile photo is present
      const avatarImg = tooltip?.querySelector('.tooltip-player-avatar') as HTMLImageElement | null;
      expect(avatarImg).not.toBeNull();
      expect(avatarImg?.src).toContain('bruno-fernandes.jpg');

      // Mouse leave closes tooltip
      await act(async () => {
        brunoGroup?.dispatchEvent(new MouseEvent('mouseout', { bubbles: true }));
      });
      expect(container.querySelector('.tactical-player-tooltip-card')).toBeNull();
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('enters 100% full screen cinema view in MatchSelectionPage and exits on Escape key', async () => {
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    const matchId = 'b2c3d4e5-f6a7-8901-bcde-f12345678901';
    const sourceSha256 = 'c'.repeat(64);

    const candidateEvents = [
      {
        eventId: 'evt-1', matchId, sequence: 1, clockSeconds: 0, period: 1, eventType: 'KickOff',
        rawPayload: {
          teamName: 'Manchester United',
          playerPositions: [
            { playerName: 'Bruno Fernandes', teamName: 'Manchester United', jerseyNumber: 8, x: 50, y: 50 },
            { playerName: 'Mohamed Salah', teamName: 'Liverpool', jerseyNumber: 11, x: 60, y: 40 }
          ]
        }
      },
      {
        eventId: 'evt-2', matchId, sequence: 2, clockSeconds: 30, period: 1, eventType: 'Pass',
        rawPayload: {
          teamName: 'Manchester United',
          playerName: 'Bruno Fernandes',
          receiverName: 'Marcus Rashford',
          pitchX: 40,
          pitchY: 30,
          endX: 60,
          endY: 45
        }
      }
    ];

    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input), 'http://localhost');
      if (url.pathname.endsWith('/simulation/candidate/status')) {
        return Response.json({
          matchId, isStreaming: true, currentSequence: 2, currentClockValue: 30, period: 1, sourceSha256
        });
      }
      if (url.pathname.endsWith('/events')) {
        return Response.json({ matchId, sourceSha256, items: candidateEvents, hasMore: false });
      }
      return Response.json({ items: [] });
    }));

    try {
      await act(async () => {
        root.render(<MatchSelectionPage route={{ kind: 'matches', matchId, replay: false }} matchId={matchId} seconds={30} />);
      });

      // Find Cinema view toggle button
      const cinemaToggleBtn = Array.from(container.querySelectorAll('button')).find(
        b => b.textContent?.includes('Cinema View')
      );
      expect(cinemaToggleBtn).toBeDefined();

      // Click to enter 100% full screen cinema mode
      await act(async () => {
        cinemaToggleBtn?.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      });

      // Verify fullscreen cinema classes and broadcast header
      const pitchSection = container.querySelector('.match-screen-pitch-col');
      expect(pitchSection?.classList.contains('is-cinema-fullscreen')).toBe(true);

      const cinemaHeader = container.querySelector('.cinema-fullscreen-header');
      expect(cinemaHeader).not.toBeNull();
      expect(cinemaHeader?.textContent).toContain('100% FULL SCREEN');
      expect(cinemaHeader?.textContent).toContain('Manchester United vs Liverpool');

      // Press Escape key to exit fullscreen cinema mode
      await act(async () => {
        window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
      });

      // Fullscreen cinema mode exited
      expect(pitchSection?.classList.contains('is-cinema-fullscreen')).toBe(false);
      expect(container.querySelector('.cinema-fullscreen-header')).toBeNull();
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });
});
