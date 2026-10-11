import { request } from './api';
import type { CanonicalLedgerEvent, CanonicalMatchSnapshot, CanonicalTimelineBackfill, RealtimeCursorV1 } from './api';

export type TimelineMode = 'LIVE' | 'HISTORICAL';

export interface RealtimeTimelineState {
  matchId: string;
  mode: TimelineMode;
  selectedTimeSeconds: number;
  canonicalRevision: number;
  throughSequence: number;
  snapshot: CanonicalMatchSnapshot;
  events: CanonicalLedgerEvent[];
  nextCursor: string | null;
  hasMore: boolean;
  needsBackfill: boolean;
  hasNewMatchUpdate: boolean;
  reconnecting: boolean;
}

export function selectHistoricalTime(state: RealtimeTimelineState, selectedTimeSeconds: number): RealtimeTimelineState {
  if (!Number.isInteger(selectedTimeSeconds) || selectedTimeSeconds < 0 || selectedTimeSeconds > 5400)
    throw new RangeError('Selected match time must be between 0 and 5400 seconds.');
  return { ...state, mode: 'HISTORICAL', selectedTimeSeconds, hasNewMatchUpdate: false };
}

export function returnToLive(state: RealtimeTimelineState): RealtimeTimelineState {
  return { ...state, mode: 'LIVE', selectedTimeSeconds: state.snapshot.asOfSeconds, hasNewMatchUpdate: false, needsBackfill: true };
}

/** Cursor messages are wake-ups only. A selected historical view is never moved by live traffic. */
export function observeRealtimeCursor(state: RealtimeTimelineState, cursor: RealtimeCursorV1): RealtimeTimelineState {
  if (cursor.kind !== 'matchUpdated' || cursor.schemaVersion !== 1 || cursor.matchId !== state.matchId ||
      !Number.isInteger(cursor.canonicalRevision) || cursor.canonicalRevision < 1 ||
      !Number.isSafeInteger(cursor.lastSequence) || cursor.lastSequence < 0)
    return state;

  if (state.mode === 'HISTORICAL')
    return { ...state, hasNewMatchUpdate: true };

  const revisionChanged = cursor.canonicalRevision !== state.canonicalRevision;
  const sequenceAdvanced = cursor.lastSequence > state.throughSequence;
  return {
    ...state,
    needsBackfill: state.needsBackfill || revisionChanged || sequenceAdvanced,
    reconnecting: false,
  };
}

/**
 * Applies a durable catch-up page idempotently. A changed revision or an impossible
 * cursor resets the client to the returned canonical page; same-revision updates
 * must be contiguous and cannot introduce events later than selected match time.
 */
export function applyBackfillPage(state: RealtimeTimelineState, response: CanonicalTimelineBackfill): RealtimeTimelineState {
  if (response.snapshot.matchId !== state.matchId || response.events.matchId !== state.matchId ||
      response.snapshot.canonicalRevision !== response.events.canonicalRevision ||
      response.snapshot.selectedTimeSeconds !== response.events.selectedTimeSeconds)
    throw new Error('Backfill snapshot and event page do not share one match-time revision.');

  const reset = response.revisionChanged || response.replayRequired || response.snapshot.canonicalRevision !== state.canonicalRevision;
  const base = reset ? [] : state.events;
  const bySequence = new Map<number, CanonicalLedgerEvent>();
  const byEventId = new Map<string, CanonicalLedgerEvent>();
  for (const event of [...base, ...response.events.items]) {
    if (event.matchId !== state.matchId || event.clockSeconds > response.snapshot.selectedTimeSeconds)
      throw new Error('Backfill contains an event outside the selected match context.');
    const sequenceMatch = bySequence.get(event.sequence);
    const idMatch = byEventId.get(event.eventId);
    if ((sequenceMatch && sequenceMatch.eventId !== event.eventId) || (idMatch && idMatch.sequence !== event.sequence))
      throw new Error('Backfill reuses an event sequence or ID with conflicting canonical identity.');
    bySequence.set(event.sequence, event);
    byEventId.set(event.eventId, event);
  }

  const events = [...bySequence.values()].sort((left, right) => left.sequence - right.sequence);
  const expectedStart = reset ? 0 : state.throughSequence + 1;
  const newEvents = events.filter(event => event.sequence > (reset ? -1 : state.throughSequence));
  if (newEvents.length > 0 && newEvents[0].sequence !== expectedStart)
    return { ...state, needsBackfill: true, reconnecting: true };
  for (let index = 1; index < newEvents.length; index++) {
    if (newEvents[index].sequence !== newEvents[index - 1].sequence + 1)
      return { ...state, needsBackfill: true, reconnecting: true };
  }

  const pageThrough = events.at(-1)?.sequence ?? (reset ? -1 : state.throughSequence);
  const caughtUp = !response.events.hasMore && pageThrough >= response.snapshot.throughSequence;
  return {
    ...state,
    mode: state.mode,
    selectedTimeSeconds: state.mode === 'HISTORICAL' ? state.selectedTimeSeconds : response.snapshot.asOfSeconds,
    canonicalRevision: response.snapshot.canonicalRevision,
    throughSequence: pageThrough,
    snapshot: state.mode === 'HISTORICAL' ? state.snapshot : response.snapshot,
    events,
    nextCursor: response.events.nextCursor,
    hasMore: response.events.hasMore,
    needsBackfill: !caughtUp,
    reconnecting: false,
    hasNewMatchUpdate: state.mode === 'HISTORICAL' ? state.hasNewMatchUpdate : false,
  };
}

export function markRealtimeDisconnected(state: RealtimeTimelineState): RealtimeTimelineState {
  return { ...state, reconnecting: true, needsBackfill: true };
}

export function buildTimelineBackfillPath(state: RealtimeTimelineState): string {
  const atSeconds = state.mode === 'LIVE' ? 5400 : state.selectedTimeSeconds;
  const query = new URLSearchParams({
    afterSequence: String(state.throughSequence),
    atSeconds: String(atSeconds),
    revision: String(state.canonicalRevision),
    limit: '100',
  });
  return `canonical/matches/${encodeURIComponent(state.matchId)}/timeline/backfill?${query.toString()}`;
}

/** Fetches the canonical head when live, while retaining the chosen timestamp for historical refreshes. */
export function fetchTimelineBackfill(state: RealtimeTimelineState, signal?: AbortSignal): Promise<CanonicalTimelineBackfill> {
  return request<CanonicalTimelineBackfill>(buildTimelineBackfillPath(state), signal);
}
