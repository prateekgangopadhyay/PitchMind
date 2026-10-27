import { afterEach, expect, it, vi } from 'vitest';

afterEach(() => {
  vi.unstubAllGlobals();
  vi.resetModules();
});

it('does not drop auto-start when the initial status request is already in flight', async () => {
  vi.resetModules();
  const matchId = 'b2c3d4e5-f6a7-8901-bcde-f12345678901';
  const sourceSha256 = 'a'.repeat(64);
  let resolveStatus!: (response: Response) => void;
  const pendingStatus = new Promise<Response>(resolve => { resolveStatus = resolve; });
  const response = (body: unknown) => new Response(JSON.stringify(body), { status: 200, headers: { 'Content-Type': 'application/json' } });
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const url = new URL(String(input), 'http://localhost');
    if (url.pathname.endsWith('/simulation/candidate/status')) return pendingStatus;
    if (url.pathname.endsWith('/simulation/candidate/start')) return response({
      matchId, isStreaming: true, currentSequence: null, currentClockValue: null, period: null, sourceSha256,
      sourceStatus: 'candidate_not_canonical', coordinateStatus: 'unverified', playerMetricsStatus: 'unvalidated'
    });
    throw new Error(`Unexpected request: ${url.pathname}`);
  });
  vi.stubGlobal('fetch', fetchMock);

  const { refreshCandidateStatus } = await import('./candidateLiveStatus');
  const initialRefresh = refreshCandidateStatus();
  const autoStartRefresh = refreshCandidateStatus(true);
  resolveStatus(response({
    matchId, isStreaming: false, currentSequence: null, currentClockValue: null, period: null, sourceSha256,
    sourceStatus: 'candidate_not_canonical', coordinateStatus: 'unverified', playerMetricsStatus: 'unvalidated'
  }));
  await Promise.all([initialRefresh, autoStartRefresh]);

  const paths = fetchMock.mock.calls.map(([input]) => new URL(String(input), 'http://localhost').pathname);
  expect(paths.filter(path => path.endsWith('/simulation/candidate/status'))).toHaveLength(1);
  expect(paths.filter(path => path.endsWith('/simulation/candidate/start'))).toHaveLength(1);
});

it('explains when a signed-in user lacks permission to start the live simulation', async () => {
  vi.resetModules();
  const matchId = 'b2c3d4e5-f6a7-8901-bcde-f12345678901';
  const sourceSha256 = 'a'.repeat(64);
  const response = (body: unknown, status = 200) => new Response(JSON.stringify(body), {
    status, headers: { 'Content-Type': 'application/json' }
  });
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
    const url = new URL(String(input), 'http://localhost');
    if (url.pathname.endsWith('/simulation/candidate/status')) return response({
      matchId, isStreaming: false, currentSequence: null, currentClockValue: null, period: null, sourceSha256,
      sourceStatus: 'candidate_not_canonical', coordinateStatus: 'unverified', playerMetricsStatus: 'unvalidated'
    });
    if (url.pathname.endsWith('/simulation/candidate/start')) return response({
      status: 403, code: 'FORBIDDEN', title: 'Forbidden', correlationId: 'test'
    }, 403);
    throw new Error(`Unexpected request: ${url.pathname}`);
  }));

  const { readCandidateStatus, refreshCandidateStatus } = await import('./candidateLiveStatus');
  await refreshCandidateStatus(true);

  expect(readCandidateStatus().error).toContain('analyst or admin role');
});

