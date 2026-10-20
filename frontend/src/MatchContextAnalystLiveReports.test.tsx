import { act } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, expect, it, vi } from 'vitest';
import { MatchContextAnalystPage } from './MatchContextPages';

afterEach(() => {
  vi.unstubAllGlobals();
  window.history.replaceState(null, '', '/');
});

it('automatically generates a report when the live candidate cursor reaches a notable event', async () => {
  const matchId = 'b2c3d4e5-f6a7-8901-bcde-f12345678901';
  const sourceSha256 = 'a'.repeat(64);
  const events = [
    ['KickOff', 0], ['Shot', 5], ['Foul', 8], ['PossessionLost', 10], ['ProgressivePass', 14], ['BallRecovery', 15]
  ].map(([eventType, clockSeconds], index) => ({
    eventId: `source-event-${index + 1}`, matchId, sequence: index + 1, eventType, clockSeconds, period: 1,
    rawPayload: { teamName: 'Synthetic FC' }
  }));
  const fetcher = vi.fn(async (input: RequestInfo | URL) => {
    const url = new URL(String(input), 'http://localhost');
    if (url.pathname.endsWith('/simulation/candidate/status')) return Response.json({
      matchId, isStreaming: true, currentSequence: 6, currentClockValue: 15, period: 1, sourceSha256
    });
    if (url.pathname.endsWith('/analyst/agents')) return Response.json({ defaultAgent: 'TacticalAnalyst', agents: ['TacticalAnalyst'] });
    if (url.pathname.endsWith('/analyst/questions')) return Response.json({
      status: 'answered', answer: 'The source records a ball recovery; the sequence alone does not establish its tactical cause.',
      claims: [{ text: 'The source records a ball recovery; the sequence alone does not establish its tactical cause.', evidenceIds: ['source-event-6'] }],
      matchId, selectedSourceClockValue: 15, sourceSha256, agentName: 'TacticalAnalyst', truthClass: 'hypothesis', limitations: ['Synthetic evidence only.']
    });
    if (url.pathname.endsWith('/events')) return Response.json({ matchId, sourceSha256, items: events, hasMore: false });
    return Response.json({ status: 404, code: 'NOT_FOUND' }, { status: 404 });
  });
  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  vi.stubGlobal('fetch', fetcher);
  const container = document.createElement('div');
  document.body.append(container);
  const root = createRoot(container);
  try {
    await act(async () => {
      root.render(<MatchContextAnalystPage route={{ kind: 'analyst' }} matchId={matchId} seconds={16} />);
      await new Promise(resolve => setTimeout(resolve, 30));
    });
    expect(container.textContent).toContain('Reports from notable events');
    expect(container.textContent).toContain('BallRecovery');
    expect(container.textContent).toContain('The source records a ball recovery');
    expect(fetcher.mock.calls.some(([input]) => new URL(String(input), 'http://localhost').pathname.endsWith('/analyst/questions'))).toBe(true);
  } finally {
    await act(async () => root.unmount());
    container.remove();
  }
});
