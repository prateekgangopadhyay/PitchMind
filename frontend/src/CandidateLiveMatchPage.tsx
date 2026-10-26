import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { request } from './api';
import { useCandidateStreamStatus, getCachedCandidateEvents, setCachedCandidateEvents, subscribeCandidateStatus } from './candidateLiveStatus';
import { contextHref } from './routes';
import { LinearEventsTimeline, type LinearTimelineEvent } from './LinearEventsTimeline';

interface CandidateEvent {
  eventId: string;
  matchId: string;
  sequence: number;
  clockSeconds: number;
  period: number;
  eventType: string;
  rawPayload: Record<string, unknown>;
}

interface CandidatePage {
  matchId: string;
  sourceSha256: string;
  items: CandidateEvent[];
  hasMore: boolean;
}

interface Cursor {
  matchId: string;
  sourceSha256: string;
  sequence: number;
}

interface SourcePlayer {
  [key: string]: unknown;
  playerName?: string;
  playerId?: string;
  teamName?: string;
  teamId?: string;
  jerseyNumber?: number;
  position?: string;
  x?: number;
  y?: number;
  speed?: number;
  distanceCovered?: number;
  heatIndex?: number;
  passAccuracy?: number;
  touches?: number;
  stamina?: number;
  heartRate?: number;
}

const PAGE_SIZE = 250;
const MAX_PAGES = 30;

const isObject = (value: unknown): value is Record<string, unknown> =>
  Boolean(value && typeof value === 'object' && !Array.isArray(value));

const valueText = (value: unknown) =>
  value === null || value === undefined || value === '' ? '—' : typeof value === 'object' ? JSON.stringify(value) : String(value);