it('enqueues events and processes them one by one every second without fast forwarding', async () => {
  vi.useFakeTimers();
  try {
    const { enqueueCandidateEvents, readCandidateStatus, clearCandidateLiveCache } = await import('./candidateLiveStatus');
    clearCandidateLiveCache();
    const matchId = 'b2c3d4e5-f6a7-8901-bcde-f12345678901';
    const events = [
      { eventId: 'e1', matchId, sequence: 1, clockSeconds: 0, period: 1, eventType: 'KickOff', rawPayload: { teamName: 'Team A' } },
      { eventId: 'e2', matchId, sequence: 2, clockSeconds: 1, period: 1, eventType: 'Pass', rawPayload: { teamName: 'Team A' } },
      { eventId: 'e3', matchId, sequence: 3, clockSeconds: 2, period: 1, eventType: 'Pass', rawPayload: { teamName: 'Team A' } },
      { eventId: 'e4', matchId, sequence: 4, clockSeconds: 3, period: 1, eventType: 'Shot', rawPayload: { teamName: 'Team A' } }
    ];

    // Enqueue all 4 events at once
    enqueueCandidateEvents(events, matchId);

    // Initial state: first event is processed immediately
    let snapshot = readCandidateStatus();
    expect(snapshot.currentEvent?.sequence).toBe(1);
    expect(snapshot.currentEvent?.clockSeconds).toBe(0);
    expect(snapshot.queueLength).toBe(3);

    // Advance 1 second -> event 2 is processed
    vi.advanceTimersByTime(1000);
    snapshot = readCandidateStatus();
    expect(snapshot.currentEvent?.sequence).toBe(2);
    expect(snapshot.currentEvent?.clockSeconds).toBe(1);
    expect(snapshot.queueLength).toBe(2);

    // Advance 1 second -> event 3 is processed
    vi.advanceTimersByTime(1000);
    snapshot = readCandidateStatus();
    expect(snapshot.currentEvent?.sequence).toBe(3);
    expect(snapshot.currentEvent?.clockSeconds).toBe(2);
    expect(snapshot.queueLength).toBe(1);

    // Advance 1 second -> event 4 is processed
    vi.advanceTimersByTime(1000);
    snapshot = readCandidateStatus();
    expect(snapshot.currentEvent?.sequence).toBe(4);
    expect(snapshot.currentEvent?.clockSeconds).toBe(3);
    expect(snapshot.queueLength).toBe(0);

    clearCandidateLiveCache();
  } finally {
    vi.useRealTimers();
  }
});

it('deduplicates simultaneous sync requests so multiple components do not trigger duplicate API calls', async () => {
  const matchId = 'b2c3d4e5-f6a7-8901-bcde-f12345678901';
  const sourceSha256 = 'c'.repeat(64);
  const events = [
    { eventId: 'e1', matchId, sequence: 1, clockSeconds: 0, period: 1, eventType: 'KickOff', rawPayload: { teamName: 'Team A' } }
  ];
  let resolveEvents!: (response: Response) => void;
  const pendingEvents = new Promise<Response>(resolve => { resolveEvents = resolve; });
  const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
    const url = new URL(String(input), 'http://localhost');
    if (url.pathname.endsWith('/events')) return pendingEvents;
    return new Response(JSON.stringify({}), { status: 404 });
  });
  vi.stubGlobal('fetch', fetchMock);

  const { syncCandidateEvents, clearCandidateLiveCache } = await import('./candidateLiveStatus');
  clearCandidateLiveCache();

  // Call syncCandidateEvents 3 times simultaneously (as would happen with multiple pages/components mounted)
  const call1 = syncCandidateEvents(matchId);
  const call2 = syncCandidateEvents(matchId);
  const call3 = syncCandidateEvents(matchId);

  resolveEvents(new Response(JSON.stringify({ matchId, sourceSha256, items: events, hasMore: false }), {
    status: 200, headers: { 'Content-Type': 'application/json' }
  }));

  const [res1, res2, res3] = await Promise.all([call1, call2, call3]);

  // All 3 callers receive the exact same data
  expect(res1).toHaveLength(1);
  expect(res2).toHaveLength(1);
  expect(res3).toHaveLength(1);

  // But fetch was called only ONCE!
  const eventCalls = fetchMock.mock.calls.filter(([input]) => new URL(String(input), 'http://localhost').pathname.endsWith('/events'));
  expect(eventCalls).toHaveLength(1);

  clearCandidateLiveCache();
});
