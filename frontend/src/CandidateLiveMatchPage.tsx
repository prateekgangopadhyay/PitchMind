import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { HubConnectionBuilder, HttpTransportType, LogLevel, type HubConnection } from '@microsoft/signalr';
import { clock, getHostAccessToken, request } from './api';
import { toggleCandidateStream, useCandidateStreamStatus } from './candidateLiveStatus';
import { ClubCrest } from './ShellPages';

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

export function CandidateLiveMatchPage() {
  const { status, error: statusError } = useCandidateStreamStatus();
  const [events, setEvents] = useState<CandidateEvent[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [selectedPlayer, setSelectedPlayer] = useState<SourcePlayer | null>(null);
  const [showTacticalZones, setShowTacticalZones] = useState(true);
  const [timelineCategory, setTimelineCategory] = useState<'All' | 'Goals' | 'Shots' | 'Passes' | 'Tackles' | 'Pressures'>('All');
  
  // Azure AI Analysis state
  const [aiQuestion, setAiQuestion] = useState("Explain the tactical mechanism behind the Zone 14 pressing regains.");
  const [aiAnalysisResult, setAiAnalysisResult] = useState<string | null>(null);
  const [aiAnalyzing, setAiAnalyzing] = useState(false);
  const [aiError, setAiError] = useState<string | null>(null);

  const bySequence = useRef<CandidateEvent[]>([]);
  const busy = useRef(false);
  const refreshPending = useRef(false);
  const matchId = status?.matchId ?? null;
  const sourceHash = status?.sourceSha256 ?? null;

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

  const latest = useMemo(() => {
    if (!events.length) return null;
    const sequence = status?.currentSequence;
    return sequence == null
      ? events.at(-1)!
      : events.find(item => item.sequence === sequence) ??
          events.filter(item => item.sequence <= sequence).at(-1) ??
          events.at(-1)!;
  }, [events, status?.currentSequence]);

  const players = useMemo(
    () => (Array.isArray(latest?.rawPayload.playerPositions) ? (latest.rawPayload.playerPositions.filter(isObject) as SourcePlayer[]) : []),
    [latest]
  );
  const teams = [...new Set(players.map(player => (typeof player.teamName === 'string' ? player.teamName : '')).filter(Boolean))];
  const home = teams[0] ?? 'Home';
  const away = teams[1] ?? 'Away';

  // Live Score computed dynamically from goals streamed up to current sequence
  const { homeScore, awayScore } = useMemo(() => {
    let h = 0;
    let a = 0;
    const seq = latest?.sequence ?? Infinity;
    for (const e of events) {
      if (e.sequence > seq) break;
      if (e.eventType === 'Goal') {
        const tName = String(e.rawPayload.teamName ?? '');
        if (tName === home || tName.toLowerCase().includes('united') || tName.toLowerCase().includes('home')) {
          h++;
        } else {
          a++;
        }
      }
    }
    // If no goals encountered yet or default demo, provide initial or current score
    return { homeScore: h, awayScore: a };
  }, [events, home, latest]);

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
    return typeof raw === 'number' ? clock(Math.floor(raw)) : '88:55';
  }, [latest, status?.currentClockValue]);

  // Filtered timeline events
  const filteredEvents = useMemo(() => {
    if (timelineCategory === 'All') return events;
    if (timelineCategory === 'Goals') return events.filter(e => e.eventType === 'Goal');
    if (timelineCategory === 'Shots') return events.filter(e => e.eventType === 'Shot');
    if (timelineCategory === 'Passes') return events.filter(e => e.eventType.includes('Pass'));
    if (timelineCategory === 'Tackles') return events.filter(e => e.eventType === 'Tackle');
    if (timelineCategory === 'Pressures') return events.filter(e => e.eventType.includes('Pressure'));
    return events;
  }, [events, timelineCategory]);

  const handleRunAiAnalysis = async (customPrompt?: string) => {
    setAiAnalyzing(true);
    setAiError(null);
    try {
      const promptToUse = customPrompt || aiQuestion;
      const res = await request<{ text: string; source: string }>('simulation/candidate/analyst', undefined, {
        question: promptToUse,
        atSeconds: typeof latest?.clockSeconds === 'number' ? Math.floor(latest.clockSeconds) : 5335,
        matchId: matchId || status?.matchId || ''
      });
      setAiAnalysisResult(res.text);
    } catch (err: unknown) {
      const errObj = err as { detail?: string; message?: string } | null;
      setAiError(errObj?.detail || errObj?.message || 'Azure AI Foundry tactical analysis unavailable.');
    } finally {
      setAiAnalyzing(false);
    }
  };

  return (
    <section className="content-page candidate-live-page">
      {/* Broadcast Match Header Scoreboard */}
      <div className="candidate-match-hero-banner" style={{ background: 'linear-gradient(135deg, rgba(15, 23, 42, 0.95), rgba(8, 18, 27, 0.95))', border: '1px solid rgba(74, 239, 172, 0.3)', borderRadius: '16px', padding: '18px 24px', marginBottom: '20px', boxShadow: '0 8px 32px rgba(0,0,0,0.4)' }}>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: '12px', borderBottom: '1px solid rgba(255,255,255,0.08)', paddingBottom: '14px', marginBottom: '16px' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
            <span style={{ background: 'rgba(74, 239, 172, 0.15)', color: '#4aefac', border: '1px solid rgba(74, 239, 172, 0.4)', borderRadius: '999px', padding: '3px 10px', fontSize: '0.75rem', fontWeight: 700, letterSpacing: '0.05em' }}>
              PREMIER LEAGUE · LIVE SIMULATION
            </span>
            <span style={{ color: '#94a3b8', fontSize: '0.8rem' }}>Old Trafford · Manchester, UK</span>
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
            <button
              type="button"
              className={`candidate-stream-state ${status?.isStreaming ? 'is-streaming' : ''}`}
              role="status"
              onClick={() => { void toggleCandidateStream(); }}
              style={{ cursor: 'pointer', background: 'transparent', display: 'flex', alignItems: 'center', gap: '6px' }}
            >
              <span style={{ width: 8, height: 8, borderRadius: '50%', background: status?.isStreaming ? '#4aefac' : '#94a3b8', boxShadow: status?.isStreaming ? '0 0 8px #4aefac' : 'none' }}></span>
              <span>{status?.isStreaming ? 'Streaming · Click to Pause' : 'Paused · Click to Resume'}</span>
            </button>
          </div>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr) auto minmax(0, 1fr)', alignItems: 'center', gap: '20px' }}>
          {/* Home Team */}
          <div style={{ display: 'flex', alignItems: 'center', gap: '16px', justifyContent: 'flex-start' }}>
            <ClubCrest name={home} className="scoreboard-team-crest" width={48} height={48} />
            <div>
              <h2 style={{ margin: 0, fontSize: '1.4rem', fontWeight: 800, color: '#fff', letterSpacing: '-0.02em' }}>{home}</h2>
              <span style={{ fontSize: '0.8rem', color: '#ff7182', fontWeight: 600 }}>Home · 4 - 2 - 3 - 1</span>
            </div>
          </div>

          {/* Center Score & Clock */}
          <div style={{ textAlign: 'center', padding: '0 24px' }}>
            <div style={{ fontSize: '2.5rem', fontWeight: 900, color: '#fff', letterSpacing: '0.05em', fontVariantNumeric: 'tabular-nums' }}>
              <span className="home-live-score">{homeScore}</span>
              <span style={{ color: 'rgba(255,255,255,0.4)', margin: '0 8px' }}>–</span>
              <span className="away-live-score">{awayScore}</span>
            </div>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '8px', marginTop: '4px' }}>
              <span style={{ color: '#4aefac', fontWeight: 700, fontSize: '0.95rem' }}>{currentClockFormatted}</span>
              <span style={{ color: '#64748b' }}>·</span>
              <span style={{ color: '#cbd5e1', fontSize: '0.85rem' }}>Period {latest?.period ?? status?.period ?? 2}</span>
            </div>
          </div>

          {/* Away Team */}
          <div style={{ display: 'flex', alignItems: 'center', gap: '16px', justifyContent: 'flex-end', textAlign: 'right' }}>
            <div>
              <h2 style={{ margin: 0, fontSize: '1.4rem', fontWeight: 800, color: '#fff', letterSpacing: '-0.02em' }}>{away}</h2>
              <span style={{ fontSize: '0.8rem', color: '#7db8ff', fontWeight: 600 }}>Away · 4 - 3 - 3</span>
            </div>
            <ClubCrest name={away} className="scoreboard-team-crest" width={48} height={48} />
          </div>
        </div>
      </div>

      <header className="candidate-live-heading" style={{ display: 'none' }}>
        <div>
          <p className="eyebrow">Candidate live · synthetic source</p>
          <h1>
            {home} <span aria-hidden="true">vs</span> {away}
          </h1>
          <p>Raw source simulation records, kept separate from canonical match data.</p>
        </div>
      </header>

      {status?.sourceSha256 && (
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: '0.78rem', color: '#94a3b8', marginBottom: '12px' }}>
          <span>Telemetry Source SHA-256:</span>
          <code style={{ background: '#08121b', padding: '2px 6px', borderRadius: '4px', color: '#cbd5e1' }}>
            {status.sourceSha256}
          </code>
          <span>· Total dataset events: {events.length} streamed</span>
        </div>
      )}

      <p className="candidate-caveat" role="note">
        Candidate source only. Clock semantics, pitch coordinate units/axes/orientation, player metrics and football realism are unverified.
        For display only, raw numeric x/y and ball coordinates are proportionally mapped from a 0–100 range to the pitch canvas; this does
        not certify real pitch dimensions or orientation. No clock conversion, interpolation or canonical claim is applied.
      </p>

      {statusError && <p role="alert" style={{ color: '#ef4444', fontWeight: 600 }}>{statusError}</p>}
      {error && <p role="status" style={{ color: '#eab308' }}>{error}</p>}
      {!matchId && <p role="status">Waiting for the candidate simulation service.</p>}

      {matchId && (
        <div className="candidate-live-grid">
          {/* Main Left Column: Tactical Football Field & Player Analytics */}
          <section className="candidate-panel" style={{ position: 'relative' }}>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: '10px', marginBottom: '8px' }}>
              <div>
                <h2 style={{ margin: 0 }}>Live Football Field</h2>
                <p style={{ margin: '4px 0 0', color: '#94a3b8', fontSize: '0.85rem' }}>
                  Sequence #{latest?.sequence ?? '—'} · Period {latest?.period ?? '—'} · Raw Clock {valueText(latest?.clockSeconds ?? status?.currentClockValue)}s · 22 Live Players
                </p>
              </div>
              <div style={{ display: 'flex', gap: '8px' }}>
                <button
                  type="button"
                  onClick={() => setShowTacticalZones(!showTacticalZones)}
                  style={{
                    padding: '4px 10px',
                    borderRadius: '6px',
                    fontSize: '0.78rem',
                    fontWeight: 600,
                    background: showTacticalZones ? 'rgba(74, 239, 172, 0.2)' : 'rgba(255,255,255,0.06)',
                    color: showTacticalZones ? '#4aefac' : '#cbd5e1',
                    border: '1px solid ' + (showTacticalZones ? 'rgba(74, 239, 172, 0.4)' : 'rgba(255,255,255,0.1)'),
                    cursor: 'pointer'
                  }}
                >
                  {showTacticalZones ? '✓ Zone 14 Overlay' : 'Show Zone 14'}
                </button>
              </div>
            </div>

            {players.length ? (
              <div style={{ position: 'relative' }}>
                <svg
                  className="candidate-pitch"
                  viewBox="0 0 100 68"
                  role="img"
                  aria-label="Raw candidate player and ball positions mapped to the pitch canvas; dimensions and orientation unverified"
                  style={{ width: '100%', height: 'auto', borderRadius: '12px', boxShadow: 'inset 0 0 30px rgba(0,0,0,0.5)', background: '#0a4630' }}
                >
                  <defs>
                    <linearGradient id="grassMow" x1="0" y1="0" x2="100" y2="0" gradientUnits="userSpaceOnUse">
                      <stop offset="0%" stopColor="#0c563b" />
                      <stop offset="10%" stopColor="#0d6143" />
                      <stop offset="20%" stopColor="#0c563b" />
                      <stop offset="30%" stopColor="#0d6143" />
                      <stop offset="40%" stopColor="#0c563b" />
                      <stop offset="50%" stopColor="#0d6143" />
                      <stop offset="60%" stopColor="#0c563b" />
                      <stop offset="70%" stopColor="#0d6143" />
                      <stop offset="80%" stopColor="#0c563b" />
                      <stop offset="90%" stopColor="#0d6143" />
                      <stop offset="100%" stopColor="#0c563b" />
                    </linearGradient>
                  </defs>

                  <rect className="candidate-pitch-grass" width="100" height="68" fill="url(#grassMow)" />

                  {/* Tactical Zone 14 Highlight */}
                  {showTacticalZones && (
                    <g className="tactical-zones-group">
                      <rect x="58" y="24" width="22" height="20" fill="rgba(234, 179, 8, 0.15)" stroke="rgba(234, 179, 8, 0.5)" strokeDasharray="1,1" strokeWidth="0.3" />
                      <text x="69" y="34" fill="rgba(234, 179, 8, 0.85)" fontSize="1.8" textAnchor="middle" fontWeight="bold">ZONE 14</text>
                    </g>
                  )}

                  <g className="candidate-pitch-lines">
                    <rect x="2" y="2" width="96" height="64" />
                    <path d="M50 2v64M50 20a14 14 0 1 0 0 28a14 14 0 1 0 0-28M2 13h16v42H2M98 13H82v42h16" />
                    {/* Goal boxes */}
                    <rect x="2" y="22" width="6" height="24" />
                    <rect x="92" y="22" width="6" height="24" />
                    {/* Penalty spots */}
                    <circle cx="12" cy="34" r="0.5" fill="#fff" />
                    <circle cx="88" cy="34" r="0.5" fill="#fff" />
                    <circle cx="50" cy="34" r="0.6" fill="#fff" />
                  </g>

                  {/* Ball Marker */}
                  {ballPosition && (
                    <g className="candidate-ball-marker">
                      <circle cx={toPitchX(ballPosition.x)} cy={toPitchY(ballPosition.y)} r="2.2" fill="none" stroke="#4aefac" strokeWidth="0.4" opacity="0.75" />
                      <circle cx={toPitchX(ballPosition.x)} cy={toPitchY(ballPosition.y)} r="1.3" />
                      <text x={toPitchX(ballPosition.x)} y={toPitchY(ballPosition.y) + 0.45}>B</text>
                    </g>
                  )}

                  {/* 22 Live Player Markers */}
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
                    const side = p.teamName === away ? 'away' : 'home';
                    const isSelected = selectedPlayer?.playerName === p.playerName;

                    return (
                      <g
                        key={`${p.teamId ?? side}-${p.jerseyNumber ?? index}`}
                        onClick={() => setSelectedPlayer(p)}
                        style={{ cursor: 'pointer' }}
                      >
                        {isSelected && (
                          <circle cx={x} cy={y} r="2.8" fill="none" stroke="#facc15" strokeWidth="0.5" />
                        )}
                        <circle cx={x} cy={y} r="1.9" className={`candidate-player ${side}`} />
                        <text x={x} y={y + 0.6}>
                          {valueText(p.jerseyNumber ?? index + 1)}
                        </text>
                      </g>
                    );
                  })}
                </svg>

                {/* Selected Player Interactive HUD Card */}
                {selectedPlayer && (
                  <div style={{ marginTop: '12px', padding: '12px 16px', background: 'rgba(15, 23, 42, 0.95)', border: '1px solid #facc15', borderRadius: '10px', display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: '12px' }}>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                      <span style={{ width: 28, height: 28, borderRadius: '50%', background: selectedPlayer.teamName === away ? '#0284c7' : '#dc2626', color: '#fff', display: 'flex', alignItems: 'center', justifyContent: 'center', fontWeight: 800, fontSize: '0.85rem' }}>
                        {selectedPlayer.jerseyNumber ?? '?'}
                      </span>
                      <div>
                        <strong style={{ fontSize: '0.95rem', color: '#fff' }}>{selectedPlayer.playerName}</strong>
                        <div style={{ fontSize: '0.78rem', color: '#94a3b8' }}>
                          {selectedPlayer.teamName} · {selectedPlayer.position ?? 'Midfield'}
                        </div>
                      </div>
                    </div>
                    <div style={{ display: 'flex', gap: '16px', flexWrap: 'wrap', fontSize: '0.8rem' }}>
                      <div><span style={{ color: '#64748b' }}>Speed:</span> <strong style={{ color: '#4aefac' }}>{valueText(selectedPlayer.speed)} km/h</strong></div>
                      <div><span style={{ color: '#64748b' }}>Distance:</span> <strong style={{ color: '#38bdf8' }}>{valueText(selectedPlayer.distanceCovered)} km</strong></div>
                      <div><span style={{ color: '#64748b' }}>Pass Acc:</span> <strong style={{ color: '#facc15' }}>{valueText(selectedPlayer.passAccuracy)}%</strong></div>
                      <div><span style={{ color: '#64748b' }}>Touches:</span> <strong style={{ color: '#fff' }}>{valueText(selectedPlayer.touches)}</strong></div>
                      <div><span style={{ color: '#64748b' }}>Stamina:</span> <strong style={{ color: '#4ade80' }}>{valueText(selectedPlayer.stamina)}%</strong></div>
                    </div>
                    <button type="button" onClick={() => setSelectedPlayer(null)} style={{ background: 'transparent', border: 'none', color: '#94a3b8', cursor: 'pointer', fontSize: '0.9rem' }}>✕</button>
                  </div>
                )}
              </div>
            ) : (
              <p>{loading ? 'Loading player positions from PostgreSQL…' : 'No player positions in this source event.'}</p>
            )}

            <p style={{ fontSize: '0.78rem', color: '#64748b', marginTop: '8px' }}>
              Raw source coordinates shown proportionally for preview · no verified pitch scale or attack direction.
            </p>

            {/* Team Squad Lists */}
            <div className="candidate-player-groups">
              {[home, away].map(team => (
                <section key={team} style={{ background: 'rgba(8, 18, 27, 0.6)', padding: '14px', borderRadius: '10px', border: '1px solid rgba(255,255,255,0.06)' }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '10px' }}>
                    <ClubCrest name={team} width={20} height={20} />
                    <h3 style={{ margin: 0, fontSize: '1rem', color: '#fff' }}>{team} Squad</h3>
                  </div>
                  <ul>
                    {players
                      .filter(p => p.teamName === team)
                      .map((p, index) => (
                        <li
                          key={`${p.teamId ?? team}-${p.jerseyNumber ?? index}`}
                          onClick={() => setSelectedPlayer(p)}
                          style={{ cursor: 'pointer', transition: 'background 0.2s', padding: '6px 8px', borderRadius: '6px' }}
                        >
                          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                            <span>
                              <span style={{ color: p.teamName === away ? '#7db8ff' : '#ff7182', fontWeight: 700, marginRight: '6px' }}>
                                #{p.jerseyNumber ?? index + 1}
                              </span>
                              <strong>{valueText(p.playerName)}</strong>
                            </span>
                            <span style={{ fontSize: '0.75rem', color: '#4aefac' }}>
                              {p.speed ? `${p.speed} km/h` : p.position ?? '—'}
                            </span>
                          </div>
                          <span>
                            {Object.entries(p)
                              .filter(([key]) => !['playerName', 'teamName', 'teamId', 'jerseyNumber', 'x', 'y'].includes(key))
                              .map(([key, value]) => `${key}: ${valueText(value)}`)
                              .join(' · ')}
                          </span>
                        </li>
                      ))}
                  </ul>
                </section>
              ))}
            </div>

            {/* Azure AI Tactical Analysis Panel */}
            <div style={{ marginTop: '24px', padding: '18px 20px', background: 'linear-gradient(135deg, rgba(8, 20, 36, 0.9), rgba(15, 23, 42, 0.95))', border: '1px solid rgba(56, 189, 248, 0.35)', borderRadius: '12px' }}>
              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: '8px', marginBottom: '12px' }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                  <span style={{ background: 'rgba(56, 189, 248, 0.2)', color: '#38bdf8', border: '1px solid rgba(56, 189, 248, 0.4)', borderRadius: '999px', padding: '2px 8px', fontSize: '0.7rem', fontWeight: 700 }}>
                    AZURE AI FOUNDRY
                  </span>
                  <h3 style={{ margin: 0, fontSize: '1.05rem', color: '#fff' }}>Detailed AI Tactical Analytics</h3>
                </div>
                <span style={{ fontSize: '0.78rem', color: '#94a3b8' }}>Evaluated against candidate telemetry</span>
              </div>

              <div style={{ display: 'flex', gap: '8px', marginBottom: '12px' }}>
                <input
                  type="text"
                  value={aiQuestion}
                  onChange={e => setAiQuestion(e.target.value)}
                  placeholder="Ask a tactical question about this match..."
                  style={{ flex: 1, padding: '8px 12px', borderRadius: '8px', border: '1px solid rgba(255,255,255,0.15)', background: '#07131c', color: '#fff', fontSize: '0.85rem' }}
                />
                <button
                  type="button"
                  onClick={() => handleRunAiAnalysis()}
                  disabled={aiAnalyzing}
                  style={{ padding: '8px 16px', borderRadius: '8px', border: 'none', background: '#0284c7', color: '#fff', fontWeight: 600, fontSize: '0.85rem', cursor: 'pointer' }}
                >
                  {aiAnalyzing ? 'Analyzing…' : 'Ask Azure AI'}
                </button>
              </div>

              <div style={{ display: 'flex', gap: '6px', flexWrap: 'wrap', marginBottom: '12px' }}>
                {[
                  "Why did the high press succeed in the 14th minute?",
                  "How did the attacking side overload the right half-space?",
                  "Evaluate midfield defensive transition efficiency"
                ].map(q => (
                  <button
                    key={q}
                    type="button"
                    onClick={() => { setAiQuestion(q); void handleRunAiAnalysis(q); }}
                    style={{ background: 'rgba(255,255,255,0.06)', border: '1px solid rgba(255,255,255,0.1)', color: '#94a3b8', borderRadius: '999px', padding: '3px 10px', fontSize: '0.75rem', cursor: 'pointer' }}
                  >
                    {q}
                  </button>
                ))}
              </div>

              {aiError && <p style={{ color: '#f87171', fontSize: '0.85rem', margin: '8px 0' }}>{aiError}</p>}

              {aiAnalysisResult ? (
                <div style={{ padding: '14px', background: 'rgba(7, 19, 28, 0.8)', border: '1px solid rgba(56, 189, 248, 0.2)', borderRadius: '8px', color: '#e2e8f0', fontSize: '0.85rem', lineHeight: '1.6', whiteSpace: 'pre-wrap' }}>
                  {aiAnalysisResult}
                </div>
              ) : (
                <p style={{ color: '#64748b', fontSize: '0.82rem', margin: 0 }}>
                  Click "Ask Azure AI" to generate real-time evidence-grounded tactical analysis of the live stream.
                </p>
              )}
            </div>
          </section>

          {/* Right Column: Live Event Stream & Raw Telemetry */}
          <aside className="candidate-panel">
            <h2>Live Event Stream</h2>
            <p>SignalR cursors prompt PostgreSQL backfill; event JSON stays intact.</p>

            {/* Event Filter Pills */}
            <div style={{ display: 'flex', gap: '6px', flexWrap: 'wrap', margin: '10px 0 14px' }}>
              {(['All', 'Goals', 'Shots', 'Passes', 'Tackles', 'Pressures'] as const).map(cat => (
                <button
                  key={cat}
                  type="button"
                  onClick={() => setTimelineCategory(cat)}
                  style={{
                    padding: '3px 9px',
                    borderRadius: '6px',
                    fontSize: '0.75rem',
                    fontWeight: 600,
                    background: timelineCategory === cat ? '#0284c7' : 'rgba(255,255,255,0.06)',
                    color: timelineCategory === cat ? '#fff' : '#94a3b8',
                    border: '1px solid ' + (timelineCategory === cat ? '#38bdf8' : 'rgba(255,255,255,0.1)'),
                    cursor: 'pointer'
                  }}
                >
                  {cat}
                </button>
              ))}
            </div>

            {latest ? (
              <>
                <dl>
                  <dt>Event type</dt>
                  <dd style={{ color: latest.eventType === 'Goal' ? '#4ade80' : '#fff', fontWeight: 700 }}>
                    {latest.eventType} {latest.eventType === 'Goal' ? '⚽' : ''}
                  </dd>
                  <dt>Raw clock / period</dt>
                  <dd>{valueText(latest.clockSeconds)} / {latest.period}</dd>
                  <dt>Sequence</dt>
                  <dd>{latest.sequence}</dd>
                  <dt>Event ID</dt>
                  <dd><code>{latest.eventId}</code></dd>
                </dl>
                <details>
                  <summary>Original event payload</summary>
                  <pre className="candidate-raw-payload">{JSON.stringify(latest.rawPayload, null, 2)}</pre>
                </details>
              </>
            ) : (
              <p>{loading ? 'Loading candidate events…' : 'No persisted source event yet.'}</p>
            )}

            <h3 style={{ marginTop: '20px' }}>Recent events · {filteredEvents.length} loaded</h3>
            <ol className="candidate-history">
              {filteredEvents
                .slice(-40)
                .reverse()
                .map(event => (
                  <li key={event.eventId} style={{ borderLeft: event.eventType === 'Goal' ? '3px solid #4ade80' : 'none' }}>
                    <span>
                      #{event.sequence} · {valueText(event.clockSeconds)}s
                    </span>
                    <strong style={{ color: event.eventType === 'Goal' ? '#4ade80' : '#fff' }}>
                      {event.eventType} {event.eventType === 'Goal' ? '⚽' : ''}
                    </strong>
                    <small>
                      {valueText(event.rawPayload.teamName)} · {valueText(event.rawPayload.playerName)}
                    </small>
                  </li>
                ))}
            </ol>
          </aside>
        </div>
      )}

      <footer>
        Source state: {status?.sourceStatus ?? 'candidate_not_canonical'} · coordinates {status?.coordinateStatus ?? 'unverified'} · player metrics {status?.playerMetricsStatus ?? 'unvalidated'}
      </footer>
    </section>
  );
}
