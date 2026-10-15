import { useEffect, useState } from 'react';
import { ApiRequestError, clock, provenanceLabel, request } from './api';
import type { CanonicalEventPage, CanonicalInsightPage, CanonicalMatchSnapshot, CanonicalTrackingFrame, CanonicalTrackingFramePage } from './api';
import { CanonicalEvidenceDisclosure } from './CanonicalEvidenceDisclosure';
import { contextHref, replaceCurrentMatchContext } from './routes';

type MatchView = {
  snapshot: CanonicalMatchSnapshot;
  events: CanonicalEventPage;
  insights: CanonicalInsightPage;
  trackingFrames: CanonicalTrackingFramePage | null;
  trackingStatus: string;
  keyMomentsStatus: string;
  eventsStatus: string;
  insightsStatus: string;
};
type FocusRequest = { kind: 'event'; eventId: string } | { kind: 'retry' };

export function CanonicalMatchPage({ matchId, selectedSeconds, requestedRevision, replay }: {
  matchId: string;
  selectedSeconds: number;
  requestedRevision?: number;
  replay: boolean;
}) {
  const [view, setView] = useState<MatchView | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const [playing, setPlaying] = useState(false);
  const [audience, setAudience] = useState<'fan' | 'analyst'>('fan');
  const [retryCounter, setRetryCounter] = useState(0);
  const [focusAfterLoad, setFocusAfterLoad] = useState<FocusRequest | null>(null);
  const [reducedMotion, setReducedMotion] = useState(() =>
    typeof window.matchMedia === 'function' && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
  const projectionRevision = view?.snapshot.projectionRevision;
  const canonicalSelectedSeconds = view?.snapshot.selectedTimeSeconds;
  const frameFragment = typeof window === 'undefined'
    ? null
    : /^#canonical-frame-(.*)$/.exec(window.location.hash)?.[1] ?? null;
  let requestedFrameId: string | null = null;
  if (frameFragment !== null) {
    try { requestedFrameId = decodeURIComponent(frameFragment); }
    catch { requestedFrameId = ''; }
  }
  const sourceRevisionParameter = typeof window === 'undefined'
    ? null
    : new URLSearchParams(window.location.search).get('sourceRevision');
  const requestedSourceRevision = sourceRevisionParameter === null
    ? null
    : /^\d+$/.test(sourceRevisionParameter) && Number.isSafeInteger(Number(sourceRevisionParameter)) && Number(sourceRevisionParameter) > 0
      ? Number(sourceRevisionParameter)
      : -1;
  useEffect(() => {
    if (typeof window.matchMedia !== 'function') return;
    const preference = window.matchMedia('(prefers-reduced-motion: reduce)');
    const updatePreference = (event: MediaQueryListEvent) => setReducedMotion(event.matches);
    setReducedMotion(preference.matches);
    preference.addEventListener('change', updatePreference);
    return () => preference.removeEventListener('change', updatePreference);
  }, []);
  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    setError('');
    const summaryQuery = new URLSearchParams({ atSeconds: String(selectedSeconds) });
    if (requestedRevision) summaryQuery.set('revision', String(requestedRevision));
    request<CanonicalMatchSnapshot>(`canonical/matches/${encodeURIComponent(matchId)}/summary?${summaryQuery}`, controller.signal)
      .then(async snapshot => {
        const revision = snapshot.projectionRevision;
        const query = new URLSearchParams({ atSeconds: String(selectedSeconds), revision: String(revision), limit: '50' });
        const frameQuery = new URLSearchParams({ atSeconds: String(selectedSeconds), revision: String(revision), limit: '12' });
        if (requestedFrameId !== null) {
          frameQuery.set('frameId', requestedFrameId);
          if (requestedSourceRevision !== null && requestedSourceRevision > 0)
            frameQuery.set('sourceRevision', String(requestedSourceRevision));
        }
        const keyMomentQuery = new URLSearchParams({ atSeconds: String(selectedSeconds), revision: String(revision) });
        const [eventResult, insightResult, frameResult, keyMomentsStatus] = await Promise.all([
          request<CanonicalEventPage>(`canonical/matches/${encodeURIComponent(matchId)}/events?${query}`, controller.signal)
            .then(page => ({ page, status: '' }))
            .catch(failure => ({ page: null, status: unavailableMessage(failure, 'Recorded events') })),
          request<CanonicalInsightPage>(`canonical/matches/${encodeURIComponent(matchId)}/insights?${query}`, controller.signal)
            .then(page => ({ page, status: '' }))
            .catch(failure => ({ page: null, status: unavailableMessage(failure, 'Saved insights') })),
          request<CanonicalTrackingFramePage>(`canonical/matches/${encodeURIComponent(matchId)}/frames?${frameQuery}`, controller.signal)
            .then(page => ({ page, status: '' }))
            .catch(error => ({
              page: null,
              status: error instanceof ApiRequestError && error.status === 503
                ? 'Observed tracking is unavailable from the canonical service at this selected time.'
                : 'Observed tracking could not be loaded; player positions are not estimated.'
            })),
          request<unknown>(`canonical/matches/${encodeURIComponent(matchId)}/key-moments?${keyMomentQuery}`, controller.signal)
            .then(() => 'The canonical key-moment response is not versioned, so this view cannot display it yet.')
            .catch(error => error instanceof ApiRequestError && error.status === 503 && error.code === 'CANONICAL_KEY_MOMENTS_UNAVAILABLE'
              ? 'Canonical Key Moments are unavailable because no canonical projection is available.'
              : 'Canonical key moments could not be loaded.')
        ]);
        if (controller.signal.aborted) return;
        const emptyContext = { matchId, canonicalRevision: snapshot.canonicalRevision, projectionRevision: snapshot.projectionRevision,
          selectedTimeSeconds: selectedSeconds, throughSequence: snapshot.throughSequence };
        const events = eventResult.page ?? { ...emptyContext, items: [], nextCursor: null, hasMore: false };
        const insights = insightResult.page ?? { ...emptyContext, items: [], nextCursor: null, hasMore: false };
        const trackingFrames = frameResult.page;
        const requiredPages = [snapshot, ...(eventResult.page ? [events] : []), ...(insightResult.page ? [insights] : [])];
        const exactContext = requiredPages.every(item => item.matchId === matchId &&
          item.canonicalRevision === snapshot.canonicalRevision && item.projectionRevision === snapshot.projectionRevision &&
          item.selectedTimeSeconds === selectedSeconds &&
          item.throughSequence === snapshot.throughSequence);
        const eventsSafe = events.items.every((event, index) =>
          event.matchId === matchId && event.clockSeconds <= selectedSeconds && event.sequence <= snapshot.throughSequence &&
          (index === 0 || events.items[index - 1].sequence < event.sequence));
        const insightsSafe = insights.items.every(insight => insight.matchId === matchId &&
          insight.canonicalRevision === snapshot.canonicalRevision && insight.projectionRevision === snapshot.projectionRevision &&
          insight.selectedTimeSeconds === selectedSeconds &&
          insight.throughSequence <= snapshot.throughSequence);
        const framesSafe = trackingFrames === null || (trackingFrames.matchId === matchId &&
          trackingFrames.canonicalRevision === snapshot.canonicalRevision && trackingFrames.projectionRevision === snapshot.projectionRevision &&
          trackingFrames.selectedTimeSeconds === selectedSeconds &&
          trackingFrames.throughSequence === snapshot.throughSequence && trackingFrames.items.every(frame =>
            frame.canonicalRevision > 0 && frame.canonicalRevision <= snapshot.canonicalRevision &&
            frame.projectionRevision === snapshot.projectionRevision && frame.sourceSha256 === snapshot.sourceSha256 &&
            frame.clockSeconds === selectedSeconds && frame.sequence <= snapshot.throughSequence &&
            frame.frame.matchId === matchId && frame.frame.frameId === frame.frameId &&
            frame.frame.sequence === frame.sequence && frame.frame.canonicalRevision === frame.canonicalRevision &&
            frame.frame.matchTime.elapsedSeconds === frame.clockSeconds &&
            frame.frame.matchTime.period === frame.period && frame.frame.source.sourceSha256 === frame.sourceSha256));
        if (!exactContext || !eventsSafe || !insightsSafe || !framesSafe) {
          setView(null);
          setPlaying(false);
          setError('Match data is outside the selected match, time, revision, sequence or event cutoff.');
          return;
        }
        setView({ snapshot, events, insights, trackingFrames, trackingStatus: frameResult.status, keyMomentsStatus,
          eventsStatus: eventResult.status, insightsStatus: insightResult.status });
      })
      .catch(failure => { if (!controller.signal.aborted) { setView(null); setPlaying(false); setError(unavailableMessage(failure, 'Canonical match data')); } })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [matchId, requestedRevision, requestedFrameId, requestedSourceRevision, selectedSeconds, retryCounter]);

  useEffect(() => {
    if (reducedMotion) setPlaying(false);
  }, [reducedMotion]);

  useEffect(() => {
    if (!view || typeof window === 'undefined') return;
    const targetId = window.location.hash.slice(1);
    if (!/^canonical-event-\d+$/.test(targetId) && !/^canonical-frame-.+$/.test(targetId)) return;
    const target = document.getElementById(targetId);
    if (!target) return;
    target.focus();
    target.scrollIntoView?.({ block: 'center' });
  }, [view]);

  useEffect(() => {
    if (!focusAfterLoad || loading) return;
    if (error) {
      document.getElementById('canonical-match-retry')?.focus();
      setFocusAfterLoad(null);
      return;
    }
    if (!view || view.snapshot.selectedTimeSeconds !== selectedSeconds || view.snapshot.matchId !== matchId) return;
    if (focusAfterLoad.kind === 'event') {
      const eventButton = [...document.querySelectorAll<HTMLButtonElement>('[data-event-id]')]
        .find(button => button.dataset.eventId === focusAfterLoad.eventId);
      if (eventButton) {
        eventButton.focus();
        setFocusAfterLoad(null);
        return;
      }
    }
    document.getElementById('selected-time')?.focus();
    setFocusAfterLoad(null);
  }, [focusAfterLoad, loading, error, view, selectedSeconds, matchId]);

  useEffect(() => {
    if (!playing) return;
    if (selectedSeconds >= 5400) { setPlaying(false); return; }
    if (canonicalSelectedSeconds !== selectedSeconds || projectionRevision === undefined) return;
    const timer = window.setInterval(() =>
      replaceCurrentMatchContext(matchId, Math.min(selectedSeconds + 1, 5400), projectionRevision), 1_000);
    return () => window.clearInterval(timer);
  }, [playing, matchId, selectedSeconds, projectionRevision, canonicalSelectedSeconds]);

  const snapshot = view?.snapshot;
  const selectedFrame = requestedFrameId === null
    ? view?.trackingFrames?.items.at(-1) ?? null
    : view?.trackingFrames?.items.find(frame => frame.frameId === requestedFrameId &&
      (requestedSourceRevision === null || frame.canonicalRevision === requestedSourceRevision)) ?? null;
  const pitch = selectedFrame ? pitchCoordinates(selectedFrame) : null;
  const trackingMessage = view?.trackingStatus || (requestedFrameId !== null && !selectedFrame
    ? 'The linked observed frame is not present at this selected time and source revision. No other frame is substituted.'
    : view?.trackingFrames?.items.length
    ? pitch ? `Observed frame ${selectedFrame?.frameId} at ${clock(selectedSeconds)} · period ${selectedFrame?.period}. No positions are interpolated.`
      : 'The recorded coordinate convention cannot be displayed safely; no player positions are drawn.'
    : `No observed tracking frame is recorded at ${clock(selectedSeconds)}. Positions are not estimated.`);

  return <section className="content-page" aria-busy={loading}>
    <div className="page-heading">
      <p className="eyebrow">{replay ? 'Replay' : 'Match'}</p>
      <h1>{replay ? 'Selected-time replay' : 'Match overview'}</h1>
      <p>How did the recorded match stand at {clock(selectedSeconds)}?</p>
    </div>
    {loading ? <p className="page-state" role="status">Loading the selected match revision…</p>
      : error ? <div className="page-state error-copy" role="alert"><p>{error}</p><button id="canonical-match-retry" type="button" onClick={() => { setFocusAfterLoad({ kind: 'retry' }); setLoading(true); setRetryCounter(value => value + 1); }}>Retry Match Data</button></div>
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
          <div className="match-context-controls">
            <label htmlFor="selected-time">Selected match time</label>
            <input id="selected-time" type="range" min="0" max="5400" step="1" value={selectedSeconds}
              aria-valuetext={clock(selectedSeconds)}
              onChange={event => { setPlaying(false); replaceCurrentMatchContext(matchId, Number(event.target.value), snapshot.projectionRevision); }} />
            <span>{clock(selectedSeconds)}</span>
            {reducedMotion
              ? <span className="reduced-motion-note" role="status">Automatic replay is off under reduced motion. Use the selected-time slider to inspect the match.</span>
              : <button type="button" aria-pressed={playing} onClick={() => setPlaying(value => !value)} disabled={selectedSeconds >= 5400}>
                {playing ? 'Pause replay' : 'Play replay'}
              </button>}
          </div>
          <div className="audience-mode" role="group" aria-label="Explanation detail">
            <span>Explanation detail</span>
            <button type="button" className={audience === 'fan' ? 'active' : ''} aria-pressed={audience === 'fan'} onClick={() => setAudience('fan')}>Fan</button>
            <button type="button" className={audience === 'analyst' ? 'active' : ''} aria-pressed={audience === 'analyst'} onClick={() => setAudience('analyst')}>Analyst</button>
            <p>Both views use the same saved insight and evidence. AI explanations are not connected; match facts remain available without them.</p>
          </div>
          <div className="dashboard canonical-dashboard pitch-dashboard">
            <section className="panel pitch-panel" aria-labelledby="pitch-title">
              <h2 id="pitch-title">Observed pitch</h2>
              <p>Only a recorded frame at the selected time is shown. Sample gaps are not filled.</p>
              {pitch && selectedFrame ? <>
                <svg className="canonical-pitch" style={{ aspectRatio: `${pitch.length} / ${pitch.width}` }} viewBox={`-1 -1 ${pitch.length + 2} ${pitch.width + 2}`} role="img"
                  aria-label={`Observed player and ball positions at ${clock(selectedSeconds)}, period ${selectedFrame.period}`} aria-describedby="pitch-description">
                  <rect className="pitch-boundary" x="0" y="0" width={pitch.length} height={pitch.width} />
                  <line className="pitch-marking" x1={pitch.length / 2} y1="0" x2={pitch.length / 2} y2={pitch.width} />
                  <circle className="pitch-marking" cx={pitch.length / 2} cy={pitch.width / 2} r={pitch.width * .135} />
                  <circle className="ball-marker" cx={pitch.ball.x} cy={pitch.ball.y} r={Math.max(pitch.width * .012, .25)} />
                  {pitch.players.map((player, index) => <g key={player.playerId}>
                    <circle className={`player-marker team-marker-${pitch.teamIds.indexOf(player.teamId) % 2}`} cx={player.position.x} cy={player.position.y} r={Math.max(pitch.width * .038, 1.1)} />
                    <text className="pitch-player-label" x={player.position.x} y={player.position.y + Math.max(pitch.width * .009, .2)} textAnchor="middle">{index + 1}</text>
                    <title>{`${player.teamId} · ${player.playerId}`}</title>
                  </g>)}
                </svg>
                <div id={`canonical-frame-${encodeURIComponent(selectedFrame.frameId)}`} tabIndex={-1}>
                  <p id="pitch-description" className="pitch-caption" role="status" aria-live="polite" aria-atomic="true">
                    Observed frame {selectedFrame.frameId} at {clock(selectedSeconds)} · period {selectedFrame.period} · {pitch.length} × {pitch.width} m. Numbered player positions are listed below.
                  </p>
                </div>
                <div className="position-table-details">
                  <table>
                    <caption>Pitch view coordinates at {clock(selectedSeconds)} · {pitch.attackTeamId} attacks right · sequence {selectedFrame.sequence}</caption>
                    <thead><tr><th scope="col">Marker</th><th scope="col">Team ID</th><th scope="col">Player ID</th><th scope="col">View X (m)</th><th scope="col">View Y (m)</th></tr></thead>
                    <tbody><tr><th scope="row">Ball</th><td>—</td><td>Ball</td><td>{pitch.ball.x.toFixed(2)}</td><td>{pitch.ball.y.toFixed(2)}</td></tr>{pitch.players.map((player, index) => <tr key={player.playerId}>
                      <td>{index + 1}</td><td>{player.teamId}</td><td>{player.playerId}</td><td>{player.position.x.toFixed(2)}</td><td>{player.position.y.toFixed(2)}</td>
                    </tr>)}</tbody>
                  </table>
                </div>
                <p className="tracking-legend">Numbered team markers map to the position table. The white marker is the observed ball.</p>
              </> : <p className="empty tracking-status" role="status" aria-live="polite">{trackingMessage}</p>}
            </section>
            <section className="panel" aria-labelledby="timeline-title">
              <h2 id="timeline-title">Recorded events</h2>
              <p>Choose an event to inspect its exact recorded time.</p>
              {view.events.items.length === 0 ? <p className="empty" role="status">{view.eventsStatus || 'No recorded events are visible at this time.'}</p>
                : <ol className="canonical-events">{view.events.items.map(event => <li id={`canonical-event-${event.sequence}`} tabIndex={-1} key={`${event.sequence}:${event.eventId}`}>
                  <button type="button" className="canonical-event" aria-label={`Select ${eventLabel(event.type)} at ${clock(event.clockSeconds)}, sequence ${event.sequence}`}
                    onClick={() => { setFocusAfterLoad({ kind: 'event', eventId: event.eventId }); setPlaying(false); replaceCurrentMatchContext(matchId, event.clockSeconds, snapshot.projectionRevision); }} data-event-id={event.eventId}>
                    <time dateTime={event.occurredAt}>{clock(event.clockSeconds)}</time>
                    <span>{eventLabel(event.type)}</span>
                    {eventMarker(event.type) && <span className="event-marker">{eventMarker(event.type)}</span>}
                    <small>Sequence {event.sequence} · period {event.period}</small>
                  </button>
                </li>)}</ol>}
            </section>
            <section className="panel" aria-labelledby="insights-title">
              <h2 id="insights-title">Evidence-backed insights</h2>
              {view.insights.items.length === 0 ? <p className="empty" role="status">{view.insightsStatus || 'No persisted insight is available at this selected time.'}</p>
                : <ul className="canonical-insights">{view.insights.items.map(insight => <li key={insight.insightId}>
                  <span className="badge" aria-label={`Insight category: ${insight.truthClass}`}>{insight.truthClass}</span><p>{insight.summary}</p>
                  {audience === 'analyst' && <div className="analyst-insight-detail">
                    <p>Detector {insight.detectorVersion} · sequence through {insight.throughSequence}</p>
                    <details><summary>Recorded conditions and limitations</summary>
                      <pre className="evidence-json">{JSON.stringify(insight.conditions, null, 2)}</pre>
                      {insight.limitations === null ? <p>Limitations are not recorded.</p>
                        : insight.limitations.length === 0 ? <p>No limitations were recorded.</p>
                          : <ul>{insight.limitations.map((limitation, limitationIndex) => <li key={`${limitationIndex}:${limitation}`}>{limitation}</li>)}</ul>}
                    </details>
                  </div>}
                  <small>{insight.evidence.length} evidence reference(s)</small>
                  <CanonicalEvidenceDisclosure matchId={matchId} selectedSeconds={selectedSeconds}
                    projectionRevision={snapshot.projectionRevision} throughSequence={snapshot.throughSequence} insight={insight} />
                  <a href={contextHref(`/insights/${encodeURIComponent(insight.insightId)}`, matchId, selectedSeconds, snapshot.projectionRevision)}>Inspect evidence</a>
                </li>)}</ul>}
              <a className="primary-link" href={contextHref('/insights', matchId, selectedSeconds, snapshot.projectionRevision)}>View insights</a>
            </section>
            <section className="panel" aria-labelledby="key-moments-title">
              <h2 id="key-moments-title">Key moments</h2>
              <p role="status">{view.keyMomentsStatus}</p>
            </section>
          </div>
        </>
        : <p className="page-state">This match is not available.</p>}
  </section>;
}

