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
      teamName: 'Manchester United'
    },
    {
      id: 'ev-2',
      sequence: 2,
      clockSeconds: 300,
      type: 'Tackle',
      teamId: 'mu-id',
      teamName: 'Manchester United'
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
      teamName: 'Liverpool'
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

  it('renders the clash hero header with derby title, club names, and live score', async () => {
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

      expect(container.textContent).toContain('The North West Derby');
      expect(container.textContent).toContain('Manchester United');
      expect(container.textContent).toContain('Liverpool');
      expect(container.textContent).toContain('VS');
      expect(container.querySelector('.home-pillar .h2h-score-badge')?.textContent).toContain('1 Goal');
      expect(container.querySelector('.away-pillar .h2h-score-badge')?.textContent).toContain('0 Goals');
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('renders all-time historical statistics for Manchester United vs Liverpool', async () => {
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

      expect(container.textContent).toContain('All-Time Head-to-Head Record');
      expect(container.textContent).toContain('214 Total Competitive Encounters');
      expect(container.textContent).toContain('Manchester United Wins');
      expect(container.textContent).toContain('Liverpool Wins');
      expect(container.textContent).toContain('All-Time Goals');
      expect(container.textContent).toContain('Clean Sheets');
      expect(container.textContent).toContain('Last 5 Competitive Encounters');

      // 3-way segmented ratio bar
      expect(container.querySelector('.h2h-win-ratio-bar')).not.toBeNull();
      expect(container.querySelector('.ratio-segment.home')).not.toBeNull();
      expect(container.querySelector('.ratio-segment.draw')).not.toBeNull();
      expect(container.querySelector('.ratio-segment.away')).not.toBeNull();
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('computes and renders live match telemetry split comparison bars', async () => {
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

  it('renders the Azure AI Foundry Multi-Agent Tactical Deliberation section with AI-generated breakdown', async () => {
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

  it('handles unknown or generic club pairings gracefully with fallback records', async () => {
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
      expect(container.textContent).toContain('Premier League Matchup');
      expect(container.textContent).toContain('84 Total Competitive Encounters');
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });
});
