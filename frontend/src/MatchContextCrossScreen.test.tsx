import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MatchContextAnalystPage, MatchContextPlayersPage, MatchContextTeamsPage } from './MatchContextPages';
import { RoutePage } from './ShellPages';

afterEach(() => {
  vi.unstubAllGlobals();
  window.history.replaceState(null, '', '/');
});

describe('Match Context Across All Screens', () => {
  const matchId = 'b2c3d4e5-f6a7-8901-bcde-f12345678901';
  const sourceSha256 = 'b'.repeat(64);

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
      eventId: 'evt-2', matchId, sequence: 2, clockSeconds: 300, period: 1, eventType: 'Goal',
      rawPayload: { teamName: 'Manchester United', playerName: 'Bruno Fernandes' }
    },
    {
      eventId: 'evt-3', matchId, sequence: 3, clockSeconds: 600, period: 1, eventType: 'Shot',
      rawPayload: { teamName: 'Liverpool', playerName: 'Mohamed Salah' }
    },
    {
      eventId: 'evt-4', matchId, sequence: 4, clockSeconds: 1200, period: 1, eventType: 'BallRecovery',
      rawPayload: { teamName: 'Manchester United', playerName: 'Bruno Fernandes' }
    },
    {
      eventId: 'evt-5', matchId, sequence: 5, clockSeconds: 2400, period: 1, eventType: 'Goal',
      rawPayload: { teamName: 'Liverpool', playerName: 'Mohamed Salah' }
    }
  ];

  const setupCandidateMock = () => {
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input), 'http://localhost');
      if (url.pathname.endsWith('/simulation/candidate/status')) {
        return Response.json({
          matchId, isStreaming: true, currentSequence: 5, currentClockValue: 2400, period: 1, sourceSha256
        });
      }
      if (url.pathname.endsWith('/events')) {
        return Response.json({ matchId, sourceSha256, items: candidateEvents, hasMore: false });
      }
      if (url.pathname.endsWith('/analyst/agents')) {
        return Response.json({ defaultAgent: 'TacticalAnalyst', agents: ['TacticalAnalyst'] });
      }
      if (url.pathname.endsWith('/analyst/questions')) {
        return Response.json({
          status: 'answered', answer: 'Analysis bounded by elapsed time.',
          claims: [{ text: 'Analysis bounded by elapsed time.', evidenceIds: ['evt-4'] }],
          matchId, selectedSourceClockValue: 1200, sourceSha256, agentName: 'TacticalAnalyst',
          truthClass: 'hypothesis', limitations: ['Synthetic evidence only.']
        });
      }
      return Response.json({ status: 404, code: 'NOT_FOUND' }, { status: 404 });
    }));
  };

  it('displays the match context with team info and play time on the Teams screen, loading data up to elapsed time', async () => {
    setupCandidateMock();
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    try {
      // Elapsed time is 600 seconds (10:00). At 600s, Manchester United has 1 goal, Liverpool has 0 goals.
      // (evt-5 goal at 2400s has NOT happened yet).
      await act(async () => {
        root.render(<MatchContextTeamsPage route={{ kind: 'teams' }} matchId={matchId} seconds={600} />);
        await new Promise(resolve => setTimeout(resolve, 40));
      });

      // Candidate Live Feed HUD is excluded on Teams page
      expect(container.querySelector('.match-context-hud')).toBeNull();

      // The screen loads data only up to elapsed time (evt-1, evt-2, evt-3 = 3 events total, NOT evt-4 or evt-5)
      expect(container.textContent).toContain('Events attributed by feed');
      expect(container.textContent).toContain('Manchester United');
      expect(container.textContent).toContain('Liverpool');
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('displays player cards up to elapsed time without Candidate Live Feed HUD on the Players screen', async () => {
    setupCandidateMock();
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    try {
      await act(async () => {
        root.render(<MatchContextPlayersPage route={{ kind: 'players' }} matchId={matchId} seconds={600} />);
        await new Promise(resolve => setTimeout(resolve, 40));
      });

      // Candidate Live Feed HUD is excluded on Players page
      expect(container.querySelector('.match-context-hud')).toBeNull();

      // Player cards observed up to elapsed time
      expect(container.textContent).toContain('Bruno Fernandes');
      expect(container.textContent).toContain('Mohamed Salah');
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('bounds live reports up to elapsed time without Candidate Live Feed HUD on the AI Analyst screen', async () => {
    setupCandidateMock();
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    try {
      // Elapsed time is 1200 seconds (20:00). Event 4 (BallRecovery at 1200s) is notable.
      // Event 5 (at 2400s) must NOT be analyzed because it is beyond elapsed time.
      await act(async () => {
        root.render(<MatchContextAnalystPage route={{ kind: 'analyst' }} matchId={matchId} seconds={1200} />);
        await new Promise(resolve => setTimeout(resolve, 40));
      });

      // Candidate Live Feed HUD is excluded on AI Analyst page
      expect(container.querySelector('.match-context-hud')).toBeNull();

      expect(container.textContent).toContain('Reports from notable events');
      expect(container.textContent).toContain('BallRecovery');
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('allows RoutePage to inherit contextMatchId and contextSeconds across all pages when query params are absent', async () => {
    setupCandidateMock();
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    try {
      // Direct navigation to /analyst without query params, but with match context set in application
      await act(async () => {
        root.render(
          <RoutePage
            route={{ kind: 'analyst' }}
            search=""
            contextMatchId={matchId}
            contextSeconds={1200}
          />
        );
        await new Promise(resolve => setTimeout(resolve, 40));
      });

      // The AI Analyst screen followed the inherited Match Context without duplicate HUD
      expect(container.querySelector('.match-context-hud')).toBeNull();
      expect(container.textContent).toContain('Reports from notable events');
      expect(container.textContent).toContain('BallRecovery');
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });
});
