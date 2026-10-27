import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { TacticalInsightsDashboard } from './TacticalInsightsDashboard';
import type { MatchContext, ContextEvent } from './MatchContextPages';
import * as api from './api';

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe('TacticalInsightsDashboard - Insights Screen', () => {
  const createMockEvents = (): ContextEvent[] => [
    {
      id: 'ev-1',
      type: 'pass',
      sequence: 10,
      clockSeconds: 120,
      teamName: 'Home',
      pitchX: 30,
      pitchY: 45,
      endX: 45,
      endY: 45
    },
    {
      id: 'ev-2',
      type: 'recovery',
      sequence: 20,
      clockSeconds: 340,
      teamName: 'Home',
      pitchX: 55,
      pitchY: 50
    },
    {
      id: 'ev-3',
      type: 'pass',
      sequence: 30,
      clockSeconds: 600,
      teamName: 'Away',
      pitchX: 20,
      pitchY: 50,
      endX: 40,
      endY: 50
    },
    {
      id: 'ev-4',
      type: 'goal',
      sequence: 50,
      clockSeconds: 1200,
      teamName: 'Home',
      playerName: 'Forward 1',
      pitchX: 85,
      pitchY: 50
    },
    {
      id: 'ev-5',
      type: 'recovery',
      sequence: 80,
      clockSeconds: 2800,
      teamName: 'Home',
      pitchX: 60,
      pitchY: 35
    },
    {
      id: 'ev-6',
      type: 'shot',
      sequence: 120,
      clockSeconds: 3800,
      teamName: 'Away',
      pitchX: 80,
      pitchY: 52
    }
  ];

  const mockContext: MatchContext = {
    matchId: 'match-test-insights',
    teams: [
      { id: 'home', name: 'Home FC', score: 1, activePlayers: 11 },
      { id: 'away', name: 'Away FC', score: 0, activePlayers: 11 }
    ],
    homeTeam: { id: 'home', name: 'Home FC', score: 1, activePlayers: 11 },
    awayTeam: { id: 'away', name: 'Away FC', score: 0, activePlayers: 11 },
    players: [
      { id: 'p1', name: 'Midfielder A', role: 'CM', teamId: 'home' },
      { id: 'p2', name: 'Midfielder B', role: 'DM', teamId: 'home' },
      { id: 'p3', name: 'Winger C', role: 'LW', teamId: 'away' }
    ],
    events: createMockEvents(),
    selectedSeconds: 3800,
    throughSequence: 120,
    provenance: 'candidate',
    insights: [],
    candidate: true
  };

  it('removes the duplicate score banner from the insights dashboard', async () => {
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = createRoot(container);

    try {
      await act(async () => {
        root.render(
          <TacticalInsightsDashboard
            context={mockContext}
            seconds={3800}
          />
        );
      });

      // Assert duplicate banner is removed
      expect(container.querySelector('.analyst-match-banner')).toBeNull();

      // Assert scope filter bar is the top bar
      const scopeBar = container.querySelector('.insights-scope-filter-bar');
      expect(scopeBar).not.toBeNull();
      expect(container.textContent).toContain('Home FC vs Away FC');
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('updates match elapsed context (clock, period, key moment, and sparklines) based on elapsed seconds', async () => {
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = createRoot(container);

    try {
      // 1. Render at 15:00 (900 seconds - First Half)
      await act(async () => {
        root.render(
          <TacticalInsightsDashboard
            context={mockContext}
            seconds={900}
          />
        );
      });

      expect(container.textContent).toContain('15:00');
      expect(container.textContent).toContain('First Half');

      // Key moment must be strictly <= 15 minutes (900 seconds)
      const keyMomentClock = container.querySelector('.km-clock-tag')?.textContent;
      expect(keyMomentClock).toBeDefined();
      const kmMin = parseInt(keyMomentClock?.replace("'", "") || '0', 10);
      expect(kmMin).toBeLessThanOrEqual(15);

      // 2. Re-render at 71:09 (4269 seconds - Second Half)
      await act(async () => {
        root.render(
          <TacticalInsightsDashboard
            context={mockContext}
            seconds={4269}
          />
        );
      });

      expect(container.textContent).toContain('71:09');
      expect(container.textContent).toContain('Second Half');

      // Verify the developing chart renders the real-time elapsed scrubber label
      const devChart = container.querySelector('.developing-chart-svg');
      expect(devChart).not.toBeNull();
      expect(devChart?.textContent).toContain('71:09');
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('queries the AI service and updates primary insight when AI responds', async () => {
    const askSpy = vi.spyOn(api, 'askCandidateAnalyst').mockResolvedValueOnce({
      status: 'answered',
      answer: 'Azure AI Foundry confirmed that high touchline pressing traps reduced central line breaks by 72% at 71:09.',
      claims: [
        {
          text: 'Tactical block suppressed central progression to 72% below baseline.',
          evidenceIds: ['ev-2', 'ev-5']
        }
      ],
      matchId: 'match-test-insights',
      selectedSourceClockValue: 4269,
      sourceSha256: 'valid-sha',
      agentName: 'TacticalAnalyst',
      truthClass: 'hypothesis',
      limitations: ['Synthetic test limitation']
    });

    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = createRoot(container);

    try {
      await act(async () => {
        root.render(
          <TacticalInsightsDashboard
            context={mockContext}
            seconds={4269}
          />
        );
      });

      // Wait for debounced query (400ms)
      await act(async () => {
        await new Promise(r => setTimeout(r, 450));
      });

      expect(askSpy).toHaveBeenCalledWith(
        expect.objectContaining({
          matchId: 'match-test-insights',
          selectedSourceClockValue: 4269,
          agentName: 'TacticalAnalyst'
        })
      );

      // Verify AI answer rendered in the primary insight headline and narrative
      expect(container.textContent).toContain('Tactical block suppressed central progression');
      expect(container.textContent).toContain('Azure AI Foundry confirmed');
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });

  it('opens AI Deliberation Drawer when secondary insight or Ask AI Analyst is clicked', async () => {
    vi.spyOn(api, 'askCandidateAnalyst').mockResolvedValue({
      status: 'answered',
      answer: 'Detailed tactical breakdown of midfield duel intensity.',
      claims: [],
      matchId: 'match-test-insights',
      selectedSourceClockValue: 2400,
      sourceSha256: 'valid-sha',
      agentName: 'TacticalAnalyst',
      truthClass: 'hypothesis',
      limitations: ['Synthetic test limitation']
    });

    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = createRoot(container);

    try {
      await act(async () => {
        root.render(
          <TacticalInsightsDashboard
            context={mockContext}
            seconds={2400}
          />
        );
      });

      const askButton = container.querySelector('.ask-ai-pill-btn') as HTMLButtonElement;
      expect(askButton).not.toBeNull();

      await act(async () => {
        askButton.click();
      });

      // Verify AI drawer opens
      const drawer = container.querySelector('.ai-analyst-drawer-panel');
      expect(drawer).not.toBeNull();
      expect(drawer?.textContent).toContain('AI Tactical Deliberation');
    } finally {
      await act(async () => root.unmount());
      container.remove();
    }
  });
});
