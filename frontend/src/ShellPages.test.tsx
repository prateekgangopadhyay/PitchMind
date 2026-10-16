import { describe, expect, it, vi } from 'vitest';
import { createElement, act } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { createRoot } from 'react-dom/client';
import { RoutePage } from './ShellPages';

describe('welcome information architecture', () => {
  it('leads to match availability without implying a fixture is already available in static markup', () => {
    const markup = renderToStaticMarkup(createElement(RoutePage, {
      route: { kind: 'welcome' },
      search: ''
    }));

    expect(markup).toContain('Find a match and inspect the evidence behind each view.');
    expect(markup).toContain('Check match availability');
    expect(markup).toContain('href="/home"');
    expect(markup).not.toContain('simulated match');
    expect(markup).not.toContain('available matches');
  });

  it('renders simulated login screen when simulation auth preview is provided', async () => {
    const previewData = {
      headline: 'Understand the Premier League beyond the scoreline',
      description: 'Evidence-grounded tactical intelligence.',
      featuredMatch: {
        matchId: 'sim-match-arsenal-mancity',
        homeTeam: 'Arsenal',
        awayTeam: 'Man City',
        homeScore: 2,
        awayScore: 1,
        clock: '67:24',
        status: 'Live Match',
        tacticalSnippet: 'Arsenal high press in Zone 14',
        pitchSnippet: {
          pressureZone: 'Zone 14',
          regainsCount: 8,
          players: [
            { id: 'p1', x: 62, y: 34, team: 'home' as const, number: 7, name: 'Saka' },
            { id: 'p2', x: 42, y: 49, team: 'away' as const, number: 16, name: 'Rodri' }
          ]
        }
      },
      valueBadges: [
        { title: 'Zero Speculative Stats', detail: 'Verified telemetry.' }
      ],
      trustCertifications: [
        { title: 'SOC-2 Type II Certified', detail: 'Controls verified.' }
      ]
    };

    vi.stubGlobal('fetch', vi.fn(async (url: string) => {
      if (url.includes('simulation/auth-preview')) {
        return { ok: true, json: async () => previewData };
      }
      return { ok: true, json: async () => ({}) };
    }));

    const container = document.createElement('div');
    const root = createRoot(container);
    try {
      await act(async () => {
        root.render(createElement(RoutePage, { route: { kind: 'welcome' }, search: '' }));
      });
      // Verify login form elements
      expect(container.querySelector('input#welcome-email')).not.toBeNull();
      expect(container.querySelector('input#welcome-password')).not.toBeNull();
      expect(container.textContent).toContain('Sign in to PitchMind');
      expect(container.textContent).toContain('Sign in with Microsoft Entra ID');
      expect(container.textContent).toContain('Check match availability');
    } finally {
      await act(async () => root.unmount());
      vi.unstubAllGlobals();
    }
  });

  it('renders teams tactical profile when simulation data is fetched', async () => {
    const teamData = {
      id: 'arsenal',
      name: 'Arsenal',
      shortName: 'ARS',
      formation: '4-3-3',
      leagueRank: 2,
      points: 63,
      form: ['W', 'W', 'D', 'W', 'L'],
      manager: 'Mikel Arteta',
      avgPossession: 61.4,
      xGPer90: 2.12,
      xGAPer90: 0.84,
      ppda: 8.4,
      summary: 'High-pressing territorial dominance.',
      zonesOfControl: {
        dominantSide: 'Right Flank',
        defensiveLineHeight: '51.4m',
        players: [{ number: 7, name: 'Saka', role: 'RW', x: 72, y: 18 }],
        tacticalZones: [{ name: 'Right Flank Dominance', x: 50, y: 0, width: 55, height: 35, intensity: 'High' }]
      },
      progressionRoutes: { left: 24, center: 18, right: 58, description: 'Right side focus' },
      keyMetrics: [{ name: 'PPDA', value: '8.4', leagueAvg: '11.2', percentile: 96, status: 'Elite' }],
      pressingStats: { highTurnovers: 12, shotsFromTurnovers: 4, defensiveDistance: '51.4m', regainsAttackingThird: 8 },
      keyPlayers: [{ id: 'saka', name: 'Bukayo Saka', number: 7, role: 'Right Winger', rating: 8.7, stat1: '12 Goals', stat2: '3.1 KP' }],
      teamComparison: { opponent: 'Man City', metrics: [{ label: 'Possession', team: 61.4, opponent: 64.8 }] },
      recentMatches: [{ opponent: 'Man City', score: '2–1', result: 'W', date: 'Today', tag: 'High Press' }],
      tacticalInsights: [{ title: 'Right-Side Overload', summary: 'White-Saka overlap.', badge: 'Mechanism' }]
    };

    vi.stubGlobal('fetch', vi.fn(async (url: string) => {
      if (url.includes('simulation/teams')) {
        return { ok: true, json: async () => teamData };
      }
      return { ok: true, json: async () => ({}) };
    }));

    const container = document.createElement('div');
    const root = createRoot(container);
    try {
      await act(async () => {
        root.render(createElement(RoutePage, { route: { kind: 'teams', teamId: 'arsenal' }, search: '' }));
      });
      expect(container.textContent).toContain('Team profile');
      expect(container.textContent).toContain('Return to available matches');
    } finally {
      await act(async () => root.unmount());
      vi.unstubAllGlobals();
    }
  });

  it('renders player intelligence when simulation player data is fetched', async () => {
    const playerData = {
      id: 'saka',
      name: 'Bukayo Saka',
      number: 7,
      role: 'Right Winger',
      club: 'Arsenal',
      nationality: 'England',
      age: 23,
      minutesPlayed: 67,
      matchStats: {
        chancesCreated: 2, progressiveCarries: 3, xA: 0.32, duelsWon: 4, passAccuracy: 88, touches: 48, shots: 2, dribblesCompleted: 3
      },
      whyItMatters: ['Isolating full-back in 1v1 situations'],
      touches: [{ x: 68, y: 14, type: 'receive', minute: 4, outcome: 'completed' }],
      progressiveCarries: [{ startX: 58, startY: 16, endX: 74, endY: 18, distance: '16.4m' }],
      chanceCreation: [{ fromX: 88, fromY: 28, toX: 94, toY: 48, recipient: 'Havertz', outcome: 'Shot', xA: 0.24, minute: 53 }],
      receivingZones: [{ zone: 'Right Touchline', pct: 42 }],
      defensiveWork: [{ x: 68, y: 22, action: 'Tackle', minute: 18, outcome: 'Won' }],
      cumulativeXA: [{ minute: 0, value: 0 }, { minute: 53, value: 0.32 }],
      comparison: { opponentPlayer: 'Phil Foden', metrics: [{ label: 'Key Passes', player: 3.1, opponent: 2.4 }] },
      recentImpact: [{ clock: '53:12', title: 'Cutback Assist', summary: 'Low cutback.' }],
      analystAssessment: 'Exceptional performance in tactical execution.'
    };

    vi.stubGlobal('fetch', vi.fn(async (url: string) => {
      if (url.includes('simulation/players')) {
        return { ok: true, json: async () => playerData };
      }
      return { ok: true, json: async () => ({}) };
    }));

    const container = document.createElement('div');
    const root = createRoot(container);
    try {
      await act(async () => {
        root.render(createElement(RoutePage, { route: { kind: 'players', playerId: 'saka' }, search: '' }));
      });
      expect(container.textContent).toContain('Player profile');
      expect(container.textContent).toContain('Return to available matches');
    } finally {
      await act(async () => root.unmount());
      vi.unstubAllGlobals();
    }
  });

  it('renders insights tactical explanation and analyst copilot without forbidden words', async () => {
    const insightDetail = {
      id: 'arsenal-press-city',
      matchId: 'sim-match',
      truthClass: 'Evidence-Supported Interpretation',
      confidence: 'High Confidence',
      clock: '67:24',
      selectedTimeSeconds: 4044,
      title: "Arsenal's press is reducing City's central progression",
      subtitle: 'Mid-to-high pressing structure.',
      pitchDiagram: {
        title: 'CENTRAL PROGRESSION UNDER PRESSURE',
        description: 'Compressing central passing lanes.',
        players: [{ id: 'p1', name: 'Rice', number: 41, team: 'home' as const, x: 50, y: 52, role: 'Presser' }],
        passingLanes: [{ fromX: 32, fromY: 42, toX: 42, toY: 48, status: 'blocked' as const, label: 'Blocked' }],
        pressureZones: [{ x: 35, y: 32, width: 30, height: 36, label: 'TRAP' }]
      },
      evidenceAtAGlance: [{ label: 'Line Breaks', value: '-68%', comparison: 'vs avg', trend: 'down' as const }],
      developingChart: [{ minute: 10, pressIntensity: 45, centralProgression: 72 }],
      causalChain: [{ step: 1, phase: 'Trigger', action: 'Rice steps up', time: '52:48' }],
      keyMoment: { clock: '53:12', title: 'Turnover Sequence', summary: 'Ball regain', videoThumbnail: 'thumb.jpg', eventIds: ['EV-1'] },
      secondaryInsights: [{ id: 's1', category: 'Pressing', title: 'Cover shadow', summary: 'Denied options', badge: 'Spatial' }]
    };

    vi.stubGlobal('fetch', vi.fn(async (url: string) => {
      if (url.includes('simulation/insights')) {
        return { ok: true, json: async () => insightDetail };
      }
      return { ok: true, json: async () => ({}) };
    }));

    const container = document.createElement('div');
    const root = createRoot(container);
    try {
      await act(async () => {
        root.render(createElement(RoutePage, { route: { kind: 'insights' }, search: '' }));
      });
      const text = container.textContent ?? '';
      expect(text).toContain('Evidence at this moment');
      expect(text).not.toMatch(/\b(canonical|API|Foundry|payload|contract)\b/i);
    } finally {
      await act(async () => root.unmount());
      vi.unstubAllGlobals();
    }
  });
});
