import { act, createElement } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { CanonicalEvidenceDisclosure } from './CanonicalEvidenceDisclosure';
import type { CanonicalEvidence, CanonicalInsight } from './api';

const matchId = '11111111-1111-1111-1111-111111111111';
const insight: CanonicalInsight = {
  insightId: 'insight-press-1', matchId, canonicalRevision: 4, projectionRevision: 4, selectedTimeSeconds: 3308,
  throughSequence: 19, sourceVersion: 'synthetic-v1', provenance: 'synthetic-approved',
  sourceSha256: 'a'.repeat(64), detectorVersion: 'press-detector-v2', truthClass: 'pattern',
  summary: 'A recorded pressing sequence is available for inspection.',
  conditions: { sampleSize: 11, selectedTimeSafe: true }, limitations: ['Sparse frame coverage'],
  evidence: [{ kind: 'event', id: 'event-source-1', relation: 'supports' }]
};

function evidenceAt(selectedTimeSeconds: number, currentInsight: CanonicalInsight = insight): CanonicalEvidence {
  return {
    insight: { ...currentInsight, selectedTimeSeconds },
    items: [{ kind: 'event', id: 'event-source-1', relation: 'supports', sourceCanonicalRevision: 4, sequence: 19,
      clockSeconds: 3308, period: 2, payload: { eventId: 'event-source-1' } }]
  };
}

function renderEvidence(container: HTMLElement, selectedSeconds = 3308, selectedInsight = insight) {
  const root = createRoot(container);
  const renderRaw = (time: number) => root.render(createElement(CanonicalEvidenceDisclosure, {
    matchId, selectedSeconds: time, projectionRevision: selectedInsight.projectionRevision, throughSequence: 20,
    insight: { ...selectedInsight, selectedTimeSeconds: time }
  }));
  act(() => renderRaw(selectedSeconds));
  const render = (time: number) => act(() => renderRaw(time));
  return { root, render };
}

async function openDisclosure(container: HTMLElement) {
  const disclosure = container.querySelector('details')!;
  await act(async () => {
    disclosure.open = true;
    disclosure.dispatchEvent(new Event('toggle'));
  });
  return disclosure;
}

async function settle() {
  await act(async () => { await new Promise(resolve => setTimeout(resolve, 0)); });
}

function response(body: unknown) {
  return Promise.resolve({ ok: true, headers: new Headers(), json: async () => body } as Response);
}

function failure(status: number, code: string) {
  return { ok: false, status, headers: new Headers(), json: async () => ({ status, code }) } as Response;
}

afterEach(() => {
  vi.unstubAllGlobals();
  document.body.replaceChildren();
});

