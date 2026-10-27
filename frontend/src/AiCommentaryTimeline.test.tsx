import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AiCommentaryTimeline, generateAiCommentaryFromEvents } from './AiCommentaryTimeline';
import type { CommentaryEventInput } from './AiCommentaryTimeline';
import { RoutePage } from './ShellPages';

describe('AI Match Commentary Generation and Synced Timeline', () => {
  beforeEach(() => {
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  const sampleEvents: CommentaryEventInput[] = [
    {
      id: 'ev-1',
      type: 'KickOff',
      sequence: 1,
      clockSeconds: 0,
      teamName: 'Manchester United',
      playerName: 'Rasmus Højlund'
    },
    {
      id: 'ev-2',
      type: 'Shot',
      sequence: 2,
      clockSeconds: 305,
      teamName: 'Manchester United',
      playerName: 'Casemiro',
      pitchX: 72,
      pitchY: 50,
      impactScore: 82
    },
    {
      id: 'ev-3',
      type: 'Foul',
      sequence: 3,
      clockSeconds: 480,
      teamName: 'Manchester United',
      playerName: 'Bruno Fernandes',
      pitchX: 42,
      pitchY: 30
    },
    {
      id: 'ev-4',
      type: 'BallRecovery',
      sequence: 4,
      clockSeconds: 915,
      teamName: 'Manchester United',
      playerName: 'Bruno Fernandes',
      pitchX: 68,
      pitchY: 62
    },
    {
      id: 'ev-5',
      type: 'ProgressivePass',
      sequence: 5,
      clockSeconds: 1240,
      teamName: 'Manchester United',
      playerName: 'Marcus Rashford',
      pitchX: 55,
      pitchY: 20,
      endX: 85,
      endY: 45
    },
    {
      id: 'ev-6',
      type: 'Goal',
      sequence: 6,
      clockSeconds: 1540,
      teamName: 'Liverpool',
      playerName: 'Mohamed Salah',
      pitchX: 92,
      pitchY: 52,
      impactScore: 98
    },
    {
      id: 'ev-7',
      type: 'YellowCard',
      sequence: 7,
      clockSeconds: 2200,
      teamName: 'Liverpool',
      playerName: 'Trent Alexander-Arnold',
      pitchX: 60,
      pitchY: 85
    }
  ];

  it('projects observed events into cards and selects a specialist agent without inventing commentary', () => {
    const items = generateAiCommentaryFromEvents(sampleEvents, 'Manchester United', 'Liverpool');

    expect(items.length).toBeGreaterThanOrEqual(7);

    // Goal event metadata stays source-bounded; the agent supplies the narrative.
    const goalItem = items.find(i => i.category === 'goal');
    expect(goalItem).toBeDefined();
    expect(goalItem?.headline).toContain('Goal');
    expect(goalItem?.playerName).toBe('Mohamed Salah');
    expect(goalItem?.commentaryText).toBe('');
    expect(goalItem?.aiAgent.name).toBe('ChanceCreationAnalyst');

    // Shot commentary check
    const shotItem = items.find(i => i.category === 'shot');
    expect(shotItem).toBeDefined();
    expect(shotItem?.headline).toContain('Shot');
    expect(shotItem?.playerName).toBe('Casemiro');
    expect(shotItem?.aiAgent.name).toBe('ChanceCreationAnalyst');

    // Ball Recovery check
    const recoveryItem = items.find(i => i.category === 'recovery');
    expect(recoveryItem).toBeDefined();
    expect(recoveryItem?.headline).toContain('BallRecovery');
    expect(recoveryItem?.playerName).toBe('Bruno Fernandes');
    expect(recoveryItem?.aiAgent.name).toBe('HighPressAnalyst');

    // Progressive pass check
    const passItem = items.find(i => i.category === 'pass');
    expect(passItem).toBeDefined();
    expect(passItem?.playerName).toBe('Marcus Rashford');
    expect(passItem?.aiAgent.name).toBe('PositionalPlayArchitect');

    // Card check
    const cardItem = items.find(i => i.category === 'card');
    expect(cardItem).toBeDefined();
    expect(cardItem?.playerName).toBe('Trent Alexander-Arnold');
    expect(cardItem?.aiAgent.name).toBe('MatchStateStrategist');
  });

  it('renders the AI Commentary Timeline and syncs active card with selectedSeconds', async () => {
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    try {
      await act(async () => {
        root.render(
          <AiCommentaryTimeline
            events={sampleEvents}
            matchId="test-sync-match-1"
            selectedSeconds={915}
            homeTeam="Manchester United"
            awayTeam="Liverpool"
          />
        );
      });

      // Renders main headers and badges
      expect(container.textContent).toContain('Match commentary');
      expect(container.textContent).toContain('AI agent commentary');
      expect(container.textContent).toContain('Manchester United vs Liverpool');

      // The vertical rail exists
      expect(container.querySelector('.commentary-vertical-axis-rail')).not.toBeNull();

      // Find active synced card at 915s (15:15)
      const activeCard = container.querySelector('.commentary-node-card.is-synced-current');
      expect(activeCard).not.toBeNull();
      expect(activeCard?.textContent).toContain('ACTIVE MATCH TIME');
      expect(activeCard?.textContent).toContain('15:15');
      expect(activeCard?.textContent).toContain('BallRecovery');
      expect(activeCard?.textContent).toContain('Bruno Fernandes');
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('seeks the shared match timeline when selecting a commentary item', async () => {
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    const onTimeSelect = vi.fn();

    try {
      await act(async () => {
        root.render(
          <AiCommentaryTimeline
            events={sampleEvents}
            matchId="test-sync-match-2"
            selectedSeconds={0}
            homeTeam="Manchester United"
            awayTeam="Liverpool"
            onTimeSelect={onTimeSelect}
          />
        );
      });

      const card = container.querySelector<HTMLElement>('.commentary-node-card');
      expect(card).not.toBeNull();

      await act(async () => {
        card?.click();
      });

      expect(onTimeSelect).toHaveBeenCalled();
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('shows a simple commentary feed without category filters', async () => {
    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    try {
      await act(async () => {
        root.render(
          <AiCommentaryTimeline
            events={sampleEvents}
            matchId="test-filter-match"
            selectedSeconds={3000}
            homeTeam="Manchester United"
            awayTeam="Liverpool"
          />
        );
      });

      const renderedCards = container.querySelectorAll('.commentary-node-card');
      expect(renderedCards.length).toBeGreaterThan(0);
      expect(container.querySelector('.commentary-filter-toolbar')).toBeNull();
      expect(container.querySelector('.commentary-controls-group')).toBeNull();
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });
});

describe('HomePage Loaded State, Hidden Buttons, and Candidate Simulation Footer', () => {
  const matchId = 'b2c3d4e5-f6a7-8901-bcde-f12345678901';

  beforeEach(() => {
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    window.history.replaceState(null, '', '/');
  });

  it('hides Choose Match heading and Refresh matches button once the match is loaded', async () => {
    const candidateStatus = {
      matchId,
      isStreaming: true,
      currentSequence: 45,
      currentClockValue: 4269,
      period: 2,
      sourceSha256: 'a'.repeat(64),
      sourceStatus: 'candidate_not_canonical',
      coordinateStatus: 'unverified',
      playerMetricsStatus: 'unvalidated'
    };

    const candidateEvents = [
      {
        eventId: 'c-1',
        matchId,
        sequence: 1,
        clockSeconds: 0,
        period: 1,
        eventType: 'KickOff',
        rawPayload: { teamName: 'Manchester United', playerName: 'Rasmus Højlund' }
      },
      {
        eventId: 'c-2',
        matchId,
        sequence: 2,
        clockSeconds: 300,
        period: 1,
        eventType: 'Shot',
        rawPayload: { teamName: 'Manchester United', playerName: 'Casemiro' }
      }
    ];

    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const path = new URL(String(input), 'http://localhost').pathname;
      if (path.endsWith('/simulation/candidate/status')) {
        return Response.json(candidateStatus);
      }
      if (path.endsWith('/events')) {
        return Response.json({ matchId, sourceSha256: 'a'.repeat(64), items: candidateEvents, hasMore: false });
      }
      if (path.endsWith('/canonical/fixtures')) {
        return Response.json({ items: [], nextMatchId: null, hasMore: false });
      }
      return Response.json({ status: 404 }, { status: 404 });
    }));

    window.history.replaceState(null, '', `/home?matchId=${matchId}&atSeconds=4269`);

    const container = document.createElement('div');
    document.body.append(container);
    const root = createRoot(container);

    try {
      await act(async () => {
        root.render(<RoutePage route={{ kind: 'home' }} search={`?matchId=${matchId}&atSeconds=4269`} />);
        await new Promise(resolve => setTimeout(resolve, 60));
      });

      // "Choose a match" heading MUST BE HIDDEN once match is loaded
      expect(container.textContent).not.toContain('Choose a match');

      // "Refresh matches" button MUST BE HIDDEN once match is loaded
      const refreshBtn = container.querySelector<HTMLButtonElement>('.refresh-matches');
      expect(refreshBtn).toBeNull();

      // Match events and commentary are visible by default.
      expect(container.querySelector('.linear-events-track')).not.toBeNull();
      expect(container.querySelector('.ai-commentary-timeline-container')).not.toBeNull();
      expect(container.textContent).toContain('Match commentary');

      // Candidate simulation portion is placed in the footer
      const footer = container.querySelector<HTMLElement>('footer.candidate-simulation-footer');
      expect(footer).not.toBeNull();
      expect(footer?.textContent).toContain('Candidate simulation');
      expect(footer?.textContent).toContain('Not canonical');
      expect(footer?.textContent).toContain('Live match feed');
      expect(footer?.textContent).toContain(matchId);
      expect(footer?.textContent).toContain('Source events and player positions are shown');

      const openLink = footer?.querySelector<HTMLAnchorElement>('a.primary-link');
      expect(openLink?.getAttribute('href')).toContain(`/candidate-live?matchId=${matchId}`);
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });
});
