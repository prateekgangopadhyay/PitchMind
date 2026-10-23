import { useEffect, useRef, useState } from 'react';
import { ApiRequestError, clock, provenanceLabel, request } from './api';
import type { CanonicalEventPage, CanonicalInsightPage, CanonicalMatchSnapshot, CanonicalTrackingFrame, CanonicalTrackingFramePage } from './api';
import { CanonicalEvidenceDisclosure } from './CanonicalEvidenceDisclosure';
import { RealtimeMatchUpdates } from './RealtimeMatchUpdates';
import { mergeHistoricalProjectionRefresh, selectHistoricalTime, type RealtimeTimelineState } from './realtime';
import { contextHref, replaceCurrentMatchContext } from './routes';

type MatchView = {
  snapshot: CanonicalMatchSnapshot;
  events: CanonicalEventPage;
  insights: CanonicalInsightPage;
  trackingFrames: CanonicalTrackingFramePage | null;
  trackingStatus: string;
  linkedEvidenceStatus: string;
  eventsStatus: string;
  insightsStatus: string;
};
type FocusRequest = { kind: 'event'; eventId: string } | { kind: 'retry' };

const INSIGHT_PAGE_SIZE = 100;
const MAX_INSIGHT_PAGES = 100;

class InvalidInsightPaginationError extends Error {}