describe('Canonical evidence disclosure', () => {
  it('requests evidence at the exact match, selected time and revision, then exposes source lineage accessibly', async () => {
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    let resolveEvidence!: (value: Response) => void;
    const fetchMock = vi.fn((...args: [RequestInfo | URL, RequestInit?]) => {
      void args;
      return new Promise<Response>(resolve => { resolveEvidence = resolve; });
    });
    vi.stubGlobal('fetch', fetchMock);
    const container = document.createElement('div');
    document.body.appendChild(container);
    const { root } = renderEvidence(container);
    try {
      await openDisclosure(container);
      expect(container.textContent).toContain('Loading evidence for the selected match, time and revision…');
      await act(async () => resolveEvidence(await response(evidenceAt(3308))));
      await settle();
      const url = new URL(fetchMock.mock.calls[0][0] as string, 'http://localhost');
      expect(url.pathname).toBe(`/api/v1/canonical/matches/${matchId}/evidence/insight-press-1`);
      expect(url.searchParams.get('atSeconds')).toBe('3308');
      expect(url.searchParams.get('revision')).toBe('4');
      expect(container.textContent).toContain('event event-source-1');
      expect(container.textContent).toContain('55:08');
      expect(container.textContent).toContain('sequence 19');
      expect(container.textContent).toContain('synthetic-approved');
      expect(container.textContent).toContain('press-detector-v2');
      expect(container.textContent).toContain('Stored detector conditions');
      expect(container.textContent).toContain('"sampleSize": 11');
      expect(container.textContent).toContain('Sparse frame coverage');
      expect(container.querySelector('summary')?.textContent).toContain('Why? Inspect evidence for 55:08');
      const sourceLink = container.querySelector('a[aria-label="Open event event-source-1 at 55:08, sequence 19"]');
      expect(sourceLink?.tagName).toBe('A');
      expect(sourceLink?.getAttribute('href')).toBe(`/replay/${matchId}?matchId=${matchId}&atSeconds=3308&revision=4#canonical-event-19`);
      const summary = container.querySelector('summary')!;
      summary.focus();
      expect(document.activeElement).toBe(summary);
    } finally {
      await act(async () => root.unmount());
    }
  });

  it('links each supported event and frame source to its selected-time replay target', async () => {
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    const sourcedInsight: CanonicalInsight = {
      ...insight,
      evidence: [
        { kind: 'event', id: 'event-source-1', relation: 'supports' },
        { kind: 'frame', id: 'frame-source-2', relation: 'supports' }
      ]
    };
    const sourcedEvidence: CanonicalEvidence = {
      insight: sourcedInsight,
      items: [
        { kind: 'event', id: 'event-source-1', relation: 'supports', sourceCanonicalRevision: 4, sequence: 19,
          clockSeconds: 3308, period: 2, payload: { eventId: 'event-source-1' } },
        { kind: 'frame', id: 'frame-source-2', relation: 'supports', sourceCanonicalRevision: 4, sequence: 18,
          clockSeconds: 3308, period: 2, payload: { frameId: 'frame-source-2' } }
      ]
    };
    vi.stubGlobal('fetch', vi.fn(async () => response(sourcedEvidence)));
    const container = document.createElement('div');
    document.body.appendChild(container);
    const { root } = renderEvidence(container, 3308, sourcedInsight);
    try {
      await openDisclosure(container);
      await settle();
      const eventLink = container.querySelector('a[aria-label="Open event event-source-1 at 55:08, sequence 19"]');
      const frameLink = container.querySelector('a[aria-label="Open frame frame-source-2 at 55:08, sequence 18"]');
      expect(eventLink?.getAttribute('href')).toBe(`/replay/${matchId}?matchId=${matchId}&atSeconds=3308&revision=4#canonical-event-19`);
      expect(frameLink?.getAttribute('href')).toBe(`/replay/${matchId}?matchId=${matchId}&atSeconds=3308&revision=4#canonical-frame-18`);
      expect(eventLink?.tagName).toBe('A');
      expect(frameLink?.tagName).toBe('A');
    } finally {
      await act(async () => root.unmount());
    }
  });

  it('uses projection revision for evidence requests while checking canonical revision independently', async () => {
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    const divergentInsight = { ...insight, canonicalRevision: 100, projectionRevision: 5 };
    let requestedAddress = '';
    const fetchMock = vi.fn(async (url: string) => {
      requestedAddress = url;
      return response(evidenceAt(3308, divergentInsight));
    });
    vi.stubGlobal('fetch', fetchMock);
    const container = document.createElement('div');
    document.body.appendChild(container);
    const { root } = renderEvidence(container, 3308, divergentInsight);
    try {
      await openDisclosure(container);
      await settle();
      const requestedUrl = new URL(requestedAddress, 'http://localhost');
      expect(requestedUrl.searchParams.get('revision')).toBe('5');
      expect(container.textContent).toContain('event event-source-1');
      const citation = container.querySelector<HTMLAnchorElement>('.canonical-evidence-content a');
      expect(citation?.getAttribute('href')).toContain('revision=5#canonical-event-19');
    } finally {
      await act(async () => root.unmount());
      vi.unstubAllGlobals();
    }
  });

  it.each(['match', 'canonical revision', 'projection revision', 'time', 'sequence', 'source lineage'])('rejects mismatched %s context without showing evidence', async mismatch => {
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    const mismatchedInsight = { ...insight };
    if (mismatch === 'match') mismatchedInsight.matchId = '22222222-2222-2222-2222-222222222222';
    if (mismatch === 'canonical revision') mismatchedInsight.canonicalRevision = 3;
    if (mismatch === 'projection revision') mismatchedInsight.projectionRevision = 3;
    if (mismatch === 'time') mismatchedInsight.selectedTimeSeconds = 3307;
    if (mismatch === 'sequence') mismatchedInsight.throughSequence = 21;
    if (mismatch === 'source lineage') mismatchedInsight.sourceSha256 = 'b'.repeat(64);
    const fetchMock = vi.fn(async () => response({ ...evidenceAt(3308), insight: mismatchedInsight }));
    vi.stubGlobal('fetch', fetchMock);
    const container = document.createElement('div');
    document.body.appendChild(container);
    const { root } = renderEvidence(container);
    try {
      await openDisclosure(container);
      await settle();
      expect(container.textContent).toContain('No complete evidence matching this match, time and revision is available.');
      expect(container.textContent).not.toContain('event event-source-1');
      expect(container.querySelector('[role="status"]')).not.toBeNull();
    } finally {
      await act(async () => root.unmount());
    }
  });

  it('aborts stale evidence requests when selected match time changes and only renders the current response', async () => {
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    const requests: Array<{ signal?: AbortSignal; resolve: (value: Response) => void }> = [];
    const fetchMock = vi.fn((_url: string, init?: RequestInit) => new Promise<Response>(resolve => {
      requests.push({ signal: init?.signal ?? undefined, resolve });
    }));
    vi.stubGlobal('fetch', fetchMock);
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = createRoot(container);
    const render = (time: number) => root.render(createElement(CanonicalEvidenceDisclosure, {
      matchId, selectedSeconds: time, projectionRevision: insight.projectionRevision, throughSequence: 20,
      insight: { ...insight, selectedTimeSeconds: time }
    }));
    try {
      await act(async () => render(3308));
      await openDisclosure(container);
      expect(requests).toHaveLength(1);
      await act(async () => { render(3309); await new Promise(resolve => setTimeout(resolve, 0)); });
      expect(requests).toHaveLength(2);
      expect(requests[0].signal?.aborted).toBe(true);
      await act(async () => requests[0].resolve(await response(evidenceAt(3308))));
      await act(async () => requests[1].resolve(await response(evidenceAt(3309))));
      await settle();
      expect(container.textContent).toContain('Why? Inspect evidence for 55:09');
      expect(container.textContent).toContain('Selected 55:09');
      expect(container.textContent).not.toContain('Selected 55:08');
    } finally {
      await act(async () => root.unmount());
    }
  });

  it('hides previously loaded evidence immediately when only the selected sequence watermark changes', async () => {
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    let resolveRefresh!: (value: Response) => void;
    const fetchMock = vi.fn()
      .mockImplementationOnce(async () => response(evidenceAt(3308)))
      .mockImplementationOnce(() => new Promise<Response>(resolve => { resolveRefresh = resolve; }));
    vi.stubGlobal('fetch', fetchMock);
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = createRoot(container);
    const render = (watermark: number) => root.render(createElement(CanonicalEvidenceDisclosure, {
      matchId, selectedSeconds: 3308, projectionRevision: insight.projectionRevision, throughSequence: watermark, insight
    }));
    try {
      await act(async () => render(20));
      await openDisclosure(container);
      await settle();
      expect(container.textContent).toContain('event event-source-1');
      await act(async () => { render(21); await new Promise(resolve => setTimeout(resolve, 0)); });
      expect(fetchMock).toHaveBeenCalledTimes(2);
      expect(container.textContent).not.toContain('event event-source-1');
      expect(container.textContent).toContain('Loading evidence for the selected match, time and revision…');
      await act(async () => resolveRefresh(await response(evidenceAt(3308))));
      await settle();
      expect(container.textContent).toContain('event event-source-1');
    } finally {
      await act(async () => root.unmount());
    }
  });

  it('uses a truthful empty state when the insight has no source references', async () => {
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    const noReferences: CanonicalInsight = { ...insight, evidence: [] };
    const empty: CanonicalEvidence = { insight: noReferences, items: [] };
    const fetchMock = vi.fn(async () => response(empty));
    vi.stubGlobal('fetch', fetchMock);
    const container = document.createElement('div');
    document.body.appendChild(container);
    const { root } = renderEvidence(container, 3308, noReferences);
    try {
      await openDisclosure(container);
      await settle();
      expect(container.textContent).toContain('No source evidence is recorded for this insight at the selected time.');
    } finally {
      await act(async () => root.unmount());
    }
  });

  it('reports missing backend evidence and service failure without presenting a claim as verified', async () => {
    vi.stubGlobal('IS_REACT_ACT_ENVIRONMENT', true);
    const fetchMock = vi.fn(async () => failure(404, 'EVIDENCE_UNAVAILABLE'));
    vi.stubGlobal('fetch', fetchMock);
    const container = document.createElement('div');
    document.body.appendChild(container);
    const { root, render } = renderEvidence(container);
    try {
      await openDisclosure(container);
      await settle();
      expect(container.textContent).toContain('No complete evidence is available for this insight at the selected match time.');
      expect(container.querySelector('[role="status"]')).not.toBeNull();
      fetchMock.mockImplementationOnce(async () => failure(503, 'DEPENDENCY_UNAVAILABLE'));
      await act(async () => { render(3310); await new Promise(resolve => setTimeout(resolve, 0)); });
      await settle();
      expect(container.textContent).toContain('Evidence could not be loaded. The match view remains available.');
      expect(container.querySelector('[role="alert"]')).not.toBeNull();
    } finally {
      await act(async () => root.unmount());
    }
  });
});
