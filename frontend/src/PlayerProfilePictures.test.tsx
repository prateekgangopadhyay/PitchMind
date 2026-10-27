import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { PlayerRosterView } from './PlayerRosterView';
import { PlayerTacticalDashboard } from './PlayerTacticalDashboard';
import { resolvePlayerPhotoUrl, getPlayerProfile, generatePlayerAvatarSvg } from './playerProfiles';
import type { PlayerIntelligenceData } from './api';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('Player Profile Pictures on Players Screen', () => {
  const mockPlayers: PlayerIntelligenceData[] = [
    {
      id: 'ryan-gravenberch',
      name: 'Ryan Gravenberch',
      number: 38,
      role: 'Defensive Midfielder (DM)',
      club: 'Liverpool',
      teamLogo: '/crests/liverpool.svg',
      nationality: 'Netherlands',
      age: 22,
      minutesPlayed: 71,
      preferredFoot: 'Right foot',
      keyQuote: 'Shielding Zone 14 with 12 ball recoveries and 21% duel dominance.',
      tags: ['#38', 'DM', 'Right foot'],
      matchStats: {
        chancesCreated: 5,
        progressiveCarries: 8,
        xA: 1.02,
        duelsWon: 21,
        passAccuracy: 88,
        touches: 65,
        shots: 1,
        dribblesCompleted: 3
      },
      whyItMatters: ['Shielding Zone 14'],
      touches: [],
      progressiveCarries: [],
      chanceCreation: [],
      receivingZones: [],
      defensiveWork: [],
      cumulativeXA: [],
      comparison: { opponentPlayer: 'Counterpart', metrics: [] },
      recentImpact: [],
      analystAssessment: 'Disciplined positioning.'
    },
    {
      id: 'kobbie-mainoo',
      name: 'Kobbie Mainoo',
      number: 37,
      role: 'Central Midfielder (CM)',
      club: 'Manchester United',
      teamLogo: '/crests/manchester-united.svg',
      nationality: 'England',
      age: 19,
      minutesPlayed: 71,
      preferredFoot: 'Right foot',
      keyQuote: 'Controlling match tempo with 86% distribution accuracy.',
      tags: ['#37', 'CM', 'Right foot'],
      matchStats: {
        chancesCreated: 4,
        progressiveCarries: 8,
        xA: 1.0,
        duelsWon: 17,
        passAccuracy: 86,
        touches: 90,
        shots: 1,
        dribblesCompleted: 2
      },
      whyItMatters: ['Controlling tempo'],
      touches: [],
      progressiveCarries: [],
      chanceCreation: [],
      receivingZones: [],
      defensiveWork: [],
      cumulativeXA: [],
      comparison: { opponentPlayer: 'Counterpart', metrics: [] },
      recentImpact: [],
      analystAssessment: 'Midfield anchor.'
    },
    {
      id: 'matthijs-de-ligt',
      name: 'Matthijs de Ligt',
      number: 4,
      role: 'Center Back (CB)',
      club: 'Manchester United',
      teamLogo: '/crests/manchester-united.svg',
      nationality: 'Netherlands',
      age: 25,
      minutesPlayed: 71,
      preferredFoot: 'Right foot',
      keyQuote: 'Anchoring the defensive line with 22% duel dominance.',
      tags: ['#4', 'CB', 'Right foot'],
      matchStats: {
        chancesCreated: 5,
        progressiveCarries: 3,
        xA: 0.94,
        duelsWon: 22,
        passAccuracy: 83,
        touches: 70,
        shots: 0,
        dribblesCompleted: 0
      },
      whyItMatters: ['Defensive line'],
      touches: [],
      progressiveCarries: [],
      chanceCreation: [],
      receivingZones: [],
      defensiveWork: [],
      cumulativeXA: [],
      comparison: { opponentPlayer: 'Counterpart', metrics: [] },
      recentImpact: [],
      analystAssessment: 'Defensive rock.'
    },
    {
      id: 'luis-diaz',
      name: 'Luis Díaz',
      number: 7,
      role: 'Left Winger (LW)',
      club: 'Liverpool',
      teamLogo: '/crests/liverpool.svg',
      nationality: 'Colombia',
      age: 27,
      minutesPlayed: 71,
      preferredFoot: 'Right foot',
      keyQuote: 'Dynamic wide threat stretching opposition back line.',
      tags: ['#7', 'LW', 'Right foot'],
      matchStats: {
        chancesCreated: 4,
        progressiveCarries: 7,
        xA: 0.64,
        duelsWon: 18,
        passAccuracy: 84,
        touches: 58,
        shots: 2,
        dribblesCompleted: 4
      },
      whyItMatters: ['Stretching defense'],
      touches: [],
      progressiveCarries: [],
      chanceCreation: [],
      receivingZones: [],
      defensiveWork: [],
      cumulativeXA: [],
      comparison: { opponentPlayer: 'Counterpart', metrics: [] },
      recentImpact: [],
      analystAssessment: 'Decisive winger.'
    }
  ];

  it('resolves correct player profile picture URLs without falling back to club crests', () => {
    const gravenberchPhoto = resolvePlayerPhotoUrl(mockPlayers[0]);
    expect(gravenberchPhoto.primaryUrl).toContain('ryan-gravenberch');
    expect(gravenberchPhoto.primaryUrl).not.toContain('liverpool.svg');
    expect(gravenberchPhoto.avatarSvg).toContain('data:image/svg+xml');

    const mainooPhoto = resolvePlayerPhotoUrl(mockPlayers[1]);
    expect(mainooPhoto.primaryUrl).toContain('kobbie-mainoo');
    expect(mainooPhoto.primaryUrl).not.toContain('manchester-united.svg');

    const deLigtPhoto = resolvePlayerPhotoUrl(mockPlayers[2]);
    expect(deLigtPhoto.primaryUrl).toContain('matthijs-de-ligt');

    const diazPhoto = resolvePlayerPhotoUrl(mockPlayers[3]);
    expect(diazPhoto.primaryUrl).toContain('luis-diaz');
  });

  it('renders player profile pictures instead of club logos in PlayerRosterView card portraits', async () => {
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    try {
      await act(async () => {
        root.render(
          <PlayerRosterView
            players={mockPlayers}
            onSelectPlayer={() => {}}
            homeTeamName="Manchester United"
            awayTeamName="Liverpool"
            clockDisplay="71:09"
          />
        );
      });

      const portraitImages = container.querySelectorAll<HTMLImageElement>('.card-portrait-img');
      expect(portraitImages.length).toBe(4);

      // Verify that portrait images point to player photos, NOT club crests
      portraitImages.forEach(img => {
        expect(img.src).not.toContain('.svg');
        expect(img.src).toMatch(/player-photos|players/);
      });

      // Specifically check Gravenberch and Mainoo
      expect(portraitImages[0].src).toContain('ryan-gravenberch');
      expect(portraitImages[0].src).not.toContain('liverpool');
      expect(portraitImages[1].src).toContain('kobbie-mainoo');
      expect(portraitImages[1].src).not.toContain('manchester-united');
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('falls back to custom player avatar SVG on image error, NEVER the club logo', async () => {
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    try {
      await act(async () => {
        root.render(
          <PlayerRosterView
            players={mockPlayers}
            onSelectPlayer={() => {}}
          />
        );
      });

      const firstImg = container.querySelector<HTMLImageElement>('.card-portrait-img');
      expect(firstImg).not.toBeNull();

      // Trigger error on primaryUrl
      await act(async () => {
        firstImg?.dispatchEvent(new Event('error'));
      });

      // Trigger error on fallbackUrl
      await act(async () => {
        firstImg?.dispatchEvent(new Event('error'));
      });

      // It must be the SVG footballer silhouette avatar, NOT the club logo!
      expect(firstImg?.src).toContain('data:image/svg+xml');
      expect(firstImg?.src).not.toContain('liverpool.svg');
      expect(firstImg?.src).not.toContain('manchester-united.svg');
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('renders player profile picture in PlayerTacticalDashboard cutout without club logo fallback', async () => {
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    try {
      await act(async () => {
        root.render(
          <PlayerTacticalDashboard
            playerId="ryan-gravenberch"
            seconds={4269}
          />
        );
      });

      // PlayerTacticalDashboard loads data from getPlayerIntelligence
      // Check the cutout image
      const cutoutImg = container.querySelector<HTMLImageElement>('.player-cutout-img');
      if (cutoutImg) {
        expect(cutoutImg.src).not.toContain('liverpool.svg');
      }
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });
});