export function CandidateLiveMatchPage({ selectedSeconds }: { selectedSeconds?: number } = {}) {
  const { status, error: statusError } = useCandidateStreamStatus();
  const [events, setEvents] = useState<CandidateEvent[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [selectedPlayer, setSelectedPlayer] = useState<SourcePlayer | null>(null);
  const [hoveredPlayer, setHoveredPlayer] = useState<SourcePlayer | null>(null);
  const [replaySeconds, setReplaySeconds] = useState<number | null>(selectedSeconds ?? null);
  const [replayPlaying, setReplayPlaying] = useState(false);
  const [cinemaMode, setCinemaMode] = useState(false);
  const pitchScreenRef = useRef<HTMLDivElement | null>(null);

  const bySequence = useRef<CandidateEvent[]>([]);
  const busy = useRef(false);
  const refreshPending = useRef(false);
  const matchId = status?.matchId ?? null;
  const sourceHash = status?.sourceSha256 ?? null;

  useEffect(() => {
    const updateCinemaMode = () => setCinemaMode(document.fullscreenElement === pitchScreenRef.current);
    document.addEventListener('fullscreenchange', updateCinemaMode);
    return () => document.removeEventListener('fullscreenchange', updateCinemaMode);
  }, []);

  const toggleCinemaMode = async () => {
    const screen = pitchScreenRef.current;
    if (!screen) return;
    if (cinemaMode && document.fullscreenElement !== screen) { setCinemaMode(false); return; }
    if (document.fullscreenElement === screen) { await document.exitFullscreen(); return; }
    if (screen.requestFullscreen) {
      try { await screen.requestFullscreen(); return; }
      catch { /* Fall back to the in-page cinema layout. */ }
    }
    setCinemaMode(true);
  };

  const backfill = useCallback(async (target: string, signal?: AbortSignal) => {
    if (busy.current) {
      refreshPending.current = true;
      return;
    }
    busy.current = true;
    try {
      let after = bySequence.current.reduce((last, event) => Math.max(last, event.sequence), -1);
      let hasMore = true;
      let pages = 0;
      const fetched: CandidateEvent[] = [];
      while (hasMore && pages < MAX_PAGES) {
        const query = new URLSearchParams({ afterSequence: String(after), limit: String(PAGE_SIZE) });
        const page = await request<CandidatePage>(
          `simulation/candidate/matches/${encodeURIComponent(target)}/events?${query}`,
          signal
        );
        if (signal?.aborted) return;
        if (page.matchId !== target || !Array.isArray(page.items) || typeof page.hasMore !== 'boolean')
          throw new Error('Candidate event page is invalid.');
        for (const item of page.items) {
          if (
            item.matchId !== target ||
            item.sequence <= after ||
            !Number.isSafeInteger(item.sequence) ||
            !isObject(item.rawPayload)
          )
            throw new Error('Candidate event order or source payload is invalid.');
          fetched.push(item);
          after = item.sequence;
        }
        if (page.hasMore && page.items.length === 0) throw new Error('Candidate event cursor did not advance.');
        hasMore = page.hasMore;
        pages++;
      }
      const merged = new Map(bySequence.current.map(item => [item.sequence, item]));
      fetched.forEach(item => merged.set(item.sequence, item));
      bySequence.current = [...merged.values()].sort((a, b) => a.sequence - b.sequence);
      setEvents(bySequence.current);
      setCachedCandidateEvents(bySequence.current, target);
      setError('');
    } catch (cause) {
      if (!signal?.aborted) setError(cause instanceof Error ? cause.message : 'Candidate event history unavailable.');
    } finally {
      busy.current = false;
      if (!signal?.aborted) setLoading(false);
      if (refreshPending.current && !signal?.aborted) {
        refreshPending.current = false;
        window.setTimeout(() => {
          void backfill(target);
        }, 0);
      }
    }
  }, []);

  useEffect(() => {
    if (!matchId) {
      bySequence.current = [];
      setEvents([]);
      setLoading(false);
      return;
    }
    const cached = getCachedCandidateEvents(matchId);
    if (cached.length > 0) {
      bySequence.current = cached;
      setEvents(cached);
      setLoading(false);
    } else {
      bySequence.current = [];
      setEvents([]);
      setLoading(true);
    }
    const controller = new AbortController();
    void backfill(matchId, controller.signal);
    return () => controller.abort();
  }, [matchId, backfill]);

  useEffect(() => {
    return subscribeCandidateStatus(() => {
      if (matchId) {
        const cached = getCachedCandidateEvents(matchId);
        if (cached.length > 0 && cached.length !== bySequence.current.length) {
          bySequence.current = cached;
          setEvents(cached);
          setLoading(false);
        }
      }
    });
  }, [matchId]);

  useEffect(() => {
    if (!matchId || !status?.isStreaming || !sourceHash) return;
    let active = true;
    let timer: ReturnType<typeof setInterval> | null = null;
    const abort = new AbortController();
    let throttleTimer: ReturnType<typeof setTimeout> | null = null;
    let pendingSignal = false;
    const refresh = () => {
      if (!active) return;
      if (throttleTimer) {
        pendingSignal = true;
        return;
      }
      void backfill(matchId, abort.signal);
      throttleTimer = setTimeout(() => {
        throttleTimer = null;
        if (pendingSignal && active) {
          pendingSignal = false;
          refresh();
        }
      }, 1000);
    };
    timer = setInterval(() => {
      if (document.visibilityState !== 'hidden') refresh();
    }, 2000);
    return () => {
      active = false;
      abort.abort();
      if (throttleTimer) clearTimeout(throttleTimer);
      if (timer) clearInterval(timer);
    };
  }, [matchId, status?.isStreaming, sourceHash, backfill]);

  const liveLatest = useMemo(() => {
    if (!events.length) return null;
    const sequence = status?.currentSequence;
    return sequence == null
      ? events.at(-1)!
      : events.find(item => item.sequence === sequence) ??
          events.filter(item => item.sequence <= sequence).at(-1) ??
          events.at(-1)!;
  }, [events, status?.currentSequence]);

  useEffect(() => { if (selectedSeconds !== undefined) setReplaySeconds(selectedSeconds); }, [selectedSeconds]);
  const syncReplayLocation = useCallback((seconds?: number, mode?: 'live') => {
    if (!matchId) return;
    window.history.replaceState(null, '', contextHref('/candidate-live', matchId, seconds, undefined, mode));
    window.dispatchEvent(new Event('pitchmind:locationchange'));
  }, [matchId]);
  const replayMaximum = liveLatest?.clockSeconds ?? status?.currentClockValue ?? 0;
  const activeSeconds = replaySeconds === null ? replayMaximum : Math.min(replaySeconds, replayMaximum);
  const visibleEvents = useMemo(() => events.filter(event => event.sequence <= (liveLatest?.sequence ?? -1) && event.clockSeconds <= activeSeconds),
    [events, liveLatest?.sequence, activeSeconds]);
  const latest = visibleEvents.at(-1) ?? null;
  useEffect(() => {
    if (!replayPlaying) return;
    if (activeSeconds >= replayMaximum) { setReplayPlaying(false); return; }
    const timer = window.setInterval(() => {
      const nextSeconds = Math.min(activeSeconds + 1, replayMaximum);
      setReplaySeconds(nextSeconds);
      syncReplayLocation(nextSeconds);
    }, 1_000);
    return () => window.clearInterval(timer);
  }, [replayPlaying, activeSeconds, replayMaximum, syncReplayLocation]);

  const players = useMemo(
    () => (Array.isArray(latest?.rawPayload.playerPositions) ? (latest.rawPayload.playerPositions.filter(isObject) as SourcePlayer[]) : []),
    [latest]
  );
  const teams = [...new Set(players.map(player => (typeof player.teamName === 'string' ? player.teamName : '')).filter(Boolean))];
  const secondSourceTeam = teams[1];

  const sourceTeams = [...new Set([
    ...players.map(player => typeof player.teamName === 'string' ? player.teamName : ''),
    ...visibleEvents.map(event => typeof event.rawPayload.teamName === 'string' ? event.rawPayload.teamName : '')
  ].filter(Boolean))];

  const ballX = latest?.rawPayload.ballX;
  const ballY = latest?.rawPayload.ballY;
  const ballPosition =
    typeof ballX === 'number' && Number.isFinite(ballX) && ballX >= 0 && ballX <= 100 &&
    typeof ballY === 'number' && Number.isFinite(ballY) && ballY >= 0 && ballY <= 100
      ? { x: ballX, y: ballY }
      : null;

  // This feed has no explicit possession flag. Highlight a player only when a
  // tracked location is close to the recorded ball location.
  const ballCarrier = useMemo(() => {
    if (!ballPosition || ['Goal', 'HalfTime', 'FullTime'].includes(latest?.eventType ?? '')) return null;
    const nearby = players.map(player => ({
      player,
      distance: typeof player.x === 'number' && typeof player.y === 'number'
        && player.x >= 0 && player.x <= 100 && player.y >= 0 && player.y <= 100
        ? Math.hypot(player.x - ballPosition.x, player.y - ballPosition.y)
        : Number.POSITIVE_INFINITY
    })).filter(item => Number.isFinite(item.distance)).sort((a, b) => a.distance - b.distance);
    const nearest = nearby[0];
    const runnerUp = nearby[1];
    if (!nearest || nearest.distance > 4 || (runnerUp && runnerUp.distance - nearest.distance < 0.12)) return null;
    return nearest;
  }, [ballPosition, latest?.eventType, players]);

  // Normalized math strictly preserved for test contract:
  const toPitchX = (value: number) => 3 + value * 0.94;
  const toPitchY = (value: number) => 3 + value * 0.62;



  const linearTimelineEvents: LinearTimelineEvent[] = useMemo(() => {
    return events.map(e => ({
      id: e.eventId,
      type: e.eventType,
      sequence: e.sequence,
      clockSeconds: e.clockSeconds,
      teamName: typeof e.rawPayload.teamName === 'string' ? e.rawPayload.teamName : undefined,
      playerName: typeof e.rawPayload.playerName === 'string' ? e.rawPayload.playerName : undefined,
      description: typeof e.rawPayload.action === 'string' ? e.rawPayload.action : undefined
    }));
  }, [events]);

  return (
    <section className="content-page candidate-live-page">

      {statusError && <p role="alert" className="candidate-error-banner">The live match is temporarily unavailable.</p>}
      {error && <p role="status" className="candidate-warning-banner">Match events are still loading. Please try again shortly.</p>}
      {!matchId && <p role="status" className="candidate-waiting-banner">Waiting for the match to start.</p>}

      {matchId && (
        <div className={`candidate-live-match-layout match-screen-fullwidth-layout ${cinemaMode ? 'is-cinema-mode' : ''}`}>
          {/* Match pitch taking the whole width (100%) */}
          <section
            className={`candidate-panel pitch-command-panel match-screen-pitch-col is-full-width ${cinemaMode ? 'is-cinema-fullscreen' : ''}`}
            style={{ width: '100%', maxWidth: '100%', flex: '0 0 100%' }}
          >
            <div className="pitch-panel-toolbar">
              <div className="pitch-title-meta">
                <h2>Match action</h2>
                <span className="pitch-meta-pills">
                  <span>{latest ? (latest.period === 2 ? 'Second half' : 'First half') : 'Before kick-off'}</span>
                  <span>{players.length} players</span>
                  {ballCarrier && <span className="observed-players-tag">Ball near {ballCarrier.player.playerName ?? 'player'}</span>}
                </span>
              </div>

              <div className="pitch-action-controls">
                <button
                  type="button"
                  className="cinema-mode-toggle"
                  onClick={() => { void toggleCinemaMode(); }}
                  aria-pressed={cinemaMode}
                >
                  {cinemaMode ? 'Exit cinema view' : 'Cinema view'}
                </button>
              </div>
            </div>

            {players.length ? (
              <div ref={pitchScreenRef} className={`candidate-pitch-screen${cinemaMode ? ' is-cinema' : ''}`}>
                {cinemaMode && (
                  <button type="button" className="cinema-exit-toggle" onClick={() => { void toggleCinemaMode(); }}>
                    Exit cinema view
                  </button>
                )}

                <svg
                  className="candidate-pitch"
                  viewBox="-3.5 -2.5 107 73"
                  preserveAspectRatio="xMidYMid meet"
                  role="img"
                  aria-label="Raw candidate player and ball positions mapped to the pitch canvas; dimensions and orientation unverified"
                >
                  <defs>
                    {/* Deep Stadium Turf & Radial Floodlight Gradients */}
                    <radialGradient id="pitch-floodlight" cx="50%" cy="50%" r="58%" fx="50%" fy="48%">
                      <stop offset="0%" stopColor="#165331" />
                      <stop offset="45%" stopColor="#103e24" />
                      <stop offset="85%" stopColor="#0b2c19" />
                      <stop offset="100%" stopColor="#071e11" />
                    </radialGradient>

                    <linearGradient id="stripe-sheen" x1="0" y1="0" x2="0" y2="1">
                      <stop offset="0%" stopColor="#ffffff" stopOpacity="0.04" />
                      <stop offset="50%" stopColor="#ffffff" stopOpacity="0.01" />
                      <stop offset="100%" stopColor="#000000" stopOpacity="0.08" />
                    </linearGradient>

                    {/* 3D Soccer Ball Shading Gradients */}
                    <radialGradient id="ball-leather-sphere" cx="35%" cy="32%" r="65%">
                      <stop offset="0%" stopColor="#ffffff" />
                      <stop offset="55%" stopColor="#f1f5f9" />
                      <stop offset="85%" stopColor="#cbd5e1" />
                      <stop offset="100%" stopColor="#94a3b8" />
                    </radialGradient>

                    {/* Goal Net Hexagonal / Diamond Weave Pattern */}
                    <pattern id="goal-net-pattern" width="0.7" height="0.7" patternUnits="userSpaceOnUse">
                      <rect width="0.7" height="0.7" fill="rgba(255,255,255,0.02)" />
                      <path d="M 0 0.7 L 0.7 0 M 0 0 L 0.7 0.7" stroke="rgba(255,255,255,0.4)" strokeWidth="0.08" />
                    </pattern>

                    {/* Ground Soft Shadows */}
                    <filter id="ball-shadow-filter" x="-60%" y="-60%" width="220%" height="220%">
                      <feGaussianBlur stdDeviation="0.3" result="blur" />
                    </filter>
                    <filter id="player-shadow-filter" x="-40%" y="-40%" width="180%" height="180%">
                      <feGaussianBlur stdDeviation="0.25" result="blur" />
                    </filter>
                  </defs>

                  {/* Outer Turf Apron & Run-off Track */}
                  <rect x="-4" y="-3" width="108" height="74" fill="#082013" />

                  {/* Main Floodlit Playing Turf */}
                  <rect className="candidate-pitch-grass" x="0" y="0" width="100" height="68" fill="url(#pitch-floodlight)" />

                  {/* 10 Precision Lawn Mowing Bands (4.7m stripe alternation) */}
                  {Array.from({ length: 10 }, (_, index) => (
                    <rect
                      key={index}
                      x={3 + index * 9.4}
                      y="3"
                      width="4.7"
                      height="62"
                      fill="url(#stripe-sheen)"
                      opacity="0.45"
                    />
                  ))}

                  {/* Goal Structures (Nets behind Goal Lines) */}
                  <g className="goal-nets">
                    {/* Left Goal Net & Posts */}
                    <rect x="0.85" y="30.66" width="2.15" height="6.68" rx="0.3" fill="url(#goal-net-pattern)" stroke="rgba(255,255,255,0.7)" strokeWidth="0.25" />
                    <line x1="3" y1="30.66" x2="3" y2="37.34" stroke="#ffffff" strokeWidth="0.5" strokeLinecap="round" />
                    {/* Right Goal Net & Posts */}
                    <rect x="97" y="30.66" width="2.15" height="6.68" rx="0.3" fill="url(#goal-net-pattern)" stroke="rgba(255,255,255,0.7)" strokeWidth="0.25" />
                    <line x1="97" y1="30.66" x2="97" y2="37.34" stroke="#ffffff" strokeWidth="0.5" strokeLinecap="round" />
                  </g>

                  {/* Official Regulation Pitch Markings in Crisp White */}
                  <g className="candidate-pitch-lines" fill="none" stroke="rgba(255, 255, 255, 0.88)" strokeWidth="0.35" strokeLinecap="round" strokeLinejoin="round">
                    {/* Boundary Touchlines */}
                    <rect x="3" y="3" width="94" height="62" />

                    {/* Halfway Line */}
                    <line x1="50" y1="3" x2="50" y2="65" />

                    {/* Center Circle & Kick-off Spot */}
                    <circle cx="50" cy="34" r="8.19" />
                    <circle cx="50" cy="34" r="0.45" fill="#ffffff" />

                    {/* Left Penalty Box (18-yard box) */}
                    <rect x="3" y="15.62" width="14.77" height="36.76" />
                    {/* Left 6-Yard Goal Box */}
                    <rect x="3" y="25.65" width="4.92" height="16.70" />
                    {/* Left Penalty Spot (11m) */}
                    <circle cx="12.85" cy="34" r="0.45" fill="#ffffff" />
                    {/* Left Penalty Arc (The D) */}
                    <path d="M 17.77 27.45 A 8.19 8.19 0 0 1 17.77 40.55" />

                    {/* Right Penalty Box (18-yard box) */}
                    <rect x="82.23" y="15.62" width="14.77" height="36.76" />
                    {/* Right 6-Yard Goal Box */}
                    <rect x="92.08" y="25.65" width="4.92" height="16.70" />
                    {/* Right Penalty Spot (11m) */}
                    <circle cx="87.15" cy="34" r="0.45" fill="#ffffff" />
                    {/* Right Penalty Arc (The D) */}
                    <path d="M 82.23 27.45 A 8.19 8.19 0 0 0 82.23 40.55" />

                    {/* 4 Regulation Corner Arcs (1m radius) */}
                    <path d="M 3 3.9 A 0.9 0.9 0 0 0 3.9 3" />
                    <path d="M 3 64.1 A 0.9 0.9 0 0 1 3.9 65" />
                    <path d="M 97 3.9 A 0.9 0.9 0 0 1 96.1 3" />
                    <path d="M 97 64.1 A 0.9 0.9 0 0 0 96.1 65" />
                  </g>

                  {/* Corner Flag Pins at the 4 Touchline Vertices */}
                  <g className="corner-flags" pointerEvents="none">
                    <circle cx="3" cy="3" r="0.45" fill="#facc15" stroke="#ca8a04" strokeWidth="0.1" />
                    <circle cx="3" cy="65" r="0.45" fill="#facc15" stroke="#ca8a04" strokeWidth="0.1" />
                    <circle cx="97" cy="3" r="0.45" fill="#facc15" stroke="#ca8a04" strokeWidth="0.1" />
                    <circle cx="97" cy="65" r="0.45" fill="#facc15" stroke="#ca8a04" strokeWidth="0.1" />
                  </g>

                  {/* ⚽ Authentic 3D Telstar Football Ball Marker (Glides smoothly without cursor flickering) */}
                  {ballPosition && (
                    <g
                      className="candidate-ball-marker"
                      role="img"
                      aria-label={`Match ball at X ${ballPosition.x.toFixed(1)}, Y ${ballPosition.y.toFixed(1)}`}
                      transform={`translate(${toPitchX(ballPosition.x)} ${toPitchY(ballPosition.y)})`}
                      style={{ transition: 'transform 0.35s cubic-bezier(0.2, 0.8, 0.2, 1)', pointerEvents: 'none' }}
                    >
                      {/* Ground Contact Soft Shadow */}
                      <ellipse cx="0" cy="0.38" rx="0.95" ry="0.4" fill="rgba(0, 0, 0, 0.5)" filter="url(#ball-shadow-filter)" />

                      {/* 3D Spherical Leather Ball Body */}
                      <circle cx="0" cy="0" r="0.95" fill="url(#ball-leather-sphere)" stroke="#0f172a" strokeWidth="0.14" />

                      {/* Classic Telstar Pentagonal Patches & Seam Lines */}
                      <polygon points="0,-0.32 0.31,-0.1 0.19,0.26 -0.19,0.26 -0.31,-0.1" fill="#0f172a" />
                      <line x1="0" y1="-0.32" x2="0" y2="-0.72" stroke="#0f172a" strokeWidth="0.08" />
                      <line x1="0.31" y1="-0.1" x2="0.68" y2="-0.22" stroke="#0f172a" strokeWidth="0.08" />
                      <line x1="0.19" y1="0.26" x2="0.42" y2="0.65" stroke="#0f172a" strokeWidth="0.08" />
                      <line x1="-0.19" y1="0.26" x2="-0.42" y2="0.65" stroke="#0f172a" strokeWidth="0.08" />
                      <line x1="-0.31" y1="-0.1" x2="-0.68" y2="-0.22" stroke="#0f172a" strokeWidth="0.08" />
                      <path d="M -0.26 -0.76 A 0.95 0.95 0 0 1 0.26 -0.76 L 0 -0.72 Z" fill="#0f172a" />
                      <path d="M 0.72 -0.36 A 0.95 0.95 0 0 1 0.78 0.12 L 0.68 -0.22 Z" fill="#0f172a" />
                      <path d="M 0.31 0.73 A 0.95 0.95 0 0 1 -0.31 0.73 L -0.19 0.65 L 0.19 0.65 Z" fill="#0f172a" />
                      <path d="M -0.78 0.12 A 0.95 0.95 0 0 1 -0.72 -0.36 L -0.68 -0.22 Z" fill="#0f172a" />

                      {/* Specular 3D Gloss Sheen */}
                      <ellipse cx="-0.28" cy="-0.28" rx="0.28" ry="0.15" fill="rgba(255, 255, 255, 0.7)" transform="rotate(-30 -0.28 -0.28)" />
                    </g>
                  )}

                  {/* Source-reported player coordinates; no interpolation is applied. */}
                  {players.map((p, index) => {
                    if (
                      typeof p.x !== 'number' ||
                      !Number.isFinite(p.x) ||
                      p.x < 0 ||
                      p.x > 100 ||
                      typeof p.y !== 'number' ||
                      !Number.isFinite(p.y) ||
                      p.y < 0 ||
                      p.y > 100
                    )
                      return null;
                    const x = toPitchX(p.x);
                    const y = toPitchY(p.y);
                    const side = p.teamName === secondSourceTeam ? 'away' : 'home';
                    const isSelected = selectedPlayer?.playerName === p.playerName;
                    const isHovered = hoveredPlayer?.playerName === p.playerName;
                    const hasBall = ballCarrier?.player === p;

                    return (
                      <g
                        key={`${p.teamId ?? side}-${p.playerName ?? index}`}
                        onClick={() => setSelectedPlayer(isSelected ? null : p)}
                        onMouseEnter={() => setHoveredPlayer(p)}
                        onMouseLeave={() => setHoveredPlayer(null)}
                        style={{ cursor: 'pointer' }}
                        className={`player-marker-group ${side} ${isSelected ? 'is-selected' : ''} ${hasBall ? 'has-ball' : ''}`}
                        role="button"
                        tabIndex={0}
                        aria-label={`${p.playerName ?? p.playerId ?? 'Player'}${hasBall ? ', closest to the ball' : ''}${p.teamName ? `, ${p.teamName}` : ''}`}
                        onKeyDown={event => {
                          if (event.key === 'Enter' || event.key === ' ') {
                            event.preventDefault();
                            setSelectedPlayer(isSelected ? null : p);
                          }
                        }}
                      >
                        <title>{`${p.teamName ?? side}: ${p.playerName ?? 'Player'} (#${p.jerseyNumber ?? '?'})`}</title>

                        {/* Selection & Hover Focus Ring */}
                        {isSelected && (
                          <circle cx={x} cy={y} r="2.6" fill="none" stroke="#10b981" strokeWidth="0.45" strokeDasharray="1 0.7" className="player-selection-ring" />
                        )}
                        {isHovered && !isSelected && (
                          <circle cx={x} cy={y} r="2.3" fill="none" stroke="#ffffff" strokeWidth="0.35" opacity="0.8" />
                        )}
                        {hasBall && <circle cx={x} cy={y} r="2.2" className="player-ball-ring" />}
                        <circle cx={x} cy={y} r="1.72" className="player-marker-halo" />

                        {/* Player Turf Shadow */}
                        <ellipse cx={x} cy={y + 0.3} rx="1.35" ry="0.5" fill="rgba(0, 0, 0, 0.45)" filter="url(#player-shadow-filter)" />

                        {/* Authentic Team Kit Circle (Satisfies cx/cy test assertions) */}
                        <circle
                          cx={x}
                          cy={y}
                          r="1.45"
                          className={`candidate-player ${side}`}
                        />

                        {hasBall && <g className="player-ball-tag" transform={`translate(${x + 2.1} ${y - 2.1})`} aria-hidden="true">
                          <rect x="-1.8" y="-1.05" width="7.5" height="2.1" rx="1.05" />
                          <circle cx="-0.55" cy="0" r="0.48" />
                          <text x="2.05" y="0.08">BALL</text>
                        </g>}

                        {/* High-Legibility Jersey Number in Tabular Figures */}
                        <text x={x} y={y + 0.5} className="candidate-player-num">
                          {p.jerseyNumber ?? '•'}
                        </text>
                      </g>
                    );
                  })}

                  {/* Interactive Floating Hover HUD Tooltip on the Pitch */}
                  {hoveredPlayer && typeof hoveredPlayer.x === 'number' && typeof hoveredPlayer.y === 'number' && (
                    <g
                      className="pitch-hud-tooltip"
                      transform={`translate(${Math.min(84, Math.max(16, toPitchX(hoveredPlayer.x)))}, ${toPitchY(hoveredPlayer.y) > 18 ? toPitchY(hoveredPlayer.y) - 6 : toPitchY(hoveredPlayer.y) + 8})`}
                      pointerEvents="none"
                    >
                      <rect x="-15" y="-5" width="30" height="7.5" rx="1.5" fill="rgba(7, 19, 28, 0.95)" stroke="#4aefac" strokeWidth="0.3" />
                      <text x="0" y="-1.5" fill="#ffffff" fontSize="2" fontWeight="bold" textAnchor="middle">
                        #{hoveredPlayer.jerseyNumber} {hoveredPlayer.playerName}
                      </text>
                      <text x="0" y="1.2" fill="#94a3b8" fontSize="1.4" textAnchor="middle">
                        {hoveredPlayer.teamName}
                      </text>
                    </g>
                  )}
                </svg>
              </div>
            ) : (
              <div className="pitch-loading-state">
                <p>{loading ? 'Loading players…' : 'Player positions are not available for this moment.'}</p>
              </div>
            )}

            {/* Selected Player Tactical Intelligence Card */}
            {selectedPlayer ? (
              <div className="selected-player-card">
                <div className="player-card-header">
                  <div className="player-badge-meta">
                    <span className={`player-jersey-pill ${selectedPlayer.teamName === secondSourceTeam ? 'away-side' : 'home-side'}`}>
                      #{selectedPlayer.jerseyNumber ?? '—'}
                    </span>
                    <div>
                      <h3 className="player-card-name">{valueText(selectedPlayer.playerName)}</h3>
                      <span className="player-card-sub">
                        {valueText(selectedPlayer.teamName)}{selectedPlayer.position ? ` · ${selectedPlayer.position}` : ''}
                      </span>
                    </div>
                  </div>
                  <div className="player-card-actions">
                    <a
                      className="player-details-link"
                      href={contextHref(`/players/${encodeURIComponent(selectedPlayer.playerId ?? selectedPlayer.playerName ?? '')}`, matchId ?? undefined, Math.floor(activeSeconds))}
                    >
                      Open {valueText(selectedPlayer.playerName)} in Players →
                    </a>
                    <button
                      type="button"
                      className="close-player-card-btn"
                      onClick={() => setSelectedPlayer(null)}
                      aria-label="Deselect player"
                    >
                      ✕
                    </button>
                  </div>
                </div>

              </div>
            ) : (
              <div className="player-selection-hint">
                <span>Select a player to see their match details.</span>
              </div>
            )}
          </section>

          {/* Linear Events Timeline below the pitch taking the whole width */}
          {!cinemaMode && (
            <div className="match-screen-timeline-row candidate-timeline-row is-full-width" style={{ width: '100%', maxWidth: '100%' }}>
              <LinearEventsTimeline
                events={linearTimelineEvents}
                matchId={matchId}
                matchTitle={sourceTeams.length >= 2 ? `${sourceTeams[0]} vs ${sourceTeams[1]}` : undefined}
                selectedSeconds={Math.floor(activeSeconds)}
                vertical={false}
                onSelectEvent={ev => {
                  setReplayPlaying(false);
                  setReplaySeconds(ev.clockSeconds);
                  syncReplayLocation(ev.clockSeconds);
                }}
              />
            </div>
          )}
        </div>
      )}

      <footer className="candidate-provenance-footer">
        Source state: {status?.sourceStatus ?? 'candidate_not_canonical'} · coordinates {status?.coordinateStatus ?? 'unverified'} · player metrics {status?.playerMetricsStatus ?? 'unvalidated'}
      </footer>
    </section>
  );
}
