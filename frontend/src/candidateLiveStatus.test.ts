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
