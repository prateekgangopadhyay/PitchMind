import { useEffect, useMemo, useRef, useState } from 'react';
import type { ReactNode } from 'react';
import { ApiRequestError, askCandidateAnalyst, clock, getCandidateAnalystAgents, request } from './api';
import type { CandidateAnalystAnswer, CanonicalEventPage, CanonicalInsightPage, CanonicalMatchPage, CanonicalMatchSnapshot, CanonicalTrackingFramePage } from './api';
import type { AppRoute } from './routes';
import { contextHref, handleLinkClick } from './routes';
import { clubInitials, resolveClubLogo, KNOWN_MATCH_FIXTURES } from './clubLogos';
import { TacticalPitchView, type TacticalPitchEvent } from './TacticalPitchView';
import { FOUNDRY_AGENTS, computeAgentDiscussionAndExplanation, computeCausalChain, deduceMatchMetrics, deduceKeyMoments, deduceEvidenceFromAnalysis, type AgentDiscussionTurn, type TacticalExplanation, type TacticalSituation, type CausalChainStep } from './tacticalAgentDiscussion';
import { DynamicPassMapThumbnail, DynamicHeatmapThumbnail, DynamicTacticalMomentThumbnail, deduceTeamFormation } from './DynamicTacticalVisuals';
import { LinearEventsTimeline, type LinearTimelineEvent } from './LinearEventsTimeline';
import { PlayerTacticalDashboard } from './PlayerTacticalDashboard';
import { PlayerRosterView } from './PlayerRosterView';
import { getPlayersIntelligenceList, calculateAiPlayerKeyQuote, type PlayerIntelligenceData } from './api';
import { TacticalInsightsDashboard } from './TacticalInsightsDashboard';
import { getCachedCandidateEvents, syncCandidateEvents, readCandidateStatus, subscribeCandidateStatus } from './candidateLiveStatus';
import { TeamHeadToHeadAiSection } from './TeamHeadToHeadAiSection';
import { getPlayerProfile } from './playerProfiles';


export type CandidateStatus = {
  matchId: string | null;
  currentSequence: number | null;
  currentClockValue: number | null;
  sourceSha256: string | null;
  isStreaming?: boolean;
};

export type CandidateEvent = {
  eventId: string;
  matchId: string;
  sequence: number;
  clockSeconds: number;
  period: number;
  eventType: string;
  rawPayload: Record<string, unknown>;
};

export type CandidateLiveReport = { event: CandidateEvent; answer: CandidateAnalystAnswer };

export type ContextPlayer = {
  id: string;
  teamId: string;
  name?: string;
  x?: number;
  y?: number;
  active?: boolean;
  dismissed?: boolean;
  yellowCards?: number;
  jerseyNumber?: number;
  role?: string;
  position?: string;
  teamName?: string;
  metrics?: Record<string, unknown>;
};

export type ContextTeam = {
  id: string;
  name: string;
  score?: number;
  logoUrl?: string;
  activePlayers: number;
};

export type ContextEvent = {
  id: string;
  type: string;
  sequence: number;
  clockSeconds: number;
  teamId?: string;
  teamName?: string;
  playerId?: string;
  playerName?: string;
  receiverName?: string;
  pitchX?: number;
  pitchY?: number;
  endX?: number;
  endY?: number;
  action?: string;
  outcome?: string;
  impactScore?: number;
  headline?: string;
  metadata?: Record<string, unknown>;
  leadupEvent?: ContextEvent;
};

export type ContextInsight = {
  id: string;
  truthClass: string;
  summary: string;
  evidence: Array<{ kind: string; id: string; relation: string }>;
  limitations: string[];
};

export type MatchContext = {
  matchId: string;
  selectedSeconds: number;
  period?: number;
  phase?: string;
  revision?: number;
  throughSequence: number;
  provenance: string;
  homeTeam?: ContextTeam;
  awayTeam?: ContextTeam;
  teams: ContextTeam[];
  players: ContextPlayer[];
  events: ContextEvent[];
  insights: ContextInsight[];
  candidate: boolean;
  sourceTimeLabel?: string;
  league?: string;
};

const isObject = (value: unknown): value is Record<string, unknown> => Boolean(value && typeof value === 'object' && !Array.isArray(value));
const asText = (value: unknown): string | undefined => typeof value === 'string' && value.trim() ? value : undefined;
const asNumber = (value: unknown): number | undefined => typeof value === 'number' && Number.isFinite(value) ? value : undefined;

export function phaseLabel(phase?: string) {
  if (!phase) return '';
  const labels: Record<string, string> = {
    notstarted: 'Not started', firsthalf: 'First half', halftime: 'Half-time',
    secondhalf: 'Second half', fulltime: 'Full-time'
  };
  return labels[phase.toLowerCase()] ?? phase.replace(/([a-z])([A-Z])/g, '$1 $2').replaceAll('-', ' ').replaceAll('_', ' ');
}

function adaptCanonical(
  snapshot: CanonicalMatchSnapshot,
  events: CanonicalEventPage,
  frames: CanonicalTrackingFramePage | null,
  insights: CanonicalInsightPage,
  selectedSeconds: number
): MatchContext {
  const frame = frames?.items.at(-1)?.frame;
  const playerStates = snapshot.players;
  const players: ContextPlayer[] = (frame?.players ?? []).map(player => {
    const state = playerStates.find(item => item.playerId === player.playerId);
    return {
      id: player.playerId,
      teamId: player.teamId,
      x: player.position.x,
      y: player.position.y,
      active: state?.active,
      dismissed: state?.dismissed,
      yellowCards: state?.yellowCards
    };
  });

  const known = KNOWN_MATCH_FIXTURES[snapshot.matchId];
  const homeTeamName = known?.home ?? 'Home';
  const awayTeamName = known?.away ?? 'Away';

  const homeTeam: ContextTeam = {
    id: 'home',
    name: homeTeamName,
    score: snapshot.homeScore,
    logoUrl: resolveClubLogo(homeTeamName),
    activePlayers: snapshot.homeActivePlayers
  };

  const awayTeam: ContextTeam = {
    id: 'away',
    name: awayTeamName,
    score: snapshot.awayScore,
    logoUrl: resolveClubLogo(awayTeamName),
    activePlayers: snapshot.awayActivePlayers
  };

  const teams = [homeTeam, awayTeam];

  return {
    matchId: snapshot.matchId,
    selectedSeconds,
    period: snapshot.period,
    phase: snapshot.phase,
    revision: snapshot.projectionRevision,
    throughSequence: snapshot.throughSequence,
    provenance: snapshot.provenance,
    homeTeam,
    awayTeam,
    teams,
    players,
    events: events.items.map(event => ({
      id: event.eventId,
      type: event.type,
      sequence: event.sequence,
      clockSeconds: event.clockSeconds
    })),
    insights: insights.items.map(insight => ({
      id: insight.insightId,
      truthClass: insight.truthClass,
      summary: insight.summary,
      evidence: insight.evidence,
      limitations: insight.limitations ?? []
    })),
    candidate: false
  };
}

interface CandidateEventsCache {
  matchId: string;
  sourceSha256: string;
  events: CandidateEvent[];
}
let candidateEventsCache: CandidateEventsCache | null = null;