type PitchPoint = { x: number; y: number };
function unavailableMessage(failure: unknown, label: string): string {
  if (failure instanceof ApiRequestError) {
    if (failure.status === 401 || failure.status === 403) return `${label} is unavailable because access was denied.`;
    if (failure.status === 404) return `${label} is unavailable for this match and selected time.`;
    if (failure.status === 503) return `${label} service is temporarily unavailable.`;
  }
  return `${label} could not be loaded.`;
}

function pitchCoordinates(record: CanonicalTrackingFrame): { length: number; width: number; ball: PitchPoint; players: Array<{playerId: string; teamId: string; position: PitchPoint}>; teamIds: string[]; attackTeamId: string } | null {
  const { frame } = record;
  const system = frame.coordinateSystem;
  if (!system || !Array.isArray(system.teamDirections) || !Array.isArray(frame.players) || !frame.players.length) return null;
  const teamIds = [...new Set(frame.players.map(player => player.teamId))].sort();
  const directionsSafe = teamIds.every(teamId => system.teamDirections.filter(direction => direction.teamId === teamId).length === 1 &&
    ['left', 'right'].includes(system.teamDirections.find(direction => direction.teamId === teamId)?.direction ?? ''));
  const reference = system.teamDirections.find(direction => direction.teamId === teamIds[0]);
  if (!reference || !directionsSafe || !Number.isFinite(system.lengthMetres) || !Number.isFinite(system.widthMetres) ||
      system.lengthMetres <= 0 || system.lengthMetres > 120 || system.widthMetres <= 0 || system.widthMetres > 90 ||
      !['metres', 'normalized_0_1', 'percent_0_100'].includes(system.unit) ||
      !['bottom_left', 'top_left'].includes(system.axisOrigin)) return null;
  const point = (raw: {x: number; y: number}): PitchPoint | null => {
    if (!Number.isFinite(raw.x) || !Number.isFinite(raw.y) || raw.x < 0 || raw.y < 0 ||
        raw.x > (system.unit === 'metres' ? system.lengthMetres : system.unit === 'normalized_0_1' ? 1 : 100) ||
        raw.y > (system.unit === 'metres' ? system.widthMetres : system.unit === 'normalized_0_1' ? 1 : 100)) return null;
    const scaleX = system.unit === 'metres' ? 1 : system.unit === 'normalized_0_1' ? system.lengthMetres : system.lengthMetres / 100;
    const scaleY = system.unit === 'metres' ? 1 : system.unit === 'normalized_0_1' ? system.widthMetres : system.widthMetres / 100;
    let x = raw.x * scaleX;
    let y = raw.y * scaleY;
    if (system.axisOrigin === 'top_left') y = system.widthMetres - y;
    if (reference.direction === 'left') { x = system.lengthMetres - x; y = system.widthMetres - y; }
    return { x, y: system.widthMetres - y };
  };
  const ball = point(frame.ball);
  const players = frame.players.map(player => {
    const position = point(player.position);
    return position ? { playerId: player.playerId, teamId: player.teamId, position } : null;
  });
  if (!ball || players.some(player => player === null)) return null;
  return { length: system.lengthMetres, width: system.widthMetres, ball, players: players as Array<{playerId: string; teamId: string; position: PitchPoint}>, teamIds, attackTeamId: reference.teamId };
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
