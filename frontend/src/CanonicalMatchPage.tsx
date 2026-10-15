import { useEffect, useState } from 'react';
import { ApiRequestError, clock, provenanceLabel, request } from './api';
import type { CanonicalEventPage, CanonicalInsightPage, CanonicalLedgerEvent, CanonicalMatchSnapshot } from './api';
import { contextHref, replaceCurrentMatchContext } from './routes';

type MatchView = {
  snapshot: CanonicalMatchSnapshot;
  events: CanonicalEventPage;
  insights: CanonicalInsightPage;
  keyMomentsStatus: string;
  eventsIncomplete: boolean;
};

const EVENT_PAGE_LIMIT = 50;
const MAX_EVENT_PAGES = 20;

export function CanonicalMatchPage({ matchId, selectedSeconds, requestedRevision, replay }: {
  matchId: string;
  selectedSeconds: number;
  requestedRevision?: number;
  replay: boolean;
}) {
  const [view, setView] = useState<MatchView | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const [selectedTimeInput, setSelectedTimeInput] = useState(String(selectedSeconds));
  useEffect(() => setSelectedTimeInput(String(selectedSeconds)), [selectedSeconds]);
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError('');
    const summaryQuery = new URLSearchParams({ atSeconds: String(selectedSeconds) });
    if (requestedRevision) summaryQuery.set('revision', String(requestedRevision));
    request<CanonicalMatchSnapshot>(`canonical/matches/${encodeURIComponent(matchId)}/summary?${summaryQuery}`, controller.signal)
      .then(async snapshot => {
        if (snapshot.asOfSeconds > selectedSeconds ||
            (requestedRevision !== undefined && snapshot.canonicalRevision > requestedRevision)) {
          throw new Error('Canonical snapshot exceeds the selected time or revision bound.');
        }
        const revision = snapshot.canonicalRevision;
        const insightQuery = new URLSearchParams({ atSeconds: String(selectedSeconds), revision: String(revision), limit: '50' });
        const keyMomentQuery = new URLSearchParams({ atSeconds: String(selectedSeconds), revision: String(revision) });
        const [eventResult, insights, keyMomentsStatus] = await Promise.all([
          readCanonicalEvents(matchId, selectedSeconds, revision, snapshot.throughSequence, controller.signal),
          request<CanonicalInsightPage>(`canonical/matches/${encodeURIComponent(matchId)}/insights?${insightQuery}`, controller.signal),
          request<unknown>(`canonical/matches/${encodeURIComponent(matchId)}/key-moments?${keyMomentQuery}`, controller.signal)
            .then(() => 'The canonical key-moment response is not versioned, so this view cannot display it yet.')
            .catch(error => error instanceof ApiRequestError && error.status === 503 && error.code === 'CANONICAL_KEY_MOMENTS_UNAVAILABLE'
              ? 'Canonical Key Moments are unavailable because no canonical projection is available.'
              : 'Canonical key moments could not be loaded.')
        ]);
        const { page: events, incomplete: eventsIncomplete } = eventResult;
        if (controller.signal.aborted) return;
        const exactContext = [snapshot, events, insights].every(item => item.matchId === matchId &&
          item.canonicalRevision === revision && item.selectedTimeSeconds === selectedSeconds &&
          item.throughSequence === snapshot.throughSequence);
        const eventsSafe = events.items.every((event, index) =>
          event.matchId === matchId && event.clockSeconds <= selectedSeconds && event.sequence <= snapshot.throughSequence &&
          (index === 0 || events.items[index - 1].sequence < event.sequence));
        const insightsSafe = insights.items.every(insight => insight.matchId === matchId &&
          insight.canonicalRevision === revision && insight.selectedTimeSeconds === selectedSeconds &&
          insight.throughSequence <= snapshot.throughSequence);
        if (!exactContext || !eventsSafe || !insightsSafe) {
          setView(null);
          setError('Match data is outside the selected match, time, revision, sequence or event cutoff.');
          return;
        }
        setView({ snapshot, events, insights, keyMomentsStatus, eventsIncomplete });
      })
      .catch(() => { if (!controller.signal.aborted) { setView(null); setError('Canonical match data is unavailable. The page does not fall back to a demo fixture.'); } })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [matchId, requestedRevision, selectedSeconds]);

  const snapshot = view?.snapshot;
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
          <section className="page-state match-provenance" aria-label="Match source and revision">
            <span className="badge">{provenanceLabel(snapshot.provenance)}</span>
            <p>Canonical revision {snapshot.canonicalRevision} · sequence through {snapshot.throughSequence}</p>
            <p>Source {snapshot.sourceSha256.slice(0, 16)} · algorithm {snapshot.algorithmVersion}</p>
          </section>
          <form className="match-context-controls" onSubmit={event => {
            event.preventDefault();
            const nextSeconds = Number(selectedTimeInput);
            if (selectedTimeInput.trim() !== '' && Number.isSafeInteger(nextSeconds) && nextSeconds >= 0) {
              replaceCurrentMatchContext(matchId, nextSeconds, snapshot.canonicalRevision);
            }
          }}>
            <label htmlFor="selected-time">Selected match time (elapsed seconds)</label>
            <input id="selected-time" type="number" min="0" step="1" inputMode="numeric" required value={selectedTimeInput}
              onChange={event => setSelectedTimeInput(event.target.value)} />
            <button type="submit" disabled={selectedTimeInput.trim() === '' || !Number.isSafeInteger(Number(selectedTimeInput)) || Number(selectedTimeInput) < 0}>Go</button>
            <span>{clock(selectedSeconds)}</span>
          </form>
          <div className="dashboard canonical-dashboard">
            <section className="panel" aria-labelledby="timeline-title">
              <h2 id="timeline-title">Recorded events</h2>
              <p>Events keep their canonical sequence and source time.</p>
              {view.eventsIncomplete && <p className="page-state" role="status">More canonical events exist, but this view reached its 1,000-event page bound.</p>}
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

async function readCanonicalEvents(matchId: string, selectedSeconds: number, revision: number,
  throughSequence: number, signal: AbortSignal): Promise<{ page: CanonicalEventPage; incomplete: boolean }> {
  const items: CanonicalLedgerEvent[] = [];
  const seenCursors = new Set<string>();
  let cursor: string | null = null;
  let lastPage: CanonicalEventPage | null = null;

  for (let pageIndex = 0; pageIndex < MAX_EVENT_PAGES; pageIndex += 1) {
    const query = new URLSearchParams({ atSeconds: String(selectedSeconds), revision: String(revision), limit: String(EVENT_PAGE_LIMIT) });
    if (cursor) query.set('cursor', cursor);
    const page = await request<CanonicalEventPage>(
      `canonical/matches/${encodeURIComponent(matchId)}/events?${query}`, signal);
    if (page.matchId !== matchId || page.canonicalRevision !== revision ||
        page.selectedTimeSeconds !== selectedSeconds || page.throughSequence !== throughSequence) {
      throw new Error('Canonical event page is outside the selected context.');
    }
    items.push(...page.items);
    lastPage = page;
    if (!page.hasMore) return { page: { ...page, items }, incomplete: false };
    if (!page.nextCursor || seenCursors.has(page.nextCursor)) {
      throw new Error('Canonical event pagination cursor is missing or repeated.');
    }
    seenCursors.add(page.nextCursor);
    cursor = page.nextCursor;
  }

  if (!lastPage) throw new Error('Canonical events returned no page.');
  return { page: { ...lastPage, items }, incomplete: true };
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