async function loadCandidateContext(matchId: string, seconds: number, signal: AbortSignal): Promise<MatchContext | null> {
  const currentSnapshot = readCandidateStatus().status;
  const status = (currentSnapshot && currentSnapshot.matchId === matchId)
    ? currentSnapshot
    : await request<CandidateStatus>('simulation/candidate/status', signal).catch(() => null);
  if (!status || status.matchId !== matchId) return null;

  let fetched = getCachedCandidateEvents(matchId);
  if (!fetched.length) {
    fetched = await syncCandidateEvents(matchId, signal).catch(() => fetched);
  }

  if (
    !fetched.length &&
    candidateEventsCache &&
    candidateEventsCache.matchId === matchId &&
    candidateEventsCache.sourceSha256 === status.sourceSha256
  ) {
    fetched = candidateEventsCache.events;
  }

  if (!fetched.length) return null;

  if (status.sourceSha256) {
    candidateEventsCache = { matchId, sourceSha256: status.sourceSha256, events: fetched };
  }
  const atTime = fetched.filter(event => Number.isFinite(event.clockSeconds) && event.clockSeconds <= seconds);
  const selectedEvent = atTime.at(-1);

  // Collect team names up to elapsed time (fallback to all fetched events if atTime has 0)
  const sourceTeams = [...new Set([
    ...atTime.map(e => asText(e.rawPayload.teamName)),
    ...atTime.flatMap(e => Array.isArray(e.rawPayload.playerPositions) ? e.rawPayload.playerPositions.map(p => isObject(p) ? asText(p.teamName) : undefined) : []),
    ...fetched.map(e => asText(e.rawPayload.teamName)),
    ...fetched.flatMap(e => Array.isArray(e.rawPayload.playerPositions) ? e.rawPayload.playerPositions.map(p => isObject(p) ? asText(p.teamName) : undefined) : [])
  ].filter((t): t is string => Boolean(t)))];

  const team1Name = sourceTeams[0] ?? 'Home FC';
  const team2Name = sourceTeams[1] ?? 'Away FC';

  // Compute goals up to the elapsed time
  const goals = atTime.filter(e => e.eventType === 'Goal');
  const team1Goals = goals.filter(e => asText(e.rawPayload.teamName) === team1Name).length;
  const team2Goals = goals.filter(e => asText(e.rawPayload.teamName) === team2Name).length;

  // Extract players from the latest event in atTime with playerPositions (or from fetched)
  const latestPosEvent = [...atTime].reverse().find(e => Array.isArray(e.rawPayload.playerPositions) && e.rawPayload.playerPositions.length > 0)
    ?? [...fetched].find(e => Array.isArray(e.rawPayload.playerPositions) && e.rawPayload.playerPositions.length > 0);
  const rawPlayers = Array.isArray(latestPosEvent?.rawPayload.playerPositions)
    ? latestPosEvent.rawPayload.playerPositions.filter(isObject) : [];

  const players: ContextPlayer[] = [];
  for (const raw of rawPlayers) {
    const teamName = asText(raw.teamName);
    const teamId = asText(raw.teamId) ?? teamName;
    if (!teamId) continue;
    const name = asText(raw.playerName);
    const playerId = asText(raw.playerId) ?? name;
    if (!playerId) continue;
    const x = asNumber(raw.x);
    const y = asNumber(raw.y);
    const jerseyNumber = asNumber(raw.jerseyNumber);
    const withinSourceRange = x !== undefined && y !== undefined && x >= 0 && x <= 100 && y >= 0 && y <= 100;
    players.push({
      id: playerId,
      teamId,
      name,
      jerseyNumber,
      ...(withinSourceRange ? { x, y } : {})
    });
  }

  const homeActive = players.filter(p => p.teamId === team1Name || p.teamId === 'home').length || (rawPlayers.length ? Math.ceil(rawPlayers.length / 2) : 0);
  const awayActive = players.filter(p => p.teamId === team2Name || p.teamId === 'away').length || (rawPlayers.length ? Math.floor(rawPlayers.length / 2) : 0);

  const homeTeam: ContextTeam = {
    id: team1Name,
    name: team1Name,
    score: team1Goals,
    logoUrl: resolveClubLogo(team1Name),
    activePlayers: homeActive
  };

  const awayTeam: ContextTeam = {
    id: team2Name,
    name: team2Name,
    score: team2Goals,
    logoUrl: resolveClubLogo(team2Name),
    activePlayers: awayActive
  };

  const teams: ContextTeam[] = [homeTeam, ...(sourceTeams.length > 1 ? [awayTeam] : [])];

  const period = selectedEvent?.period ?? (seconds > 2700 ? 2 : 1);
  const phase = selectedEvent?.eventType === 'FullTime' ? 'fulltime' : selectedEvent?.eventType === 'HalfTime' ? 'halftime' : (period === 2 ? 'secondhalf' : 'firsthalf');

  const events: ContextEvent[] = atTime.map((event, idx) => {
    const raw = event.rawPayload;
    const meta = isObject(raw.metadata) ? raw.metadata : undefined;
    const pitchX = asNumber(raw.pitchX) ?? (meta ? asNumber(meta.pitchX) : undefined);
    const pitchY = asNumber(raw.pitchY) ?? (meta ? asNumber(meta.pitchY) : undefined);
    const endX = asNumber(raw.endX) ?? (meta ? asNumber(meta.end_x) ?? asNumber(meta.endX) : undefined);
    const endY = asNumber(raw.endY) ?? (meta ? asNumber(meta.end_y) ?? asNumber(meta.endY) : undefined);
    const action = asText(raw.action) ?? (meta ? asText(meta.action) : undefined);
    const outcome = asText(raw.outcome) ?? (meta ? asText(meta.outcome) : undefined);
    const impactScore = asNumber(raw.impactScore) ?? (meta ? asNumber(meta.impact_score) ?? asNumber(meta.impactScore) : undefined);
    const headline = meta ? asText(meta.headline) : undefined;

    const prev = idx > 0 ? atTime[idx - 1] : undefined;
    const prevRaw = prev?.rawPayload;
    const prevMeta = prevRaw && isObject(prevRaw.metadata) ? prevRaw.metadata : undefined;

    return {
      id: event.eventId,
      type: event.eventType,
      sequence: event.sequence,
      clockSeconds: event.clockSeconds,
      teamId: asText(raw.teamId) ?? asText(raw.teamName),
      teamName: asText(raw.teamName),
      playerId: asText(raw.playerId) ?? asText(raw.playerName),
      playerName: asText(raw.playerName),
      receiverName: asText(raw.receiverName),
      pitchX,
      pitchY,
      endX,
      endY,
      action,
      outcome,
      impactScore,
      headline,
      metadata: meta,
      leadupEvent: prev ? {
        id: prev.eventId,
        type: prev.eventType,
        sequence: prev.sequence,
        clockSeconds: prev.clockSeconds,
        teamName: asText(prevRaw?.teamName),
        playerName: asText(prevRaw?.playerName),
        pitchX: asNumber(prevRaw?.pitchX) ?? (prevMeta ? asNumber(prevMeta.pitchX) : undefined),
        pitchY: asNumber(prevRaw?.pitchY) ?? (prevMeta ? asNumber(prevMeta.pitchY) : undefined),
        endX: asNumber(prevRaw?.endX) ?? (prevMeta ? asNumber(prevMeta.end_x) : undefined),
        endY: asNumber(prevRaw?.endY) ?? (prevMeta ? asNumber(prevMeta.end_y) : undefined),
        action: asText(prevRaw?.action) ?? (prevMeta ? asText(prevMeta.action) : undefined)
      } : undefined
    };
  });

  return {
    matchId,
    selectedSeconds: seconds,
    period,
    phase,
    throughSequence: selectedEvent?.sequence ?? -1,
    provenance: 'Candidate feed · not yet canonical',
    homeTeam,
    awayTeam,
    teams,
    players,
    events,
    insights: [],
    candidate: true,
    sourceTimeLabel: 'Source clock value'
  };
}

function ContextState({ loading, error, empty }: { loading: boolean; error?: string; empty: string }) {
  if (loading) return <p className="page-state" role="status">Loading selected match data…</p>;
  if (error) return <p className="page-state error-copy" role="alert">{error}</p>;
  return <p className="page-state">{empty}</p>;
}

export function useMatchContext(matchId: string | undefined, seconds: number, revision?: number) {
  const [context, setContext] = useState<MatchContext | null>(null);
  const [fixtures] = useState<CanonicalMatchPage | null>(null);
  const [candidateStatus] = useState<CandidateStatus | null>(null);
  const [loading, setLoading] = useState(Boolean(matchId));
  const [error, setError] = useState('');
  const lastMatchIdRef = useRef<string | undefined>(undefined);

  useEffect(() => {
    const controller = new AbortController();
    if (lastMatchIdRef.current !== matchId) {
      setContext(null);
      lastMatchIdRef.current = matchId;
    }
    setError('');
    if (!matchId) {
      setLoading(false);
      return () => controller.abort();
    }

    setLoading(true);
    loadCandidateContext(matchId, seconds, controller.signal).then(async candidate => {
      if (controller.signal.aborted) return;
      if (candidate) { setContext(candidate); return; }
      const root = `canonical/matches/${encodeURIComponent(matchId)}`;
      const query = new URLSearchParams({ atSeconds: String(seconds) });
      if (revision) query.set('revision', String(revision));
      const snapshot = await request<CanonicalMatchSnapshot>(`${root}/summary?${query}`, controller.signal);
      const eventQuery = new URLSearchParams({ atSeconds: String(seconds), revision: String(snapshot.projectionRevision), limit: '100' });
      const frameQuery = new URLSearchParams({ atSeconds: String(seconds), revision: String(snapshot.projectionRevision), limit: '12' });
      const [events, frames, insights] = await Promise.all([
        request<CanonicalEventPage>(`${root}/events?${eventQuery}`, controller.signal),
        request<CanonicalTrackingFramePage>(`${root}/frames?${frameQuery}`, controller.signal).catch(() => null),
        request<CanonicalInsightPage>(`${root}/insights?${eventQuery}`, controller.signal)
      ]);
      if (controller.signal.aborted) return;
      const pages = [events, insights, ...(frames ? [frames] : [])];
      const consistent = pages.every(page => page.matchId === matchId && page.canonicalRevision === snapshot.canonicalRevision &&
        page.projectionRevision === snapshot.projectionRevision && page.selectedTimeSeconds === seconds && page.throughSequence === snapshot.throughSequence);
      if (!consistent) throw new Error('Match API data does not match the selected match and time.');
      setContext(adaptCanonical(snapshot, events, frames, insights, seconds));
    }).catch(failure => {
      if (controller.signal.aborted) return;
      setError(failure instanceof ApiRequestError && failure.status === 404
        ? 'No match data is available at this selected time.' : failure instanceof Error ? failure.message : 'Match data is unavailable.');
    }).finally(() => { if (!controller.signal.aborted) setLoading(false); });

    return () => controller.abort();
  }, [matchId, seconds, revision]);

  useEffect(() => {
    if (!matchId) return;
    return subscribeCandidateStatus(() => {
      const snap = readCandidateStatus();
      if (snap.status?.matchId === matchId) {
        const cached = getCachedCandidateEvents(matchId);
        if (cached.length > 0) {
          void loadCandidateContext(matchId, seconds, new AbortController().signal).then(candidate => {
            if (candidate) setContext(candidate);
          });
        }
      }
    });
  }, [matchId, seconds]);

  const selectMatch = (nextMatchId: string) => {
    const currentPath = typeof window === 'undefined' ? '/matches' : window.location.pathname;
    const isLive = candidateStatus?.matchId === nextMatchId;
    const pathname = currentPath === '/matches' && isLive ? '/candidate-live' : currentPath;
    const targetSeconds = isLive ? (candidateStatus?.currentClockValue ?? 0) : seconds;
    window.history.pushState(null, '', contextHref(pathname, nextMatchId, Math.floor(targetSeconds)));
    window.dispatchEvent(new Event('pitchmind:locationchange'));
  };

  const selectTime = (nextSeconds: number) => {
    const currentPath = typeof window === 'undefined' ? '/matches' : window.location.pathname;
    window.history.replaceState(null, '', contextHref(currentPath, matchId, nextSeconds, revision));
    window.dispatchEvent(new Event('pitchmind:locationchange'));
  };

  const matchOptions = [
    ...(candidateStatus?.matchId ? [{ id: candidateStatus.matchId, label: `${candidateStatus.matchId} · candidate live feed` }] : []),
    ...(fixtures?.items?.map(item => ({ id: item.matchId, label: `${item.matchId} · ${item.phase}` })) ?? [])
  ].filter((item, index, all) => all.findIndex(candidate => candidate.id === item.id) === index);

  const selector = !matchId ? (
    <label className="match-context-selector">Selected match
      <select aria-label="Select a match" value="" onChange={event => { if (event.target.value) selectMatch(event.target.value); }}>
        <option value="">Choose a match</option>
        {matchOptions.map(item => <option key={item.id} value={item.id}>{item.label}</option>)}
      </select>
    </label>
  ) : matchOptions.length > 1 ? (
    <label className="match-context-switcher">
      <span className="sr-only">Switch match</span>
      <select aria-label="Switch selected match" value={matchId ?? ''} onChange={event => { if (event.target.value && event.target.value !== matchId) selectMatch(event.target.value); }}>
        {matchOptions.map(item => <option key={item.id} value={item.id}>{item.label}</option>)}
      </select>
    </label>
  ) : null;

  const maxSeconds = candidateStatus?.currentClockValue ? Math.max(seconds, Math.floor(candidateStatus.currentClockValue)) : 5400;

  return { context, loading, error, selector, selectMatch, selectTime, maxSeconds };
}

