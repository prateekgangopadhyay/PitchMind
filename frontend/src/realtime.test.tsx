import { describe, expect, it } from 'vitest';
import { applyBackfillPage, buildTimelineBackfillPath, markRealtimeDisconnected, observeRealtimeCursor, returnToLive, selectHistoricalTime } from './realtime';
import type { CanonicalEventPage, CanonicalLedgerEvent, CanonicalMatchSnapshot, CanonicalTimelineBackfill } from './api';
import type { RealtimeTimelineState } from './realtime';

const snapshot = (revision = 2, throughSequence = 2, asOfSeconds = 120, selectedTimeSeconds = asOfSeconds): CanonicalMatchSnapshot => ({
  matchId: 'match-a', canonicalRevision: revision, selectedTimeSeconds, asOfSeconds, throughSequence,
  algorithmVersion: 'rules-v1', sourceSha256: 'a'.repeat(64), stateSha256: 'b'.repeat(64), provenance: 'synthetic',
  period: 1, phase: 'firsthalf', homeScore: 0, awayScore: 0, possessionTeamId: null,
  homeActivePlayers: 11, awayActivePlayers: 11, players: [],
});

const event = (sequence: number, clockSeconds = sequence * 60): CanonicalLedgerEvent => ({
  eventId: `event-${sequence}`, matchId: 'match-a', sequence, type: 'pass', clockSeconds, period: 1,
  occurredAt: new Date(clockSeconds * 1000).toISOString(),
});

const state = (): RealtimeTimelineState => ({
  matchId: 'match-a', mode: 'LIVE', selectedTimeSeconds: 60, observedLiveRevision: 2, observedLiveSequence: 0,
  canonicalRevision: 2, throughSequence: 0,
  snapshot: snapshot(2, 0, 60, 5400), events: [event(0)], nextCursor: null, hasMore: false,
  needsBackfill: false, hasNewMatchUpdate: false, reconnecting: false,
});

const backfill = (items: CanonicalLedgerEvent[], options: Partial<CanonicalTimelineBackfill> = {}): CanonicalTimelineBackfill => {
  const current = snapshot(2, 2, 120, 5400);
  const page: CanonicalEventPage = {
    matchId: 'match-a', canonicalRevision: current.canonicalRevision, selectedTimeSeconds: current.selectedTimeSeconds,
    throughSequence: current.throughSequence, items, nextCursor: null, hasMore: false,
  };
  return { snapshot: current, events: page, revisionChanged: false, replayRequired: false, requestedAfterSequence: 0, ...options };
};

