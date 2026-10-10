import { useEffect, useRef, useState, useSyncExternalStore } from 'react';
import { clock, request } from './api';
import type { Evidence, Explanation, Fixture, Insight, MatchEvent, Snapshot } from './api';
import { RoutePage } from './ShellPages';
import { CanonicalMatchPage } from './CanonicalMatchPage';
import { contextHref, currentLocation, replaceCurrentMatchContext, resolveRoute, selectedTimeFromSearch, subscribeLocation } from './routes';

function MatchExperience({ expectedMatchId, initialSeconds, replay }: { expectedMatchId?: string; initialSeconds: number; replay: boolean }) {
  const contextVersion = useRef(0);
  const [fixtures, setFixtures] = useState<Fixture[]>([]);
  const [matchId, setMatchId] = useState(expectedMatchId ?? new URLSearchParams(typeof window === 'undefined' ? '' : window.location.search).get('matchId') ?? '');
  const [seconds, setSeconds] = useState(initialSeconds);
  const [speed, setSpeed] = useState(15);
  const [playing, setPlaying] = useState(false);
  const [data, setData] = useState<{state: Snapshot; events: MatchEvent[]; insights: Insight[]} | null>(null);
  const [evidence, setEvidence] = useState<Evidence | null>(null);
  const [explanation, setExplanation] = useState<Explanation | null>(null);
  const [audience, setAudience] = useState('fan');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const fixture = fixtures.find(f => f.matchId === matchId);
  const pitchLengthMetres = fixture?.pitchLengthMetres ?? 0;
  const pitchWidthMetres = fixture?.pitchWidthMetres ?? 0;
  const penaltyAreaDepth = 16.5;
  const penaltyAreaWidth = 40.32;
  const goalAreaDepth = 5.5;
  const goalAreaWidth = 18.32;
  useEffect(() => {
    const controller = new AbortController();
    request<Fixture[]>('matches', controller.signal).then(items => {
      if (controller.signal.aborted) return;
      setFixtures(items);
      const requestedMatch = expectedMatchId ?? new URLSearchParams(window.location.search).get('matchId') ?? undefined;
      const selected = requestedMatch ? items.find(item => item.matchId === requestedMatch) : items[0];
      if (selected) { setMatchId(selected.matchId); setError(''); }
      else setError('This simulated match is unavailable. Choose a match from Home.');
    }).catch(e => { if (!controller.signal.aborted) setError(String(e.message)); });
    return () => controller.abort();
  }, [expectedMatchId]);
  useEffect(() => {
    if (!matchId) return;
    const controller = new AbortController();
    Promise.all([request<Snapshot>(`matches/${matchId}/state?atSeconds=${seconds}`, controller.signal), request<MatchEvent[]>(`matches/${matchId}/events?atSeconds=${seconds}`, controller.signal), request<Insight[]>(`matches/${matchId}/insights?atSeconds=${seconds}`, controller.signal)])
      .then(([state, events, insights]) => {if (controller.signal.aborted) return; setData({state, events, insights}); setError('');})
      .catch(e => { if (!controller.signal.aborted) {setData(null); setError(String(e.message)); setPlaying(false);} });
    return () => controller.abort();
  }, [matchId, seconds]);
  useEffect(() => {
    if (!playing || !fixture || seconds >= fixture.durationSeconds) return;
    const timer = setInterval(() => {
      setSeconds(previous => Math.min(previous + speed, fixture.durationSeconds));
    }, 1000);
    return () => clearInterval(timer);
  }, [playing, fixture, speed, seconds]);
  useEffect(() => {
    if (matchId) replaceCurrentMatchContext(matchId, seconds);
  }, [matchId, seconds]);
  const selectTime = (time: number) => {contextVersion.current++; setSeconds(time); setEvidence(null); setExplanation(null);};
  const current = data?.state.clockSeconds === seconds ? data : null;
  const insight = current?.insights.at(-1);
  async function showEvidence() {
    if (!insight) return;
    setPlaying(false); setBusy(true);
    const version = contextVersion.current;
    try {const result = await request<Evidence>(`matches/${matchId}/evidence/${insight.id}?atSeconds=${seconds}`); if (version === contextVersion.current) setEvidence(result);}
    catch(e) {if (version === contextVersion.current) setError(e instanceof Error ? e.message : 'Evidence unavailable.');}
    finally {setBusy(false);}
  }
  async function askAnalyst() {
    if (!insight) return;
    setPlaying(false); setBusy(true);
    const version = contextVersion.current;
    try {const result = await request<Explanation>(`matches/${matchId}/analyst`, undefined, {atSeconds: seconds, insightId: insight.id, audience}); if (version === contextVersion.current) setExplanation(result);}
    catch(e) {if (version === contextVersion.current) setError(e instanceof Error ? e.message : 'Explanation unavailable.');}
    finally {setBusy(false);}
  }
  return <>
    <div className="intro"><div><p className="eyebrow">Football intelligence, grounded in evidence</p><h1>{replay ? 'Match replay' : 'The match. The mechanism. The proof.'}</h1></div>{fixtures.length > 0 && <select aria-label="Select simulated match" value={matchId} onChange={e => {setMatchId(e.target.value); selectTime(0); setPlaying(false);}}>{fixtures.map(f => <option key={f.matchId} value={f.matchId}>{f.home.name} vs {f.away.name}</option>)}</select>}</div>
    {error && <div role="alert" className="notice error">{error} <button onClick={() => window.location.reload()}>Retry connection</button></div>}
    {!fixture && !error && <div role="status" className="notice">Loading simulated fixtures…</div>}
    {fixture && <><section className="scoreboard" aria-label="Match score"><div className="team">{fixture.home.name}<p>{fixture.home.formation}</p></div><span className="score">{current?.state.homeScore ?? '—'}</span><div className="clock">{clock(seconds)}<small>{current ? current.state.status : 'Loading selected moment…'}</small></div><span className="score">{current?.state.awayScore ?? '—'}</span><div className="team">{fixture.away.name}<p>{fixture.away.formation}</p></div></section>
    <section className="story"><div><span className="badge">{current?.insights.at(-1)?.classification ?? 'Simulated telemetry'}</span><p>{current?.insights.at(-1)?.title ?? 'Watch the match unfold. Tactical insights appear when the evidence supports them.'}</p></div><button disabled={!current?.insights.length || busy} onClick={showEvidence}>Show me why →</button></section>
    <div className="dashboard"><section className="panel"><div className="panel-head"><h2>Intelligent Pitch</h2><p>Sampled tracking frame {current ? clock(current.state.frame.clockSeconds) : 'loading…'} · Selected {clock(seconds)}</p></div>{current && <svg className="pitch" viewBox={`-3 -3 ${pitchLengthMetres + 6} ${pitchWidthMetres + 6}`} role="img" aria-label={`Simulated player and ball positions at ${clock(current.state.frame.clockSeconds)}`}><g fill="none" stroke="#c1dfcc" strokeWidth={pitchWidthMetres * .005}><rect width={pitchLengthMetres} height={pitchWidthMetres}/><path d={`M${pitchLengthMetres / 2} 0v${pitchWidthMetres}`}/><circle cx={pitchLengthMetres / 2} cy={pitchWidthMetres / 2} r="9.15"/><rect data-marking="penalty-left" x="0" y={(pitchWidthMetres - penaltyAreaWidth) / 2} width={penaltyAreaDepth} height={penaltyAreaWidth}/><rect data-marking="penalty-right" x={pitchLengthMetres - penaltyAreaDepth} y={(pitchWidthMetres - penaltyAreaWidth) / 2} width={penaltyAreaDepth} height={penaltyAreaWidth}/><rect data-marking="goal-left" x="0" y={(pitchWidthMetres - goalAreaWidth) / 2} width={goalAreaDepth} height={goalAreaWidth}/><rect data-marking="goal-right" x={pitchLengthMetres - goalAreaDepth} y={(pitchWidthMetres - goalAreaWidth) / 2} width={goalAreaDepth} height={goalAreaWidth}/></g>{current.state.frame.players.map(position => <g key={position.playerId}><circle cx={position.x} cy={position.y} r={pitchWidthMetres * .028} fill={position.teamId === fixture.home.id ? '#fa697b' : '#6ba5ff'}/><text x={position.x} y={position.y + pitchWidthMetres * .0125} textAnchor="middle">{[...fixture.home.players, ...fixture.away.players].find(p => p.id === position.playerId)?.shirtNumber}</text></g>)}<circle cx={current.state.frame.ballX} cy={current.state.frame.ballY} r={pitchWidthMetres * .013} fill="white" stroke="#07131c" strokeWidth={pitchWidthMetres * .0044}/></svg>}<div className="legend"><span><i className="dot home"/>{fixture.home.name}</span><span><i className="dot away"/>{fixture.away.name}</span><span>○ Ball</span><span>{pitchLengthMetres} × {pitchWidthMetres} m · fixture coordinates</span></div></section>
    <section className="panel"><h2>Match Timeline</h2><p>Choose an event to inspect its moment</p><div className="timeline">{current?.events.slice(-20).reverse().map(event => <button className="event" key={event.event.eventId} onClick={() => {setPlaying(false); selectTime(event.event.clockSeconds);}}><span className="minute">{clock(event.event.clockSeconds)}</span><span><strong>{event.action.replaceAll('-', ' ')}</strong><small>{event.teamId === fixture.home.id ? fixture.home.name : event.teamId === fixture.away.id ? fixture.away.name : 'Match'} · {event.outcome}</small></span></button>)}{!current?.events.length && <p className="empty">No events available at this moment.</p>}</div></section>
    <div className="side"><section className="panel"><h2>Why it matters</h2><span className="badge">{insight?.classification ?? 'Awaiting evidence'}</span><p>{current?.insights.at(-1)?.summary ?? 'Replay the fixture to discover a supported pressing sequence. No tactical conclusion is assumed before it occurs.'}</p>{evidence && <><h3>Evidence</h3><ul className="evidence-list">{evidence.conditions.map(condition => <li key={condition}>{condition}</li>)}</ul><p className="badge">{evidence.events.length} source events · {evidence.frames.length} tracking frames</p><details><summary>Source IDs and limitations</summary><ul className="evidence-list">{evidence.insight.eventIds.map(id => <li key={id}>{id}</li>)}{evidence.limitations.map(item => <li key={item}>{item}</li>)}</ul></details></>}</section>
    <section className="panel"><h2>Match Analyst</h2><p>Explain the selected insight with the same evidence.</p><div className="mode">{['fan','analyst'].map(mode => <button key={mode} className={mode === audience ? 'active' : ''} aria-pressed={mode === audience} onClick={() => {contextVersion.current++; setAudience(mode); setExplanation(null);}}>{mode === 'fan' ? 'Fan' : 'Analyst'}</button>)}</div><button disabled={!current?.insights.length || busy} onClick={askAnalyst}>{busy ? 'Loading…' : 'Explain this moment →'}</button>{explanation && <><p>{explanation.text}</p><p className="badge">{explanation.source} · {clock(explanation.clockSeconds)}</p><p>{explanation.limitations.join(' ')}</p></>}</section></div></div>
    <section className="panel replay"><div className="replay-head"><div><h2>Match Replay</h2><p>Every view follows the selected timestamp. Playback advances {speed} match seconds per second.</p></div><select aria-label="Simulation playback speed" value={speed} onChange={e => setSpeed(Number(e.target.value))}><option value="1">1× speed</option><option value="15">15× speed</option><option value="60">60× speed</option></select><button className={playing ? 'active' : ''} onClick={() => {contextVersion.current++; setEvidence(null); setExplanation(null); setPlaying(!playing);}} disabled={seconds >= fixture.durationSeconds}>{seconds >= fixture.durationSeconds ? 'Simulation finished' : playing ? 'Pause' : 'Play simulation'}</button></div><input aria-label="Selected match timestamp in seconds" type="range" min="0" max={fixture.durationSeconds} value={seconds} onChange={e => {setPlaying(false); selectTime(Number(e.target.value));}}/><div className="bounds"><span>Kick-off · 00:00</span><span aria-live="off">Selected {clock(seconds)}</span><span>Full time · {clock(fixture.durationSeconds)}</span></div></section>
    <footer>Synthetic fixture {fixture.version} · Seed {fixture.seed}. All displayed football facts come from the match API. Tracking frames are sampled; positions do not imply an unobserved tactical intent.</footer></>}
  </>;
}

