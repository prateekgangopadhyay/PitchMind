import { useEffect, useState } from 'react';
import { ApiRequestError, clock, request } from './api';
import type { CanonicalEvidence, CanonicalEvidencePayload, CanonicalInsight } from './api';

type EvidenceState =
  | { key: string; status: 'loading' }
  | { key: string; status: 'unavailable' | 'error'; message: string }
  | { key: string; status: 'loaded'; evidence: CanonicalEvidence };

type ExpectedContext = {
  matchId: string;
  selectedSeconds: number;
  revision: number;
  throughSequence: number;
  insight: CanonicalInsight;
};

export function CanonicalEvidenceDisclosure({ matchId, selectedSeconds, revision, throughSequence, insight }: ExpectedContext) {
  const [expanded, setExpanded] = useState(false);
  const [state, setState] = useState<EvidenceState | null>(null);
  const key = `${matchId}:${selectedSeconds}:${revision}:${throughSequence}:${insight.insightId}`;
  const current = state?.key === key ? state : null;

  useEffect(() => {
    if (!expanded) return;
    const controller = new AbortController();
    setState({ key, status: 'loading' });
    const query = new URLSearchParams({ atSeconds: String(selectedSeconds), revision: String(revision) });
    request<CanonicalEvidence>(
      `canonical/matches/${encodeURIComponent(matchId)}/evidence/${encodeURIComponent(insight.insightId)}?${query}`,
      controller.signal)
      .then(evidence => {
        if (controller.signal.aborted) return;
        if (!canonicalEvidenceMatches(evidence, { matchId, selectedSeconds, revision, throughSequence, insight })) {
          setState({ key, status: 'unavailable', message: 'No complete evidence matching this match, time and revision is available.' });
          return;
        }
        setState({ key, status: 'loaded', evidence });
      })
      .catch(error => {
        if (controller.signal.aborted) return;
        setState(error instanceof ApiRequestError && error.status === 404
          ? { key, status: 'unavailable', message: 'No complete evidence is available for this insight at the selected match time.' }
          : { key, status: 'error', message: 'Evidence could not be loaded. The match view remains available.' });
      });
    return () => controller.abort();
  }, [expanded, key, matchId, selectedSeconds, revision, throughSequence, insight]);

  return <details className="canonical-evidence-disclosure" onToggle={event => setExpanded(event.currentTarget.open)}>
    <summary>Why? Inspect evidence for {clock(insight.selectedTimeSeconds)}</summary>
    {!current || current.status === 'loading'
      ? <p role="status" aria-live="polite">Loading evidence for the selected match, time and revision…</p>
      : current.status === 'loaded'
        ? <EvidenceDetails evidence={current.evidence} />
        : <p role={current.status === 'unavailable' ? 'status' : 'alert'}>{current.message}</p>}
  </details>;
}

function EvidenceDetails({ evidence }: { evidence: CanonicalEvidence }) {
  const { insight } = evidence;
  return <div className="canonical-evidence-content">
    <p className="canonical-evidence-provenance">
      {insight.provenance} · source {insight.sourceVersion} · SHA-256 {insight.sourceSha256} · detector {insight.detectorVersion}
    </p>
    <p>Selected {clock(insight.selectedTimeSeconds)} · revision {insight.canonicalRevision} · sequence through {insight.throughSequence}</p>
    {evidence.items.length === 0
      ? <p role="status">No source evidence is recorded for this insight at the selected time.</p>
      : <>
        <p>{insight.summary}</p>
        <ul className="evidence-list" aria-label="Source evidence">
          {evidence.items.map(item => <EvidenceRow key={`${item.kind}:${item.id}:${item.sequence}`} item={item} />)}
        </ul>
      </>}
  </div>;
}

function EvidenceRow({ item }: { item: CanonicalEvidencePayload }) {
  return <li>
    <strong>{item.kind} {item.id}</strong> · {item.relation}
    <span> · <time dateTime={`PT${item.clockSeconds}S`}>{clock(item.clockSeconds)}</time> · period {item.period} · sequence {item.sequence}</span>
  </li>;
}

function canonicalEvidenceMatches(evidence: CanonicalEvidence, expected: ExpectedContext): boolean {
  const returned = evidence?.insight;
  if (!returned || returned.insightId !== expected.insight.insightId || returned.matchId !== expected.matchId ||
      returned.canonicalRevision !== expected.revision || returned.selectedTimeSeconds !== expected.selectedSeconds ||
      returned.throughSequence !== expected.insight.throughSequence || returned.throughSequence > expected.throughSequence ||
      returned.sourceVersion !== expected.insight.sourceVersion || returned.provenance !== expected.insight.provenance ||
      returned.sourceSha256 !== expected.insight.sourceSha256 || returned.detectorVersion !== expected.insight.detectorVersion ||
      returned.truthClass !== expected.insight.truthClass || returned.summary !== expected.insight.summary ||
      !Array.isArray(returned.evidence) || !Array.isArray(evidence.items)) return false;

  if (returned.evidence.length !== expected.insight.evidence.length ||
      !returned.evidence.every(reference => expected.insight.evidence.some(expectedReference =>
        expectedReference.kind === reference.kind && expectedReference.id === reference.id && expectedReference.relation === reference.relation))) return false;

  if (evidence.items.length !== returned.evidence.length) return false;
  const seen = new Set<string>();
  return evidence.items.every(item => {
    if (!item || !Number.isInteger(item.sequence) || item.sequence < 0 || item.sequence > returned.throughSequence ||
        item.sequence > expected.throughSequence || !Number.isInteger(item.clockSeconds) || item.clockSeconds < 0 ||
        item.clockSeconds > expected.selectedSeconds || !Number.isInteger(item.period) || item.period < 1 ||
        !returned.evidence.some(reference => reference.kind === item.kind && reference.id === item.id && reference.relation === item.relation)) return false;
    const identity = `${item.kind}:${item.id}:${item.relation}`;
    if (seen.has(identity)) return false;
    seen.add(identity);
    return true;
  });
}
