import { useEffect, useState } from 'react';
import { ApiRequestError, clock, provenanceLabel, request } from './api';
import type { CanonicalEventPage, CanonicalInsightPage, CanonicalMatchSnapshot } from './api';
import { contextHref, replaceCurrentMatchContext } from './routes';

type MatchView = {
  snapshot: CanonicalMatchSnapshot;
  events: CanonicalEventPage;
  insights: CanonicalInsightPage;
  keyMomentsStatus: string;
};

const INSIGHT_PAGE_SIZE = 100;
const MAX_INSIGHT_PAGES = 100;

class InvalidInsightPaginationError extends Error {}

async function loadSelectedTimeInsights(
  matchId: string,
  selectedSeconds: number,
  revision: number,
  throughSequence: number,
  signal: AbortSignal
): Promise<CanonicalInsightPage> {
  const items: CanonicalInsightPage['items'] = [];
  const seenInsightIds = new Set<string>();
  const seenCursors = new Set<string>();
  let cursor: string | null = null;
  let lastPage: CanonicalInsightPage | null = null;

  for (let pageIndex = 0; pageIndex < MAX_INSIGHT_PAGES; pageIndex++) {
    const query = new URLSearchParams({
      atSeconds: String(selectedSeconds),
      revision: String(revision),
      limit: String(INSIGHT_PAGE_SIZE)
    });
    if (cursor) query.set('cursor', cursor);
    const page = await request<CanonicalInsightPage>(
      `canonical/matches/${encodeURIComponent(matchId)}/insights?${query}`, signal);
    const pageContextIsValid = page.matchId === matchId && page.canonicalRevision === revision &&
      page.selectedTimeSeconds === selectedSeconds && Number.isSafeInteger(page.throughSequence) &&
      page.throughSequence >= 0 && page.throughSequence === throughSequence;
    if (!pageContextIsValid || !Array.isArray(page.items) || typeof page.hasMore !== 'boolean')
      throw new InvalidInsightPaginationError('Match data is outside the selected match, time, revision, sequence or event cutoff.');

    for (const insight of page.items) {
      const validInsight = insight.matchId === matchId && insight.canonicalRevision === revision &&
        insight.selectedTimeSeconds === selectedSeconds && Number.isSafeInteger(insight.throughSequence) &&
        insight.throughSequence >= 0 && insight.throughSequence <= throughSequence &&
        typeof insight.summary === 'string' && !seenInsightIds.has(insight.insightId);
      if (!validInsight)
        throw new InvalidInsightPaginationError('Match data is outside the selected match, time, revision, sequence or event cutoff.');
      seenInsightIds.add(insight.insightId);
      items.push(insight);
    }

    if (!page.hasMore) {
      if (page.nextCursor !== null)
        throw new InvalidInsightPaginationError('Canonical insight pagination returned an unexpected cursor.');
      lastPage = page;
      break;
    }
    if (page.items.length === 0 || typeof page.nextCursor !== 'string' || !page.nextCursor.trim() ||
        seenCursors.has(page.nextCursor))
      throw new InvalidInsightPaginationError('Canonical insight pagination did not advance.');
    seenCursors.add(page.nextCursor);
    cursor = page.nextCursor;
    lastPage = page;
  }

  if (!lastPage || lastPage.hasMore)
    throw new InvalidInsightPaginationError('Canonical insight history exceeds the safe pagination limit.');
  return { ...lastPage, items, nextCursor: null, hasMore: false };
}