async function loadSelectedTimeInsights(
  matchId: string,
  selectedSeconds: number,
  canonicalRevision: number,
  projectionRevision: number,
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
      revision: String(projectionRevision),
      limit: String(INSIGHT_PAGE_SIZE)
    });
    if (cursor) query.set('cursor', cursor);
    const page = await request<CanonicalInsightPage>(
      `canonical/matches/${encodeURIComponent(matchId)}/insights?${query}`, signal);
    const pageContextIsValid = page.matchId === matchId && page.canonicalRevision === canonicalRevision &&
      page.projectionRevision === projectionRevision && page.selectedTimeSeconds === selectedSeconds && Number.isSafeInteger(page.throughSequence) &&
      page.throughSequence >= 0 && page.throughSequence === throughSequence;
    if (!pageContextIsValid || !Array.isArray(page.items) || typeof page.hasMore !== 'boolean')
      throw new InvalidInsightPaginationError('This match data does not match the selected moment.');

    for (const insight of page.items) {
      const validInsight = insight.matchId === matchId && insight.canonicalRevision === canonicalRevision &&
        insight.projectionRevision === projectionRevision && insight.selectedTimeSeconds === selectedSeconds && Number.isSafeInteger(insight.throughSequence) &&
        insight.throughSequence >= 0 && insight.throughSequence <= throughSequence &&
        typeof insight.insightId === 'string' && insight.insightId.length > 0 &&
        typeof insight.summary === 'string' && !seenInsightIds.has(insight.insightId);
      if (!validInsight)
        throw new InvalidInsightPaginationError('This match data does not match the selected moment.');
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

export function CanonicalMatchPage({ matchId, selectedSeconds, requestedRevision, replay, initialMode }: {
  matchId: string;
  selectedSeconds: number;
  requestedRevision?: number;
  replay: boolean;
  initialMode: 'LIVE' | 'HISTORICAL';
}) {
  const [view, setView] = useState<MatchView | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const [playing, setPlaying] = useState(false);
  const [audience, setAudience] = useState<'fan' | 'analyst'>('fan');
  const [retryCounter, setRetryCounter] = useState(0);
  const [focusAfterLoad, setFocusAfterLoad] = useState<FocusRequest | null>(null);
  const [realtimeMode, setRealtimeMode] = useState<'LIVE' | 'HISTORICAL'>(initialMode);
  const [realtimeState, setRealtimeState] = useState<RealtimeTimelineState | null>(null);
  const [cinemaMode, setCinemaMode] = useState(false);
  const pitchPanelRef = useRef<HTMLElement | null>(null);
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
    const updateCinemaMode = () => setCinemaMode(document.fullscreenElement === pitchPanelRef.current);
    document.addEventListener('fullscreenchange', updateCinemaMode);
    return () => document.removeEventListener('fullscreenchange', updateCinemaMode);
  }, []);
  const toggleCinemaMode = async () => {
    const panel = pitchPanelRef.current;
    if (!panel) return;
    if (cinemaMode && document.fullscreenElement !== panel) {
      setCinemaMode(false);
      return;
    }
    if (document.fullscreenElement === panel) {
      await document.exitFullscreen();
      return;
    }
    if (panel.requestFullscreen) {
      try {
        await panel.requestFullscreen();
        return;
      } catch {
        // Use the in-page cinema layout when browser fullscreen is unavailable.
      }
    }
    setCinemaMode(value => !value);
  };
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
    const summaryPath = `canonical/matches/${encodeURIComponent(matchId)}/summary`;
    const exactSummaryQuery = new URLSearchParams({ atSeconds: String(selectedSeconds) });
    if (requestedRevision && realtimeMode === 'HISTORICAL') exactSummaryQuery.set('revision', String(requestedRevision));
    const loadSummary = async (): Promise<CanonicalMatchSnapshot | null> => {
      if (realtimeMode === 'HISTORICAL') return request<CanonicalMatchSnapshot>(`${summaryPath}?${exactSummaryQuery}`, controller.signal);

      const headQuery = new URLSearchParams({ atSeconds: '5400' });
      const head = await request<CanonicalMatchSnapshot>(`${summaryPath}?${headQuery}`, controller.signal);
      if (controller.signal.aborted) return null;
      const currentUrl = new URLSearchParams(window.location.search);
      if (head.asOfSeconds !== selectedSeconds || currentUrl.get('mode') !== 'live' || currentUrl.has('revision')) {
        replaceCurrentMatchContext(matchId, head.asOfSeconds, undefined, 'live');
        return null;
      }
      return request<CanonicalMatchSnapshot>(`${summaryPath}?${exactSummaryQuery}`, controller.signal);
    };
    loadSummary()
      .then(async snapshot => {
        if (!snapshot) return;
        const revision = snapshot.projectionRevision;
        const query = new URLSearchParams({ atSeconds: String(selectedSeconds), revision: String(revision), limit: '50' });
        const frameQuery = new URLSearchParams({ atSeconds: String(selectedSeconds), revision: String(revision), limit: '12' });
        if (requestedFrameId !== null) {
          frameQuery.set('frameId', requestedFrameId);
          if (requestedSourceRevision !== null && requestedSourceRevision > 0)
            frameQuery.set('sourceRevision', String(requestedSourceRevision));
        }
        const [eventResult, insightResult, frameResult] = await Promise.all([
          request<CanonicalEventPage>(`canonical/matches/${encodeURIComponent(matchId)}/events?${query}`, controller.signal)
            .then(page => ({ page, status: '' }))
            .catch(failure => ({ page: null, status: unavailableMessage(failure, 'Recorded events') })),
          loadSelectedTimeInsights(matchId, selectedSeconds, snapshot.canonicalRevision, revision, snapshot.throughSequence, controller.signal)
            .then(page => ({ page, status: '' }))
            .catch(failure => {
              if (failure instanceof InvalidInsightPaginationError) throw failure;
              return { page: null, status: unavailableMessage(failure, 'Saved insights') };
            }),
          request<CanonicalTrackingFramePage>(`canonical/matches/${encodeURIComponent(matchId)}/frames?${frameQuery}`, controller.signal)
            .then(page => ({ page, status: '' }))
            .catch(error => ({
              page: null,
              status: error instanceof ApiRequestError && error.status === 503
                ? 'Observed tracking is unavailable from the canonical service at this selected time.'
                : 'Observed tracking could not be loaded; player positions are not estimated.'
            })),
        ]);
        if (controller.signal.aborted) return;
        const emptyContext = { matchId, canonicalRevision: snapshot.canonicalRevision, projectionRevision: snapshot.projectionRevision,
          selectedTimeSeconds: selectedSeconds, throughSequence: snapshot.throughSequence };
        const events = eventResult.page ?? { ...emptyContext, items: [], nextCursor: null, hasMore: false };
        const insights = insightResult.page ?? { ...emptyContext, items: [], nextCursor: null, hasMore: false };
        const trackingFrames = frameResult.page;
        const requiredPages = [snapshot, ...(eventResult.page ? [events] : []), ...(insightResult.page ? [insights] : []),
          ...(trackingFrames ? [trackingFrames] : [])];
        const watermarksSafe = requiredPages.every(item => Number.isSafeInteger(item.throughSequence) && item.throughSequence >= 0);
        const exactContext = watermarksSafe && requiredPages.every(item => item.matchId === matchId &&
          item.canonicalRevision === snapshot.canonicalRevision && item.projectionRevision === snapshot.projectionRevision &&
          item.selectedTimeSeconds === selectedSeconds &&
          item.throughSequence === snapshot.throughSequence);
        const eventsSafe = events.items.every((event, index) =>
          event.matchId === matchId && Number.isSafeInteger(event.clockSeconds) && event.clockSeconds >= 0 &&
          Number.isSafeInteger(event.sequence) && event.sequence >= 0 &&
          event.clockSeconds <= selectedSeconds && event.sequence <= snapshot.throughSequence &&
          (index === 0 || events.items[index - 1].sequence < event.sequence));
        const insightsSafe = insights.items.every(insight => insight.matchId === matchId &&
          insight.canonicalRevision === snapshot.canonicalRevision && insight.projectionRevision === snapshot.projectionRevision &&
          insight.selectedTimeSeconds === selectedSeconds &&
          Number.isSafeInteger(insight.throughSequence) && insight.throughSequence >= 0 &&
          insight.throughSequence <= snapshot.throughSequence);
        const framesSafe = trackingFrames === null || (trackingFrames.matchId === matchId &&
          trackingFrames.canonicalRevision === snapshot.canonicalRevision && trackingFrames.projectionRevision === snapshot.projectionRevision &&
          trackingFrames.selectedTimeSeconds === selectedSeconds &&
          trackingFrames.throughSequence === snapshot.throughSequence && trackingFrames.items.every(frame =>
            Number.isSafeInteger(frame.canonicalRevision) && frame.canonicalRevision > 0 && frame.canonicalRevision <= snapshot.canonicalRevision &&
            Number.isSafeInteger(frame.sequence) && frame.sequence >= 0 &&
            Number.isSafeInteger(frame.clockSeconds) && frame.clockSeconds >= 0 &&
            frame.projectionRevision === snapshot.projectionRevision && frame.sourceSha256 === snapshot.sourceSha256 &&
            frame.clockSeconds === selectedSeconds && frame.sequence <= snapshot.throughSequence &&
            frame.frame.matchId === matchId && frame.frame.frameId === frame.frameId &&
            frame.frame.sequence === frame.sequence && frame.frame.canonicalRevision === frame.canonicalRevision &&
            frame.frame.matchTime.elapsedSeconds === frame.clockSeconds &&
            frame.frame.matchTime.period === frame.period && frame.frame.source.sourceSha256 === frame.sourceSha256));
        if (!exactContext || !eventsSafe || !insightsSafe || !framesSafe) {
          setView(null);
          setPlaying(false);
          setError('This match data does not match the selected moment. Return to the match list and try again.');
          return;
        }
        const linkedEventSequence = typeof window === 'undefined'
          ? null
          : /^#canonical-event-(\d+)$/.exec(window.location.hash)?.[1] ?? null;
        let currentView: MatchView = { snapshot, events, insights, trackingFrames,
          trackingStatus: frameResult.status,
          eventsStatus: eventResult.status, insightsStatus: insightResult.status,
          linkedEvidenceStatus: '' };
        const parsedLinkedSequence = linkedEventSequence === null ? null : Number(linkedEventSequence);
        const linkedSequence = parsedLinkedSequence !== null && Number.isSafeInteger(parsedLinkedSequence) && parsedLinkedSequence >= 0
          ? parsedLinkedSequence : null;
        const eventInPage = (items: CanonicalEventPage['items']) => linkedSequence !== null &&
          items.some(event => event.sequence === linkedSequence);
        setView(currentView);
        if (linkedSequence === null || eventInPage(events.items) || !eventResult.page || !events.hasMore) return;

        currentView = { ...currentView, linkedEvidenceStatus: 'Searching the selected match, time and revision for the linked event…' };
        setView(currentView);
        try {
          let cursor = events.nextCursor;
          let hasMore: boolean = events.hasMore;
          let lastSequence = events.items.at(-1)?.sequence ?? -1;
          const seenCursors = new Set<string>();
          let pagesLoaded = 0;
          let foundEvent: CanonicalEventPage['items'][number] | undefined;
          let partial = false;
          const pageLimit = 512;
          if (hasMore && !cursor) partial = true;
          while (hasMore && cursor && pagesLoaded < pageLimit && !controller.signal.aborted) {
            if (seenCursors.has(cursor)) { partial = true; break; }
            seenCursors.add(cursor);
            const pageQuery = new URLSearchParams({ atSeconds: String(selectedSeconds), revision: String(revision), limit: '50', cursor });
            const page = await request<CanonicalEventPage>(`canonical/matches/${encodeURIComponent(matchId)}/events?${pageQuery}`, controller.signal);
            pagesLoaded++;
            const pageMatchesContext = page.matchId === matchId && page.canonicalRevision === snapshot.canonicalRevision &&
              page.projectionRevision === snapshot.projectionRevision && page.selectedTimeSeconds === selectedSeconds &&
              page.throughSequence === snapshot.throughSequence;
            const pageEventsAreSafe = page.items.every((event, index) => event.matchId === matchId &&
              event.clockSeconds <= selectedSeconds && event.sequence <= snapshot.throughSequence &&
              (index === 0 || page.items[index - 1].sequence < event.sequence));
            if (!pageMatchesContext || !pageEventsAreSafe || !page.items.length || page.items[0].sequence <= lastSequence) {
              partial = true;
              break;
            }
            foundEvent = page.items.find(event => event.sequence === linkedSequence);
            if (foundEvent) break;
            lastSequence = page.items.at(-1)!.sequence;
            hasMore = page.hasMore;
            if (!hasMore) break;
            cursor = page.nextCursor;
            if (!cursor) { partial = true; break; }
          }
          if (controller.signal.aborted) return;
          if (hasMore && pagesLoaded >= pageLimit) partial = true;
          if (foundEvent) {
            currentView = { ...currentView, events: { ...currentView.events,
              items: [...currentView.events.items, foundEvent] }, linkedEvidenceStatus: '' };
          } else {
            currentView = { ...currentView, linkedEvidenceStatus: partial
              ? 'The linked event could not be verified because additional selected-time pages were unavailable.'
              : 'The linked event is not present in the selected match, time and revision.' };
          }
          setView(currentView);
        } catch {
          if (!controller.signal.aborted) {
            currentView = { ...currentView, linkedEvidenceStatus: 'Some event pages could not be loaded. The linked event was not verified.' };
            setView(currentView);
          }
        }
      })
      .catch(failure => { if (!controller.signal.aborted) {
        setView(null);
        setPlaying(false);
        setError(failure instanceof InvalidInsightPaginationError
          ? failure.message
          : unavailableMessage(failure, 'Match data'));
      } })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [matchId, requestedRevision, requestedFrameId, requestedSourceRevision, selectedSeconds, retryCounter, realtimeMode]);

  useEffect(() => {
    if (!view || view.snapshot.matchId !== matchId || view.snapshot.selectedTimeSeconds !== selectedSeconds) return;
    const refreshedState: RealtimeTimelineState = {
      matchId,
      mode: realtimeMode,
      selectedTimeSeconds: realtimeMode === 'LIVE' ? view.snapshot.asOfSeconds : selectedSeconds,
      liveAsOfSeconds: view.snapshot.asOfSeconds,
      observedLiveRevision: view.snapshot.canonicalRevision,
      observedLiveSequence: view.snapshot.throughSequence,
      canonicalRevision: view.snapshot.canonicalRevision,
      throughSequence: view.snapshot.throughSequence,
      snapshot: view.snapshot,
      events: view.events.items,
      nextCursor: view.events.nextCursor,
      hasMore: view.events.hasMore,
      needsBackfill: false,
      hasNewMatchUpdate: false,
      reconnecting: false,
    };
    setRealtimeState(current => mergeHistoricalProjectionRefresh(current, refreshedState));
  }, [view, matchId, selectedSeconds, realtimeMode]);

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
  const approvedTrackingFrames = view?.trackingFrames?.items.filter(frame => frame?.frame?.source?.rightsReviewStatus === 'approved_for_use') ?? [];
  const hasUnapprovedTrackingFrames = view?.trackingFrames?.items.some(frame => frame?.frame?.source?.rightsReviewStatus !== 'approved_for_use') ?? false;
  const selectedTimeInsight = view
    ? view.insights.items.filter(insight => insight.summary.trim().length > 0)
      .reduce<CanonicalInsightPage['items'][number] | null>((latest, insight) =>
        latest === null || insight.throughSequence > latest.throughSequence ? insight : latest, null)
    : null;
  const selectedFrame = requestedFrameId === null
    ? approvedTrackingFrames.at(-1) ?? null
    : approvedTrackingFrames.find(frame => frame.frameId === requestedFrameId &&
      (requestedSourceRevision === null || frame.canonicalRevision === requestedSourceRevision)) ?? null;
  const pitch = selectedFrame ? pitchCoordinates(selectedFrame) : null;
  const trackingMessage = view?.trackingStatus || (hasUnapprovedTrackingFrames && !selectedFrame
    ? 'Observed tracking is unavailable until the source is approved for canonical use. Positions are not estimated.'
    : requestedFrameId !== null && !selectedFrame
    ? 'The linked observed frame is not present at this selected time and source revision. No other frame is substituted.'
    : approvedTrackingFrames.length
    ? pitch ? `Observed frame ${selectedFrame?.frameId} at ${clock(selectedSeconds)} · period ${selectedFrame?.period}. No positions are interpolated.`
      : 'The recorded coordinate convention cannot be displayed safely; no player positions are drawn.'
    : `No observed tracking frame approved for display is available at ${clock(selectedSeconds)}. Positions are not estimated.`);

  const onRealtimeStateChange = (next: RealtimeTimelineState) => {
    setRealtimeState(next);
    if (next.mode !== realtimeMode) setRealtimeMode(next.mode);
    if (next.mode === 'LIVE' && typeof window !== 'undefined') {
      const currentUrl = new URLSearchParams(window.location.search);
      if (next.selectedTimeSeconds !== selectedSeconds || currentUrl.get('mode') !== 'live' || currentUrl.has('revision')) {
        replaceCurrentMatchContext(matchId, next.selectedTimeSeconds, undefined, 'live');
      } else if (view && (next.snapshot.canonicalRevision !== view.snapshot.canonicalRevision ||
          next.snapshot.throughSequence !== view.snapshot.throughSequence)) {
        setRetryCounter(value => value + 1);
      }
    }
  };

  const chooseHistoricalTime = (seconds: number, revision: number) => {
    setPlaying(false);
    setRealtimeMode('HISTORICAL');
    setRealtimeState(current => current ? selectHistoricalTime(current, seconds) : current);
    replaceCurrentMatchContext(matchId, seconds, revision);
  };

  const toggleReplay = () => {
    if (playing) {
      setPlaying(false);
      return;
    }
    setRealtimeMode('HISTORICAL');
    if (snapshot) replaceCurrentMatchContext(matchId, selectedSeconds, snapshot.projectionRevision);
    setPlaying(true);
  };

  return <section className="content-page screen-page canonical-match-page" aria-busy={loading}>
    <div className="page-heading">
      <p className="eyebrow">{replay ? 'Replay' : 'Match'}</p>
      <h1>{replay ? 'Selected-time replay' : 'Match overview'}</h1>
      <p>How did the recorded match stand at {clock(selectedSeconds)}?</p>
    </div>
    {realtimeState && <RealtimeMatchUpdates state={realtimeState} onStateChange={onRealtimeStateChange} />}
    {loading ? <p className="page-state" role="status">Loading the selected match revision…</p>
      : error ? <div className="page-state error-copy" role="alert"><p>{error}</p><button id="canonical-match-retry" type="button" onClick={() => { setFocusAfterLoad({ kind: 'retry' }); setLoading(true); setRetryCounter(value => value + 1); }}>Retry Match Data</button></div>
        : snapshot && view ? <>
          <section className="scoreboard" aria-label="Match score">
            <div className="team">Home</div><span className="score">{snapshot.homeScore}</span>
            <div className="clock">{clock(snapshot.selectedTimeSeconds)}<small>{phaseLabel(snapshot.phase)}</small></div>
            <span className="score">{snapshot.awayScore}</span><div className="team">Away</div>
          </section>
          {selectedTimeInsight && <section className="selected-time-explanation" aria-labelledby="selected-time-explanation-title">
            <div><p className="eyebrow">{selectedTimeInsight.truthClass} · {clock(selectedSeconds)}</p>
              <h2 id="selected-time-explanation-title">{selectedTimeInsight.summary}</h2></div>
            <a href={contextHref(`/insights/${encodeURIComponent(selectedTimeInsight.insightId)}`, matchId, selectedSeconds, snapshot.canonicalRevision)}>
              Inspect evidence
            </a>
          </section>}
          <details className="match-provenance">
            <summary><span className="badge">{provenanceLabel(snapshot.provenance)}</span><span>Source and revision details</span></summary>
            <p>Canonical revision {snapshot.canonicalRevision} · sequence through {snapshot.throughSequence}</p>
            <p>Source {snapshot.sourceSha256} · algorithm {snapshot.algorithmVersion}</p>
          </details>
          <div className="match-context-controls">
            <label htmlFor="selected-time">Selected match time</label>
            <input id="selected-time" type="range" min="0" max="5400" step="1" value={selectedSeconds}
              aria-valuetext={clock(selectedSeconds)}
              onChange={event => chooseHistoricalTime(Number(event.target.value), snapshot.projectionRevision)} />
            <span>{clock(selectedSeconds)}</span>
            {reducedMotion
              ? <span className="reduced-motion-note" role="status">Automatic replay is off under reduced motion. Use the selected-time slider to inspect the match.</span>
              : <button type="button" aria-pressed={playing} onClick={toggleReplay} disabled={selectedSeconds >= 5400}>
                {playing ? 'Pause replay' : 'Play replay'}
              </button>}
          </div>
          {view.insights.items.length > 0 && <div className="audience-mode" role="group" aria-label="Explanation detail">
            <span>Explanation detail</span>
            <button type="button" className={audience === 'fan' ? 'active' : ''} aria-pressed={audience === 'fan'} onClick={() => setAudience('fan')}>Fan</button>
            <button type="button" className={audience === 'analyst' ? 'active' : ''} aria-pressed={audience === 'analyst'} onClick={() => setAudience('analyst')}>Analyst</button>
            <p>Both views use the same saved insight and evidence. AI explanations are not connected; match facts remain available without them.</p>
          </div>}
          {!view.insights.items.length && view.events.items.length > 0 && <p className="page-state" role="status">AI explanations are not connected; match facts remain available without them.</p>}
          <div className="dashboard canonical-dashboard pitch-dashboard">
            {pitch && selectedFrame && <section ref={pitchPanelRef} className={`panel pitch-panel${cinemaMode ? ' is-cinema' : ''}`} aria-labelledby="pitch-title">
              <div className="pitch-panel-heading">
                <div className="pitch-title-meta">
                  <h2 id="pitch-title">Observed pitch</h2>
                  <span className="pitch-meta-pills">
                    <span>{pitch.length} × {pitch.width} m</span>
                    <span>Period {selectedFrame.period}</span>
                    <span>Clock {clock(selectedSeconds)}</span>
                    <span className="observed-players-tag">{pitch.players.length} observed players</span>
                    <span>{pitch.attackTeamId} attacks right →</span>
                  </span>
                </div>
                <button type="button" className="cinema-mode-toggle" onClick={() => { void toggleCinemaMode(); }} aria-pressed={cinemaMode}>
                  {cinemaMode ? 'Exit cinema view' : 'Cinema view'}
                </button>
              </div>
              <p>Only a recorded frame at the selected time is shown. Sample gaps are not filled.</p>
              <>
                <svg
                  className="canonical-pitch"
                  style={{ aspectRatio: `${pitch.length + 6} / ${pitch.width + 4.4}` }}
                  viewBox={`-3 -2.2 ${pitch.length + 6} ${pitch.width + 4.4}`}
                  role="img"
                  aria-label={`Observed player and ball positions at ${clock(selectedSeconds)}, period ${selectedFrame.period}`}
                  aria-describedby="pitch-description"
                >
                  <defs>
                    {/* Deep Stadium Floodlight & Turf Illumination */}
                    <radialGradient id="canonical-floodlight" cx="50%" cy="50%" r="62%" fx="50%" fy="48%">
                      <stop offset="0%" stopColor="#1a5a36" />
                      <stop offset="42%" stopColor="#134729" />
                      <stop offset="82%" stopColor="#0b301b" />
                      <stop offset="100%" stopColor="#061e11" />
                    </radialGradient>

                    {/* Outer Stadium Apron Gradient */}
                    <linearGradient id="canonical-apron" x1="0" y1="0" x2="0" y2="1">
                      <stop offset="0%" stopColor="#0a2517" />
                      <stop offset="100%" stopColor="#05160d" />
                    </linearGradient>

                    {/* Lawn Sheen Bands */}
                    <linearGradient id="canonical-sheen" x1="0" y1="0" x2="0" y2="1">
                      <stop offset="0%" stopColor="#ffffff" stopOpacity="0.04" />
                      <stop offset="50%" stopColor="#ffffff" stopOpacity="0.01" />
                      <stop offset="100%" stopColor="#000000" stopOpacity="0.07" />
                    </linearGradient>

                    {/* 3D Soccer Ball Shading */}
                    <radialGradient id="canonical-ball-sphere" cx="35%" cy="30%" r="68%">
                      <stop offset="0%" stopColor="#ffffff" />
                      <stop offset="55%" stopColor="#f8fafc" />
                      <stop offset="85%" stopColor="#cbd5e1" />
                      <stop offset="100%" stopColor="#64748b" />
                    </radialGradient>

                    {/* Goal Net Diamond Weave Pattern */}
                    <pattern id="canonical-net-pattern" width="0.7" height="0.7" patternUnits="userSpaceOnUse">
                      <rect width="0.7" height="0.7" fill="rgba(255, 255, 255, 0.02)" />
                      <path d="M 0 0.7 L 0.7 0 M 0 0 L 0.7 0.7" stroke="rgba(255, 255, 255, 0.38)" strokeWidth="0.08" />
                    </pattern>

                    {/* Ground Soft Shadows */}
                    <filter id="canonical-ball-shadow" x="-50%" y="-50%" width="200%" height="200%">
                      <feGaussianBlur in="SourceAlpha" stdDeviation="0.25" />
                      <feColorMatrix type="matrix" values="0 0 0 0 0   0 0 0 0 0   0 0 0 0 0  0 0 0 0.5 0" />
                    </filter>
                    <filter id="canonical-player-shadow" x="-40%" y="-40%" width="180%" height="180%">
                      <feGaussianBlur in="SourceAlpha" stdDeviation="0.28" />
                      <feColorMatrix type="matrix" values="0 0 0 0 0   0 0 0 0 0   0 0 0 0 0  0 0 0 0.45 0" />
                    </filter>
                  </defs>

                  {/* Outer Stadium Turf Apron & Run-off Track */}
                  <rect x="-3" y="-2.2" width={pitch.length + 6} height={pitch.width + 4.4} fill="url(#canonical-apron)" />

                  {/* Main Floodlit Playing Turf */}
                  <rect className="pitch-boundary" x="0" y="0" width={pitch.length} height={pitch.width} fill="url(#canonical-floodlight)" />

                  {/* 10 Precision Lawn Mowing Bands */}
                  {Array.from({ length: 10 }, (_, index) => (
                    <rect
                      key={index}
                      x={index * (pitch.length / 10)}
                      y="0"
                      width={pitch.length / 10}
                      height={pitch.width}
                      fill="url(#canonical-sheen)"
                      opacity={index % 2 === 0 ? 0.6 : 0.25}
                      pointerEvents="none"
                    />
                  ))}

                  {/* Goal Structures & Nets (Behind Goal Lines) */}
                  <g className="canonical-goal-nets" pointerEvents="none">
                    {/* Left Goal Net & Posts */}
                    <rect
                      x="-2.2"
                      y={pitch.width / 2 - 3.66}
                      width="2.2"
                      height="7.32"
                      rx="0.3"
                      fill="url(#canonical-net-pattern)"
                      stroke="rgba(255, 255, 255, 0.65)"
                      strokeWidth="0.22"
                    />
                    <line
                      x1="0"
                      y1={pitch.width / 2 - 3.66}
                      x2="0"
                      y2={pitch.width / 2 + 3.66}
                      stroke="#ffffff"
                      strokeWidth="0.45"
                      strokeLinecap="round"
                    />

                    {/* Right Goal Net & Posts */}
                    <rect
                      x={pitch.length}
                      y={pitch.width / 2 - 3.66}
                      width="2.2"
                      height="7.32"
                      rx="0.3"
                      fill="url(#canonical-net-pattern)"
                      stroke="rgba(255, 255, 255, 0.65)"
                      strokeWidth="0.22"
                    />
                    <line
                      x1={pitch.length}
                      y1={pitch.width / 2 - 3.66}
                      x2={pitch.length}
                      y2={pitch.width / 2 + 3.66}
                      stroke="#ffffff"
                      strokeWidth="0.45"
                      strokeLinecap="round"
                    />
                  </g>

                  {/* Sideline Technical Coaching Benches */}
                  <g className="pitch-sideline-benches" pointerEvents="none">
                    <rect x={pitch.length / 2 - 14} y="-1.6" width="10" height="1.0" fill="none" stroke="rgba(255, 255, 255, 0.2)" strokeWidth="0.12" strokeDasharray="0.5 0.3" />
                    <rect x={pitch.length / 2 + 4} y="-1.6" width="10" height="1.0" fill="none" stroke="rgba(255, 255, 255, 0.2)" strokeWidth="0.12" strokeDasharray="0.5 0.3" />
                  </g>

                  {/* Official Regulation Pitch Markings in Crisp High-Visibility White */}
                  <g className="canonical-pitch-markings">
                    {/* Halfway line & center circle */}
                    <line className="pitch-marking" x1={pitch.length / 2} y1="0" x2={pitch.length / 2} y2={pitch.width} />
                    <circle className="pitch-marking" cx={pitch.length / 2} cy={pitch.width / 2} r={pitch.width * 0.135} fill="none" />
                    <circle className="pitch-marking" cx={pitch.length / 2} cy={pitch.width / 2} r={0.5} fill="currentColor" />

                    {/* Left penalty area and goal box */}
                    <rect className="pitch-marking" x="0" y={pitch.width / 2 - 20.15} width={16.5} height={40.3} fill="none" />
                    <rect className="pitch-marking" x="0" y={pitch.width / 2 - 9.15} width={5.5} height={18.3} fill="none" />
                    <circle className="pitch-marking" cx={11} cy={pitch.width / 2} r={0.5} fill="currentColor" />
                    {/* Left Penalty Arc (The D) */}
                    <path
                      className="pitch-marking"
                      d={`M 16.5 ${pitch.width / 2 - 7.31} A 9.15 9.15 0 0 1 16.5 ${pitch.width / 2 + 7.31}`}
                      fill="none"
                    />

                    {/* Right penalty area and goal box */}
                    <rect className="pitch-marking" x={pitch.length - 16.5} y={pitch.width / 2 - 20.15} width={16.5} height={40.3} fill="none" />
                    <rect className="pitch-marking" x={pitch.length - 5.5} y={pitch.width / 2 - 9.15} width={5.5} height={18.3} fill="none" />
                    <circle className="pitch-marking" cx={pitch.length - 11} cy={pitch.width / 2} r={0.5} fill="currentColor" />
                    {/* Right Penalty Arc (The D) */}
                    <path
                      className="pitch-marking"
                      d={`M ${pitch.length - 16.5} ${pitch.width / 2 - 7.31} A 9.15 9.15 0 0 0 ${pitch.length - 16.5} ${pitch.width / 2 + 7.31}`}
                      fill="none"
                    />

                    {/* 4 Regulation Corner Arcs (1m radius) */}
                    <path className="pitch-marking" d="M 0 1 A 1 1 0 0 0 1 0" fill="none" />
                    <path className="pitch-marking" d={`M 0 ${pitch.width - 1} A 1 1 0 0 1 1 ${pitch.width}`} fill="none" />
                    <path className="pitch-marking" d={`M ${pitch.length} 1 A 1 1 0 0 1 ${pitch.length - 1} 0`} fill="none" />
                    <path className="pitch-marking" d={`M ${pitch.length} ${pitch.width - 1} A 1 1 0 0 0 ${pitch.length - 1} ${pitch.width}`} fill="none" />
                  </g>

                  {/* Corner Flag Markers */}
                  <g className="pitch-corner-flags" pointerEvents="none">
                    <circle cx="0" cy="0" r="0.45" fill="#facc15" stroke="#a16207" strokeWidth="0.1" />
                    <circle cx="0" cy={pitch.width} r="0.45" fill="#facc15" stroke="#a16207" strokeWidth="0.1" />
                    <circle cx={pitch.length} cy="0" r="0.45" fill="#facc15" stroke="#a16207" strokeWidth="0.1" />
                    <circle cx={pitch.length} cy={pitch.width} r="0.45" fill="#facc15" stroke="#a16207" strokeWidth="0.1" />
                  </g>

                  {/* Ball Marker Group */}
                  <g className="canonical-ball-group">
                    {/* Ball Turf Shadow */}
                    <ellipse
                      cx={pitch.ball.x}
                      cy={pitch.ball.y + 0.3}
                      rx={Math.max(pitch.width * 0.016, 0.35)}
                      ry={Math.max(pitch.width * 0.008, 0.18)}
                      fill="rgba(0, 0, 0, 0.55)"
                      filter="url(#canonical-ball-shadow)"
                    />
                    {/* Tactical Ball Pulse Aura */}
                    <circle
                      cx={pitch.ball.x}
                      cy={pitch.ball.y}
                      r={Math.max(pitch.width * 0.022, 0.65)}
                      fill="rgba(74, 239, 172, 0.22)"
                      stroke="rgba(74, 239, 172, 0.45)"
                      strokeWidth="0.15"
                    />
                    {/* 3D Ball Marker */}
                    <circle
                      className="ball-marker"
                      cx={pitch.ball.x}
                      cy={pitch.ball.y}
                      r={Math.max(pitch.width * 0.012, 0.28)}
                      fill="url(#canonical-ball-sphere)"
                      stroke="#0f172a"
                      strokeWidth="0.16"
                    />
                    <circle
                      cx={pitch.ball.x}
                      cy={pitch.ball.y}
                      r={Math.max(pitch.width * 0.004, 0.09)}
                      fill="#0f172a"
                      pointerEvents="none"
                    />
                  </g>

                  {/* Player Markers with Turf Contact Shadow and Sleek Badges */}
                  {pitch.players.map((player, index) => {
                    const teamIdx = pitch.teamIds.indexOf(player.teamId) % 2;
                    const radius = Math.max(pitch.width * 0.038, 1.15);
                    return (
                      <g key={player.playerId} className="canonical-player-group">
                        <ellipse
                          cx={player.position.x}
                          cy={player.position.y + 0.32}
                          rx={radius * 1.05}
                          ry={radius * 0.42}
                          fill="rgba(0, 0, 0, 0.5)"
                          filter="url(#canonical-player-shadow)"
                        />
                        <circle
                          cx={player.position.x}
                          cy={player.position.y}
                          r={radius + 0.28}
                          fill="none"
                          stroke={teamIdx === 0 ? 'rgba(239, 113, 130, 0.45)' : 'rgba(113, 168, 247, 0.45)'}
                          strokeWidth="0.2"
                        />
                        <circle
                          className={`player-marker team-marker-${teamIdx}`}
                          cx={player.position.x}
                          cy={player.position.y}
                          r={radius}
                        />
                        <text
                          className="pitch-player-label"
                          x={player.position.x}
                          y={player.position.y + Math.max(pitch.width * 0.009, 0.22)}
                          textAnchor="middle"
                        >
                          {index + 1}
                        </text>
                        <title>{`${player.teamId} · ${player.playerId}`}</title>
                      </g>
                    );
                  })}
                </svg>
                <div id={`canonical-frame-${encodeURIComponent(selectedFrame.frameId)}`} tabIndex={-1}>
                  <p id="pitch-description" className="pitch-caption" role="status" aria-live="polite" aria-atomic="true">
                    Observed frame {selectedFrame.frameId} at {clock(selectedSeconds)} · period {selectedFrame.period} · {pitch.length} × {pitch.width} m. Numbered player positions are listed below.
                  </p>
                </div>
                <div className="position-table-details" role="region" aria-label="Observed pitch coordinates"
                  aria-description="Use the left and right arrow keys to scroll the coordinate table on narrow screens."
                  tabIndex={0} onKeyDown={event => {
                    const direction = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0;
                    if (direction) {
                      event.preventDefault();
                      event.currentTarget.scrollBy({ left: direction * 64 });
                    }
                  }}>
                  <table>
                    <caption>Pitch view coordinates at {clock(selectedSeconds)} · {pitch.attackTeamId} attacks right · sequence {selectedFrame.sequence}</caption>
                    <thead><tr><th scope="col">Marker</th><th scope="col">Team ID</th><th scope="col">Player ID</th><th scope="col">View X (m)</th><th scope="col">View Y (m)</th></tr></thead>
                    <tbody><tr><th scope="row">Ball</th><td>—</td><td>Ball</td><td>{pitch.ball.x.toFixed(2)}</td><td>{pitch.ball.y.toFixed(2)}</td></tr>{pitch.players.map((player, index) => <tr key={player.playerId}>
                      <td>{index + 1}</td><td>{player.teamId}</td><td>{player.playerId}</td><td>{player.position.x.toFixed(2)}</td><td>{player.position.y.toFixed(2)}</td>
                    </tr>)}</tbody>
                  </table>
                </div>
                <ul className="pitch-team-legend" aria-label="Pitch marker teams">
                  {pitch.teamIds.map((teamId, teamIndex) => <li key={teamId}>
                    <span className={`pitch-team-swatch team-marker-${teamIndex % 2}`} aria-hidden="true" />Team {teamId}
                  </li>)}
                </ul>
                <p className="tracking-legend">Numbered team markers map to the position table. The white marker is the observed ball.</p>
              </>
            </section>}
            {view.events.items.length > 0 && <section className="panel timeline-panel" aria-labelledby="timeline-title">
              <h2 id="timeline-title">Recorded events</h2>
              <p>Choose an event to inspect its exact recorded time.</p>
              {view.events.items.length === 0 ? <p className="empty" role="status">{view.eventsStatus || 'No recorded events are visible at this time.'}</p>
                : <ol className="canonical-events">{view.events.items.map(event => <li id={`canonical-event-${event.sequence}`} tabIndex={-1} key={`${event.sequence}:${event.eventId}`}>
                  <button type="button" className="canonical-event" aria-label={`Select ${eventLabel(event.type)} at ${clock(event.clockSeconds)}, sequence ${event.sequence}`}
                    onClick={() => { setFocusAfterLoad({ kind: 'event', eventId: event.eventId }); chooseHistoricalTime(event.clockSeconds, snapshot.projectionRevision); }} data-event-id={event.eventId}>
                    <time dateTime={event.occurredAt}>{clock(event.clockSeconds)}</time>
                    <span>{eventLabel(event.type)}</span>
                    {eventMarker(event.type) && <span className="event-marker">{eventMarker(event.type)}</span>}
                    <small>Sequence {event.sequence} · period {event.period}</small>
                  </button>
                </li>)}</ol>}
            </section>}
            {view.insights.items.length > 0 && <section className="panel insights-panel" aria-labelledby="insights-title">
              <h2 id="insights-title">Evidence-backed insights</h2>
              <ul className="canonical-insights">{view.insights.items.map(insight => <li key={insight.insightId}>
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
                </li>)}</ul>
              <a className="primary-link" href={contextHref('/insights', matchId, selectedSeconds, snapshot.projectionRevision)}>View insights</a>
            </section>}
            {!pitch && <p className="page-state tracking-status" role="status">{trackingMessage}</p>}
            {view.linkedEvidenceStatus && <p className="page-state" role="status">{view.linkedEvidenceStatus}</p>}
            {!view.events.items.length && view.eventsStatus && <p className="page-state" role="status">{view.eventsStatus}</p>}
            {!view.insights.items.length && view.insightsStatus && <p className="page-state" role="status">{view.insightsStatus}</p>}
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