export function App() {
  const location = useSyncExternalStore(subscribeLocation, currentLocation, () => '/');
  const separator = location.indexOf('?');
  const pathname = separator === -1 ? location : location.slice(0, separator);
  const search = separator === -1 ? '' : location.slice(separator);
  const route = resolveRoute(pathname);
  const params = new URLSearchParams(search);
  const matchId = route.kind === 'matches' ? route.matchId ?? params.get('matchId') ?? undefined : params.get('matchId') ?? undefined;
  const selectedSeconds = selectedTimeFromSearch(search);
  const revisionValue = Number(params.get('revision'));
  const revision = Number.isInteger(revisionValue) && revisionValue > 0 ? revisionValue : undefined;
  const matchPath = matchId ? `/matches/${encodeURIComponent(matchId)}` : '/home';
  const links = [
    { label: 'Home', path: '/home', active: route.kind === 'home' },
    { label: 'Match', path: matchPath, active: route.kind === 'matches' || route.kind === 'demo-match' },
    { label: 'Teams', path: '/teams', active: route.kind === 'teams' },
    { label: 'Players', path: '/players', active: route.kind === 'players' },
    { label: 'Insights', path: '/insights', active: route.kind === 'insights' },
    { label: 'AI Analyst', path: '/analyst', active: route.kind === 'analyst' }
  ];
  return <div className="app-shell">
    <a href="#main-content" className="skip-link">Skip to main content</a>
    <header className="site-header">
      <a className="brand" href={contextHref('/home', matchId, selectedSeconds, revision)} aria-label="PitchMind home"><span aria-hidden="true">◈</span> PitchMind</a>
      <span className="tag">{route.kind === 'demo-match' ? 'Simulated match · Demo fixture' : 'Canonical match data · Source labelled'}</span>
    </header>
    <nav className="main-navigation" aria-label="Main navigation">
      {links.map(link => <a key={link.label} href={contextHref(link.path, matchId, selectedSeconds, revision)} aria-current={link.active ? 'page' : undefined}>{link.label}</a>)}
    </nav>
    <main id="main-content" tabIndex={-1}>
      {route.kind === 'matches' && matchId
        ? <CanonicalMatchPage key={`${matchId}:${revision ?? ''}:${route.replay}`} matchId={matchId} selectedSeconds={selectedSeconds} requestedRevision={revision} replay={route.replay} />
        : route.kind === 'demo-match'
          ? <MatchExperience key={`${route.matchId ?? ''}:${route.replay}`} expectedMatchId={route.matchId} initialSeconds={selectedSeconds} replay={route.replay} />
        : route.kind === 'matches'
          ? <RoutePage route={{ kind: 'home' }} search={search} />
        : <RoutePage route={route} search={search} />}
    </main>
  </div>;
}
