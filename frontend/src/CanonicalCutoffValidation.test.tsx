import { act, createElement } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, expect, it, vi } from 'vitest';
import { App } from './App';

const matchId = '11111111-1111-1111-1111-111111111111';
const invalidCutoffs: Array<[string, 'snapshot' | 'eventsPage' | 'insightsPage' | 'eventSequence' | 'eventClock' | 'insightSequence', unknown]> = [
  ['null snapshot watermark', 'snapshot', null],
  ['string snapshot watermark', 'snapshot', '19'],
  ['fractional snapshot watermark', 'snapshot', 19.5],
  ['negative snapshot watermark', 'snapshot', -1],
  ['null event page watermark', 'eventsPage', null],
  ['null insight page watermark', 'insightsPage', null],
  ['null event sequence', 'eventSequence', null],
  ['string event sequence', 'eventSequence', '18'],
  ['fractional event sequence', 'eventSequence', 18.5],
  ['negative event sequence', 'eventSequence', -1],
  ['null event clock', 'eventClock', null],
  ['string event clock', 'eventClock', '3300'],
  ['fractional event clock', 'eventClock', 3300.5],
  ['negative event clock', 'eventClock', -1],
  ['null insight sequence cutoff', 'insightSequence', null],
  ['string insight sequence cutoff', 'insightSequence', '19'],
  ['fractional insight sequence cutoff', 'insightSequence', 19.5],
  ['negative insight sequence cutoff', 'insightSequence', -1]
];

afterEach(() => {
  vi.unstubAllGlobals();
  window.history.replaceState(null, '', '/');
});

it.each(invalidCutoffs)('fails closed for %s', async (_label, target, value) => {
  const snapshot = {
    matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3309, asOfSeconds: 3309, throughSequence: 19,
    algorithmVersion: 'projection-v1', sourceSha256: 'a'.repeat(64), stateSha256: 'b'.repeat(64),
    provenance: 'synthetic-approved', period: 2, phase: 'secondhalf', homeScore: 1, awayScore: 0,
    possessionTeamId: null, homeActivePlayers: 11, awayActivePlayers: 11, players: []
  };
  const event = {eventId: 'candidate-event', matchId, sequence: 18, type: 'goal', clockSeconds: 3300, period: 2, occurredAt: '2026-01-01T00:55:00Z'};
  const insight = {insightId: 'candidate-insight', matchId, canonicalRevision: 4, projectionRevision: 4,
    selectedTimeSeconds: 3309, throughSequence: 19, detectorVersion: 'test-v1', truthClass: 'interpretation',
    summary: 'Must not be rendered', evidence: []};
  const eventPage = {matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3309,
    throughSequence: 19, items: [event], nextCursor: null, hasMore: false};
  const insightPage = {matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3309,
    throughSequence: 19, items: [insight], nextCursor: null, hasMore: false};
  if (target === 'snapshot') snapshot.throughSequence = value as number;
  if (target === 'eventsPage') eventPage.throughSequence = value as number;
  if (target === 'insightsPage') insightPage.throughSequence = value as number;
  if (target === 'eventSequence') event.sequence = value as number;
  if (target === 'eventClock') event.clockSeconds = value as number;
  if (target === 'insightSequence') insight.throughSequence = value as number;

  vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
  window.history.replaceState(null, '', `/matches/${matchId}?matchId=${matchId}&atSeconds=3309&revision=4`);
  vi.stubGlobal('fetch', vi.fn(async (url: string) => {
    const parsed = new URL(url, 'http://localhost');
    if (parsed.pathname.endsWith('/key-moments')) return {ok: false, status: 503, json: async () => ({status: 503, code: 'DEPENDENCY_UNAVAILABLE'})};
    if (parsed.pathname.endsWith('/frames')) return {ok: true, json: async () => ({matchId, canonicalRevision: 4, projectionRevision: 4,
      selectedTimeSeconds: 3309, throughSequence: snapshot.throughSequence, items: [], nextCursor: null, hasMore: false})};
    const body = parsed.pathname.endsWith('/summary') ? snapshot
      : parsed.pathname.endsWith('/events') ? eventPage : insightPage;
    return {ok: true, json: async () => body};
  }));
  const container = document.createElement('div');
  const root = createRoot(container);
  try {
    await act(async () => { root.render(createElement(App)); });
    expect(container.textContent).toContain('This match data does not match the selected moment.');
    expect(container.textContent).not.toContain('Must not be rendered');
    expect(container.textContent).not.toContain('candidate-event');
  } finally {
    await act(async () => root.unmount());
  }
});
