import { act, createElement } from 'react';
import { createRoot } from 'react-dom/client';
import { afterEach, describe, expect, it } from 'vitest';
import { CanonicalEvidenceReplay } from './CanonicalEvidenceReplay';
import type { CanonicalEvidence, CanonicalEvidencePayload, CanonicalInsight } from './api';

const sourceHash = 'a'.repeat(64);
const insight: CanonicalInsight = {
  insightId: 'insight-1', matchId: '11111111-1111-1111-1111-111111111111', canonicalRevision: 4,
  projectionRevision: 4, selectedTimeSeconds: 600, throughSequence: 20, sourceVersion: 'source-v1',
  provenance: 'synthetic-approved', sourceSha256: sourceHash, detectorVersion: 'detector-v1', truthClass: 'pattern',
  summary: 'A recorded mechanism sequence.', conditions: {}, limitations: [], evidence: []
};

function frame(frameId: string, sequence: number, elapsedSeconds: number): CanonicalEvidencePayload {
  return {
    kind: 'frame', id: frameId, relation: 'illustrates', sourceCanonicalRevision: 4, sequence,
    clockSeconds: elapsedSeconds, period: 1,
    payload: {
      schemaVersion: 1, matchId: insight.matchId, frameId, sequence, canonicalRevision: 4,
      matchTime: { period: 1, elapsedSeconds, sourceClockValue: elapsedSeconds, sourceClockUnit: 'seconds', sourceClockBasis: 'match_elapsed' },
      coordinateSystem: { lengthMetres: 105, widthMetres: 68, unit: 'metres', axisOrigin: 'bottom_left',
        teamDirections: [{ teamId: 'team-a', direction: 'left' }, { teamId: 'team-z', direction: 'right' }] },
      ball: { x: 52, y: 34 },
      players: [
        { playerId: 'player-a', teamId: 'team-a', position: { x: 30, y: 20 } },
        { playerId: 'player-z', teamId: 'team-z', position: { x: 71, y: 42 } }
      ],
      source: { providerId: 'synthetic-test', sourceMatchId: 'source-match', sourceRecordId: frameId,
        sourceSequence: sequence, sourceSchemaVersion: 'tracking-v1', sourceSha256: sourceHash,
        provenanceStatus: 'synthetic-owner-declared', rightsReviewStatus: 'approved_for_use' }
    }
  };
}

function event(): CanonicalEvidencePayload {
  return {
    kind: 'event', id: 'event-1', relation: 'supports', sourceCanonicalRevision: 4,
    sequence: 3, clockSeconds: 50, period: 1,
    payload: { schemaVersion: 1, matchId: insight.matchId, canonicalRevision: 4, eventId: 'event-1', sequence: 3,
      matchTime: { period: 1, elapsedSeconds: 50 },
      source: { sourceSha256: sourceHash, provenanceStatus: 'synthetic-owner-declared', rightsReviewStatus: 'approved_for_use' },
      playerId: 'player-a', relatedPlayerId: null }
  };
}

function makeEvidence(items: CanonicalEvidencePayload[]): CanonicalEvidence {
  return { insight: { ...insight, evidence: items.map(item => ({ kind: item.kind, id: item.id, relation: item.relation })) }, items };
}

function mount(container: HTMLElement, evidence: CanonicalEvidence) {
  const root = createRoot(container);
  act(() => root.render(createElement(CanonicalEvidenceReplay, { evidence })));
  return root;
}

afterEach(() => document.body.replaceChildren());