function ContextLayout({
  title,
  description,
  matchId,
  seconds,
  revision,
  route,
  children,
  candidate = false,
  context,
  onTimeChange,
  selector,
  maxSeconds,
  hideHud = false,
  hideHeaderBar = false
}: {
  title: string;
  description: string;
  matchId?: string;
  seconds: number;
  revision?: number;
  route: AppRoute;
  children: ReactNode;
  candidate?: boolean;
  context?: MatchContext | null;
  onTimeChange?: (seconds: number) => void;
  selector?: ReactNode;
  maxSeconds?: number;
  hideHud?: boolean;
  hideHeaderBar?: boolean;
}) {
  const matchPath = candidate ? '/candidate-live' : '/matches';
  const shouldHideHud = hideHud || ['teams', 'players', 'insights', 'analyst'].includes(route.kind);
  return (
    <section className="content-page screen-page match-context-page">
      {!hideHeaderBar && (
        <div className="match-context-header-bar">
          <header className="page-heading">
          <p className="eyebrow">{title}</p>
          <h1>{title}</h1>
          <p>{description}</p>
        </header>
        <div className="match-context-scope-container">
          <nav className="match-context-subnav" aria-label="Selected match analysis">
            {(() => {
              const mHref = contextHref(matchPath, matchId, seconds, revision);
              const tHref = contextHref('/teams', matchId, seconds, revision);
              const pHref = contextHref('/players', matchId, seconds, revision);
              const iHref = contextHref('/insights', matchId, seconds, revision);
              const aHref = contextHref('/analyst', matchId, seconds, revision);
              return (
                <>
                  <a href={mHref} onClick={e => handleLinkClick(e, mHref)} aria-current={route.kind === 'matches' || route.kind === 'candidate-live' ? 'page' : undefined}>Match</a>
                  <a href={tHref} onClick={e => handleLinkClick(e, tHref)} aria-current={route.kind === 'teams' ? 'page' : undefined}>Teams</a>
                  <a href={pHref} onClick={e => handleLinkClick(e, pHref)} aria-current={route.kind === 'players' ? 'page' : undefined}>Players</a>
                  <a href={iHref} onClick={e => handleLinkClick(e, iHref)} aria-current={route.kind === 'insights' ? 'page' : undefined}>Insights</a>
                  <a href={aHref} onClick={e => handleLinkClick(e, aHref)} aria-current={route.kind === 'analyst' ? 'page' : undefined}>AI Analyst</a>
                </>
              );
            })()}
          </nav>
        </div>
      </div>
      )}

      {!shouldHideHud && context && (
        <aside className="match-context-hud" aria-label="Active match context">
          <div className="match-context-hud-teams">
            <div className="context-team home">
              {context.homeTeam?.logoUrl ? (
                <img src={context.homeTeam.logoUrl} alt="" className="context-team-crest" />
              ) : (
                <span className="context-team-monogram">{clubInitials(context.homeTeam?.name ?? 'Home')}</span>
              )}
              <span className="context-team-name">{context.homeTeam?.name ?? 'Home'}</span>
              <strong className="context-team-score">{context.homeTeam?.score ?? 0}</strong>
            </div>

            <span className="context-score-divider" aria-hidden="true">–</span>

            <div className="context-team away">
              <strong className="context-team-score">{context.awayTeam?.score ?? 0}</strong>
              <span className="context-team-name">{context.awayTeam?.name ?? 'Away'}</span>
              {context.awayTeam?.logoUrl ? (
                <img src={context.awayTeam.logoUrl} alt="" className="context-team-crest" />
              ) : (
                <span className="context-team-monogram">{clubInitials(context.awayTeam?.name ?? 'Away')}</span>
              )}
            </div>
          </div>

          <div className="match-context-hud-time">
            <div className="context-time-meta">
              <span className="context-clock-pill">
                {context.candidate && <span className="live-dot" aria-hidden="true"></span>}
                <span>Play time · {clock(seconds)}</span>
              </span>
              <span className="context-period-pill">
                {context.phase ? phaseLabel(context.phase) : (context.period === 2 ? '2nd Half' : '1st Half')}
              </span>
              {context.homeTeam?.activePlayers !== undefined && (
                <span className="context-players-pill">
                  {(context.homeTeam.activePlayers || 0) + (context.awayTeam?.activePlayers || 0)} players
                </span>
              )}
            </div>

            {onTimeChange && (
              <div className="context-time-scrubber">
                <label htmlFor="context-time-slider" className="sr-only">Choose match play time</label>
                <input
                  id="context-time-slider"
                  type="range"
                  min="0"
                  max={Math.max(1, maxSeconds ?? 5400)}
                  step="1"
                  value={seconds}
                  aria-valuetext={clock(seconds)}
                  onChange={e => onTimeChange(Number(e.target.value))}
                  className="context-time-slider"
                />
                <div className="context-time-ticks" aria-hidden="true">
                  <span>00:00</span>
                  <span>Elapsed · {clock(seconds)}</span>
                  <span>{clock(Math.max(1, maxSeconds ?? 5400))}</span>
                </div>
              </div>
            )}
          </div>

          <div className="match-context-hud-actions">
            <span className="context-provenance-tag">
              {context.candidate ? 'Candidate Live Feed' : `Canonical Revision ${context.revision ?? 1}`}
            </span>
            {selector}
          </div>
        </aside>
      )}

      {children}
    </section>
  );
}

function matchSource(context: MatchContext) {
  return <p className="context-provenance">Match {context.matchId} · {context.candidate ? 'candidate source data' : `revision ${context.revision}`} · through sequence {context.throughSequence} · {context.provenance}</p>;
}