export function CanonicalMatchPage({ matchId, selectedSeconds, requestedRevision, replay }: {
  matchId: string;
  selectedSeconds: number;
  requestedRevision?: number;
  replay: boolean;
}) {
  const [view, setView] = useState<MatchView | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError('');
    const summaryQuery = new URLSearchParams({ atSeconds: String(selectedSeconds) });
    if (requestedRevision) summaryQuery.set('revision', String(requestedRevision));
    request<CanonicalMatchSnapshot>(`canonical/matches/${encodeURIComponent(matchId)}/summary?${summaryQuery}`, controller.signal)
      .then(async snapshot => {
        const revision = snapshot.canonicalRevision;
        const query = new URLSearchParams({ atSeconds: String(selectedSeconds), revision: String(revision), limit: '50' });
        const keyMomentQuery = new URLSearchParams({ atSeconds: String(selectedSeconds), revision: String(revision) });
        const [events, insights, keyMomentsStatus] = await Promise.all([
          request<CanonicalEventPage>(`canonical/matches/${encodeURIComponent(matchId)}/events?${query}`, controller.signal),
          loadSelectedTimeInsights(matchId, selectedSeconds, revision, snapshot.throughSequence, controller.signal),
          request<unknown>(`canonical/matches/${encodeURIComponent(matchId)}/key-moments?${keyMomentQuery}`, controller.signal)
            .then(() => 'The canonical key-moment response is not versioned, so this view cannot display it yet.')
            .catch(error => error instanceof ApiRequestError && error.status === 503 && error.code === 'CANONICAL_KEY_MOMENTS_UNAVAILABLE'
              ? 'Canonical Key Moments are unavailable because no canonical projection is available.'
              : 'Canonical key moments could not be loaded.')
        ]);
        if (controller.signal.aborted) return;
        const watermarks = [snapshot.throughSequence, events.throughSequence, insights.throughSequence];
        const watermarksSafe = watermarks.every(watermark => Number.isSafeInteger(watermark) && watermark >= 0);
        const exactContext = watermarksSafe && [snapshot, events, insights].every(item => item.matchId === matchId &&
          item.canonicalRevision === revision && item.selectedTimeSeconds === selectedSeconds &&
          item.throughSequence === snapshot.throughSequence);
        const eventsSafe = events.items.every((event, index) =>
          event.matchId === matchId && Number.isSafeInteger(event.sequence) && event.sequence >= 0 &&
          Number.isSafeInteger(event.clockSeconds) && event.clockSeconds >= 0 &&
          event.clockSeconds <= selectedSeconds && event.sequence <= snapshot.throughSequence &&
          (index === 0 || events.items[index - 1].sequence < event.sequence));
        const insightsSafe = insights.items.every(insight => insight.matchId === matchId &&
          insight.canonicalRevision === revision && insight.selectedTimeSeconds === selectedSeconds &&
          Number.isSafeInteger(insight.throughSequence) && insight.throughSequence >= 0 &&
          insight.throughSequence <= snapshot.throughSequence);
        if (!exactContext || !eventsSafe || !insightsSafe) {
          setView(null);
          setError('Match data is outside the selected match, time, revision, sequence or event cutoff.');
          return;
        }
        setView({ snapshot, events, insights, keyMomentsStatus });
      })
      .catch(error => { if (!controller.signal.aborted) {
        setView(null);
        setError(error instanceof InvalidInsightPaginationError
          ? error.message
          : 'Canonical match data is unavailable. The page does not fall back to a demo fixture.');
      } })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [matchId, requestedRevision, selectedSeconds]);

  const snapshot = view?.snapshot;
  const selectedTimeInsight = view
    ? view.insights.items.filter(insight => insight.summary.trim().length > 0)
      .reduce<CanonicalInsightPage['items'][number] | null>((latest, insight) =>
        latest === null || insight.throughSequence > latest.throughSequence ? insight : latest, null)
    : null;
  return <section className="content-page">
    <div className="page-heading">
      <p className="eyebrow">{replay ? 'Replay' : 'Match'}</p>
      <h1>{replay ? 'Selected-time replay' : 'Match overview'}</h1>
      <p>How did the recorded match stand at {clock(selectedSeconds)}?</p>
    </div>
    {loading ? <p className="page-state" role="status">Loading the selected match revision…</p>
      : error ? <p className="page-state error-copy" role="alert">{error}</p>
        : snapshot && view ? <>
          <section className="scoreboard" aria-label="Canonical match score">
            <div className="team">Home</div><span className="score">{snapshot.homeScore}</span>
            <div className="clock">{clock(snapshot.selectedTimeSeconds)}<small>{phaseLabel(snapshot.phase)}</small></div>
            <span className="score">{snapshot.awayScore}</span><div className="team">Away</div>
          </section>
          <section className="panel selected-time-explanation" aria-labelledby="selected-time-explanation-title">
            <h2 id="selected-time-explanation-title">Explanation at {clock(selectedSeconds)}</h2>
            {selectedTimeInsight ? <>
              <p><span className="badge">{selectedTimeInsight.truthClass}</span> Saved insight through sequence {selectedTimeInsight.throughSequence}</p>
              <p>{selectedTimeInsight.summary}</p>
              <a href={contextHref(`/insights/${encodeURIComponent(selectedTimeInsight.insightId)}`, matchId, selectedSeconds, snapshot.canonicalRevision)}>
                Inspect supporting evidence
              </a>
            </> : <p className="empty">No saved explanation is available at this selected time.</p>}
          </section>
          <section className="page-state match-provenance" aria-label="Match source and revision">
            <span className="badge">{provenanceLabel(snapshot.provenance)}</span>
            <p>Canonical revision {snapshot.canonicalRevision} · sequence through {snapshot.throughSequence}</p>
            <p>Source {snapshot.sourceSha256.slice(0, 16)} · algorithm {snapshot.algorithmVersion}</p>
          </section>
          <div className="match-context-controls">
            <label htmlFor="selected-time">Selected match time</label>
            <input id="selected-time" type="range" min="0" max="5400" step="1" value={selectedSeconds}
              aria-valuetext={clock(selectedSeconds)}
              onChange={event => replaceCurrentMatchContext(matchId, Number(event.target.value), snapshot.canonicalRevision)} />
            <span>{clock(selectedSeconds)}</span>
          </div>
          <div className="dashboard canonical-dashboard">
            <section className="panel" aria-labelledby="timeline-title">
              <h2 id="timeline-title">Recorded events</h2>
              <p>Events keep their canonical sequence and source time.</p>
              {view.events.items.length === 0 ? <p className="empty">No recorded events are visible at this time.</p>
                : <ol className="canonical-events">{view.events.items.map(event => <li key={`${event.sequence}:${event.eventId}`}>
                  <time dateTime={event.occurredAt}>{clock(event.clockSeconds)}</time>
                  <span>{eventLabel(event.type)}</span>
                  {eventMarker(event.type) && <span className="event-marker">{eventMarker(event.type)}</span>}
                  <small>Sequence {event.sequence} · period {event.period}</small>
                </li>)}</ol>}
            </section>
            <section className="panel" aria-labelledby="insights-title">
              <h2 id="insights-title">Evidence-backed insights</h2>
              {view.insights.items.length === 0 ? <p className="empty">No persisted insight is available at this selected time.</p>
                : <ul className="canonical-insights">{view.insights.items.map(insight => <li key={insight.insightId}>
                  <span className="badge">{insight.truthClass}</span><p>{insight.summary}</p>
                  <small>{insight.evidence.length} evidence reference(s)</small>
                  <a href={contextHref(`/insights/${encodeURIComponent(insight.insightId)}`, matchId, selectedSeconds, snapshot.canonicalRevision)}>Inspect evidence</a>
                </li>)}</ul>}
              <a className="primary-link" href={contextHref('/insights', matchId, selectedSeconds, snapshot.canonicalRevision)}>View insights</a>
            </section>
            <section className="panel" aria-labelledby="key-moments-title">
              <h2 id="key-moments-title">Key moments</h2>
              <p role="status">{view.keyMomentsStatus}</p>
            </section>
          </div>
          <p className="page-state data-limit">Pitch tracking frames are not returned by the current canonical match contract, so no player positions are drawn.</p>
        </>
        : <p className="page-state">This match is not available.</p>}
  </section>;
}

function eventLabel(type: string) {
  const normalized = type.replaceAll('-', '').toLowerCase();
  if (normalized === 'yellowcard') return 'Yellow card';
  if (normalized === 'redcard') return 'Red card';
  if (normalized === 'goal') return 'Goal';
  return type.replace(/([a-z])([A-Z])/g, '$1 $2').replaceAll('-', ' ');
}

function eventMarker(type: string) {
  const normalized = type.replaceAll('-', '').toLowerCase();
  if (normalized === 'goal') return 'Score event';
  if (normalized === 'yellowcard' || normalized === 'redcard') return 'Discipline event';
  return '';
}

function phaseLabel(phase: string) {
  const labels: Record<string, string> = {
    notstarted: 'Not started', firsthalf: 'First half', halftime: 'Half-time',
    secondhalf: 'Second half', fulltime: 'Full-time'
  };
  return labels[phase.toLowerCase()] ?? phase.replace(/([a-z])([A-Z])/g, '$1 $2').replaceAll('-', ' ').replaceAll('_', ' ');
}
