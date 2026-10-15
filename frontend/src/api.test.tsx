import { afterEach, describe, expect, it, vi } from 'vitest';
import { getHostAccessToken, request } from './api';

describe('host access-token boundary', () => {
  afterEach(() => {
    delete window.pitchMindAuth;
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
});