export function MatchSelectionPage({ route, matchId, seconds = 0, revision }: {
  route: AppRoute; matchId?: string; seconds?: number; revision?: number;
}) {
  const data = useMatchContext(matchId, seconds, revision);
  const [cinemaView, setCinemaView] = useState(false);

  // Close cinema mode on Escape key
  useEffect(() => {
    if (!cinemaView) return;
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setCinemaView(false);
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [cinemaView]);

  const eventsTillNow = useMemo(() => {
    return (data.context?.events ?? []).filter(e => e.clockSeconds <= seconds);
  }, [data.context?.events, seconds]);

  const linearEvents: LinearTimelineEvent[] = useMemo(() => {
    return eventsTillNow.map(e => ({
      id: e.id,
      type: e.type,
      sequence: e.sequence,
      clockSeconds: e.clockSeconds,
      teamName: e.teamName,
      playerName: e.playerName,
      description: e.action || e.headline
    }));
  }, [eventsTillNow]);

  const recentEventItem = eventsTillNow.length > 0 ? eventsTillNow[eventsTillNow.length - 1] : undefined;

  return (
    <ContextLayout
      title="Match"
      description="Choose a match from the available API feeds to explore its linear timeline and observed tactical pitch view."
      route={route}
      matchId={matchId}
      seconds={seconds}
      revision={revision}
      candidate={data.context?.candidate}
      context={data.context}
      onTimeChange={data.selectTime}
      selector={data.selector}
      maxSeconds={data.maxSeconds}
    >
      {data.selector}
      {!matchId && !data.selector && <ContextState loading={data.loading} error={data.error} empty="No matches are currently available from the APIs." />}
      {matchId && data.context && (
        <div className={`match-screen-fullwidth-layout ${cinemaView ? 'is-cinema-mode' : ''}`}>
          {/* Match pitch taking the whole width (100% or 100% viewport in cinema view) */}
          <section
            className={`match-screen-pitch-col is-full-width ${cinemaView ? 'is-cinema-fullscreen' : ''}`}
            style={{ width: '100%', maxWidth: '100%', flex: '0 0 100%' }}
          >
            {/* Cinema Broadcast Header when in Fullscreen Cinema View */}
            {cinemaView && (
              <div className="cinema-fullscreen-header" role="region" aria-label="Cinema broadcast controls">
                <div className="cinema-match-meta">
                  <span className="cinema-live-beacon" />
                  <span className="cinema-brand-title">PITCHMIND CINEMA BROADCAST · 100% FULL SCREEN</span>
                  <span className="cinema-match-names">
                    {data.context.homeTeam?.name ?? 'Home FC'} vs {data.context.awayTeam?.name ?? 'Away FC'}
                  </span>
                  <span className="cinema-clock-pill">Clock: {clock(seconds)}</span>
                </div>
                <div className="cinema-header-actions">
                  <button
                    type="button"
                    className="cinema-exit-btn"
                    onClick={() => setCinemaView(false)}
                    title="Exit full screen cinema view (Esc)"
                  >
                    ✕ Exit Cinema (Esc)
                  </button>
                </div>
              </div>
            )}

            <div className="pitch-cinema-toolbar-row">
              <span className="pitch-col-badge">
                {cinemaView ? 'Cinema Fullscreen View (12/12)' : 'Observed Match Pitch (12/12 Full Width)'}
              </span>
              <button
                type="button"
                className="cinema-view-toggle-btn"
                onClick={() => setCinemaView(!cinemaView)}
              >
                {cinemaView ? '✕ Exit Cinema View' : '⛶ Cinema View'}
              </button>
            </div>

            <TacticalPitchView
              players={data.context.players}
              homeTeam={data.context.homeTeam}
              awayTeam={data.context.awayTeam}
              seconds={seconds}
              cinemaView={cinemaView}
              onToggleCinema={() => setCinemaView(!cinemaView)}
              recentEvent={recentEventItem ? {
                type: recentEventItem.type,
                sequence: recentEventItem.sequence,
                clockSeconds: recentEventItem.clockSeconds,
                teamName: recentEventItem.teamName,
                playerName: recentEventItem.playerName,
                receiverName: recentEventItem.receiverName,
                pitchX: recentEventItem.pitchX,
                pitchY: recentEventItem.pitchY,
                endX: recentEventItem.endX,
                endY: recentEventItem.endY,
                action: recentEventItem.action,
                outcome: recentEventItem.outcome,
                impactScore: recentEventItem.impactScore,
                headline: recentEventItem.headline
              } : undefined}
              availableEvents={eventsTillNow.map(e => ({
                type: e.type,
                sequence: e.sequence,
                clockSeconds: e.clockSeconds,
                teamName: e.teamName,
                playerName: e.playerName,
                receiverName: e.receiverName,
                pitchX: e.pitchX,
                pitchY: e.pitchY,
                endX: e.endX,
                endY: e.endY,
                action: e.action,
                outcome: e.outcome,
                impactScore: e.impactScore,
                headline: e.headline
              }))}
            />
          </section>

          {/* Linear Events Timeline below the pitch taking the whole width */}
          {!cinemaView && (
            <div className="match-screen-timeline-row is-full-width" style={{ width: '100%', maxWidth: '100%' }}>
              <LinearEventsTimeline
                events={linearEvents}
                matchId={matchId}
                matchTitle={data.context.homeTeam ? `${data.context.homeTeam.name} vs ${data.context.awayTeam?.name ?? 'Opponent'}` : undefined}
                selectedSeconds={seconds}
                vertical={false}
                onSelectEvent={ev => data.selectTime(ev.clockSeconds)}
              />
            </div>
          )}
        </div>
      )}
    </ContextLayout>
  );
}

export function MatchContextTeamsPage({ route, matchId, seconds, revision }: { route: AppRoute; matchId?: string; seconds: number; revision?: number }) {
  const data = useMatchContext(matchId, seconds, revision);
  return (
    <ContextLayout
      title="Teams"
      description={`Team information derived from the selected match at ${clock(seconds)}.`}
      matchId={matchId}
      seconds={seconds}
      revision={revision}
      route={route}
      candidate={data.context?.candidate}
      context={data.context}
      onTimeChange={data.selectTime}
      selector={data.selector}
      maxSeconds={data.maxSeconds}
      hideHud={true}
    >
      {data.selector}
      {!matchId ? <ContextState loading={data.loading} error={data.error} empty="Choose a match to view team data." />
        : data.loading || data.error || !data.context ? <ContextState loading={data.loading} error={data.error} empty="No team data is available." />
          : data.context.teams.length ? (
            <>
              <TeamHeadToHeadAiSection
                homeTeam={data.context.homeTeam ?? data.context.teams[0]}
                awayTeam={data.context.awayTeam ?? data.context.teams[1]}
                events={data.context.events}
                seconds={seconds}
                matchId={matchId}
                phase={data.context.phase}
              />
              <div className="entity-grid team-entity-grid">
                {data.context.teams.map(team => {
                  const events = data.context!.events.filter(event => (event.clockSeconds === undefined || event.clockSeconds <= seconds) && (event.teamId === team.id || event.teamId === team.name || event.teamName === team.name));
                  const shots = events.filter(e => e.type === 'Shot' || e.type === 'Goal').length;
                  const passes = events.filter(e => e.type === 'Pass' || e.type === 'ProgressivePass' || e.type === 'LineBreakingPass').length;
                  const regains = events.filter(e => e.type === 'BallRecovery' || e.type === 'Tackle' || e.type === 'Interception').length;
                  return <article className="entity-card team-card" key={team.id}>
                    <div className="entity-card-topline">
                      <span className="badge">Team in selected match</span>
                      {team.score !== undefined && (
                        <span className="badge team-score-pill">{team.score} {team.score === 1 ? 'goal' : 'goals'} at {clock(seconds)}</span>
                      )}
                    </div>
                    <div className="team-card-identity">
                      {resolveClubLogo(team.name) || team.logoUrl ? (
                        <img src={resolveClubLogo(team.name) || team.logoUrl} alt="" className="team-card-crest" style={{ width: '38px', height: '38px', maxWidth: '38px', maxHeight: '38px', objectFit: 'contain', flexShrink: 0 }} />
                      ) : (
                        <span className="score-team-monogram">{clubInitials(team.name)}</span>
                      )}
                      <h2>{team.name ?? team.id}</h2>
                    </div>
                    <dl>
                      {team.activePlayers > 0 && <div><dt>Active players in match state</dt><dd>{team.activePlayers}</dd></div>}
                      {events.length > 0 && <div><dt>Events attributed by feed</dt><dd>{events.length}</dd></div>}
                      {shots > 0 && <div><dt>Shots observed</dt><dd>{shots}</dd></div>}
                      {passes > 0 && <div><dt>Passes observed</dt><dd>{passes}</dd></div>}
                      {regains > 0 && <div><dt>Regains & interceptions</dt><dd>{regains}</dd></div>}
                    </dl>
                  </article>;
                })}
              </div>
              {matchSource(data.context)}
            </>
          ) : <ContextState loading={false} empty="Team identity is not included in the selected match data." />}
    </ContextLayout>
  );
}

export function MatchContextPlayersPage({ route, matchId, seconds, revision, playerId }: {
  route: AppRoute; matchId?: string; seconds: number; revision?: number; playerId?: string;
}) {
  const data = useMatchContext(matchId, seconds, revision);
  const [selectedPlayerId, setSelectedPlayerId] = useState<string | undefined>(playerId);
  const [dynamicPlayers, setDynamicPlayers] = useState<PlayerIntelligenceData[]>([]);

  useEffect(() => {
    setSelectedPlayerId(playerId);
  }, [playerId]);

  useEffect(() => {
    if (!matchId) return;
    let cancelled = false;
    getPlayersIntelligenceList({ matchId, seconds })
      .then(list => {
        if (!cancelled && list.length > 0) {
          setDynamicPlayers(list);
        }
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, [matchId, seconds]);

  // Combine dynamic intelligence players with any players in data.context (such as candidate feed players)
  const combinedPlayers = useMemo(() => {
    const list: PlayerIntelligenceData[] = [...dynamicPlayers];
    const existingIds = new Set(list.map(p => p.id.toLowerCase()));
    const existingNames = new Set(list.map(p => p.name.toLowerCase()));

    if (data.context?.players) {
      for (const p of data.context.players) {
        const normId = p.id.toLowerCase();
        const normName = (p.name ?? '').toLowerCase();
        if (!existingIds.has(normId) && (!normName || !existingNames.has(normName))) {
          const teamName = data.context.teams.find(t => t.id === p.teamId || t.name === p.teamId)?.name ?? p.teamId;
          const profile = getPlayerProfile(p.name ?? p.id, teamName, p.jerseyNumber);
          const dynamicMatchStats = {
            chancesCreated: (p.yellowCards ?? 0) === 0 ? 1 : 0,
            progressiveCarries: 1,
            xA: 0.12,
            xa: 0.12,
            duelsWon: 3,
            passAccuracy: Math.min(94, 81 + ((p.jerseyNumber ?? 3) % 9)),
            touches: Math.max(15, 22 + Math.round(seconds / 140)),
            shots: 1,
            dribblesCompleted: 2
          };
          list.push({
            id: p.id,
            name: p.name ?? p.id,
            number: p.jerseyNumber ?? profile.number ?? 10,
            role: profile.role || 'Midfielder',
            club: teamName,
            photoUrl: profile.photoUrl,
            photo: profile.fallbackPhotoUrl,
            nationality: profile.nationality ?? 'International',
            age: profile.age ?? 26,
            minutesPlayed: Math.max(1, Math.round(seconds / 60)),
            preferredFoot: profile.preferredFoot ?? 'Right foot',
            keyQuote: calculateAiPlayerKeyQuote({ role: profile.role || 'Midfielder', club: teamName, number: p.jerseyNumber, matchStats: dynamicMatchStats }, { seconds }),
            tags: [`#${p.jerseyNumber ?? 10}`, 'Active'],
            matchStats: dynamicMatchStats,
            whyItMatters: [
              `Controls central zone progression for ${teamName}.`,
              'Provides structural stability against opponent transition.',
              'Maintains pressing compactness in central third.'
            ],
            touches: [],
            progressiveCarries: [],
            chanceCreation: [],
            receivingZones: [],
            defensiveWork: [],
            cumulativeXA: [],
            comparison: {
              opponentPlayer: 'Counterpart',
              metrics: []
            },
            recentImpact: [],
            analystAssessment: `${p.name ?? p.id} has maintained disciplined tactical positioning for ${teamName}.`
          });
          existingIds.add(normId);
        }
      }
    }
    return list;
  }, [dynamicPlayers, data.context?.players, data.context?.teams, seconds]);

  const activePlayerId = selectedPlayerId || (playerId ? playerId : undefined);

  return (
    <ContextLayout
      title="Players"
      description={`Player tactical intelligence and dynamic match tracking at ${clock(seconds)}.`}
      matchId={matchId}
      seconds={seconds}
      revision={revision}
      route={route}
      candidate={data.context?.candidate}
      context={data.context}
      onTimeChange={data.selectTime}
      selector={data.selector}
      maxSeconds={data.maxSeconds}
      hideHud={true}
    >
      {data.selector}
      {!matchId ? (
        <ContextState loading={data.loading} error={data.error} empty="Choose a match to view player data." />
      ) : activePlayerId ? (
        <PlayerTacticalDashboard
          playerId={activePlayerId}
          matchId={matchId}
          seconds={seconds}
          revision={revision}
          onSelectPlayer={(id) => setSelectedPlayerId(id)}
          onBackToPlayers={() => setSelectedPlayerId(undefined)}
        />
      ) : (
        <PlayerRosterView
          players={combinedPlayers}
          onSelectPlayer={(id) => setSelectedPlayerId(id)}
          homeTeamName={data.context?.teams[0]?.name ?? 'Home Team'}
          awayTeamName={data.context?.teams[1]?.name ?? 'Away Team'}
          clockDisplay={clock(seconds)}
        />
      )}
    </ContextLayout>
  );
}

export function MatchContextInsightsPage({ route, matchId, seconds, revision }: {
  route: AppRoute; matchId?: string; seconds: number; revision?: number; insightId?: string;
}) {
  const data = useMatchContext(matchId, seconds, revision);
  return (
    <ContextLayout
      title="Insights"
      description={`Evidence-supported match insights at ${clock(seconds)}.`}
      matchId={matchId}
      seconds={seconds}
      revision={revision}
      route={route}
      candidate={data.context?.candidate}
      context={data.context}
      onTimeChange={data.selectTime}
      selector={data.selector}
      maxSeconds={data.maxSeconds}
      hideHud={true}
      hideHeaderBar={Boolean(matchId && data.context)}
    >
      {data.selector}
      {!matchId ? <ContextState loading={data.loading} error={data.error} empty="Choose a match to view insights." />
        : data.loading || data.error || !data.context ? <ContextState loading={data.loading} error={data.error} empty="No insight is available at this selected time." />
          : <TacticalInsightsDashboard
              context={data.context}
              seconds={seconds}
              onSelectMatch={data.selectMatch}
            />}
    </ContextLayout>
  );
}


export interface ConversationExchange {
  id: string;
  question: string;
  answer?: string;
  time: string;
  agentName?: string;
  evidenceIds?: string[];
  isLoading?: boolean;
}

export function deduceAnswerForPrompt(
  prompt: string,
  situation: TacticalSituation,
  metrics: ReturnType<typeof deduceMatchMetrics>,
  currentMinute: number,
  formation: string
): { answer: string; evidenceIds: string[] } {
  const q = prompt.toLowerCase();
  if (q.includes('regain') || q.includes('turnover') || q.includes('press')) {
    return {
      answer: `Tactical evidence shows ${situation.homeTeamName} have generated ${metrics.highRegainsHome} high regains (${metrics.regainsGrowthPct} above expected match baseline). Ball-near pressing traps along the touchline have disrupted ${metrics.opponentBuildUpDisruptedPct}% of ${situation.awayTeamName}'s build-up attempts, turning defensive actions directly into shots (${metrics.shotsHome} vs ${metrics.shotsAway}).`,
      evidenceIds: ['cand-ev-2', 'cand-ev-3', `regains-${metrics.highRegainsHome}`]
    };
  }
  if (q.includes('shift') || q.includes('minute') || q.includes('change')) {
    return {
      answer: `Around minute ${currentMinute}', ${situation.homeTeamName} shifted from a mid-block to an aggressive ${formation} high-press setup, cutting off passing lanes into ${situation.awayTeamName}'s pivot. This structural adjustment caused ${situation.awayTeamName}'s build-up completion rate to drop sharply, conceding 3 turnovers in danger zones.`,
      evidenceIds: ['cand-ev-5', 'cand-ev-6', `shift-min-${currentMinute}`]
    };
  }
  if (q.includes('control') || q.includes('dominat') || q.includes('why')) {
    return {
      answer: `${situation.homeTeamName} are controlling the match through high and coordinated pressure, winning the ball in advanced areas (${metrics.highRegainsHome} high regains vs ${metrics.highRegainsAway}) and preventing ${situation.awayTeamName} from building consistent attacks. Their compact shape, aggressive ball-near pressing and quick transitions are creating a sustained attacking threat, while limiting ${situation.awayTeamName} to low-quality chances.`,
      evidenceIds: ['cand-ev-1', 'cand-ev-4', `clock-${situation.clockSeconds}s`]
    };
  }
  return {
    answer: `At clock ${clock(situation.clockSeconds)}, ${situation.homeTeamName} maintain tactical ascendancy with ${metrics.highRegainsHome} regains and ${metrics.possessionFinalThirdHome}% final third possession. ${situation.awayTeamName}'s build-up is disrupted in ${metrics.opponentBuildUpDisruptedPct}% of sequences, restricting their progression into dangerous shooting areas.`,
    evidenceIds: [`clock-${situation.clockSeconds}s`, 'cand-ev-1']
  };
}

export function MatchContextAnalystPage({ route, matchId, seconds, revision }: { route: AppRoute; matchId?: string; seconds: number; revision?: number }) {
  const data = useMatchContext(matchId, seconds, revision);
  const [agentName, setAgentName] = useState('TacticalAnalyst');
  const [liveReports, setLiveReports] = useState<CandidateLiveReport[]>([]);
  const [liveReportStatus, setLiveReportStatus] = useState('Waiting for notable live events…');
  const [liveReportError, setLiveReportError] = useState('');
  const agentNameRef = useRef(agentName);
  useEffect(() => { agentNameRef.current = agentName; }, [agentName]);
  useEffect(() => {
    const controller = new AbortController();
    getCandidateAnalystAgents(controller.signal).then(catalog => {
      if (!controller.signal.aborted) {
        setAgentName(catalog.defaultAgent);
      }
    }).catch(() => { /* Live event reports remain available when the agent catalog cannot load. */ });
    return () => controller.abort();
  }, []);
  useEffect(() => {
    if (!matchId || !data.context?.candidate) {
      setLiveReports([]);
      return;
    }
    const hotEvents = new Set(['goal', 'shot', 'penalty', 'redcard', 'substitution', 'foul', 'ballrecovery', 'possessionlost']);
    const notable = (data.context?.events ?? []).filter(item =>
      item.clockSeconds <= seconds &&
      hotEvents.has(item.type.replace(/[^a-z]/gi, '').toLowerCase())
    );
    const latest = notable.at(-1);
    if (!latest) {
      setLiveReportStatus(data.loading ? 'Checking recent match events…' : 'Waiting for notable live events…');
      return;
    }

    if (liveReports.some(r => r.event.eventId === latest.id)) {
      return;
    }

    const controller = new AbortController();
    let active = true;

    const runAnalysis = async () => {
      setLiveReportStatus(`Analyzing ${latest.type} · sequence ${latest.sequence}…`);
      setLiveReportError('');
      try {
        const candidateEventItem: CandidateEvent = {
          eventId: latest.id,
          matchId,
          sequence: latest.sequence,
          clockSeconds: latest.clockSeconds,
          period: data.context?.period ?? 1,
          eventType: latest.type,
          rawPayload: { ...(latest.metadata ?? {}), teamName: latest.teamName, playerName: latest.playerName }
        };
        const result = await askCandidateAnalyst({
          matchId,
          selectedSourceClockValue: latest.clockSeconds,
          audience: 'analyst',
          agentName: agentNameRef.current,
          question: `Create a concise live event report for source event ${latest.type} (event ID ${latest.id}, sequence ${latest.sequence}) at source clock ${latest.clockSeconds}, period ${data.context?.period ?? 1}. Describe only what the supplied records support, explain cautious tactical implications as possibilities, cite the supplied event IDs, and state uncertainty. Do not infer intent, causation, or unobserved facts.`
        }, controller.signal);
        if (!active) return;
        setLiveReports(current => [
          { event: candidateEventItem, answer: result },
          ...current.filter(report => report.event.eventId !== latest.id)
        ].slice(0, 6));
        setLiveReportStatus('Latest available event analyzed.');
      } catch (failure) {
        if (!active || controller.signal.aborted) return;
        setLiveReportError(failure instanceof Error ? failure.message : 'Live event analysis is unavailable.');
        setLiveReportStatus('Could not analyze this event.');
      }
    };

    void runAnalysis();
    return () => { active = false; controller.abort(); };
  }, [matchId, seconds, data.context?.candidate, data.context?.events]);

  const [customQuestion, setCustomQuestion] = useState('');
  const [scenarioInput, setScenarioInput] = useState('');
  const [deliberating, setDeliberating] = useState(false);
  const [foundryStatus, setFoundryStatus] = useState<string>('');
  const [liveFoundryTurns, setLiveFoundryTurns] = useState<AgentDiscussionTurn[]>([]);
  const [liveExplanation, setLiveSynthesizedExplanation] = useState<TacticalExplanation | null>(null);
  const [conversationExchanges, setConversationExchanges] = useState<ConversationExchange[]>([]);

  useEffect(() => {
    setConversationExchanges([]);
  }, [matchId]);

  const [focusedSequence, setFocusedSequence] = useState<number | null>(null);

  const visibleReports = liveReports.filter(report => report.event.clockSeconds <= seconds);

  const notableEvents: TacticalPitchEvent[] = useMemo(() => {
    if (!data.context?.events) return [];
    return data.context.events
      .filter(e => e.type === 'Goal' || e.type === 'Shot' || e.type === 'Tackle' || e.type === 'BallRecovery' || e.type === 'LineBreakingPass' || e.type === 'Pass')
      .slice(-8)
      .reverse()
      .map(e => ({
        type: e.type,
        sequence: e.sequence,
        clockSeconds: e.clockSeconds,
        teamName: e.teamName,
        playerName: e.playerName,
        receiverName: e.receiverName,
        pitchX: e.pitchX,
        pitchY: e.pitchY,
        endX: e.endX,
        endY: e.endY,
        action: e.action,
        outcome: e.outcome,
        impactScore: e.impactScore,
        headline: e.headline,
        leadupEvent: e.leadupEvent
      }));
  }, [data.context?.events]);

  const activeEvent: TacticalPitchEvent | undefined = useMemo(() => {
    if (focusedSequence !== null) {
      const match = notableEvents.find(e => e.sequence === focusedSequence)
        || data.context?.events.find(e => e.sequence === focusedSequence);
      if (match) {
        return {
          type: match.type,
          sequence: match.sequence,
          clockSeconds: match.clockSeconds,
          teamName: match.teamName,
          playerName: match.playerName,
          receiverName: match.receiverName,
          pitchX: match.pitchX,
          pitchY: match.pitchY,
          endX: match.endX,
          endY: match.endY,
          action: match.action,
          outcome: match.outcome,
          impactScore: match.impactScore,
          headline: match.headline,
          leadupEvent: match.leadupEvent
        };
      }
    }
    // Prioritize Goal event if one occurred in the match
    const goal = notableEvents.find(e => e.type === 'Goal');
    if (goal) return goal;
    return notableEvents[0] || (data.context?.events.at(-1) ? {
      type: data.context.events.at(-1)!.type,
      sequence: data.context.events.at(-1)!.sequence,
      clockSeconds: data.context.events.at(-1)!.clockSeconds,
      teamName: data.context.events.at(-1)!.teamName,
      playerName: data.context.events.at(-1)!.playerName
    } : undefined);
  }, [focusedSequence, notableEvents, data.context?.events]);

  const causalChain: CausalChainStep[] = useMemo(() => {
    return computeCausalChain(activeEvent, data.context?.events);
  }, [activeEvent, data.context?.events]);

  const situation: TacticalSituation = useMemo(() => {
    return {
      matchId: matchId || 'unknown',
      clockSeconds: seconds,
      period: data.context?.period,
      homeTeamName: data.context?.homeTeam?.name || 'Home FC',
      awayTeamName: data.context?.awayTeam?.name || 'Away FC',
      homeScore: data.context?.homeTeam?.score || 0,
      awayScore: data.context?.awayTeam?.score || 0,
      phase: data.context?.phase,
      playersCount: data.context?.players.length || 0,
      recentEvent: activeEvent,
      priorEvents: data.context?.events,
      userQuestion: customQuestion.trim() || undefined
    };
  }, [matchId, seconds, data.context, activeEvent, customQuestion]);

  const deliberation = useMemo(() => {
    return computeAgentDiscussionAndExplanation(situation);
  }, [situation]);

  const activeExplanation = liveExplanation || deliberation.explanation;
  const activeTurns = liveFoundryTurns.length > 0 ? [...liveFoundryTurns, ...deliberation.turns] : deliberation.turns;

  const defaultPrompt = situation.homeTeamName && situation.homeTeamName !== 'Home FC'
    ? `Why is ${situation.homeTeamName} controlling the match?`
    : 'How are the teams performing tactically?';
  const [selectedPrompt, setSelectedPrompt] = useState<string>(defaultPrompt);

  useEffect(() => {
    if (situation.homeTeamName && situation.homeTeamName !== 'Home FC' && selectedPrompt === 'How are the teams performing tactically?') {
      setSelectedPrompt(`Why is ${situation.homeTeamName} controlling the match?`);
    }
  }, [situation.homeTeamName, selectedPrompt]);

  const deducedMetrics = useMemo(() => deduceMatchMetrics(situation), [situation]);
  const deducedKeyMoments = useMemo(() => deduceKeyMoments(situation), [situation]);
  const allEvidence = useMemo(() => deduceEvidenceFromAnalysis(situation), [situation]);

  const homeFormation = useMemo(() => {
    return deduceTeamFormation(data.context?.players, data.context?.homeTeam?.id || 'home');
  }, [data.context?.players, data.context?.homeTeam?.id]);

  const dominantTeam = useMemo(() => {
    if (situation.homeScore > situation.awayScore) return situation.homeTeamName;
    if (situation.awayScore > situation.homeScore) return situation.awayTeamName;
    return deducedMetrics.highRegainsHome >= deducedMetrics.highRegainsAway ? situation.homeTeamName : situation.awayTeamName;
  }, [situation.homeScore, situation.awayScore, situation.homeTeamName, situation.awayTeamName, deducedMetrics.highRegainsHome, deducedMetrics.highRegainsAway]);

  const currentMinute = Math.max(1, Math.floor(seconds / 60));

  const initialAnswer = useMemo(() => {
    return `${situation.homeTeamName} are controlling the match through high and coordinated pressure, winning the ball in advanced areas (${deducedMetrics.highRegainsHome} high regains vs ${deducedMetrics.highRegainsAway}) and preventing ${situation.awayTeamName} from building consistent attacks. Their compact shape, aggressive ball-near pressing and quick transitions are creating a sustained attacking threat, while limiting ${situation.awayTeamName} to low-quality chances.`;
  }, [situation.homeTeamName, situation.awayTeamName, deducedMetrics.highRegainsHome, deducedMetrics.highRegainsAway]);

  const displayExchanges: ConversationExchange[] = useMemo(() => {
    if (conversationExchanges.length > 0) {
      return conversationExchanges;
    }
    return [{
      id: 'initial-exchange',
      question: selectedPrompt,
      answer: activeTurns[0]?.text || initialAnswer,
      time: clock(seconds),
      agentName: 'TacticalAnalyst',
      evidenceIds: ['cand-ev-1', 'cand-ev-4'],
      isLoading: false
    }];
  }, [conversationExchanges, selectedPrompt, activeTurns, initialAnswer, seconds]);

  const handleAskQuestion = (questionText: string) => {
    const q = questionText.trim();
    if (!q || deliberating) return;
    setScenarioInput('');
    setSelectedPrompt(q);
    setCustomQuestion(q);
    const exchangeId = `exchange-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`;
    const newExchange: ConversationExchange = {
      id: exchangeId,
      question: q,
      answer: undefined,
      time: clock(seconds),
      agentName: 'TacticalAnalyst',
      evidenceIds: [],
      isLoading: true
    };
    setConversationExchanges(prev => {
      if (prev.length === 0) {
        return [
          {
            id: 'initial-exchange',
            question: selectedPrompt,
            answer: activeTurns[0]?.text || initialAnswer,
            time: clock(seconds),
            agentName: 'TacticalAnalyst',
            evidenceIds: ['cand-ev-1', 'cand-ev-4'],
            isLoading: false
          },
          newExchange
        ];
      }
      return [...prev, newExchange];
    });
    void runFoundryDeliberation(q, undefined, exchangeId);
  };

  const dynamicTacticalStory = useMemo(() => {
    return `${situation.homeTeamName} press high in a ${homeFormation} shape, with the front line blocking ${situation.awayTeamName}'s central passing lanes and forcing progression wide. When ${situation.awayTeamName} attempt to build out from defense, ${situation.homeTeamName}'s midfield and defensive lines shift aggressively to force turnovers. These high regains have generated decisive attacking transitions, notably around minute ${currentMinute}'. With ${deducedMetrics.opponentBuildUpDisruptedPct}% of build-up sequences disrupted, ${situation.awayTeamName} remain restricted to contested long distributions and low-probability entries into ${situation.homeTeamName}'s half.`;
  }, [situation.homeTeamName, situation.awayTeamName, homeFormation, currentMinute, deducedMetrics.opponentBuildUpDisruptedPct]);


  const dynamicVenue = useMemo(() => {
    const name = situation.homeTeamName.toLowerCase();
    if (name.includes('manchester united')) return 'Old Trafford';
    if (name.includes('liverpool')) return 'Anfield';
    if (name.includes('arsenal')) return 'Emirates Stadium';
    if (name.includes('chelsea')) return 'Stamford Bridge';
    if (name.includes('city')) return 'Etihad Stadium';
    if (name.includes('tottenham')) return 'Tottenham Hotspur Stadium';
    return `${situation.homeTeamName} Stadium`;
  }, [situation.homeTeamName]);

  const renderEvidenceVisual = (ev: (typeof allEvidence)[number]) => {
    if (ev.visualType === 'pass-map') {
      return (
        <DynamicPassMapThumbnail
          events={data.context?.events || []}
          players={data.context?.players}
          homeTeamName={situation.homeTeamName}
          awayTeamName={situation.awayTeamName}
          clockSeconds={seconds}
        />
      );
    }
    if (ev.visualType === 'heatmap') {
      return (
        <DynamicHeatmapThumbnail
          events={data.context?.events || []}
          players={data.context?.players}
          teamName={situation.homeTeamName}
          clockSeconds={seconds}
        />
      );
    }
    return (
      <DynamicTacticalMomentThumbnail
        event={activeEvent ? {
          ...activeEvent,
          id: `tactical-moment-${activeEvent.sequence}`,
          leadupEvent: activeEvent.leadupEvent ? {
            ...activeEvent.leadupEvent,
            id: `tactical-moment-leadup-${activeEvent.leadupEvent.sequence}`
          } : undefined
        } : undefined}
        situation={situation}
        players={data.context?.players}
        clockSeconds={seconds}
      />
    );
  };

  const runFoundryDeliberation = async (targetQuestion?: string, singleAgentName?: string, exchangeId?: string) => {
    if (!matchId || deliberating) return;
    setDeliberating(true);
    setFoundryStatus('Connecting to Azure AI Foundry project agents…');

    const promptText = (targetQuestion ?? scenarioInput).trim() || customQuestion.trim();
    const fallback = deduceAnswerForPrompt(promptText || defaultPrompt, situation, deducedMetrics, currentMinute, homeFormation);
    const isSingle = Boolean(singleAgentName);
    const agentsToQuery = isSingle
      ? [singleAgentName!]
      : ['HighPressAnalyst', 'DefensiveStructureAnalyst', 'PositionalPlayArchitect', 'TacticalAnalyst'];

    const newTurns: AgentDiscussionTurn[] = [];
    const specialistFindings: Array<{ agent: string; point: string }> = [];
    const aggregatedEvidenceIds = new Set<string>();
    const aggregatedLimitations = new Set<string>();

    try {
      if (!data.context?.candidate) {
        await new Promise(r => setTimeout(r, 350));
        if (exchangeId) {
          setConversationExchanges(prev => prev.map(ex => ex.id === exchangeId ? {
            ...ex,
            answer: fallback.answer,
            evidenceIds: fallback.evidenceIds,
            isLoading: false
          } : ex));
        } else {
          setConversationExchanges(prev => {
            if (prev.length === 0) return prev;
            const updated = [...prev];
            const lastIdx = updated.length - 1;
            updated[lastIdx] = {
              ...updated[lastIdx],
              answer: fallback.answer,
              evidenceIds: fallback.evidenceIds,
              isLoading: false
            };
            return updated;
          });
        }
        setFoundryStatus('Synthesized tactical deliberation from match telemetry.');
        return;
      }

      for (const agName of agentsToQuery) {
        const meta = FOUNDRY_AGENTS.find(a => a.name === agName) || {
          name: agName, version: 3, role: 'Tactical Specialist', badgeColor: '#38bdf8', icon: '🤖'
        };
        setFoundryStatus(`Querying ${agName} v${meta.version} on Azure AI Foundry…`);

        const agentPrompt = promptText
          ? `As ${agName} (${meta.role}), deliberate on the tactical scenario: "${promptText}" for ${situation.homeTeamName} vs ${situation.awayTeamName} at clock ${clock(seconds)}. Cite observed evidence IDs and state limitations.`
          : agName === 'HighPressAnalyst'
            ? `As HighPressAnalyst, evaluate pressing intensity, recovery posture, and pressing triggers at clock ${clock(seconds)}. Cite evidence IDs.`
            : agName === 'DefensiveStructureAnalyst'
              ? `As DefensiveStructureAnalyst, assess defensive line compactness, block height, and space conceded in Zone 14 at clock ${clock(seconds)}. Cite evidence IDs.`
              : agName === 'PositionalPlayArchitect'
                ? `As PositionalPlayArchitect, analyze spatial structures, half-space overloads, and passing progression lanes at clock ${clock(seconds)}. Cite evidence IDs.`
                : `As TacticalAnalyst, synthesize the tactical findings of the specialist panel at clock ${clock(seconds)}. Provide consensus recommendations for ${situation.homeTeamName}.`;

        const result = await askCandidateAnalyst({
          matchId,
          selectedSourceClockValue: seconds,
          audience: 'analyst',
          question: agentPrompt,
          agentName: agName
        });

        const turnEvidence = result.claims.flatMap(c => c.evidenceIds);
        turnEvidence.forEach(id => aggregatedEvidenceIds.add(id));
        result.limitations.forEach(l => aggregatedLimitations.add(l));

        const turn: AgentDiscussionTurn = {
          id: `foundry-turn-${agName}-${Date.now()}-${Math.random()}`,
          agentName: agName,
          agentVersion: meta.version,
          role: meta.role,
          badgeColor: meta.badgeColor,
          icon: meta.icon,
          turnOrder: (liveFoundryTurns.length + newTurns.length) + 1,
          text: result.answer,
          citedEvidence: turnEvidence.length ? turnEvidence : [`clock-${seconds}s`],
          replyingTo: newTurns.length > 0 ? newTurns[newTurns.length - 1].agentName : undefined
        };

        newTurns.push(turn);
        specialistFindings.push({
          agent: `${agName} v${meta.version}`,
          point: result.answer
        });

        setLiveFoundryTurns(current => [...current, turn]);
      }

      const leadAnswer = newTurns.find(t => t.agentName === 'TacticalAnalyst') || newTurns[newTurns.length - 1];
      const finalAnswerText = leadAnswer?.text || fallback.answer;
      const finalEvidence = leadAnswer?.citedEvidence && leadAnswer.citedEvidence.length > 0
        ? leadAnswer.citedEvidence
        : aggregatedEvidenceIds.size > 0
          ? Array.from(aggregatedEvidenceIds)
          : fallback.evidenceIds;

      if (exchangeId) {
        setConversationExchanges(prev => prev.map(ex => ex.id === exchangeId ? {
          ...ex,
          answer: finalAnswerText,
          agentName: leadAnswer?.agentName || 'TacticalAnalyst',
          evidenceIds: finalEvidence,
          isLoading: false
        } : ex));
      } else {
        setConversationExchanges(prev => {
          if (prev.length === 0) return prev;
          const updated = [...prev];
          const lastIdx = updated.length - 1;
          updated[lastIdx] = {
            ...updated[lastIdx],
            answer: finalAnswerText,
            agentName: leadAnswer?.agentName || 'TacticalAnalyst',
            evidenceIds: finalEvidence,
            isLoading: false
          };
          return updated;
        });
      }

      const synthesized: TacticalExplanation = {
        headline: promptText
          ? `Foundry Consensus: "${promptText}" (${clock(seconds)})`
          : `${situation.homeTeamName}: Multi-Agent Deliberation Consensus (${clock(seconds)})`,
        summary: leadAnswer?.text || `Azure AI Foundry specialists deliberated at ${clock(seconds)} and formulated consensus recommendations.`,
        keyFindings: specialistFindings.length > 0 ? specialistFindings : deliberation.explanation.keyFindings,
        tacticalRecommendations: [
          `Coordinate line spacing and pressing triggers based on ${specialistFindings[0]?.agent || 'HighPressAnalyst'} assessment.`,
          `Shield Zone 14 central corridor while maintaining half-space progression triangles.`,
          `Calibrate risk according to match state at ${clock(seconds)}.`
        ],
        evidenceIds: aggregatedEvidenceIds.size > 0 ? Array.from(aggregatedEvidenceIds) : deliberation.explanation.evidenceIds,
        limitations: aggregatedLimitations.size > 0 ? Array.from(aggregatedLimitations) : deliberation.explanation.limitations
      };

      setLiveSynthesizedExplanation(synthesized);
      setFoundryStatus(`Consensus computed from ${agentsToQuery.length} Azure AI Foundry agents.`);
    } catch {
      if (exchangeId) {
        setConversationExchanges(prev => prev.map(ex => ex.id === exchangeId ? {
          ...ex,
          answer: fallback.answer,
          evidenceIds: fallback.evidenceIds,
          isLoading: false
        } : ex));
      } else {
        setConversationExchanges(prev => {
          if (prev.length === 0) return prev;
          const updated = [...prev];
          const lastIdx = updated.length - 1;
          updated[lastIdx] = {
            ...updated[lastIdx],
            answer: fallback.answer,
            evidenceIds: fallback.evidenceIds,
            isLoading: false
          };
          return updated;
        });
      }
      setFoundryStatus('Foundry project agents configured · autonomous simulation fallback active.');
    } finally {
      setDeliberating(false);
    }
  };

  const handleReDeliberate = () => {
    if (data.context?.candidate) {
      void runFoundryDeliberation();
    } else {
      setDeliberating(true);
      setTimeout(() => setDeliberating(false), 500);
    }
  };

  return (
    <ContextLayout
      title="AI Analyst"
      description={`${data.context?.candidate ? 'Candidate source events' : 'Verified match evidence'} available to the analyst at ${clock(seconds)}.`}
      matchId={matchId}
      seconds={seconds}
      revision={revision}
      route={route}
      candidate={data.context?.candidate}
      context={data.context}
      onTimeChange={data.selectTime}
      selector={data.selector}
      maxSeconds={data.maxSeconds}
      hideHud={true}
      hideHeaderBar={Boolean(data.context)}
    >
      {data.selector}
      {!matchId ? <ContextState loading={data.loading} error={data.error} empty="Choose a match before opening the analyst." />
        : data.loading || data.error || !data.context ? <ContextState loading={data.loading} error={data.error} empty="Match context is unavailable." />
          : <>
            <div className="analyst-screen-root" aria-label="PitchMind AI Analyst">
              {/* Sleek Action Bar with Header, Azure AI Foundry Status, and Quick Moment Jump */}
              <div className="analyst-header-action-bar">
                <div className="analyst-header-brand-group">
                  <span className="copilot-spark-icon" aria-hidden="true">✦</span>
                  <div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
                      <h1 className="analyst-header-title">Tactical Pitch View & AI Multi-Agent Analysis</h1>
                      <span className="analyst-header-status-badge">Azure AI Foundry</span>
                    </div>
                    <p className="analyst-header-subtitle">
                      {situation.homeTeamName} vs {situation.awayTeamName} · {clock(seconds)} ({situation.homeScore}-{situation.awayScore}) · {dynamicVenue}
                    </p>
                  </div>
                </div>

                <div className="analyst-quick-moments-row" aria-label="Quick Tactical Timeline Jump">
                  <span className="quick-moments-label">Quick Jump:</span>
                  <div className="quick-moments-chips">
                    {deducedKeyMoments.slice(0, 4).map((item, idx) => (
                      <button
                        key={idx}
                        type="button"
                        className="quick-moment-chip"
                        onClick={() => data.selectTime(item.minute * 60)}
                        title={`Jump to minute ${item.minute}: ${item.headline}`}
                      >
                        <span>{item.icon}</span>
                        <strong>{item.minute}'</strong>
                        <span>{item.headline}</span>
                      </button>
                    ))}
                  </div>
                </div>
              </div>

              {/* Subnav Tabs Bar */}
              <div className="analyst-subnav-bar">
                <nav className="analyst-subnav-tabs" aria-label="Selected match analysis">
                  {(() => {
                    const oHref = contextHref('/home', matchId, seconds, revision);
                    const mcHref = contextHref('/candidate-live', matchId, seconds, revision);
                    const taHref = contextHref('/teams', matchId, seconds, revision);
                    const paHref = contextHref('/players', matchId, seconds, revision);
                    const aaHref = contextHref('/analyst', matchId, seconds, revision);
                    return (
                      <>
                        <a href={oHref} onClick={e => handleLinkClick(e, oHref)} className="analyst-tab-link">Overview</a>
                        <a href={mcHref} onClick={e => handleLinkClick(e, mcHref)} className="analyst-tab-link">Match Centre</a>
                        <a href={taHref} onClick={e => handleLinkClick(e, taHref)} className="analyst-tab-link">Team Analysis</a>
                        <a href={paHref} onClick={e => handleLinkClick(e, paHref)} className="analyst-tab-link">Player Analysis</a>
                        <a href={aaHref} onClick={e => handleLinkClick(e, aaHref)} className="analyst-tab-link is-active" aria-current="page">AI Analyst</a>
                      </>
                    );
                  })()}
                </nav>
                {(() => {
                  const openHref = contextHref('/candidate-live', matchId, seconds, revision);
                  return (
                    <a href={openHref} onClick={e => handleLinkClick(e, openHref)} className="open-match-view-btn">
                      <span>Open in Match View</span>
                      <span className="btn-arrow" aria-hidden="true">→</span>
                    </a>
                  );
                })()}
              </div>

              {/* Redesigned Two-Column Split Screen: Left = Tactical Pitch & Causal Sequence, Right = AI Deliberation & Copilot */}
              <div className="analyst-screen-grid tactical-split-screen">
                {/* LEFT COLUMN: The Tactical Pitch View & Causal Sequence Expanded by Default */}
                <div className="tactical-pitch-column">
                  {/* 1. Tactical Pitch View */}
                  <TacticalPitchView
                    players={data.context.players}
                    homeTeam={data.context.homeTeam}
                    awayTeam={data.context.awayTeam}
                    seconds={seconds}
                    recentEvent={activeEvent}
                    availableEvents={notableEvents}
                    onSelectEvent={seq => setFocusedSequence(seq)}
                  />

                  {/* 2. Causal Sequence (Expanded by Default) */}
                  <div className="tactical-causal-chain-card" aria-label="Derived Tactical Causal Chain">
                    <div className="causal-chain-header">
                      <div className="causal-chain-title-wrap">
                        <h3 className="causal-chain-title">Causal Sequence</h3>
                        <span className="causal-chain-derived-tag">EXPANDED</span>
                      </div>
                      <span className="causal-chain-meta">
                        {activeEvent ? `${activeEvent.type} · Seq #${activeEvent.sequence} (${clock(activeEvent.clockSeconds)})` : 'Match Sequence'}
                      </span>
                    </div>
                    <div className="causal-chain-track">
                      {causalChain.map(step => (
                        <div key={step.step} className={`causal-step-card ${step.status}`}>
                          <div className="step-card-top">
                            <span className="step-number-tag">STEP {step.step}</span>
                            <span className="step-time-badge">{step.time}</span>
                          </div>
                          <div className="step-phase-pill">{step.phase}</div>
                          <div className="step-action-text">{step.action}</div>
                          <div className="step-detail-text">{step.detail}</div>
                        </div>
                      ))}
                    </div>
                  </div>

                  {/* 3. Observed Tactical Metrics */}
                  <div className="analyst-4metric-grid" aria-label="Observed Tactical Metrics">
                    <div className="metric-box">
                      <div className="metric-header">
                        <span className="metric-icon" aria-hidden="true">📊</span>
                        <span className="metric-title">High regains</span>
                      </div>
                      <div className="metric-values-row">
                        <div className="metric-big-digits">
                          <strong>{deducedMetrics.highRegainsHome}</strong>
                          <span className="vs-tag">vs</span>
                          <strong>{deducedMetrics.highRegainsAway}</strong>
                        </div>
                        <span className="metric-growth-pill up">↑ {deducedMetrics.regainsGrowthPct}</span>
                      </div>
                      <div className="metric-team-labels">
                        <span>{situation.homeTeamName}</span>
                        <span>{situation.awayTeamName}</span>
                      </div>
                      <p className="metric-subtext">More high regains giving {situation.homeTeamName} repeated attacks.</p>
                    </div>

                    <div className="metric-box">
                      <div className="metric-header">
                        <span className="metric-icon" aria-hidden="true">⭕</span>
                        <span className="metric-title">Possession (final third)</span>
                      </div>
                      <div className="metric-values-row">
                        <div className="metric-big-digits">
                          <strong>{deducedMetrics.possessionFinalThirdHome}%</strong>
                          <span className="vs-tag">vs</span>
                          <strong>{deducedMetrics.possessionFinalThirdAway}%</strong>
                        </div>
                      </div>
                      <div className="metric-team-labels">
                        <span>{situation.homeTeamName}</span>
                        <span>{situation.awayTeamName}</span>
                      </div>
                      <p className="metric-subtext">{situation.homeTeamName} are spending more time in dangerous areas.</p>
                    </div>

                    <div className="metric-box">
                      <div className="metric-header">
                        <span className="metric-icon" aria-hidden="true">⚡</span>
                        <span className="metric-title">Shots</span>
                      </div>
                      <div className="metric-values-row">
                        <div className="metric-big-digits">
                          <strong>{deducedMetrics.shotsHome}</strong>
                          <span className="vs-tag">vs</span>
                          <strong>{deducedMetrics.shotsAway}</strong>
                        </div>
                      </div>
                      <div className="metric-team-labels">
                        <span>{situation.homeTeamName}</span>
                        <span>{situation.awayTeamName}</span>
                      </div>
                      <p className="metric-subtext">More shots and higher xG from sustained pressure.</p>
                    </div>

                    <div className="metric-box">
                      <div className="metric-header">
                        <span className="metric-icon" aria-hidden="true">🛡️</span>
                        <span className="metric-title">Opponent build-up</span>
                      </div>
                      <div className="metric-values-row">
                        <div className="metric-big-digits">
                          <strong>{deducedMetrics.opponentBuildUpDisruptedPct}%</strong>
                        </div>
                        <span className="metric-growth-pill down">{deducedMetrics.buildUpDisruptedGrowthPct}</span>
                      </div>
                      <div className="metric-team-labels">
                        <span>{situation.awayTeamName} sequences ended early</span>
                      </div>
                      <p className="metric-subtext">{situation.awayTeamName}'s build-up sequences interrupted by {situation.homeTeamName}'s pressing line ({deducedMetrics.opponentBuildUpDisruptedPct}% disruption rate).</p>
                    </div>
                  </div>

                  {/* 4. Tactical Story & Evidence Cards */}
                  <div className="tactical-story-card" aria-label="Tactical Story and Evidence">
                    <div className="story-card-heading">
                      <span className="story-compass-icon" aria-hidden="true">🧭</span>
                      <h3 className="story-title">Tactical story</h3>
                    </div>
                    <p className="story-narrative-paragraph">
                      {dynamicTacticalStory}
                    </p>

                    <div className="key-evidence-subheading">
                      <span className="link-icon" aria-hidden="true">🔗</span>
                      <span>Key evidence</span>
                    </div>

                    <div className="key-evidence-media-grid">
                      {allEvidence.slice(0, 3).map((ev, idx) => (
                        <div key={ev.id} className="evidence-media-item">
                          <div className="media-thumbnail-wrap">
                            {renderEvidenceVisual(ev)}
                          </div>
                          <div className="media-meta-wrap">
                            <span className="media-badge-tag">
                              {idx === 0
                                ? `${currentMinute}' Tactical Phase`
                                : idx === 1
                                  ? `Pass map (${seconds >= 2700 ? '2nd Half' : '1st Half'})`
                                  : `${situation.awayTeamName} Disruption`}
                            </span>
                            <strong className="media-title">
                              {ev.title}
                            </strong>
                            <div className="media-tags-row">
                              <span className="media-tag-pill">{ev.tag}</span>
                              {idx === 0 && <span className="media-tag-pill evidence">Evidence</span>}
                              <span className="media-chevron" aria-hidden="true">❯</span>
                            </div>
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>
                </div>

                {/* RIGHT COLUMN: Azure AI Foundry Deliberation, Synthesis Consensus, and Copilot Chat */}
                <aside className="tactical-explanation-column" aria-label="AI Multi-Agent Deliberation and Explanation">
                  {/* 1. Azure AI Foundry Specialists Roster */}
                  <div className="foundry-agent-roster" aria-label="Azure AI Foundry Tactical Agents">
                    <div className="roster-topline">
                      <span className="roster-dot" aria-hidden="true"></span>
                      <span className="roster-eyebrow">Azure AI Foundry Multi-Agent Deliberation</span>
                      <span className="roster-count-badge">10 Agents Deployed</span>
                      {foundryStatus && <span className="roster-status-pill">{foundryStatus}</span>}
                    </div>
                    <div className="agent-pills-wrap">
                      {FOUNDRY_AGENTS.map(agent => (
                        <button
                          key={agent.name}
                          type="button"
                          className={`agent-roster-pill ${agentName === agent.name ? 'is-selected' : ''}`}
                          onClick={() => {
                            setAgentName(agent.name);
                            if (data.context?.candidate) void runFoundryDeliberation(undefined, agent.name);
                          }}
                          title={`Click to deliberate with ${agent.name} v${agent.version} (${agent.role})`}
                        >
                          <span className="agent-pill-icon">{agent.icon}</span>
                          <span className="agent-pill-name">{agent.name}</span>
                          <span className="agent-pill-ver">v{agent.version}</span>
                        </button>
                      ))}
                    </div>
                    <div className="roster-action-bar">
                      <button
                        type="button"
                        className="consult-specialist-btn"
                        onClick={() => void runFoundryDeliberation(undefined, agentName)}
                        disabled={deliberating || !data.context?.candidate}
                      >
                        <span>Consult {agentName} on Foundry</span>
                      </button>
                      <button
                        type="button"
                        className="deliberate-all-btn"
                        onClick={() => void runFoundryDeliberation()}
                        disabled={deliberating || !data.context?.candidate}
                      >
                        <span>{deliberating ? 'Foundry Agents Discussing…' : 'Start Multi-Agent Panel Deliberation'}</span>
                      </button>
                    </div>
                  </div>

                  {/* 2. Synthesized Consensus Card */}
                  <article className="tactical-synthesized-card" aria-label="Computed Tactical Explanation">
                    <div className="explanation-card-header">
                      <div className="explanation-card-tagline">
                        <span className="explanation-pill">Computed via Multi-Agent Consensus</span>
                        <small className="explanation-clock">Elapsed Clock: {clock(seconds)}</small>
                      </div>
                      <h2 className="explanation-headline">{activeExplanation.headline}</h2>
                    </div>

                    <p className="explanation-summary-text">{activeExplanation.summary}</p>

                    <div className="explanation-key-findings">
                      <h4 className="findings-subhead">Key Findings from Agent Discussion</h4>
                      <ul className="findings-list">
                        {activeExplanation.keyFindings.map((finding: { agent: string; point: string }, idx: number) => (
                          <li key={idx} className="finding-item">
                            <span className="finding-agent-badge">{finding.agent}</span>
                            <span className="finding-text">{finding.point}</span>
                          </li>
                        ))}
                      </ul>
                    </div>

                    <div className="explanation-recommendations">
                      <h4 className="findings-subhead">Tactical Recommendations</h4>
                      <ul className="recommendations-list">
                        {activeExplanation.tacticalRecommendations.map((rec: string, idx: number) => (
                          <li key={idx} className="recommendation-item">
                            <span className="rec-arrow" aria-hidden="true">→</span>
                            <span>{rec}</span>
                          </li>
                        ))}
                      </ul>
                    </div>

                    <div className="explanation-meta-footer">
                      <div className="explanation-evidence-refs">
                        <strong>Evidence:</strong>
                        {activeExplanation.evidenceIds.map((id: string) => (
                          <code key={id} className="evidence-code-pill">{id}</code>
                        ))}
                      </div>
                      <button
                        type="button"
                        className="re-deliberate-btn"
                        onClick={handleReDeliberate}
                        disabled={deliberating}
                      >
                        {deliberating ? 'Agents Discussing…' : '🔄 Re-deliberate Situation'}
                      </button>
                    </div>
                  </article>

                  {/* 3. Discussion dialogue stream */}
                  <div className="agent-discussion-stream" aria-label="Specialists Deliberation Dialogue">
                    <div className="stream-header-info">
                      <h3>Specialists Deliberation Dialogue</h3>
                      <p>Agents discussing match situation at {clock(seconds)} and evaluating tactical structure.</p>
                    </div>

                    <div className="discussion-turns-stack">
                      {activeTurns.map(turn => (
                        <div key={turn.id} className="discussion-turn-bubble">
                          <div className="turn-author-header">
                            <span className="author-avatar">{turn.icon}</span>
                            <strong className="author-name">{turn.agentName}</strong>
                            <span className="author-version-tag">v{turn.agentVersion}</span>
                            <span className="author-role-label">{turn.role}</span>
                            {turn.replyingTo && (
                              <span className="replying-to-tag">replying to {turn.replyingTo}</span>
                            )}
                          </div>
                          <p className="turn-speech-text">{turn.text}</p>
                          <div className="turn-citations">
                            {turn.citedEvidence.map((cid: string) => (
                              <small key={cid} className="turn-cite-pill">{cid}</small>
                            ))}
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>

                  {/* 4. Conversational Copilot & Interactive Inquiry */}
                  <div className="analyst-copilot-card">
                    <div className="copilot-brand-title">
                      <span className="copilot-spark-icon" aria-hidden="true">✦</span>
                      <div>
                        <h2 className="copilot-title">PitchMind AI Tactical Copilot</h2>
                        <p className="copilot-subtitle">Ask questions or test tactical scenarios</p>
                      </div>
                    </div>
                    <div className="copilot-badges-row">
                      <span className="copilot-badge"><span className="badge-check">✓</span> Evidence-backed</span>
                      <span className="copilot-badge"><span className="badge-shield">🛡️</span> No unsupported claims</span>
                    </div>
                  </div>

                  {/* Try asking prompt chips */}
                  <div className="analyst-try-asking-row" aria-label="Suggested Tactical Inquiries">
                    <span className="try-asking-label">↗ Try asking</span>
                    <div className="try-asking-chips">
                      {[
                        `Why is ${dominantTeam} controlling the match?`,
                        `Show me the tactical evidence behind the ${deducedMetrics.highRegainsHome} high regains`,
                        `What tactical shift occurred around minute ${currentMinute}?`
                      ].map(prompt => (
                        <button
                          key={prompt}
                          type="button"
                          className={`try-asking-chip ${selectedPrompt === prompt ? 'is-active' : ''}`}
                          onClick={() => handleAskQuestion(prompt)}
                          disabled={deliberating}
                        >
                          <span>{prompt}</span>
                          <span className="chip-chevron" aria-hidden="true">❯</span>
                        </button>
                      ))}
                    </div>
                  </div>

                  {/* Conversation Card */}
                  <div className="analyst-conversation-card" aria-label="Tactical Conversation">
                    {displayExchanges.map((exchange) => (
                      <div key={exchange.id} className="conversation-exchange-block">
                        <div className="conversation-user-bubble-row">
                          <div className="user-bubble">
                            <span className="user-text">{exchange.question}</span>
                            <span className="user-time">{exchange.time}</span>
                          </div>
                          <div className="user-avatar-tag">AN</div>
                        </div>

                        <div className="conversation-agent-bubble-row">
                          <div className="agent-avatar-icon" aria-hidden="true">✦</div>
                          <div className="agent-bubble">
                            {exchange.isLoading ? (
                              <p className="agent-speech agent-loading">
                                <span className="agent-loading-pulse">✦</span>
                                <span>Analyzing tactical evidence and consulting specialists…</span>
                              </p>
                            ) : (
                              <>
                                <p className="agent-speech">
                                  {exchange.answer || activeTurns[0]?.text || initialAnswer}
                                </p>
                                {exchange.evidenceIds && exchange.evidenceIds.length > 0 && (
                                  <div className="agent-evidence-citations">
                                    <span className="citations-label">Cited Evidence:</span>
                                    {exchange.evidenceIds.map(eid => (
                                      <span key={eid} className="agent-evidence-chip">#{eid}</span>
                                    ))}
                                  </div>
                                )}
                                <span className="agent-time">{exchange.time}</span>
                              </>
                            )}
                          </div>
                        </div>
                      </div>
                    ))}
                  </div>

                  {/* Chat input form */}
                  <div className="analyst-chat-input-bar">
                    <form
                      className="analyst-chat-form"
                      onSubmit={e => {
                        e.preventDefault();
                        if (scenarioInput.trim()) {
                          handleAskQuestion(scenarioInput);
                        }
                      }}
                    >
                      <button type="button" className="chat-attach-btn" aria-label="Attach evidence references">
                        📎
                      </button>
                      <input
                        type="text"
                        className="chat-text-input"
                        placeholder="Ask about tactics, pressing structure, or scenario..."
                        value={scenarioInput}
                        onChange={e => setScenarioInput(e.target.value)}
                        disabled={deliberating}
                      />
                      <button
                        type="submit"
                        className="chat-send-btn"
                        disabled={deliberating || !scenarioInput.trim()}
                        aria-label="Send tactical inquiry"
                      >
                        ➤
                      </button>
                    </form>
                    <div className="chat-bottom-info">
                      <p className="chat-disclaimer-note">
                        All responses are evidence-backed using match data verified across 10 Foundry agents.
                      </p>
                    </div>
                  </div>

                  {/* 5. Notable Events Live Reports */}
                  {data.context.candidate && (
                    <section className="candidate-live-reports" aria-label="Live event analysis reports">
                      <div className="candidate-live-reports-heading">
                        <h3>Reports from notable events</h3>
                        <span role="status">{liveReportStatus}</span>
                      </div>
                      {liveReportError && <p role="alert" className="candidate-analyst-error">{liveReportError}</p>}
                      {visibleReports.length ? (
                        <div className="candidate-live-report-list">
                          {visibleReports.map(report => (
                            <article className="candidate-live-report" key={report.event.eventId}>
                              <div className="candidate-analyst-answer-heading">
                                <span className="badge">{report.event.eventType} · {report.answer.agentName} · hypothesis</span>
                                <small>Source clock {clock(report.event.clockSeconds)} · sequence {report.event.sequence}</small>
                              </div>
                              <p>{report.answer.answer}</p>
                              {report.answer.claims.length > 0 && (
                                <details>
                                  <summary>Evidence references</summary>
                                  <ul>
                                    {report.answer.claims.flatMap((claim, index) =>
                                      claim.evidenceIds.map(id => <li key={`${index}:${id}`}>{id}</li>)
                                    )}
                                  </ul>
                                </details>
                              )}
                              <details>
                                <summary>Limitations</summary>
                                <ul>
                                  {report.answer.limitations.map(item => <li key={item}>{item}</li>)}
                                </ul>
                              </details>
                            </article>
                          ))}
                        </div>
                      ) : (
                        <p className="candidate-live-reports-empty">
                          Reports appear when a notable source event is persisted. No demo analysis is substituted.
                        </p>
                      )}
                    </section>
                  )}
                </aside>
              </div>
            </div>
          </>
      }
    </ContextLayout>
  );
}
