import { afterEach, describe, expect, it, vi } from 'vitest';
import { askCandidateAnalyst, clearAllDevData, getCandidateAnalystAgents, getHostAccessToken, request, setStoredAuthSession } from './api';

describe('host access-token boundary', () => {
  afterEach(() => {
    delete window.pitchMindAuth;
    window.sessionStorage.removeItem('pitchmind_guest_access_token');
    setStoredAuthSession(null);
    vi.unstubAllGlobals();
  });

  it('uses the host token provider for same-origin BFF requests without persisting credentials', async () => {
    window.pitchMindAuth = { getAccessToken: vi.fn(async () => 'short-lived-host-token') };
    const fetcher = vi.fn<typeof fetch>(async () => Response.json({ ready: true }));
    vi.stubGlobal('fetch', fetcher);

    await expect(request<{ready: boolean}>('canonical/fixtures')).resolves.toEqual({ ready: true });

    expect(window.pitchMindAuth.getAccessToken).toHaveBeenCalledOnce();
    expect(new Headers(fetcher.mock.calls[0][1]?.headers).get('Authorization')).toBe('Bearer short-lived-host-token');
    expect(window.sessionStorage.getItem('accessToken')).toBeNull();
    expect(window.localStorage.getItem('accessToken')).toBeNull();
  });

  it('does not synthesize a token when the host has no signed-in identity', async () => {
    const fetcher = vi.fn<typeof fetch>(async () => Response.json({ ready: true }));
    vi.stubGlobal('fetch', fetcher);

    await request('canonical/fixtures');

    await expect(getHostAccessToken()).resolves.toBeNull();
    expect(new Headers(fetcher.mock.calls[0][1]?.headers).has('Authorization')).toBe(false);
  });

  it('loads the project agents and sends the selected specialist with the match question', async () => {
    const fetcher = vi.fn<typeof fetch>()
      .mockResolvedValueOnce(Response.json({ defaultAgent: 'TacticalAnalyst', agents: ['TacticalAnalyst', 'GoalkeeperAnalyst'] }))
      .mockResolvedValueOnce(Response.json({ status: 'answered', answer: 'Evidence-bound answer.', claims: [],
        matchId: 'match-1', selectedSourceClockValue: 32, sourceSha256: 'abc', agentName: 'GoalkeeperAnalyst',
        truthClass: 'hypothesis', limitations: [] }));
    vi.stubGlobal('fetch', fetcher);

    await expect(getCandidateAnalystAgents()).resolves.toEqual({
      defaultAgent: 'TacticalAnalyst', agents: ['TacticalAnalyst', 'GoalkeeperAnalyst']
    });
    const result = await askCandidateAnalyst({ matchId: 'match-1', selectedSourceClockValue: 32,
      audience: 'analyst', question: 'What does the evidence show?', agentName: 'GoalkeeperAnalyst' });

    expect(result.agentName).toBe('GoalkeeperAnalyst');
    expect(JSON.parse(String(fetcher.mock.calls[1][1]?.body)).agentName).toBe('GoalkeeperAnalyst');
  });

  it('sends the signed guest token and limits guest questions to the fan audience', async () => {
    window.sessionStorage.setItem('pitchmind_guest_access_token', 'pmguest.v1.signed-demo-token');
    setStoredAuthSession({
      success: true,
      token: '',
      authenticatedAt: new Date().toISOString(),
      user: { name: 'Guest', email: '', role: 'Demo viewer', permissions: ['MatchRead'], provider: 'guest' }
    });
    const fetcher = vi.fn<typeof fetch>(async () => Response.json({
      status: 'answered', answer: 'Fan-safe answer.', claims: [], matchId: 'match-1',
      selectedSourceClockValue: 32, sourceSha256: 'abc', agentName: 'TacticalAnalyst',
      truthClass: 'hypothesis', limitations: []
    }));
    vi.stubGlobal('fetch', fetcher);

    await askCandidateAnalyst({ matchId: 'match-1', selectedSourceClockValue: 32,
      audience: 'analyst', question: 'What does the evidence show?', agentName: 'TacticalAnalyst' });

    expect(new Headers(fetcher.mock.calls[0][1]?.headers).get('Authorization'))
      .toBe('Bearer pmguest.v1.signed-demo-token');
    expect(JSON.parse(String(fetcher.mock.calls[0][1]?.body)).audience).toBe('fan');
  });

  it('posts to simulation/clear-data when clearing dev database and cache records', async () => {
    const fetcher = vi.fn<typeof fetch>(async () =>
      Response.json({ cleared: true, clearedCandidateRecords: 12, message: 'Candidate records cleared and replay restarted.' })
    );
    vi.stubGlobal('fetch', fetcher);

    const result = await clearAllDevData();

    expect(result.cleared).toBe(true);
    expect(result.clearedCandidateRecords).toBe(12);
    expect(fetcher.mock.calls[0][0]).toBe('/api/v1/simulation/clear-data');
    expect(fetcher.mock.calls[0][1]?.method).toBe('POST');
  });
});
