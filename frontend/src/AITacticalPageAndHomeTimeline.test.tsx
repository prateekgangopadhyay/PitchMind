import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { LinearEventsTimeline, getEventIcon } from './LinearEventsTimeline';
import { TacticalPitchView } from './TacticalPitchView';
import { MatchContextAnalystPage } from './MatchContextPages';
import { RoutePage } from './ShellPages';
import { computeAgentDiscussionAndExplanation, computeCausalChain, FOUNDRY_AGENTS } from './tacticalAgentDiscussion';
import { resolveRoute } from './routes';
import { DynamicPassMapThumbnail, DynamicHeatmapThumbnail, deduceTeamFormation } from './DynamicTacticalVisuals';
import { clearCandidateLiveCache } from './candidateLiveStatus';

describe('Home Page Linear Events Timeline and Differentiated Icons', () => {
  it('differentiates event types with distinct icons and theme classes', () => {
    const goal = getEventIcon('Goal');
    expect(goal.symbol).toBe('⚽');
    expect(goal.themeClass).toBe('icon-goal');

    const shot = getEventIcon('Shot');
    expect(shot.symbol).toBe('🎯');
    expect(shot.themeClass).toBe('icon-shot');

    const pass = getEventIcon('ProgressivePass');
    expect(pass.symbol).toBe('↗️');
    expect(pass.themeClass).toBe('icon-pass');

    const tackle = getEventIcon('Tackle');
    expect(tackle.symbol).toBe('🛑');
    expect(tackle.themeClass).toBe('icon-tackle');

    const yellowCard = getEventIcon('YellowCard');
    expect(yellowCard.symbol).toBe('🟨');
    expect(yellowCard.themeClass).toBe('icon-yellow-card');

    const recovery = getEventIcon('BallRecovery');
    expect(recovery.symbol).toBe('🔄');
    expect(recovery.themeClass).toBe('icon-recovery');

    const turnover = getEventIcon('PossessionLost');
    expect(turnover.symbol).toBe('📉');
    expect(turnover.themeClass).toBe('icon-turnover');

    const foul = getEventIcon('Foul');
    expect(foul.symbol).toBe('⚠️');
    expect(foul.themeClass).toBe('icon-foul');
  });

  it('renders events in a linear sequence connected by a timeline rail', async () => {
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    const testEvents = [
      { id: 'ev-1', type: 'KickOff', sequence: 1, clockSeconds: 0, teamName: 'Arsenal' },
      { id: 'ev-2', type: 'ProgressivePass', sequence: 2, clockSeconds: 140, playerName: 'Martin Ødegaard', teamName: 'Arsenal' },
      { id: 'ev-3', type: 'Tackle', sequence: 3, clockSeconds: 210, playerName: 'Rodri', teamName: 'Manchester City' },
      { id: 'ev-4', type: 'BallRecovery', sequence: 4, clockSeconds: 320, playerName: 'Declan Rice', teamName: 'Arsenal' },
      { id: 'ev-5', type: 'Shot', sequence: 5, clockSeconds: 540, playerName: 'Bukayo Saka', teamName: 'Arsenal' },
      { id: 'ev-6', type: 'Goal', sequence: 6, clockSeconds: 545, playerName: 'Bukayo Saka', teamName: 'Arsenal' }
    ];

    try {
      await act(async () => {
        root.render(
          <LinearEventsTimeline
            events={testEvents}
            matchId="arsenal-vs-mancity"
            matchTitle="Arsenal vs Manchester City"
            selectedSeconds={540}
          />
        );
      });

      // Renders main linear timeline elements
      expect(container.querySelector('.linear-events-track')).not.toBeNull();
      expect(container.querySelector('.linear-timeline-rail')).not.toBeNull();

      // Differentiated events with icons and player attributions
      const cards = container.querySelectorAll('.linear-event-node');
      expect(cards.length).toBe(6);

      expect(container.textContent).toContain('Linear Match Events Timeline');
      expect(container.textContent).toContain('Arsenal vs Manchester City');
      expect(container.textContent).toContain('Bukayo Saka');
      expect(container.textContent).toContain('Declan Rice');
      expect(container.textContent).toContain('Rodri');

      // Contains icon classes for differentiation
      expect(container.querySelector('.icon-goal')).not.toBeNull();
      expect(container.querySelector('.icon-shot')).not.toBeNull();
      expect(container.querySelector('.icon-tackle')).not.toBeNull();
      expect(container.querySelector('.icon-recovery')).not.toBeNull();
      expect(container.querySelector('.icon-pass')).not.toBeNull();
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('identifies and highlights the current relevant event card and scrolls it into view', async () => {
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = createRoot(container);

    const testEvents = [
      { id: 'ev-1', type: 'KickOff', sequence: 1, clockSeconds: 0, teamName: 'Arsenal' },
      { id: 'ev-2', type: 'ProgressivePass', sequence: 2, clockSeconds: 140, playerName: 'Martin Ødegaard', teamName: 'Arsenal' },
      { id: 'ev-3', type: 'Tackle', sequence: 3, clockSeconds: 210, playerName: 'Rodri', teamName: 'Manchester City' },
      { id: 'ev-4', type: 'BallRecovery', sequence: 4, clockSeconds: 320, playerName: 'Declan Rice', teamName: 'Arsenal' },
      { id: 'ev-5', type: 'Shot', sequence: 5, clockSeconds: 540, playerName: 'Bukayo Saka', teamName: 'Arsenal' },
      { id: 'ev-6', type: 'Goal', sequence: 6, clockSeconds: 545, playerName: 'Bukayo Saka', teamName: 'Arsenal' }
    ];

    const scrollToMock = vi.fn();
    const originalScrollTo = HTMLDivElement.prototype.scrollTo;
    HTMLDivElement.prototype.scrollTo = scrollToMock;

    try {
      await act(async () => {
        root.render(
          <LinearEventsTimeline
            events={testEvents}
            selectedSeconds={350}
          />
        );
      });

      // At selectedSeconds = 350, latest event is ev-4 (at 320s)
      const currentCard = container.querySelector('.linear-event-node.is-selected');
      expect(currentCard).not.toBeNull();
      expect(currentCard?.getAttribute('data-event-id')).toBe('ev-4');
      expect(currentCard?.getAttribute('aria-current')).toBe('true');
      expect(currentCard?.textContent).toContain('Current');

      // Check that scrollTo was invoked to scroll to the current card
      expect(scrollToMock).toHaveBeenCalled();
      const lastCall = scrollToMock.mock.calls[scrollToMock.mock.calls.length - 1][0];
      expect(lastCall).toHaveProperty('behavior');

      // Advance clock to 542 seconds (Shot at 540s should become current)
      await act(async () => {
        root.render(
          <LinearEventsTimeline
            events={testEvents}
            selectedSeconds={542}
          />
        );
      });

      const updatedCard = container.querySelector('.linear-event-node.is-selected');
      expect(updatedCard?.getAttribute('data-event-id')).toBe('ev-5');
      expect(updatedCard?.getAttribute('aria-current')).toBe('true');
    } finally {
      HTMLDivElement.prototype.scrollTo = originalScrollTo;
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('scrolls to and selects event card when clicked by user', async () => {
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = createRoot(container);

    const testEvents = [
      { id: 'ev-1', type: 'KickOff', sequence: 1, clockSeconds: 0 },
      { id: 'ev-2', type: 'Shot', sequence: 2, clockSeconds: 120 },
      { id: 'ev-3', type: 'Goal', sequence: 3, clockSeconds: 300 }
    ];

    const scrollToMock = vi.fn();
    const originalScrollTo = HTMLDivElement.prototype.scrollTo;
    HTMLDivElement.prototype.scrollTo = scrollToMock;
    const selectMock = vi.fn();

    try {
      await act(async () => {
        root.render(
          <LinearEventsTimeline
            events={testEvents}
            selectedSeconds={0}
            onSelectEvent={selectMock}
          />
        );
      });

      // Initially ev-1 is current
      expect(container.querySelector('.linear-event-node.is-selected')?.getAttribute('data-event-id')).toBe('ev-1');

      // Click on ev-3 (Goal)
      const goalCard = container.querySelector('[data-event-id="ev-3"]') as HTMLElement;
      expect(goalCard).not.toBeNull();

      await act(async () => {
        goalCard.click();
      });

      expect(selectMock).toHaveBeenCalledWith(expect.objectContaining({ id: 'ev-3', type: 'Goal' }));
      expect(scrollToMock).toHaveBeenCalled();
    } finally {
      HTMLDivElement.prototype.scrollTo = originalScrollTo;
      await act(async () => root.unmount());
      container.remove();
    }
  });
});

describe('AI Tactical Page Split View & Multi-Agent Deliberation', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  });

  it('resolves both /analyst and /tactical routes to analyst', () => {
    expect(resolveRoute('/analyst')).toEqual({ kind: 'analyst' });
    expect(resolveRoute('/tactical')).toEqual({ kind: 'analyst' });
  });

  it('computes a synthesized explanation by letting the 10 Foundry agents discuss the situation', () => {
    expect(FOUNDRY_AGENTS).toHaveLength(10);
    const agentNames = FOUNDRY_AGENTS.map(a => a.name);
    expect(agentNames).toContain('TacticalAnalyst');
    expect(agentNames).toContain('HighPressAnalyst');
    expect(agentNames).toContain('DefensiveStructureAnalyst');
    expect(agentNames).toContain('PositionalPlayArchitect');
    expect(agentNames).toContain('TransitionStrategist');
    expect(agentNames).toContain('CoachFrameworkAnalyst');
    expect(agentNames).toContain('ChanceCreationAnalyst');
    expect(agentNames).toContain('GoalkeeperAnalyst');
    expect(agentNames).toContain('MatchStateStrategist');
    expect(agentNames).toContain('SetPieceStrategist');

    const deliberation = computeAgentDiscussionAndExplanation({
      matchId: 'match-tactical-test',
      clockSeconds: 1800,
      period: 1,
      homeTeamName: 'Arsenal',
      awayTeamName: 'Chelsea',
      homeScore: 1,
      awayScore: 0,
      playersCount: 22,
      recentEvent: {
        type: 'BallRecovery',
        sequence: 18,
        clockSeconds: 1795,
        teamName: 'Arsenal',
        playerName: 'Declan Rice'
      },
      ballPosition: { x: 55.2, y: 32.1 }
    });

    // Deliberation turns contain multi-agent discussion
    expect(deliberation.turns.length).toBeGreaterThanOrEqual(6);
    expect(deliberation.turns[0].agentName).toBe('TacticalAnalyst');
    expect(deliberation.turns[1].agentName).toBe('HighPressAnalyst');
    expect(deliberation.turns[2].agentName).toBe('DefensiveStructureAnalyst');
    expect(deliberation.turns[3].agentName).toBe('PositionalPlayArchitect');
    expect(deliberation.turns[4].agentName).toBe('TransitionStrategist');

    // Synthesis is computed from specialist inputs
    expect(deliberation.explanation.headline).toContain('Arsenal');
    expect(deliberation.explanation.summary).toContain('multi-agent consensus');
    expect(deliberation.explanation.keyFindings.length).toBeGreaterThanOrEqual(4);
    expect(deliberation.explanation.tacticalRecommendations.length).toBeGreaterThanOrEqual(2);
  });

  it('renders the TacticalPitchView with players, ball, and tactical zone overlays', async () => {
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    const testPlayers = [
      { id: 'p1', teamId: 'Arsenal', name: 'Raya', jerseyNumber: 22, x: 8, y: 50 },
      { id: 'p2', teamId: 'Arsenal', name: 'Saliba', jerseyNumber: 2, x: 28, y: 35 },
      { id: 'p3', teamId: 'Arsenal', name: 'Gabriel', jerseyNumber: 6, x: 28, y: 65 },
      { id: 'p4', teamId: 'Chelsea', name: 'Palmer', jerseyNumber: 20, x: 62, y: 40 },
      { id: 'p5', teamId: 'Chelsea', name: 'Jackson', jerseyNumber: 15, x: 75, y: 50 }
    ];

    try {
      await act(async () => {
        root.render(
          <TacticalPitchView
            players={testPlayers}
            homeTeam={{ id: 'Arsenal', name: 'Arsenal', score: 1, activePlayers: 3 }}
            awayTeam={{ id: 'Chelsea', name: 'Chelsea', score: 0, activePlayers: 2 }}
            seconds={1200}
            ballPosition={{ x: 55, y: 42 }}
          />
        );
      });

      expect(container.querySelector('.tactical-pitch-svg')).not.toBeNull();
      expect(container.querySelector('.tactical-ball-marker')).not.toBeNull();
      expect(container.querySelectorAll('.tactical-player.home-team').length).toBe(3);
      expect(container.querySelectorAll('.tactical-player.away-team').length).toBe(2);
      expect(container.textContent).toContain('Tactical Pitch View');
      expect(container.textContent).toContain('5 players');
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('displays the pitch on the left side and the computed agent explanation on the right side', async () => {
    const matchId = 'candidate-split-screen-match';
    const sourceSha256 = 'f'.repeat(64);

    const candidateEvents = [
      {
        eventId: 'cand-ev-1',
        matchId,
        sequence: 1,
        clockSeconds: 0,
        period: 1,
        eventType: 'KickOff',
        rawPayload: { teamName: 'Real Madrid' }
      },
      {
        eventId: 'cand-ev-2',
        matchId,
        sequence: 2,
        clockSeconds: 15,
        period: 1,
        eventType: 'BallRecovery',
        rawPayload: {
          teamName: 'Real Madrid',
          playerName: 'Valverde',
          playerPositions: [
            { playerId: 'pl-1', teamName: 'Real Madrid', playerName: 'Courtois', jerseyNumber: 1, x: 5, y: 50 },
            { playerId: 'pl-2', teamName: 'Real Madrid', playerName: 'Valverde', jerseyNumber: 15, x: 45, y: 35 },
            { playerId: 'pl-3', teamName: 'Barcelona', playerName: 'Lewandowski', jerseyNumber: 9, x: 65, y: 50 }
          ]
        }
      }
    ];

    const fetcher = vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input), 'http://localhost');
      if (url.pathname.endsWith('/simulation/candidate/status')) {
        return Response.json({
          matchId,
          isStreaming: true,
          currentSequence: 2,
          currentClockValue: 15,
          period: 1,
          sourceSha256
        });
      }
      if (url.pathname.endsWith('/analyst/agents')) {
        return Response.json({
          defaultAgent: 'TacticalAnalyst',
          agents: FOUNDRY_AGENTS.map(a => a.name)
        });
      }
      if (url.pathname.endsWith('/analyst/questions')) {
        return Response.json({
          status: 'answered',
          answer: 'The source records a ball recovery; the sequence indicates transition posture.',
          claims: [{ text: 'Ball recovery recorded at sequence 2', evidenceIds: ['cand-ev-2'] }],
          matchId,
          selectedSourceClockValue: 15,
          sourceSha256,
          agentName: 'TacticalAnalyst',
          truthClass: 'hypothesis',
          limitations: ['Synthetic candidate simulation records.']
        });
      }
      if (url.pathname.endsWith('/events')) {
        return Response.json({ matchId, sourceSha256, items: candidateEvents, hasMore: false });
      }
      return Response.json({ status: 404, code: 'NOT_FOUND' }, { status: 404 });
    });

    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    vi.stubGlobal('fetch', fetcher);

    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);
    const setIntervalSpy = vi.spyOn(window, 'setInterval').mockImplementation(() => 0 as unknown as number);
    const clearIntervalSpy = vi.spyOn(window, 'clearInterval').mockImplementation(() => undefined);

    try {
      await act(async () => {
        root.render(
          <MatchContextAnalystPage
            route={{ kind: 'analyst' }}
            matchId={matchId}
            seconds={15}
          />
        );
        await new Promise(resolve => setTimeout(resolve, 50));
      });

      // Split Screen Layout container
      const splitScreen = container.querySelector('.tactical-split-screen');
      expect(splitScreen).not.toBeNull();

      // LEFT COLUMN: The Pitch
      const pitchColumn = container.querySelector('.tactical-pitch-column');
      expect(pitchColumn).not.toBeNull();
      expect(pitchColumn?.querySelector('.tactical-pitch-card')).not.toBeNull();
      expect(pitchColumn?.querySelector('.tactical-pitch-svg')).not.toBeNull();
      expect(pitchColumn?.textContent).toContain('Tactical Pitch View');

      // RIGHT COLUMN: The Explanation computed by letting agents discuss
      const explanationColumn = container.querySelector('.tactical-explanation-column');
      expect(explanationColumn).not.toBeNull();

      // Foundry Agent roster with 10 agents
      expect(explanationColumn?.querySelector('.foundry-agent-roster')).not.toBeNull();
      expect(explanationColumn?.textContent).toContain('TacticalAnalyst');
      expect(explanationColumn?.textContent).toContain('HighPressAnalyst');
      expect(explanationColumn?.textContent).toContain('DefensiveStructureAnalyst');
      expect(explanationColumn?.textContent).toContain('PositionalPlayArchitect');

      // Computed Tactical Explanation Card
      expect(explanationColumn?.querySelector('.tactical-synthesized-card')).not.toBeNull();
      expect(explanationColumn?.textContent).toContain('Computed via Multi-Agent Consensus');
      expect(explanationColumn?.textContent).toContain('Key Findings from Agent Discussion');
      expect(explanationColumn?.textContent).toContain('Tactical Recommendations');

      // Multi-Agent Deliberation Dialogue stream
      expect(explanationColumn?.querySelector('.agent-discussion-stream')).not.toBeNull();
      expect(explanationColumn?.textContent).toContain('Specialists Deliberation Dialogue');

      // Notable events reports section preserved
      expect(container.textContent).toContain('Reports from notable events');
      expect(container.textContent).toContain('BallRecovery');
    } finally {
      await act(async () => root.unmount());
      setIntervalSpy.mockRestore();
      clearIntervalSpy.mockRestore();
      container.remove();
    }
  });

  it('shows the Linear Events Timeline on HomePage by default', async () => {
    clearCandidateLiveCache();
    const matchId = 'home-timeline-match-123';
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);

    const matchSnapshot = {
      matchId,
      canonicalRevision: 2,
      projectionRevision: 2,
      selectedTimeSeconds: 5400,
      asOfSeconds: 1200,
      throughSequence: 5,
      algorithmVersion: 'projection-v1',
      sourceSha256: 'd'.repeat(64),
      provenance: 'synthetic-approved',
      period: 1,
      phase: 'firsthalf',
      homeScore: 1,
      awayScore: 0,
      homeActivePlayers: 11,
      awayActivePlayers: 11,
      players: []
    };

    const homeEvents = [
      { eventId: 'ev-1', matchId, sequence: 1, type: 'KickOff', clockSeconds: 0 },
      { eventId: 'ev-2', matchId, sequence: 2, type: 'Goal', clockSeconds: 420 },
      { eventId: 'ev-3', matchId, sequence: 3, type: 'YellowCard', clockSeconds: 850 }
    ];

    const canonicalPageData = {
      matchId,
      canonicalRevision: 2,
      projectionRevision: 2,
      selectedTimeSeconds: 5400,
      throughSequence: 5,
      items: homeEvents,
      nextCursor: null,
      hasMore: false
    };

    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const url = new URL(String(input), 'http://localhost');
      if (url.pathname.endsWith('/canonical/fixtures')) {
        return Response.json({
          items: [{
            matchId,
            canonicalRevision: 2,
            asOfSeconds: 1200,
            throughSequence: 5,
            phase: 'firsthalf',
            homeScore: 1,
            awayScore: 0,
            provenance: 'synthetic-approved',
            algorithmVersion: 'test',
            sourceSha256: 'd'.repeat(64)
          }],
          nextMatchId: null,
          hasMore: false
        });
      }
      if (url.pathname.endsWith('/summary')) return Response.json(matchSnapshot);
      if (url.pathname.endsWith('/events')) return Response.json(canonicalPageData);
      if (url.pathname.endsWith('/frames') || url.pathname.endsWith('/insights')) {
        return Response.json({ ...canonicalPageData, items: [] });
      }
      return Response.json({ status: 404 }, { status: 404 });
    }));

    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);
    const setIntervalSpy = vi.spyOn(window, 'setInterval').mockImplementation(() => 0 as unknown as number);
    const clearIntervalSpy = vi.spyOn(window, 'clearInterval').mockImplementation(() => undefined);

    try {
      await act(async () => {
        root.render(<RoutePage route={{ kind: 'home' }} search="" />);
        await new Promise(resolve => setTimeout(resolve, 100));
      });

      // The event timeline and commentary are mounted without an extra click.
      expect(container.querySelector('.linear-events-track')).not.toBeNull();
      expect(container.querySelector('.linear-timeline-rail')).not.toBeNull();
      expect(container.querySelector('.icon-goal')).not.toBeNull();
      expect(container.querySelector('.icon-yellow-card')).not.toBeNull();
      expect(container.querySelector('.ai-commentary-timeline-container')).not.toBeNull();
    } finally {
      await act(async () => root.unmount());
      setIntervalSpy.mockRestore();
      clearIntervalSpy.mockRestore();
      clearCandidateLiveCache();
      container.remove();
    }
  });

  it('leverages the deployed Foundry project agents to compute live deliberation and explanation', async () => {
    const matchId = 'foundry-live-deliberation-match';
    const sourceSha256 = 'e'.repeat(64);
    const askedAgents: string[] = [];

    const fetcher = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(String(input), 'http://localhost');
      if (url.pathname.endsWith('/simulation/candidate/status')) {
        return Response.json({
          matchId, isStreaming: true, currentSequence: 5, currentClockValue: 1200, period: 1, sourceSha256
        });
      }
      if (url.pathname.endsWith('/analyst/agents')) {
        return Response.json({
          defaultAgent: 'TacticalAnalyst',
          agents: FOUNDRY_AGENTS.map(a => a.name)
        });
      }
      if (url.pathname.endsWith('/analyst/questions')) {
        const body = JSON.parse(String(init?.body || '{}'));
        askedAgents.push(body.agentName);
        return Response.json({
          status: 'answered',
          answer: `Live hypothesis from Foundry Agent ${body.agentName}: tactical structure validated.`,
          claims: [{ text: `Observation by ${body.agentName}`, evidenceIds: ['cand-ev-9'] }],
          matchId,
          selectedSourceClockValue: 1200,
          sourceSha256,
          agentName: body.agentName,
          truthClass: 'hypothesis',
          limitations: ['Verified against Azure AI Foundry project agent deployment.']
        });
      }
      if (url.pathname.endsWith('/events')) {
        return Response.json({
          matchId, sourceSha256,
          items: [{
            eventId: 'cand-ev-9', matchId, sequence: 5, clockSeconds: 1200, period: 1, eventType: 'BallRecovery',
            rawPayload: { teamName: 'Arsenal' }
          }],
          hasMore: false
        });
      }
      return Response.json({ status: 404 }, { status: 404 });
    });

    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    vi.stubGlobal('fetch', fetcher);

    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    try {
      await act(async () => {
        root.render(<MatchContextAnalystPage route={{ kind: 'analyst' }} matchId={matchId} seconds={1200} />);
        await new Promise(resolve => setTimeout(resolve, 50));
      });

      // Roster action buttons exist
      const consultBtn = container.querySelector<HTMLButtonElement>('.consult-specialist-btn');
      const deliberateAllBtn = container.querySelector<HTMLButtonElement>('.deliberate-all-btn');
      expect(consultBtn).not.toBeNull();
      expect(deliberateAllBtn).not.toBeNull();

      // Click "Start Multi-Agent Panel Deliberation" to query the Foundry project agents
      await act(async () => {
        deliberateAllBtn?.click();
        await new Promise(resolve => setTimeout(resolve, 80));
      });

      // Verified that Foundry specialist agents were queried
      expect(askedAgents).toContain('HighPressAnalyst');
      expect(askedAgents).toContain('DefensiveStructureAnalyst');
      expect(askedAgents).toContain('PositionalPlayArchitect');
      expect(askedAgents).toContain('TacticalAnalyst');

      // Explanation updated with computed consensus from Foundry agents
      const explanation = container.querySelector('.tactical-synthesized-card');
      expect(explanation?.textContent).toContain('Live hypothesis from Foundry Agent TacticalAnalyst');
      expect(explanation?.textContent).toContain('cand-ev-9');
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('submits a question in the AI Analyst chat and renders the query response in the conversation UI', async () => {
    const matchId = 'foundry-live-deliberation-match';
    const sourceSha256 = 'e'.repeat(64);
    let querySent = '';

    const fetcher = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = new URL(String(input), 'http://localhost');
      if (url.pathname.endsWith('/simulation/candidate/status')) {
        return Response.json({
          matchId, isStreaming: true, currentSequence: 5, currentClockValue: 1200, period: 1, sourceSha256
        });
      }
      if (url.pathname.endsWith('/analyst/agents')) {
        return Response.json({
          defaultAgent: 'TacticalAnalyst',
          agents: ['TacticalAnalyst']
        });
      }
      if (url.pathname.endsWith('/analyst/questions')) {
        const body = JSON.parse(String(init?.body || '{}'));
        querySent = body.question || '';
        return Response.json({
          status: 'answered',
          answer: 'Detailed tactical breakdown: High press forced 12 turnovers in Zone 14 leading directly to 2 big chances.',
          claims: [
            { text: 'High press forced 12 turnovers in Zone 14', evidenceIds: ['cand-ev-12', 'cand-ev-15'] }
          ],
          matchId,
          selectedSourceClockValue: 1200,
          sourceSha256,
          agentName: body.agentName,
          truthClass: 'hypothesis',
          limitations: ['Verified against Azure AI Foundry project agent deployment.']
        });
      }
      if (url.pathname.endsWith('/events')) {
        return Response.json({
          matchId, sourceSha256,
          items: [{
            eventId: 'cand-ev-9', matchId, sequence: 5, clockSeconds: 1200, period: 1, eventType: 'BallRecovery',
            rawPayload: { teamName: 'Arsenal' }
          }],
          hasMore: false
        });
      }
      return Response.json({ status: 404 }, { status: 404 });
    });

    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    vi.stubGlobal('fetch', fetcher);

    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    try {
      await act(async () => {
        root.render(<MatchContextAnalystPage route={{ kind: 'analyst' }} matchId={matchId} seconds={1200} />);
        await new Promise(resolve => setTimeout(resolve, 50));
      });

      const chatInput = container.querySelector<HTMLInputElement>('.chat-text-input');
      const chatForm = container.querySelector<HTMLFormElement>('.analyst-chat-form');
      expect(chatInput).not.toBeNull();
      expect(chatForm).not.toBeNull();

      // Enter a custom question
      await act(async () => {
        if (chatInput) {
          const nativeInputValueSetter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value')?.set;
          nativeInputValueSetter?.call(chatInput, 'How are wingers tracking back?');
          chatInput.dispatchEvent(new Event('input', { bubbles: true }));
        }
      });

      // Submit the question
      await act(async () => {
        chatForm?.dispatchEvent(new Event('submit', { cancelable: true, bubbles: true }));
        await new Promise(resolve => setTimeout(resolve, 100));
      });

      // Check that query was sent
      expect(querySent).toContain('How are wingers tracking back?');

      // Check that chat input was cleared
      expect(chatInput?.value).toBe('');

      // Check that the conversation card displays the answer and cited evidence
      const conversationCard = container.querySelector('.analyst-conversation-card');
      expect(conversationCard).not.toBeNull();
      expect(conversationCard?.textContent).toContain('How are wingers tracking back?');
      expect(conversationCard?.textContent).toContain('Detailed tactical breakdown: High press forced 12 turnovers');
      expect(conversationCard?.textContent).toContain('#cand-ev-12');
      expect(conversationCard?.textContent).toContain('#cand-ev-15');
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('computes 4-step Causal chain (DERIVED) for Goal and Shot events without hardcoding', () => {
    const goalEvent = {
      type: 'Goal',
      sequence: 334,
      clockSeconds: 854,
      teamName: 'Manchester United',
      playerName: 'Bruno Fernandes',
      pitchX: 78,
      pitchY: 52,
      endX: 100,
      endY: 50,
      action: 'InsideBoxFinish',
      outcome: 'Goal',
      impactScore: 0.89
    };

    const priorEvents = [
      { type: 'Shot', sequence: 330, clockSeconds: 847, teamName: 'Liverpool', playerName: 'Dominik Szoboszlai' },
      { type: 'Tackle', sequence: 332, clockSeconds: 850, teamName: 'Manchester United', playerName: 'Kobbie Mainoo' },
      { type: 'Pass', sequence: 333, clockSeconds: 852, teamName: 'Manchester United', playerName: 'Kobbie Mainoo' }
    ];

    const chain = computeCausalChain(goalEvent, priorEvents);
    expect(chain.length).toBe(4);
    expect(chain[0].step).toBe(1);
    expect(chain[0].phase).toBe('Press trigger');
    expect(chain[0].action).toContain('Kobbie Mainoo');

    expect(chain[1].step).toBe(2);
    expect(chain[1].phase).toBe('Wide circulation');

    expect(chain[2].step).toBe(3);
    expect(chain[2].phase).toContain('Regain');
    expect(chain[2].action).toContain('Kobbie Mainoo');

    expect(chain[3].step).toBe(4);
    expect(chain[3].phase).toBe('Transition chance');
    expect(chain[3].action).toContain('InsideBoxFinish · GOAL ⚽');
    expect(chain[3].detail).toContain('0.89');
    expect(chain[3].playerName).toBe('Bruno Fernandes');
  });

  it('renders dynamic tactical pitch diagram with action vector, constraint zone, and pressure collapse for Goal', async () => {
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    const goalEvent = {
      type: 'Goal',
      sequence: 334,
      clockSeconds: 854,
      teamName: 'Manchester United',
      playerName: 'Bruno Fernandes',
      pitchX: 78,
      pitchY: 52,
      endX: 100,
      endY: 50,
      action: 'InsideBoxFinish',
      outcome: 'Goal',
      impactScore: 0.89,
      leadupEvent: {
        type: 'Pass',
        sequence: 333,
        clockSeconds: 852,
        playerName: 'Kobbie Mainoo',
        pitchX: 65,
        pitchY: 45
      }
    };

    const players = [
      { id: 'p1', name: 'Bruno Fernandes', teamId: 'Manchester United', jerseyNumber: 8, x: 78, y: 52 },
      { id: 'p2', name: 'Kobbie Mainoo', teamId: 'Manchester United', jerseyNumber: 37, x: 65, y: 45 },
      { id: 'p3', name: 'Virgil van Dijk', teamId: 'Liverpool', jerseyNumber: 4, x: 88.3, y: 58.6 },
      { id: 'p4', name: 'Ibrahima Konaté', teamId: 'Liverpool', jerseyNumber: 5, x: 83.6, y: 28.7 },
      { id: 'p5', name: 'Alisson Becker', teamId: 'Liverpool', jerseyNumber: 1, x: 94.8, y: 50.5 }
    ];

    try {
      await act(async () => {
        root.render(
          <TacticalPitchView
            players={players}
            homeTeam={{ id: 'Manchester United', name: 'Manchester United', activePlayers: 11 }}
            awayTeam={{ id: 'Liverpool', name: 'Liverpool', activePlayers: 11 }}
            seconds={854}
            recentEvent={goalEvent}
          />
        );
      });

      // Renders the tactical diagram header banner with title and metrics
      expect(container.querySelector('.tactical-diagram-banner')).not.toBeNull();
      expect(container.textContent).toContain('CENTRAL PROGRESSION UNDER PRESSURE');
      expect(container.textContent).toContain('Bruno Fernandes');
      expect(container.textContent).toContain('InsideBoxFinish');
      expect(container.textContent).toContain('0.89');

      // Renders the shaded constraint / danger zone
      expect(container.querySelector('.tactical-constraint-zone-group')).not.toBeNull();
      expect(container.textContent).toContain('GOAL CREATION POCKET');

      // Renders the finishing action vector and pass leadup vector
      expect(container.querySelector('.tactical-action-vector-group')).not.toBeNull();
      expect(container.querySelector('.tactical-leadup-vector-group')).not.toBeNull();
      expect(container.textContent).toContain('Pass: Kobbie Mainoo');

      // Renders key actor marker on Bruno Fernandes
      expect(container.querySelector('.tactical-player.home-team.is-key-actor')).not.toBeNull();
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('dynamically computes pass vectors and player nodes in DynamicPassMapThumbnail without static SVG attachments', async () => {
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    const testEvents = [
      { id: 'ev-1', type: 'ProgressivePass', sequence: 1, clockSeconds: 100, pitchX: 30, pitchY: 40, endX: 55, endY: 60, teamName: 'Arsenal' },
      { id: 'ev-2', type: 'ShortPass', sequence: 2, clockSeconds: 200, pitchX: 55, pitchY: 60, endX: 70, endY: 45, teamName: 'Arsenal' }
    ];
    const testPlayers = [
      { id: 'p1', name: 'Saka', teamId: 'Arsenal', x: 70, y: 45, active: true },
      { id: 'p2', name: 'Rice', teamId: 'Arsenal', x: 55, y: 60, active: true }
    ];

    try {
      await act(async () => {
        root.render(
          <DynamicPassMapThumbnail
            events={testEvents}
            players={testPlayers}
            homeTeamName="Arsenal"
            awayTeamName="Chelsea"
            clockSeconds={300}
          />
        );
      });

      const svg = container.querySelector('svg.dynamic-pass-map-svg');
      expect(svg).not.toBeNull();
      // Verifies dynamic pass vectors are rendered
      const paths = container.querySelectorAll('svg path[marker-end]');
      expect(paths.length).toBeGreaterThan(0);
      // Verifies dynamic players are plotted
      const circles = container.querySelectorAll('circle');
      expect(circles.length).toBeGreaterThan(0);
      // No video play overlays
      expect(container.querySelector('.media-play-overlay')).toBeNull();
      expect(container.querySelector('.carousel-play-overlay')).toBeNull();
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('dynamically computes spatial heat density in DynamicHeatmapThumbnail without static images', async () => {
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    const testEvents = [
      { id: 'ev-1', type: 'Tackle', sequence: 1, clockSeconds: 100, pitchX: 75, pitchY: 30, teamName: 'Arsenal' },
      { id: 'ev-2', type: 'BallRecovery', sequence: 2, clockSeconds: 200, pitchX: 80, pitchY: 35, teamName: 'Arsenal' },
      { id: 'ev-3', type: 'Shot', sequence: 3, clockSeconds: 300, pitchX: 85, pitchY: 50, teamName: 'Arsenal' }
    ];

    try {
      await act(async () => {
        root.render(
          <DynamicHeatmapThumbnail
            events={testEvents}
            teamName="Arsenal"
            clockSeconds={400}
          />
        );
      });

      const svg = container.querySelector('svg.dynamic-heatmap-svg');
      expect(svg).not.toBeNull();
      // Verifies dynamic thermal blobs (ellipses with radial gradients and blur filter)
      const ellipses = container.querySelectorAll('ellipse');
      expect(ellipses.length).toBeGreaterThan(0);
      expect(container.querySelector('filter#dynHeatBlur')).not.toBeNull();
      // No video play buttons
      expect(container.querySelector('.media-play-overlay')).toBeNull();
      expect(container.querySelector('.carousel-play-overlay')).toBeNull();
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('dynamically deduces team formation from player pitch coordinates', () => {
    const testPlayers = [
      { id: 'gk', name: 'GK', teamId: 'TeamA', x: 5, y: 50, active: true },
      { id: 'd1', name: 'LB', teamId: 'TeamA', x: 25, y: 15, active: true },
      { id: 'd2', name: 'CB1', teamId: 'TeamA', x: 22, y: 38, active: true },
      { id: 'd3', name: 'CB2', teamId: 'TeamA', x: 22, y: 62, active: true },
      { id: 'd4', name: 'RB', teamId: 'TeamA', x: 25, y: 85, active: true },
      { id: 'm1', name: 'CM1', teamId: 'TeamA', x: 50, y: 30, active: true },
      { id: 'm2', name: 'DM', teamId: 'TeamA', x: 45, y: 50, active: true },
      { id: 'm3', name: 'CM2', teamId: 'TeamA', x: 50, y: 70, active: true },
      { id: 'f1', name: 'LW', teamId: 'TeamA', x: 75, y: 20, active: true },
      { id: 'f2', name: 'ST', teamId: 'TeamA', x: 80, y: 50, active: true },
      { id: 'f3', name: 'RW', teamId: 'TeamA', x: 75, y: 80, active: true }
    ];

    const formation = deduceTeamFormation(testPlayers, 'TeamA');
    expect(formation).toBe('4 - 3 - 3');
  });
});


