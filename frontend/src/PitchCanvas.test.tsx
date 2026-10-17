import { describe, expect, it } from 'vitest';
import { createElement, act } from 'react';
import { createRoot } from 'react-dom/client';
import { PitchCanvas, TURF_PALETTES, type PitchMatchEvent, type PlayerPosition } from './components/PitchCanvas';

describe('PitchCanvas visual themes and tactical features', () => {
  const samplePlayers: PlayerPosition[] = [
    {
      playerName: 'Declan Rice',
      teamName: 'Arsenal',
      teamId: 'd0000001-0000-0000-0000-000000000001',
      jerseyNumber: 41,
      position: 'CM',
      x: 60,
      y: 37,
      speed: 28.1,
      distanceCovered: 10.6,
      heartRate: 176,
      passAccuracy: 89,
      heatIndex: 0.94,
    },
    {
      playerName: 'Bukayo Saka',
      teamName: 'Arsenal',
      teamId: 'd0000001-0000-0000-0000-000000000001',
      jerseyNumber: 7,
      position: 'RW',
      x: 74,
      y: 82,
      speed: 31.4,
      distanceCovered: 9.3,
      heartRate: 174,
      passAccuracy: 84,
      heatIndex: 0.96,
    },
    {
      playerName: 'Rodri',
      teamName: 'Manchester City',
      teamId: 'd0000002-0000-0000-0000-000000000002',
      jerseyNumber: 16,
      position: 'DM',
      x: 58,
      y: 50,
      speed: 21.4,
      distanceCovered: 10.0,
      heartRate: 164,
      passAccuracy: 93,
      heatIndex: 0.87,
    }
  ];

  const sampleEvents: PitchMatchEvent[] = [
    {
      eventId: 'p-1',
      matchId: 'm-1',
      sequence: 1,
      eventType: 'Pass',
      clockSeconds: 3960,
      period: 2,
      teamId: 'd0000001-0000-0000-0000-000000000001',
      playerName: 'Gabriel',
      receiverName: 'Saliba',
      pitchX: 22,
      pitchY: 38,
      endX: 22,
      endY: 62,
    },
    {
      eventId: 'p-2',
      matchId: 'm-1',
      sequence: 2,
      eventType: 'Pass',
      clockSeconds: 3970,
      period: 2,
      teamId: 'd0000001-0000-0000-0000-000000000001',
      playerName: 'Saliba',
      receiverName: 'White',
      pitchX: 22,
      pitchY: 62,
      endX: 28,
      endY: 82,
    },
    {
      eventId: 'p-3',
      matchId: 'm-1',
      sequence: 3,
      eventType: 'Pass',
      clockSeconds: 3980,
      period: 2,
      teamId: 'd0000001-0000-0000-0000-000000000001',
      playerName: 'White',
      receiverName: 'Partey',
      pitchX: 28,
      pitchY: 82,
      endX: 42,
      endY: 50,
    },
    {
      eventId: 'p-4',
      matchId: 'm-1',
      sequence: 4,
      eventType: 'Pass',
      clockSeconds: 3990,
      period: 2,
      teamId: 'd0000001-0000-0000-0000-000000000001',
      playerName: 'Partey',
      receiverName: 'Timber',
      pitchX: 42,
      pitchY: 50,
      endX: 28,
      endY: 18,
    },
    {
      eventId: 'p-5',
      matchId: 'm-1',
      sequence: 5,
      eventType: 'ProgressivePass',
      clockSeconds: 4000,
      period: 2,
      teamId: 'd0000001-0000-0000-0000-000000000001',
      playerName: 'Timber',
      receiverName: 'Rice',
      pitchX: 28,
      pitchY: 18,
      endX: 50,
      endY: 38,
    },
    {
      eventId: 'p-6',
      matchId: 'm-1',
      sequence: 6,
      eventType: 'LineBreakingPass',
      clockSeconds: 4010,
      period: 2,
      teamId: 'd0000001-0000-0000-0000-000000000001',
      playerName: 'Rice',
      receiverName: 'Martinelli',
      pitchX: 50,
      pitchY: 38,
      endX: 68,
      endY: 16,
    },
    {
      eventId: 'p-7',
      matchId: 'm-1',
      sequence: 7,
      eventType: 'Pass',
      clockSeconds: 4020,
      period: 2,
      teamId: 'd0000001-0000-0000-0000-000000000001',
      playerName: 'Martinelli',
      receiverName: 'Rice',
      pitchX: 68,
      pitchY: 16,
      endX: 60,
      endY: 37,
    },
    {
      eventId: 'p-8',
      matchId: 'm-1',
      sequence: 8,
      eventType: 'LineBreakingPass',
      clockSeconds: 4028,
      period: 2,
      teamId: 'd0000001-0000-0000-0000-000000000001',
      playerName: 'Rice',
      receiverName: 'Ødegaard',
      pitchX: 60,
      pitchY: 37,
      endX: 66,
      endY: 60,
    },
    {
      eventId: 'p-9',
      matchId: 'm-1',
      sequence: 9,
      eventType: 'ThroughBall',
      clockSeconds: 4036,
      period: 2,
      teamId: 'd0000001-0000-0000-0000-000000000001',
      playerName: 'Ødegaard',
      receiverName: 'Saka',
      pitchX: 66,
      pitchY: 60,
      endX: 74,
      endY: 82,
    },
    {
      eventId: 'p-10',
      matchId: 'm-1',
      sequence: 10,
      eventType: 'Cross',
      clockSeconds: 4040,
      period: 2,
      teamId: 'd0000001-0000-0000-0000-000000000001',
      playerName: 'Saka',
      receiverName: 'Havertz',
      pitchX: 74,
      pitchY: 82,
      endX: 88,
      endY: 50,
      outcome: 'Assist',
    },
    {
      eventId: 'ev-goal',
      matchId: 'm-1',
      sequence: 11,
      eventType: 'Goal',
      clockSeconds: 4044,
      period: 2,
      teamId: 'd0000001-0000-0000-0000-000000000001',
      playerName: 'Havertz',
      receiverName: 'Saka',
      pitchX: 88,
      pitchY: 50,
      endX: 96,
      endY: 50,
      outcome: 'Goal',
    }
  ];

  it('renders hardcoded Dark Turf stripes with no customization buttons', async () => {
    const container = document.createElement('div');
    const root = createRoot(container);
    try {
      await act(async () => {
        root.render(
          createElement(PitchCanvas, {
            events: sampleEvents,
            selectedEventId: 'p-1',
            showZones: true,
            homeTeamName: 'Arsenal',
            awayTeamName: 'Man City',
            playerPositionsOverride: samplePlayers,
          })
        );
      });

      // Verify no turf customization buttons exist
      const turfButtons = Array.from(container.querySelectorAll('button')).filter(b =>
        ['☀️ Light Daylight', '✨ Ultra Bright', '🌿 Emerald', '🌙 Dark Turf', 'Daylight', 'Dark Turf'].some(t => b.textContent?.includes(t))
      );
      expect(turfButtons.length).toBe(0);

      // Verify pitch grass stripes are rendered using hardcoded Dark Turf palette
      const stripesPattern = container.querySelector('#pitch-grass-stripes');
      expect(stripesPattern).not.toBeNull();
      const firstRect = stripesPattern?.querySelector('rect');
      expect(firstRect?.getAttribute('fill')).toBe(TURF_PALETTES.dark.stripeA);
    } finally {
      await act(async () => root.unmount());
    }
  });

  it('renders 18 tactical zones without customization toolbar buttons', async () => {
    const container = document.createElement('div');
    const root = createRoot(container);
    try {
      await act(async () => {
        root.render(
          createElement(PitchCanvas, {
            events: sampleEvents,
            selectedEventId: 'p-1',
            showZones: true,
            homeTeamName: 'Arsenal',
            awayTeamName: 'Man City',
            playerPositionsOverride: samplePlayers,
          })
        );
      });

      // 18 tactical zones layer should be present
      expect(container.querySelector('#tactical-18-zones')).not.toBeNull();
      expect(container.textContent).toContain('14 · ZONE 14');
      expect(container.textContent).toContain('8 · ENGINE ROOM');

      // Verify no region mode toggle buttons exist on the pitch
      const modeButtons = Array.from(container.querySelectorAll('button')).filter(b =>
        ['5 Channels', '3 Thirds', '18 Zones', '✕ Off', 'Sector Guide'].some(t => b.textContent?.includes(t))
      );
      expect(modeButtons.length).toBe(0);
    } finally {
      await act(async () => root.unmount());
    }
  });

  it('renders goal build-up 10-pass HUD banner and progressive pass opacity', async () => {
    const container = document.createElement('div');
    const root = createRoot(container);
    try {
      await act(async () => {
        root.render(
          createElement(PitchCanvas, {
            events: sampleEvents,
            selectedEventId: 'ev-goal',
            showZones: true,
            homeTeamName: 'Arsenal',
            awayTeamName: 'Man City',
            playerPositionsOverride: samplePlayers,
          })
        );
      });

      // Goal 10-pass HUD banner should be rendered
      expect(container.textContent).toContain('GOAL BUILD-UP SEQUENCE');
      expect(container.textContent).toContain('Last 10 passes leading to goal');
      expect(container.textContent).toContain('● Pass #1 (15% faint)');
      expect(container.textContent).toContain('● Pass #10 (Assist 100%)');

      // Check layer contains pass nodes
      const goalLayer = container.querySelector('#goal-10-pass-buildup-layer');
      expect(goalLayer).not.toBeNull();
      expect(container.textContent).toContain('#1');
      expect(container.textContent).toContain('#10');
      expect(container.textContent).toContain('★ ASSIST');
    } finally {
      await act(async () => root.unmount());
    }
  });

  it('renders player numbers and displays observed metrics in tooltip on hover', async () => {
    const container = document.createElement('div');
    const root = createRoot(container);
    try {
      await act(async () => {
        root.render(
          createElement(PitchCanvas, {
            events: sampleEvents,
            selectedEventId: 'p-1',
            showZones: true,
            homeTeamName: 'Arsenal',
            awayTeamName: 'Man City',
            playerPositionsOverride: samplePlayers,
          })
        );
      });

      // Verify player markers exist
      const playerMarkers = container.querySelectorAll('.pitch-player-marker');
      expect(playerMarkers.length).toBe(3);
      expect(container.textContent).toContain('41'); // Rice
      expect(container.textContent).toContain('7');  // Saka
      expect(container.textContent).toContain('16'); // Rodri

      // Hover on Declan Rice
      const riceMarker = playerMarkers[0];
      await act(async () => {
        riceMarker.dispatchEvent(new MouseEvent('mouseover', { bubbles: true }));
        riceMarker.dispatchEvent(new FocusEvent('focus', { bubbles: true }));
        (riceMarker as HTMLElement).focus?.();
      });

      // Tooltip should appear with Rice's live metrics
      const tooltip = container.querySelector('#pitch-player-tooltip');
      expect(tooltip).not.toBeNull();
      expect(tooltip?.textContent).toContain('Declan Rice');
      expect(tooltip?.textContent).toContain('28.1 km/h');
      expect(tooltip?.textContent).toContain('10.60 km');
      expect(tooltip?.textContent).toContain('176 bpm');
      expect(tooltip?.textContent).toContain('89%');
    } finally {
      await act(async () => root.unmount());
    }
  });

  it('renders hardcoded cinema Huge Full Ground with only one fullscreen icon and no other icons', async () => {
    const container = document.createElement('div');
    const root = createRoot(container);
    try {
      await act(async () => {
        root.render(
          createElement(PitchCanvas, {
            events: sampleEvents,
            selectedEventId: 'p-1',
            showZones: true,
            homeTeamName: 'Arsenal',
            awayTeamName: 'Man City',
            playerPositionsOverride: samplePlayers,
          })
        );
      });

      // Verify exactly ONE button exists on the pitch: the fullscreen toggle button
      const allButtons = Array.from(container.querySelectorAll('button'));
      expect(allButtons.length).toBe(1);
      const fsBtn = container.querySelector('#pitch-fullscreen-btn');
      expect(fsBtn).not.toBeNull();
      expect(fsBtn?.getAttribute('aria-pressed')).toBe('false');

      // Toggle fullscreen button
      await act(async () => {
        (fsBtn as HTMLButtonElement).click();
      });
      expect(fsBtn?.getAttribute('aria-pressed')).toBe('true');

      // SVG pitch dimensions: 16:9 cinema viewBox (1600x900)
      const svg = container.querySelector('svg[viewBox="0 0 1600 900"]');
      expect(svg).not.toBeNull();

      // Verify no zoom, scale, or trails buttons exist
      const customizationButtons = Array.from(container.querySelectorAll('button')).filter(b =>
        b.id !== 'pitch-fullscreen-btn'
      );
      expect(customizationButtons.length).toBe(0);
    } finally {
      await act(async () => root.unmount());
    }
  });
});