describe('realtime reconnect state', () => {
  it('marks a disconnected client stale and requests durable catch-up after reconnect', () => {
    const disconnected = markRealtimeDisconnected(state());
    expect(disconnected.reconnecting).toBe(true);
    expect(disconnected.needsBackfill).toBe(true);
  });

  it('uses cursor notifications only to trigger catch-up when live', () => {
    const next = observeRealtimeCursor(state(), {
      kind: 'matchUpdated', matchId: 'match-a', canonicalRevision: 2, lastSequence: 2,
      serverUtc: '2026-10-11T00:00:00Z', schemaVersion: 1,
    });
    expect(next.needsBackfill).toBe(true);
    expect(next.throughSequence).toBe(0);
    expect(next.snapshot.homeScore).toBe(0);
  });

  it('keeps historical selection fixed and shows a nonintrusive update indicator', () => {
    const historical = selectHistoricalTime(state(), 45);
    const next = observeRealtimeCursor(historical, {
      kind: 'matchUpdated', matchId: 'match-a', canonicalRevision: 3, lastSequence: 7,
      serverUtc: '2026-10-11T00:00:00Z', schemaVersion: 1,
    });
    expect(next.mode).toBe('HISTORICAL');
    expect(next.selectedTimeSeconds).toBe(45);
    expect(next.hasNewMatchUpdate).toBe(true);
    expect(next.snapshot.canonicalRevision).toBe(2);
  });

  it('ignores duplicate and stale cursors when deciding whether newer match state exists', () => {
    const caughtUpLive = applyBackfillPage(state(), backfill([event(1), event(2)]));
    const historicalView = selectHistoricalTime(caughtUpLive, 45);
    const historical = applyBackfillPage(historicalView, backfill([event(0)], {
      snapshot: snapshot(2, 0, 0, 45),
      events: { ...backfill([]).events, selectedTimeSeconds: 45, throughSequence: 0, items: [event(0)] },
    }));
    const duplicate = observeRealtimeCursor(historical, {
      kind: 'matchUpdated', matchId: 'match-a', canonicalRevision: 2, lastSequence: 2,
      serverUtc: '2026-10-11T00:00:00Z', schemaVersion: 1,
    });
    const stale = observeRealtimeCursor(historical, {
      kind: 'matchUpdated', matchId: 'match-a', canonicalRevision: 2, lastSequence: 1,
      serverUtc: '2026-10-11T00:00:00Z', schemaVersion: 1,
    });
    const newer = observeRealtimeCursor(historical, {
      kind: 'matchUpdated', matchId: 'match-a', canonicalRevision: 2, lastSequence: 3,
      serverUtc: '2026-10-11T00:00:00Z', schemaVersion: 1,
    });

    expect(duplicate.hasNewMatchUpdate).toBe(false);
    expect(stale).toBe(historical);
    expect(newer.hasNewMatchUpdate).toBe(true);
    const live = state();
    expect(observeRealtimeCursor(live, {
      kind: 'matchUpdated', matchId: 'match-a', canonicalRevision: 2, lastSequence: 0,
      serverUtc: '2026-10-11T00:00:00Z', schemaVersion: 1,
    })).toMatchObject({ throughSequence: 0, needsBackfill: false, reconnecting: false });
  });

  it('switches to the latest observed match clock only on explicit return to live', () => {
    const live = returnToLive(selectHistoricalTime(state(), 45));
    expect(live.mode).toBe('LIVE');
    expect(live.selectedTimeSeconds).toBe(60);
    expect(live.needsBackfill).toBe(true);
  });

  it('requests the canonical head in live mode and the exact selected timestamp in historical mode', () => {
    const historical = selectHistoricalTime(state(), 45);
    expect(buildTimelineBackfillPath(historical)).toContain('atSeconds=45');
    expect(buildTimelineBackfillPath(returnToLive(historical))).toContain('atSeconds=5400');
    expect(buildTimelineBackfillPath(historical)).toContain('afterSequence=-1');
    expect(buildTimelineBackfillPath(historical)).toContain('revision=2');
  });

  it('replaces live events and snapshot with the selected historical context', () => {
    const live = { ...state(), events: [event(0), event(1, 120), event(2, 240)], throughSequence: 2,
      snapshot: snapshot(2, 2, 240, 5400) };
    const historical = selectHistoricalTime(live, 45);
    const page = backfill([event(0)], { snapshot: snapshot(2, 0, 0, 45),
      events: { ...backfill([]).events, selectedTimeSeconds: 45, throughSequence: 0, items: [event(0)] } });

    const selected = applyBackfillPage(historical, page);

    expect(selected.mode).toBe('HISTORICAL');
    expect(selected.selectedTimeSeconds).toBe(45);
    expect(selected.snapshot.selectedTimeSeconds).toBe(45);
    expect(selected.snapshot.asOfSeconds).toBe(0);
    expect(selected.events.map(item => item.sequence)).toEqual([0]);
    expect(selected.throughSequence).toBe(0);
    expect(selected.needsBackfill).toBe(false);
  });

  it('returns to the last known live time after applying a historical backfill', () => {
    const caughtUpLive = applyBackfillPage(state(), backfill([event(1), event(2)]));
    const historical = selectHistoricalTime(caughtUpLive, 45);
    const historicalPage = backfill([event(0)], {
      snapshot: snapshot(2, 0, 0, 45),
      events: { ...backfill([]).events, selectedTimeSeconds: 45, throughSequence: 0, items: [event(0)] },
    });

    const selected = applyBackfillPage(historical, historicalPage);
    const returned = returnToLive(selected);

    expect(selected.snapshot.asOfSeconds).toBe(0);
    expect(returned.mode).toBe('LIVE');
    expect(returned.selectedTimeSeconds).toBe(120);
    expect(returned.needsBackfill).toBe(true);
  });

  it('merges duplicate catch-up pages idempotently and advances the cursor contiguously', () => {
    const first = applyBackfillPage(state(), backfill([event(1), event(2)]));
    expect(first.events.map(item => item.sequence)).toEqual([0, 1, 2]);
    expect(first.throughSequence).toBe(2);
    expect(first.needsBackfill).toBe(false);
    const duplicate = applyBackfillPage(first, backfill([event(1), event(2)]));
    expect(duplicate.events.map(item => item.eventId)).toEqual(['event-0', 'event-1', 'event-2']);
  });

  it('resets on a canonical revision change and requests the remaining replay pages', () => {
    const changed = backfill([event(0), event(1)], { snapshot: snapshot(3, 2, 120, 5400), revisionChanged: true,
      events: { ...backfill([]).events, canonicalRevision: 3, items: [event(0), event(1)], hasMore: true, nextCursor: 'next' } });
    const next = applyBackfillPage(state(), changed);
    expect(next.canonicalRevision).toBe(3);
    expect(next.events.map(item => item.sequence)).toEqual([0, 1]);
    expect(next.throughSequence).toBe(1);
    expect(next.hasMore).toBe(true);
    expect(next.needsBackfill).toBe(true);
  });

  it('does not advance state across a sequence gap or admit future events', () => {
    const gap = applyBackfillPage(state(), backfill([event(2)]));
    expect(gap.reconnecting).toBe(true);
    expect(gap.throughSequence).toBe(0);
    const selected = selectHistoricalTime(state(), 120);
    const futureEvent = event(1, 121);
    const futurePage = backfill([futureEvent], { snapshot: snapshot(2, 2, 120, 120),
      events: { ...backfill([]).events, selectedTimeSeconds: 120, items: [futureEvent] } });
    expect(() => applyBackfillPage(selected, futurePage)).toThrow(/outside the selected match context/);
  });
});