describe('canonical evidence sequence replay', () => {
  it('steps through exact observed frames, preserves source orientation and labels each team direction', () => {
    viActEnvironment();
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = mount(container, makeEvidence([frame('frame-1', 5, 70), frame('frame-2', 7, 90), event()]));
    try {
      expect(container.textContent).toContain('Frame 1 of 2 · 01:10');
      expect(container.querySelectorAll('.evidence-participant')).toHaveLength(1);
      expect(container.textContent).toContain('Team team-a · attacks toward left');
      expect(container.textContent).toContain('Team team-z · attacks toward right');
      expect(container.querySelector('.evidence-participant circle')?.getAttribute('cx')).toBe('30');
      expect(container.querySelector('.evidence-replay-legend')?.textContent).toContain('Team team-a');
      expect(container.querySelectorAll('svg rect.team-marker-1')).toHaveLength(1);
      const buttons = [...container.querySelectorAll('button')];
      expect(buttons.map(button => button.textContent?.trim())).toEqual(['Previous observed frame', 'Next observed frame']);
      expect(buttons.every(button => button.tabIndex >= 0 && button.offsetHeight >= 0)).toBe(true);
      expect(container.querySelector('output[aria-live="polite"]')).not.toBeNull();
      act(() => buttons[1].click());
      expect(container.textContent).toContain('Frame 2 of 2 · 01:30');
      expect(container.querySelector('table caption')?.textContent).toContain('sequence 7');
      expect(container.textContent).not.toContain('Interpolated');
    } finally {
      act(() => root.unmount());
    }
  });

  it('does not render a frame until rights are approved for use', () => {
    viActEnvironment();
    const pending = frame('frame-pending', 5, 70);
    (pending.payload.source as { rightsReviewStatus: string }).rightsReviewStatus = 'pending';
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = mount(container, makeEvidence([pending]));
    try {
      expect(container.textContent).toContain('Tracking replay is unavailable until the source is approved for canonical use.');
      expect(container.querySelector('svg')).toBeNull();
    } finally {
      act(() => root.unmount());
    }
  });

  it('rejects a frame whose source hash or canonical revision differs from its evidence index', () => {
    viActEnvironment();
    const badHash = frame('frame-hash', 5, 70);
    (badHash.payload.source as { sourceSha256: string }).sourceSha256 = 'b'.repeat(64);
    const badRevision = frame('frame-revision', 6, 80);
    (badRevision.payload as { canonicalRevision: number }).canonicalRevision = 3;
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = mount(container, makeEvidence([badHash, badRevision]));
    try {
      expect(container.textContent).toContain('Linked tracking data does not match the selected evidence or has unsupported coordinates. The overlay is hidden.');
      expect(container.querySelector('svg')).toBeNull();
    } finally {
      act(() => root.unmount());
    }
  });

  it('rejects coordinates outside the declared pitch bounds', () => {
    viActEnvironment();
    const invalid = frame('frame-outside-pitch', 5, 70);
    (invalid.payload.players as Array<{ position: { x: number; y: number } }>)[0].position.x = 106;
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = mount(container, makeEvidence([invalid]));
    try {
      expect(container.textContent).toContain('Linked tracking data does not match the selected evidence or has unsupported coordinates. The overlay is hidden.');
      expect(container.querySelector('svg')).toBeNull();
    } finally {
      act(() => root.unmount());
    }
  });

  it('rejects pitch dimensions outside the telemetry contract bounds', () => {
    viActEnvironment();
    const invalid = frame('frame-small-pitch', 5, 70);
    (invalid.payload.coordinateSystem as { lengthMetres: number }).lengthMetres = 80;
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = mount(container, makeEvidence([invalid]));
    try {
      expect(container.textContent).toContain('Linked tracking data does not match the selected evidence or has unsupported coordinates. The overlay is hidden.');
      expect(container.querySelector('svg')).toBeNull();
    } finally {
      act(() => root.unmount());
    }
  });

  it('hides malformed player and direction entries without throwing', () => {
    viActEnvironment();
    const malformedPlayer = frame('frame-null-player', 5, 70);
    (malformedPlayer.payload.players as unknown[]).push(null);
    const malformedDirection = frame('frame-null-direction', 6, 80);
    (malformedDirection.payload.coordinateSystem as { teamDirections: unknown[] }).teamDirections.push(null);
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = mount(container, makeEvidence([malformedPlayer, malformedDirection]));
    try {
      expect(container.textContent).toContain('Linked tracking data does not match the selected evidence or has unsupported coordinates. The overlay is hidden.');
      expect(container.querySelector('svg')).toBeNull();
    } finally {
      act(() => root.unmount());
    }
  });

  it('shows a truthful no-frame state when the selected insight links only events', () => {
    viActEnvironment();
    const container = document.createElement('div');
    document.body.appendChild(container);
    const root = mount(container, makeEvidence([event()]));
    try {
      expect(container.textContent).toContain('No observed tracking frame is linked to this insight. A tactical overlay is unavailable.');
      expect(container.querySelector('svg')).toBeNull();
    } finally {
      act(() => root.unmount());
    }
  });
});

function viActEnvironment() {
  (globalThis as typeof globalThis & { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT = true;
}
