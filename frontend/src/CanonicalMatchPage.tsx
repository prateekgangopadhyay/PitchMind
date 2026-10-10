import { useEffect, useState } from 'react';
import { clock, provenanceLabel, request } from './api';
import type { CanonicalEventPage, CanonicalInsightPage, CanonicalMatchSnapshot } from './api';
import { contextHref, replaceCurrentMatchContext } from './routes';

type MatchView = { snapshot: CanonicalMatchSnapshot; events: CanonicalEventPage; insights: CanonicalInsightPage };

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
        const [events, insights] = await Promise.all([
          request<CanonicalEventPage>(`canonical/matches/${encodeURIComponent(matchId)}/events?${query}`, controller.signal),
          request<CanonicalInsightPage>(`canonical/matches/${encodeURIComponent(matchId)}/insights?${query}`, controller.signal)
        ]);
        if (controller.signal.aborted) return;
        const exactContext = [snapshot, events, insights].every(item => item.matchId === matchId &&
          item.canonicalRevision === revision && item.selectedTimeSeconds === selectedSeconds);
        if (!exactContext) {
          setView(null);
          setError('Match data did not share the requested match, selected time and canonical revision.');
          return;
        }
        setView({ snapshot, events, insights });
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
            <div className="clock">{clock(snapshot.selectedTimeSeconds)}<small>{snapshot.phase}</small></div>
            <span className="score">{snapshot.awayScore}</span><div className="team">Away</div>
          </section>
          <section className="page-state match-provenance" aria-label="Match source and revision">
            <span className="badge">{provenanceLabel(snapshot.provenance)}</span>
            <p>Canonical revision {snapshot.canonicalRevision} · sequence through {snapshot.throughSequence}</p>
            <p>Source {snapshot.sourceSha256.slice(0, 16)} · algorithm {snapshot.algorithmVersion}</p>
          </section>
          <div className="match-context-controls">
            <label htmlFor="selected-time">Selected match time</label>
            <input id="selected-time" type="range" min="0" max="5400" step="1" value={selectedSeconds}
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
                  <span>{event.type.replaceAll('-', ' ')}</span>
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
          </div>
          <p className="page-state data-limit">Pitch tracking frames are not returned by the current canonical match contract, so no player positions are drawn.</p>
        </>
        : <p className="page-state">This match is not available.</p>}
  </section>;
}
