import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { TeamHeadToHeadAiSection } from './TeamHeadToHeadAiSection';
import type { ContextTeam, ContextEvent } from './MatchContextPages';
import * as api from './api';

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('TeamHeadToHeadAiSection', () => {
  const mockHomeTeam: ContextTeam = {
    id: 'mu-id',
    name: 'Manchester United',
    score: 1,
    activePlayers: 11
  };

  const mockAwayTeam: ContextTeam = {
    id: 'liv-id',
    name: 'Liverpool',
    score: 0,
    activePlayers: 11
  };

  const mockEvents: ContextEvent[] = [
    {
      id: 'ev-1',
      sequence: 1,
      clockSeconds: 120,
      type: 'Pass',
      teamId: 'mu-id',
      teamName: 'Manchester United',
      outcome: 'Successful'
    },
    {
      id: 'ev-2',
      sequence: 2,
      clockSeconds: 300,
      type: 'Tackle',
      teamId: 'mu-id',
      teamName: 'Manchester United',
      outcome: 'Won'
    },
    {
      id: 'ev-3',
      sequence: 3,
      clockSeconds: 650,
      type: 'Shot',
      teamId: 'mu-id',
      teamName: 'Manchester United'
    },
    {
      id: 'ev-4',
      sequence: 4,
      clockSeconds: 800,
      type: 'Pass',
      teamId: 'liv-id',
      teamName: 'Liverpool',
      outcome: 'Successful'
    },
    {
      id: 'ev-5',
      sequence: 5,
      clockSeconds: 900,
      type: 'BallRecovery',
      teamId: 'liv-id',
      teamName: 'Liverpool'
    }
  ];

  it('renders the clash hero header with club names, live score, and live context', async () => {
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    try {
      await act(async () => {
        root.render(
          <TeamHeadToHeadAiSection
            homeTeam={mockHomeTeam}
            awayTeam={mockAwayTeam}
            events={mockEvents}
            seconds={1200}
            matchId="match-1"
          />
        );
      });

      expect(container.textContent).toContain('Manchester United');
      expect(container.textContent).toContain('Liverpool');
      expect(container.textContent).toContain('VS');
      expect(container.querySelector('.home-pillar .h2h-score-badge')?.textContent).toContain('1 Goal');
      expect(container.querySelector('.away-pillar .h2h-score-badge')?.textContent).toContain('0 Goals');

      // Verify club crests are constrained and not enormous
      const homeCrestImg = container.querySelector<HTMLImageElement>('.home-pillar .h2h-club-crest');
      const awayCrestImg = container.querySelector<HTMLImageElement>('.away-pillar .h2h-club-crest');
      expect(homeCrestImg).not.toBeNull();
      expect(awayCrestImg).not.toBeNull();
      expect(homeCrestImg?.style.maxWidth).toBe('42px');
      expect(awayCrestImg?.style.maxWidth).toBe('42px');
      expect(homeCrestImg?.style.maxHeight).toBe('42px');
      expect(awayCrestImg?.style.maxHeight).toBe('42px');
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('does NOT render any hardcoded historical statistics, meeting counts, or fake records', async () => {
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    try {
      await act(async () => {
        root.render(
          <TeamHeadToHeadAiSection
            homeTeam={mockHomeTeam}
            awayTeam={mockAwayTeam}
            events={mockEvents}
            seconds={1200}
            matchId="match-1"
          />
        );
      });

      // No hardcoded historical record cards or fake historical counters
      expect(container.textContent).not.toContain('All-Time Head-to-Head Record');
      expect(container.textContent).not.toContain('Total Competitive Encounters');
      expect(container.textContent).not.toContain('214 Total Competitive Encounters');
      expect(container.textContent).not.toContain('84 Total Competitive Encounters');
      expect(container.textContent).not.toContain('All-Time Goals');
      expect(container.textContent).not.toContain('Clean Sheets');
      expect(container.textContent).not.toContain('Last 5 Competitive Encounters');

      // 3-way historical segmented ratio bar is completely removed
      expect(container.querySelector('.h2h-historical-card')).toBeNull();
      expect(container.querySelector('.h2h-win-ratio-bar')).toBeNull();
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('computes and renders live match telemetry split comparison bars dynamically from match events', async () => {
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    try {
      await act(async () => {
        root.render(
          <TeamHeadToHeadAiSection
            homeTeam={mockHomeTeam}
            awayTeam={mockAwayTeam}
            events={mockEvents}
            seconds={1200}
            matchId="match-1"
          />
        );
      });

      expect(container.textContent).toContain('Live Match Head-to-Head Comparison');
      expect(container.textContent).toContain('Ball Possession');
      expect(container.textContent).toContain('Expected Goals (xG)');
      expect(container.textContent).toContain('Total Shots');
      expect(container.textContent).toContain('Pass Completion Rate');
      expect(container.textContent).toContain('Pressing Regains');
      expect(container.textContent).toContain('Duels & Physical Contests');

      const statRows = container.querySelectorAll('.h2h-stat-row');
      expect(statRows.length).toBeGreaterThanOrEqual(6);
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('renders the Azure AI Foundry Multi-Agent Tactical Deliberation section with dynamic AI-generated breakdown', async () => {
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    try {
      await act(async () => {
        root.render(
          <TeamHeadToHeadAiSection
            homeTeam={mockHomeTeam}
            awayTeam={mockAwayTeam}
            events={mockEvents}
            seconds={1200}
            matchId="match-1"
          />
        );
      });

      expect(container.textContent).toContain('Azure AI Foundry · Multi-Agent Head-to-Head Deliberation');
      expect(container.textContent).toContain('Decisive Head-to-Head Duel');
      expect(container.textContent).toContain('AI Dynamic Win Probability');
      expect(container.textContent).toContain('Manager Directive');
      expect(container.querySelector('.home-directive')).not.toBeNull();
      expect(container.querySelector('.away-directive')).not.toBeNull();
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('switches AI specialist lens and updates the tactical narrative', async () => {
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    try {
      await act(async () => {
        root.render(
          <TeamHeadToHeadAiSection
            homeTeam={mockHomeTeam}
            awayTeam={mockAwayTeam}
            events={mockEvents}
            seconds={1200}
            matchId="match-1"
          />
        );
      });

      const highPressBtn = Array.from(container.querySelectorAll<HTMLButtonElement>('.agent-selector-pill')).find(
        btn => btn.textContent?.includes('High Press Analyst')
      );
      expect(highPressBtn).toBeDefined();

      await act(async () => {
        highPressBtn?.click();
      });

      expect(highPressBtn?.classList.contains('active')).toBe(true);
      expect(container.querySelector('.ai-specialist-badge')?.textContent).toContain('High Press Analyst');
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('regenerates AI breakdown via AI service when regenerate button is clicked', async () => {
    const askSpy = vi.spyOn(api, 'askCandidateAnalyst').mockResolvedValueOnce({
      answer: 'AI generated tactical synthesis: Manchester United are utilizing direct transition vectors through Liverpool defensive half spaces.'
    } as any);

    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    try {
      await act(async () => {
        root.render(
          <TeamHeadToHeadAiSection
            homeTeam={mockHomeTeam}
            awayTeam={mockAwayTeam}
            events={mockEvents}
            seconds={1200}
            matchId="match-1"
          />
        );
      });

      const regenBtn = container.querySelector<HTMLButtonElement>('.ai-regenerate-btn');
      expect(regenBtn).not.toBeNull();

      await act(async () => {
        regenBtn?.click();
      });

      expect(askSpy).toHaveBeenCalled();
      expect(container.textContent).toContain('AI generated tactical synthesis');
    } finally {
      askSpy.mockRestore();
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('handles any club pairings dynamically without hardcoded historical records', async () => {
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    try {
      await act(async () => {
        root.render(
          <TeamHeadToHeadAiSection
            homeTeam={{ id: 'team-a', name: 'Aston Villa', activePlayers: 11 }}
            awayTeam={{ id: 'team-b', name: 'Brentford', activePlayers: 11 }}
            events={[]}
            seconds={600}
          />
        );
      });

      expect(container.textContent).toContain('Aston Villa');
      expect(container.textContent).toContain('Brentford');
      // No hardcoded historical meetings or fake derby names
      expect(container.textContent).not.toContain('84 Total Competitive Encounters');
      expect(container.textContent).not.toContain('Premier League Matchup');
      expect(container.textContent).not.toContain('All-Time Head-to-Head Record');
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('ensures ZERO hardcoded statistics, fake fallback duels, or arbitrary probability baselines at kickoff', async () => {
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    try {
      await act(async () => {
        root.render(
          <TeamHeadToHeadAiSection
            homeTeam={{ id: 'team-a', name: 'Arsenal', score: 0, activePlayers: 11 }}
            awayTeam={{ id: 'team-b', name: 'Chelsea', score: 0, activePlayers: 11 }}
            events={[]}
            seconds={0}
          />
        );
      });

      // No hardcoded 37% / 33% / 30% baseline percentages: at 0 seconds with 0-0, win probability is completely symmetrical (33% Arsenal, 34% Draw, 33% Chelsea)
      expect(container.textContent).toContain('Arsenal Win');
      expect(container.textContent).toContain('Chelsea Win');
      expect(container.textContent).not.toContain('37%');
      expect(container.textContent).toContain('33%');

      // Zero hardcoded duel percentages (never defaults to fake 50% when 0 duels have occurred)
      const statRows = Array.from(container.querySelectorAll('.h2h-stat-row'));
      const duelsRow = statRows.find(r => r.textContent?.includes('Duels & Physical Contests'));
      expect(duelsRow?.textContent).toContain('0%');
      expect(duelsRow?.textContent).not.toContain('50%');

      // Zero arbitrary buzzwords
      expect(container.textContent).not.toContain('3+2 shape');
      expect(container.textContent).not.toContain('Zone 14');
      expect(container.textContent).not.toContain('High Spatial Decisive Zone');
      expect(container.textContent).not.toContain('Midfield Pivot & Half-Space Infiltration');
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('dynamically computes decisive player duels and manager directives from actual match telemetry', async () => {
    const playerEvents: ContextEvent[] = [
      {
        id: 'pe-1',
        sequence: 1,
        clockSeconds: 300,
        type: 'Shot',
        teamId: 'mu-id',
        teamName: 'Manchester United',
        playerName: 'Bruno Fernandes'
      },
      {
        id: 'pe-2',
        sequence: 2,
        clockSeconds: 400,
        type: 'Pass',
        teamId: 'liv-id',
        teamName: 'Liverpool',
        playerName: 'Mohamed Salah',
        outcome: 'Successful'
      }
    ];

    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    try {
      await act(async () => {
        root.render(
          <TeamHeadToHeadAiSection
            homeTeam={{ id: 'mu-id', name: 'Manchester United', score: 2, activePlayers: 11 }}
            awayTeam={{ id: 'liv-id', name: 'Liverpool', score: 0, activePlayers: 11 }}
            events={playerEvents}
            seconds={1800}
            matchId="match-dynamic"
          />
        );
      });

      // Decisive duel dynamically identified the active players from the event log
      expect(container.textContent).toContain('Bruno Fernandes (Manchester United) vs Mohamed Salah (Liverpool)');

      // Manager directives dynamically derived from the 2-0 scoreline
      expect(container.querySelector('.home-directive')?.textContent).toContain('2-0 lead');
      expect(container.querySelector('.away-directive')?.textContent).toContain('2-0 deficit');
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });
});

