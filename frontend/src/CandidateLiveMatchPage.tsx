import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { HubConnectionBuilder, HttpTransportType, LogLevel, type HubConnection } from '@microsoft/signalr';
import { clock, getHostAccessToken, request } from './api';
import { toggleCandidateStream, useCandidateStreamStatus } from './candidateLiveStatus';
import { contextHref } from './routes';

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
  const [feedTab, setFeedTab] = useState<'events' | 'telemetry' | 'audit'>('events');
  const [showMethodology, setShowMethodology] = useState(false);
  const pitchScreenRef = useRef<HTMLDivElement | null>(null);
  const [timelineCategory, setTimelineCategory] = useState<'All' | 'Goals' | 'Shots' | 'Passes' | 'Tackles' | 'Pressures'>('All');

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
      setError(hasMore ? 'More candidate history remains; refreshing on the next cursor update.' : '');
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
    bySequence.current = [];
    setEvents([]);
    setLoading(true);
    if (!matchId) {
      setLoading(false);
      return;
    }
    const controller = new AbortController();
    void backfill(matchId, controller.signal);
    return () => controller.abort();
  }, [matchId, backfill]);

  useEffect(() => {
    if (!matchId || !status?.isStreaming || !sourceHash) return;
    let active = true;
    let hub: HubConnection | null = null;
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
    const poll = () => {
      if (!timer && active)
        timer = setInterval(() => {
          if (document.visibilityState !== 'hidden') refresh();
        }, 2500);
    };
    async function connect() {
      try {
        const token = await getHostAccessToken();
        if (!active) return;
        const local = ['localhost', '127.0.0.1'].includes(window.location.hostname);
        const headers: Record<string, string> = { 'Content-Type': 'application/json' };
        if (token) headers.Authorization = `Bearer ${token}`;
        else if (local) {
          headers['X-Dev-Subject'] = 'local-analyst';
          headers['X-Dev-Roles'] = 'analyst';
        }
        const response = await fetch('/api/v1/simulation/candidate/realtime/negotiate', {
          method: 'POST',
          headers,
          body: JSON.stringify({ matchId, sourceSha256: sourceHash }),
          signal: abort.signal
        });
        if (!response.ok) throw new Error('Candidate SignalR unavailable.');
        const negotiation = (await response.json()) as { transport: string; url: string; accessToken: string | null };
        const url = new URL(negotiation.url, window.location.origin);
        if (
          negotiation.transport !== 'aspnetcore-signalr' ||
          url.origin !== window.location.origin ||
          url.pathname !== '/hubs/matches' ||
          negotiation.accessToken
        )
          throw new Error('Unsupported SignalR endpoint.');
        hub = new HubConnectionBuilder()
          .withUrl(url.toString(), {
            accessTokenFactory: async () => (await getHostAccessToken()) ?? '',
            ...(local
              ? { headers: { 'X-Dev-Subject': 'local-analyst', 'X-Dev-Roles': 'analyst' }, transport: HttpTransportType.LongPolling }
              : {})
          })
          .withAutomaticReconnect()
          .configureLogging(LogLevel.Error)
          .build();
        hub.on('CandidateMatchUpdated', (cursor: Cursor) => {
          if (active && cursor.matchId === matchId && cursor.sourceSha256 === sourceHash) refresh();
        });
        hub.onreconnecting(poll);
        hub.onclose(poll);
        hub.onreconnected(() => {
          if (timer) clearInterval(timer);
          timer = null;
          refresh();
        });
        await hub.start();
        if (!active) {
          await hub.stop();
          return;
        }
        await hub.invoke('SubscribeToCandidateMatch', matchId, sourceHash);
        refresh();
      } catch {
        poll();
      }
    }
    void connect();
    return () => {
      active = false;
      abort.abort();
      if (throttleTimer) clearTimeout(throttleTimer);
      if (timer) clearInterval(timer);
      if (hub) void hub.stop().catch(() => undefined);
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
  const sourceGoals = visibleEvents.filter(event => event.eventType === 'Goal');
  const sourceScore = sourceTeams.slice(0, 2).map(team => sourceGoals.filter(event => event.rawPayload.teamName === team).length);

  const ballX = latest?.rawPayload.ballX;
  const ballY = latest?.rawPayload.ballY;
  const ballPosition =
    typeof ballX === 'number' && Number.isFinite(ballX) && ballX >= 0 && ballX <= 100 &&
    typeof ballY === 'number' && Number.isFinite(ballY) && ballY >= 0 && ballY <= 100
      ? { x: ballX, y: ballY }
      : null;

  // Normalized math strictly preserved for test contract:
  const toPitchX = (value: number) => 3 + value * 0.94;
  const toPitchY = (value: number) => 3 + value * 0.62;

  const currentClockFormatted = useMemo(() => {
    const raw = latest?.clockSeconds ?? status?.currentClockValue;
    return typeof raw === 'number' ? clock(Math.floor(raw)) : '—';
  }, [latest, status?.currentClockValue]);

  // Filtered timeline events
  const filteredEvents = useMemo(() => {
    if (timelineCategory === 'All') return visibleEvents;
    if (timelineCategory === 'Goals') return visibleEvents.filter(e => e.eventType === 'Goal');
    if (timelineCategory === 'Shots') return visibleEvents.filter(e => e.eventType === 'Shot');
    if (timelineCategory === 'Passes') return visibleEvents.filter(e => e.eventType.includes('Pass'));
    if (timelineCategory === 'Tackles') return visibleEvents.filter(e => e.eventType === 'Tackle');
    if (timelineCategory === 'Pressures') return visibleEvents.filter(e => e.eventType.includes('Pressure'));
    return visibleEvents;
  }, [visibleEvents, timelineCategory]);

  return (
    <section className="content-page candidate-live-page">
      {/* Broadcast Scoreboard & Live Telemetry Hub */}
      <div className="candidate-match-hero-banner">
        {/* Top telemetry & status bar */}
        <div className="hero-top-bar">
          <div className="hero-status-pills">
            <span className="live-badge-glow">
              <span className="pulse-dot" aria-hidden="true"></span>
              CANDIDATE LIVE FEED
            </span>
            <span className="candidate-feed-label">Synthetic source · not canonical</span>
          </div>

          <div className="hero-stream-action">
            <button
              type="button"
              className={`candidate-stream-state ${status?.isStreaming ? 'is-streaming' : ''}`}
              aria-pressed={status?.isStreaming ?? false}
              aria-label={status?.isStreaming ? 'Pause candidate feed' : 'Resume candidate feed'}
              onClick={() => { void toggleCandidateStream(); }}
            >
              <span className="stream-dot" aria-hidden="true"></span>
              <span>{status?.isStreaming ? 'Streaming · Click to Pause' : 'Paused · Click to Resume'}</span>
            </button>
          </div>
        </div>

        <div className="candidate-match-context">
          <div className="candidate-match-identity"><p className="eyebrow">Selected source match</p>
            <h1>{sourceTeams.length >= 2 ? `${sourceTeams[0]} · ${sourceTeams[1]}` : 'Match in progress'}</h1>
            <p>Source clock {currentClockFormatted} · {latest ? `period ${latest.period}` : 'period unavailable'}</p></div>
          {sourceTeams.length >= 2 && <div className="candidate-source-scoreboard" aria-label={`Source-reported score: ${sourceTeams[0]} ${sourceScore[0] ?? 0}, ${sourceTeams[1]} ${sourceScore[1] ?? 0}; unverified`}>
            <div className="source-score-team"><span>{sourceTeams[0]}</span><strong>{sourceScore[0] ?? 0}</strong></div>
            <span className="source-score-separator" aria-hidden="true">–</span>
            <div className="source-score-team"><span>{sourceTeams[1]}</span><strong>{sourceScore[1] ?? 0}</strong></div>
            <small>Source-reported goals · unverified</small>
          </div>}
        </div>

        {/* Integrated Scrubber Timeline Bar */}
        {matchId && (
          <div className="candidate-replay-controls" aria-label="Match event replay">
            <div className="replay-meta">
              <span className="replay-title">Event replay</span>
              <span className="replay-clock">Source clock · {clock(Math.floor(activeSeconds))}</span>
            </div>

            <div className="replay-slider-wrap">
              <input
                aria-label="Select source event time"
                type="range"
                min="0"
                max={Math.max(1, replayMaximum)}
                step="1"
                value={activeSeconds}
                className="replay-range-slider"
                onChange={event => {
                  const seconds = Number(event.target.value);
                  setReplayPlaying(false);
                  setReplaySeconds(seconds);
                  syncReplayLocation(seconds);
                }}
              />
              <div className="slider-ticks" aria-hidden="true">
                <span>0</span>
                <span>Latest source time · {clock(Math.max(1, replayMaximum))}</span>
              </div>
            </div>

            <div className="replay-btn-group">
              <button
                type="button"
                className="replay-toggle-btn"
                onClick={() => setReplayPlaying(value => !value)}
                disabled={activeSeconds >= replayMaximum}
                aria-pressed={replayPlaying}
              >
                {replayPlaying ? 'Pause replay' : 'Play replay'}
              </button>
              <button
                type="button"
                className="return-live-btn"
                onClick={() => {
                  setReplayPlaying(false);
                  setReplaySeconds(null);
                  syncReplayLocation(undefined, 'live');
                }}
              >
                Return to live
              </button>
            </div>
          </div>
        )}
      </div>

      {/* Refined Methodology & Verification Notice */}
      <div className="candidate-caveat-box">
        <div className="caveat-summary-row">
          <div className="caveat-badge-wrap">
            <span className="caveat-icon" aria-hidden="true">i</span>
            <strong>Candidate Source Notice:</strong>
            <span className="caveat-short-text">Clock semantics, pitch coordinate units/axes/orientation, player metrics and football realism are unverified.</span>
          </div>
          <button type="button" className="caveat-expand-btn" aria-expanded={showMethodology} aria-controls="candidate-source-methodology" onClick={() => setShowMethodology(value => !value)}>
            {showMethodology ? 'Hide methodology' : 'Show methodology'}
          </button>
        </div>
          <div id="candidate-source-methodology" className="candidate-caveat" role="note" style={{ display: showMethodology ? 'block' : 'none' }}>
          Candidate source only. Clock semantics, pitch coordinate units/axes/orientation, player metrics and football realism are unverified.
          For display only, raw numeric x/y and ball coordinates are proportionally mapped from a 0–100 range to the pitch canvas; this does
          not certify real pitch dimensions or orientation. No clock conversion, interpolation or canonical claim is applied.
            <dl className="candidate-source-diagnostics"><div><dt>Source match ID</dt><dd>{matchId ?? 'Unavailable'}</dd></div>
              <div><dt>Records loaded</dt><dd>{events.length}</dd></div>
              <div><dt>Source SHA-256</dt><dd>{status?.sourceSha256 ?? 'Unavailable'}</dd></div></dl>
          </div>
      </div>

      {statusError && <p role="alert" className="candidate-error-banner">{statusError}</p>}
      {error && <p role="status" className="candidate-warning-banner">{error}</p>}
      {!matchId && <p role="status" className="candidate-waiting-banner">Waiting for the candidate simulation service.</p>}

      {matchId && (
        <div className="candidate-live-grid">
          {/* Left Column: Tactical Pitch Command Center */}
          <section className="candidate-panel pitch-command-panel">
            <div className="pitch-panel-toolbar">
              <div className="pitch-title-meta">
                <h2>Match Pitch</h2>
                <span className="pitch-meta-pills">
                  <span>Seq #{latest?.sequence ?? '—'}</span>
                  <span>Period {latest?.period ?? '—'}</span>
                  <span>Clock {valueText(latest?.clockSeconds)}s</span>
                  <span className="observed-players-tag">{players.length} source players</span>
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
                      style={{
                        transform: `translate(${toPitchX(ballPosition.x)}px, ${toPitchY(ballPosition.y)}px)`,
                        transition: 'transform 0.35s cubic-bezier(0.2, 0.8, 0.2, 1)',
                        pointerEvents: 'none',
                      }}
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

                    return (
                      <g
                        key={`${p.teamId ?? side}-${p.playerName ?? index}`}
                        onClick={() => setSelectedPlayer(isSelected ? null : p)}
                        onMouseEnter={() => setHoveredPlayer(p)}
                        onMouseLeave={() => setHoveredPlayer(null)}
                        style={{ cursor: 'pointer' }}
                        className={`player-marker-group ${isSelected ? 'is-selected' : ''}`}
                        role="button"
                        tabIndex={0}
                        aria-label={`Inspect ${p.playerName ?? p.playerId ?? 'source player'} from ${p.teamName ?? 'the source feed'}`}
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

                        {/* Player Turf Shadow */}
                        <ellipse cx={x} cy={y + 0.3} rx="1.35" ry="0.5" fill="rgba(0, 0, 0, 0.45)" filter="url(#player-shadow-filter)" />

                        {/* Authentic Team Kit Circle (Satisfies cx/cy test assertions) */}
                        <circle
                          cx={x}
                          cy={y}
                          r="1.45"
                          className={`candidate-player ${side}`}
                        />

                        {/* High-Legibility Jersey Number in Tabular Figures */}
                        <text x={x} y={y + 0.5} className="candidate-player-num">
                          {valueText(p.jerseyNumber)}
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
                <p>{loading ? 'Loading player tracking telemetry from candidate feed…' : 'No player positions recorded in this source event.'}</p>
              </div>
            )}

            <p className="candidate-coordinate-note">
              Raw source coordinates are displayed proportionally for preview · pitch scale and attack direction are unverified.
            </p>

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
                        {valueText(selectedPlayer.teamName)} · Position {valueText(selectedPlayer.position)}
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

                <p className="candidate-metric-note">Player performance metrics are withheld until their definitions and units are validated.</p>
              </div>
            ) : (
              <div className="player-selection-hint">
                <span>Select a source marker to inspect its identifier and source-reported fields.</span>
              </div>
            )}
          </section>

          {/* Right Column: Live Match Operations & Event Feed */}
          <aside className="candidate-panel candidate-feed-section">
            <div className="feed-header-row">
              <h2>Match Operations</h2>
              <div className="feed-segmented-tabs">
                <button
                  type="button"
                  className={`feed-tab-btn ${feedTab === 'events' ? 'is-active' : ''}`}
                  onClick={() => setFeedTab('events')}
                >
                  Live Feed ({filteredEvents.length})
                </button>
                <button
                  type="button"
                  className={`feed-tab-btn ${feedTab === 'telemetry' ? 'is-active' : ''}`}
                  onClick={() => setFeedTab('telemetry')}
                >
                  Telemetry
                </button>
                <button
                  type="button"
                  className={`feed-tab-btn ${feedTab === 'audit' ? 'is-active' : ''}`}
                  onClick={() => setFeedTab('audit')}
                >
                  Audit
                </button>
              </div>
            </div>

            {/* TAB 1: Live Event Feed */}
            {feedTab === 'events' && (
              <div className="feed-tab-content">
                {/* Event Filter Pills */}
                <div className="category-filter-bar">
                  {(['All', 'Goals', 'Shots', 'Passes', 'Tackles', 'Pressures'] as const).map(cat => (
                    <button
                      key={cat}
                      type="button"
                      className={`cat-pill-btn ${timelineCategory === cat ? 'active' : ''}`}
                      onClick={() => setTimelineCategory(cat)}
                    >
                      {cat}
                    </button>
                  ))}
                </div>

                <div className="events-scroll-container">
                  <ol className="candidate-history">
                    {filteredEvents
                      .slice(-40)
                      .reverse()
                      .map(event => (
                        <li key={event.eventId} className={`event-history-item ${event.eventType === 'Goal' ? 'is-goal-event' : ''}`}>
                          <div className="event-item-top">
                            <span className="event-seq-time">
                              #{event.sequence} · {valueText(event.clockSeconds)}s
                            </span>
                            <strong className="event-type-label">
                              {event.eventType}
                            </strong>
                          </div>
                          <div className="event-item-meta">
                            <span className="event-team-name">{valueText(event.rawPayload.teamName)}</span>
                            {Boolean(event.rawPayload.playerName) && (
                              <span className="event-player-name">· {valueText(event.rawPayload.playerName)}</span>
                            )}
                            <span className="event-id-pill">· <code>{event.eventId}</code></span>
                          </div>
                        </li>
                      ))}
                  </ol>
                </div>
              </div>
            )}

            {/* TAB 2: Telemetry & Raw Payload */}
            {feedTab === 'telemetry' && (
              <div className="feed-tab-content">
                {latest ? (
                  <div className="telemetry-details-card">
                    <dl className="telemetry-dl">
                      <div className="dl-row">
                        <dt>Event Type</dt>
                        <dd className="bold-emerald">{latest.eventType}</dd>
                      </div>
                      <div className="dl-row">
                        <dt>Clock / Period</dt>
                        <dd>{valueText(latest.clockSeconds)}s / Period {latest.period}</dd>
                      </div>
                      <div className="dl-row">
                        <dt>Sequence #</dt>
                        <dd>{latest.sequence}</dd>
                      </div>
                      <div className="dl-row">
                        <dt>Event ID</dt>
                        <dd><code>{latest.eventId}</code></dd>
                      </div>
                      <div className="dl-row">
                        <dt>Ball Location</dt>
                        <dd>{ballPosition ? `X: ${ballPosition.x.toFixed(1)}, Y: ${ballPosition.y.toFixed(1)}` : 'No ball position recorded'}</dd>
                      </div>
                    </dl>

                    <details className="raw-payload-collapsible" open>
                      <summary>Original Event Payload (JSON)</summary>
                      <pre className="candidate-raw-payload">{JSON.stringify(latest.rawPayload, null, 2)}</pre>
                    </details>
                  </div>
                ) : (
                  <p>{loading ? 'Loading candidate telemetry…' : 'No persisted source event yet.'}</p>
                )}
              </div>
            )}

            {/* TAB 3: Feed Audit Status */}
            {feedTab === 'audit' && (
              <div className="feed-tab-content">
                <div className="audit-specs-card">
                  <h3 className="audit-subheading">Data Provenance & Rigor</h3>
                  <dl className="telemetry-dl">
                    <div className="dl-row">
                      <dt>Source Status</dt>
                      <dd><code>{status?.sourceStatus ?? 'candidate_not_canonical'}</code></dd>
                    </div>
                    <div className="dl-row">
                      <dt>Coordinates</dt>
                      <dd><code>{status?.coordinateStatus ?? 'unverified'}</code></dd>
                    </div>
                    <div className="dl-row">
                      <dt>Player Metrics</dt>
                      <dd><code>{status?.playerMetricsStatus ?? 'unvalidated'}</code></dd>
                    </div>
                    <div className="dl-row">
                      <dt>SignalR Stream</dt>
                      <dd>{status?.isStreaming ? 'Active · PostgreSQL Backfill Synced' : 'Paused'}</dd>
                    </div>
                    <div className="dl-row">
                      <dt>Source SHA-256</dt>
                      <dd><code className="full-hash-break">{status?.sourceSha256 ?? '—'}</code></dd>
                    </div>
                  </dl>
                </div>
              </div>
            )}
          </aside>
        </div>
      )}

      <footer className="candidate-provenance-footer">
        Source state: {status?.sourceStatus ?? 'candidate_not_canonical'} · coordinates {status?.coordinateStatus ?? 'unverified'} · player metrics {status?.playerMetricsStatus ?? 'unvalidated'}
      </footer>
    </section>
  );
}
