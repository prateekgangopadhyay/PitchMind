import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { HubConnectionBuilder, HttpTransportType, LogLevel, type HubConnection } from '@microsoft/signalr';
import { getHostAccessToken, request } from './api';
import { useCandidateStreamStatus } from './candidateLiveStatus';

interface CandidateEvent { eventId: string; matchId: string; sequence: number; clockSeconds: number; period: number; eventType: string; rawPayload: Record<string, unknown> }
interface CandidatePage { matchId: string; sourceSha256: string; items: CandidateEvent[]; hasMore: boolean }
interface Cursor { matchId: string; sourceSha256: string; sequence: number }
interface SourcePlayer { [key: string]: unknown; playerName?: string; teamName?: string; teamId?: string; x?: number; y?: number }
const PAGE_SIZE = 100;
const MAX_PAGES = 30;
const isObject = (value: unknown): value is Record<string, unknown> => Boolean(value && typeof value === 'object' && !Array.isArray(value));
const valueText = (value: unknown) => value === null || value === undefined || value === '' ? '—' : typeof value === 'object' ? JSON.stringify(value) : String(value);

export function CandidateLiveMatchPage() {
  const { status, error: statusError } = useCandidateStreamStatus();
  const [events, setEvents] = useState<CandidateEvent[]>([]); const [loading, setLoading] = useState(true); const [error, setError] = useState('');
  const bySequence = useRef<CandidateEvent[]>([]); const busy = useRef(false); const refreshPending = useRef(false); const matchId = status?.matchId ?? null;
  const sourceHash = status?.sourceSha256 ?? null;
  const backfill = useCallback(async (target: string, signal?: AbortSignal) => {
    if (busy.current) { refreshPending.current = true; return; }
    busy.current = true;
    try {
      let after = bySequence.current.reduce((last, event) => Math.max(last, event.sequence), -1);
      let hasMore = true; let pages = 0; const fetched: CandidateEvent[] = [];
      while (hasMore && pages < MAX_PAGES) {
        const query = new URLSearchParams({ afterSequence: String(after), limit: String(PAGE_SIZE) });
        const page = await request<CandidatePage>(`simulation/candidate/matches/${encodeURIComponent(target)}/events?${query}`, signal);
        if (signal?.aborted) return;
        if (page.matchId !== target || !Array.isArray(page.items) || typeof page.hasMore !== 'boolean') throw new Error('Candidate event page is invalid.');
        for (const item of page.items) {
          if (item.matchId !== target || item.sequence <= after || !Number.isSafeInteger(item.sequence) || !isObject(item.rawPayload))
            throw new Error('Candidate event order or source payload is invalid.');
          fetched.push(item); after = item.sequence;
        }
        if (page.hasMore && page.items.length === 0) throw new Error('Candidate event cursor did not advance.');
        hasMore = page.hasMore; pages++;
      }
      const merged = new Map(bySequence.current.map(item => [item.sequence, item]));
      fetched.forEach(item => merged.set(item.sequence, item));
      bySequence.current = [...merged.values()].sort((a, b) => a.sequence - b.sequence); setEvents(bySequence.current);
      setError(hasMore ? 'More candidate history remains; refreshing on the next cursor update.' : '');
    } catch (cause) { if (!signal?.aborted) setError(cause instanceof Error ? cause.message : 'Candidate event history unavailable.'); }
    finally {
      busy.current = false;
      if (!signal?.aborted) setLoading(false);
      if (refreshPending.current && !signal?.aborted) {
        refreshPending.current = false;
        window.setTimeout(() => { void backfill(target); }, 0);
      }
    }
  }, []);

  useEffect(() => {
    bySequence.current = []; setEvents([]); setLoading(true);
    if (!matchId) { setLoading(false); return; }
    const controller = new AbortController(); void backfill(matchId, controller.signal);
    return () => controller.abort();
  }, [matchId, backfill]);

  useEffect(() => {
    if (!matchId || !status?.isStreaming || !sourceHash) return;
    let active = true; let hub: HubConnection | null = null; let timer: ReturnType<typeof setInterval> | null = null;
    const abort = new AbortController(); const refresh = () => { if (active) void backfill(matchId, abort.signal); };
    const poll = () => { if (!timer && active) timer = setInterval(() => { if (document.visibilityState !== 'hidden') refresh(); }, 2500); };
    async function connect() {
      try {
        const token = await getHostAccessToken(); if (!active) return;
        const local = ['localhost', '127.0.0.1'].includes(window.location.hostname);
        const headers: Record<string, string> = { 'Content-Type': 'application/json' };
        if (token) headers.Authorization = `Bearer ${token}`;
        else if (local) { headers['X-Dev-Subject'] = 'local-analyst'; headers['X-Dev-Roles'] = 'analyst'; }
        const response = await fetch('/api/v1/simulation/candidate/realtime/negotiate', { method: 'POST', headers,
          body: JSON.stringify({ matchId, sourceSha256: sourceHash }), signal: abort.signal });
        if (!response.ok) throw new Error('Candidate SignalR unavailable.');
        const negotiation = await response.json() as { transport: string; url: string; accessToken: string | null };
        const url = new URL(negotiation.url, window.location.origin);
        if (negotiation.transport !== 'aspnetcore-signalr' || url.origin !== window.location.origin || url.pathname !== '/hubs/matches' || negotiation.accessToken)
          throw new Error('Unsupported SignalR endpoint.');
        hub = new HubConnectionBuilder().withUrl(url.toString(), {
          accessTokenFactory: async () => await getHostAccessToken() ?? '',
          ...(local ? { headers: { 'X-Dev-Subject': 'local-analyst', 'X-Dev-Roles': 'analyst' }, transport: HttpTransportType.LongPolling } : {})
        })
          .withAutomaticReconnect().configureLogging(LogLevel.Error).build();
        hub.on('CandidateMatchUpdated', (cursor: Cursor) => {
          if (active && cursor.matchId === matchId && cursor.sourceSha256 === sourceHash) refresh();
        });
        hub.onreconnecting(poll); hub.onclose(poll);
        hub.onreconnected(() => { if (timer) clearInterval(timer); timer = null; refresh(); });
        await hub.start(); if (!active) { await hub.stop(); return; }
        await hub.invoke('SubscribeToCandidateMatch', matchId, sourceHash); refresh();
      } catch { poll(); }
    }
    void connect();
    return () => { active = false; abort.abort(); if (timer) clearInterval(timer); if (hub) void hub.stop().catch(() => undefined); };
  }, [matchId, status?.isStreaming, sourceHash, backfill]);

  const latest = useMemo(() => {
    if (!events.length) return null;
    const sequence = status?.currentSequence;
    return sequence == null ? events.at(-1)! : events.find(item => item.sequence === sequence) ?? events.filter(item => item.sequence <= sequence).at(-1) ?? events.at(-1)!;
  }, [events, status?.currentSequence]);
  const players = useMemo(() => Array.isArray(latest?.rawPayload.playerPositions)
    ? latest.rawPayload.playerPositions.filter(isObject) as SourcePlayer[] : [], [latest]);
  const teams = [...new Set(players.map(player => typeof player.teamName === 'string' ? player.teamName : '').filter(Boolean))];
  const home = teams[0] ?? 'Home team'; const away = teams[1] ?? 'Away team';
  const xs = players.flatMap(p => typeof p.x === 'number' && Number.isFinite(p.x) ? [p.x] : []);
  const ys = players.flatMap(p => typeof p.y === 'number' && Number.isFinite(p.y) ? [p.y] : []);
  const minX = Math.min(...xs, 0); const maxX = Math.max(...xs, 1); const minY = Math.min(...ys, 0); const maxY = Math.max(...ys, 1);

  return <section className="content-page candidate-live-page">
    <header className="candidate-live-heading"><div><p className="eyebrow">Candidate live · synthetic source</p><h1>{home} <span aria-hidden="true">vs</span> {away}</h1><p>Raw source simulation records, kept separate from canonical match data.</p></div><span className={`candidate-stream-state ${status?.isStreaming ? 'is-streaming' : ''}`} role="status">{status?.isStreaming ? 'Streaming' : 'Paused'}</span></header>
    {status?.sourceSha256 && <p>Source SHA-256 <code>{status.sourceSha256}</code></p>}
    <p className="candidate-caveat" role="note">Candidate source only. Clock semantics, pitch coordinate units/axes/orientation, player metrics and football realism are unverified. Markers are scaled to each event’s raw coordinate bounds to show relative shape; no clock conversion, interpolation or canonical claim is applied.</p>
    {statusError && <p role="alert">{statusError}</p>}{error && <p role="status">{error}</p>}
    {!matchId && <p role="status">Waiting for the candidate simulation service.</p>}
    {matchId && <div className="candidate-live-grid">
      <section className="candidate-panel"><h2>Player positions</h2><p>Sequence {latest?.sequence ?? '—'} · raw period {latest?.period ?? '—'} · raw clock {valueText(latest?.clockSeconds ?? status?.currentClockValue)}</p>
        {players.length ? <svg className="candidate-pitch" viewBox="0 0 100 68" role="img" aria-label="Player positions scaled to raw source-frame extents; pitch orientation unverified">
          <rect className="candidate-pitch-grass" width="100" height="68"/><g className="candidate-pitch-lines"><rect x="2" y="2" width="96" height="64"/><path d="M50 2v64M50 20a14 14 0 1 0 0 28a14 14 0 1 0 0-28M2 13h16v42H2M98 13H82v42h16"/></g>
          {players.map((p, index) => { const x = 3 + (typeof p.x === 'number' && maxX !== minX ? (p.x - minX) / (maxX - minX) * 94 : 47); const y = 3 + (typeof p.y === 'number' && maxY !== minY ? (p.y - minY) / (maxY - minY) * 62 : 31); const side = p.teamName === away ? 'away' : 'home'; return <g key={`${p.teamId ?? side}-${p.jerseyNumber ?? index}`}><circle cx={x} cy={y} r="1.9" className={`candidate-player ${side}`}/><text x={x} y={y + .6}>{valueText(p.jerseyNumber ?? index + 1)}</text></g>; })}
        </svg> : <p>{loading ? 'Loading player positions from PostgreSQL…' : 'No player positions in this source event.'}</p>}
        <p>Relative raw x/y only · no verified pitch scale or attack direction.</p>
        <div className="candidate-player-groups">{[home, away].map(team => <section key={team}><h3>{team}</h3><ul>{players.filter(p => p.teamName === team).map((p, index) => <li key={`${p.teamId ?? team}-${p.jerseyNumber ?? index}`}><strong>{valueText(p.playerName)}</strong><span>{Object.entries(p).filter(([key]) => !['playerName','teamName','teamId'].includes(key)).map(([key, value]) => `${key}: ${valueText(value)}`).join(' · ')}</span></li>)}</ul></section>)}</div>
      </section>
      <aside className="candidate-panel"><h2>Event stream</h2><p>SignalR cursors prompt PostgreSQL backfill; event JSON stays intact.</p>{latest ? <><dl><dt>Event type</dt><dd>{latest.eventType}</dd><dt>Raw clock / period</dt><dd>{valueText(latest.clockSeconds)} / {latest.period}</dd><dt>Sequence</dt><dd>{latest.sequence}</dd><dt>Event ID</dt><dd><code>{latest.eventId}</code></dd></dl><details><summary>Original event payload</summary><pre className="candidate-raw-payload">{JSON.stringify(latest.rawPayload, null, 2)}</pre></details></> : <p>{loading ? 'Loading candidate events…' : 'No persisted source event yet.'}</p>}
        <h3>Recent events · {events.length} loaded</h3><ol className="candidate-history">{events.slice(-40).reverse().map(event => <li key={event.eventId}><span>#{event.sequence} · {valueText(event.clockSeconds)}</span><strong>{event.eventType}</strong><small>{valueText(event.rawPayload.teamName)} · {valueText(event.rawPayload.playerName)}</small></li>)}</ol></aside>
    </div>}
    <footer>Source state: {status?.sourceStatus ?? 'candidate_not_canonical'} · coordinates {status?.coordinateStatus ?? 'unverified'} · player metrics {status?.playerMetricsStatus ?? 'unvalidated'}</footer>
  </section>;
}
