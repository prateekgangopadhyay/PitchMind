import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import type { MouseEvent } from 'react';
import { clock, request } from './api';
import type { Evidence, Explanation, Fixture, Insight, MatchEvent, Snapshot } from './api';

const sections = [
  { path: '/home', label: 'Home', question: 'What can I follow now?' },
  { path: '/match', label: 'Match', question: 'How is this match being played?' },
  { path: '/teams', label: 'Teams', question: 'What supports a team’s performance?' },
  { path: '/players', label: 'Players', question: 'What is a player contributing?' },
  { path: '/insights', label: 'Insights', question: 'Why does this moment matter?' },
  { path: '/analyst', label: 'Analyst', question: 'What do you want to understand?' },
] as const;

type SectionPath = typeof sections[number]['path'];

function routeFor(pathname: string): typeof sections[number] | null {
  const path = pathname.split('?')[0].replace(/\/$/, '') || '/match';
  return sections.find(section => section.path === path) ?? null;
}

function routeFromLocation(): SectionPath | null {
  const routePath = window.location.hash.startsWith('#/') ? window.location.hash.slice(1) : window.location.pathname;
  return routeFor(routePath)?.path ?? null;
}

export function App() {
  const contextVersion = useRef(0);
  const previousRoute = useRef<SectionPath | null | undefined>(undefined);
  const headingRef = useRef<HTMLHeadingElement>(null);
  const [pathname, setPathname] = useState<SectionPath | null>(() => typeof window === 'undefined' ? '/match' : routeFromLocation());
  const [retryVersion, setRetryVersion] = useState(0);
  const [fixtures, setFixtures] = useState<Fixture[]>([]);
  const [matchId, setMatchId] = useState('');
  const [seconds, setSeconds] = useState(0);
  const [speed, setSpeed] = useState(15);
  const [playing, setPlaying] = useState(false);
  const [data, setData] = useState<{state: Snapshot; events: MatchEvent[]; insights: Insight[]} | null>(null);
  const [evidence, setEvidence] = useState<Evidence | null>(null);
  const [explanation, setExplanation] = useState<Explanation | null>(null);
  const [audience, setAudience] = useState('fan');
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);
  const [loadingFixtures, setLoadingFixtures] = useState(true);
  const [failedAction, setFailedAction] = useState<'evidence' | 'analyst' | null>(null);
  const fixture = fixtures.find(f => f.matchId === matchId);
  const section = pathname ? routeFor(pathname) : null;
  const isMatchRoute = section?.path === '/match';
  useEffect(() => {
    const syncHistoryRoute = () => setPathname(routeFromLocation());
    const syncHashRoute = () => setPathname(routeFromLocation());
    window.addEventListener('popstate', syncHistoryRoute);
    window.addEventListener('hashchange', syncHashRoute);
    return () => {
      window.removeEventListener('popstate', syncHistoryRoute);
      window.removeEventListener('hashchange', syncHashRoute);
    };
  }, []);
  useLayoutEffect(() => {
    if (previousRoute.current !== undefined && previousRoute.current !== pathname) headingRef.current?.focus();
    previousRoute.current = pathname;
  }, [pathname]);
  useEffect(() => {
    const controller = new AbortController();
    setLoadingFixtures(true);
    request<Fixture[]>('matches', controller.signal)
      .then(items => { setFixtures(items); setMatchId(items[0]?.matchId ?? ''); setError(''); })
      .catch(() => { if (!controller.signal.aborted) { setFixtures([]); setMatchId(''); setError('unavailable'); } })
      .finally(() => { if (!controller.signal.aborted) setLoadingFixtures(false); });
    return () => controller.abort();
  }, [retryVersion]);
  useEffect(() => {
    if (!matchId) return;
    const controller = new AbortController();
    Promise.all([request<Snapshot>(`matches/${matchId}/state?atSeconds=${seconds}`, controller.signal), request<MatchEvent[]>(`matches/${matchId}/events?atSeconds=${seconds}`, controller.signal), request<Insight[]>(`matches/${matchId}/insights?atSeconds=${seconds}`, controller.signal)])
      .then(([state, events, insights]) => {if (controller.signal.aborted) return; setData({state, events, insights}); setError('');})
      .catch(() => { if (!controller.signal.aborted) {setData(null); setError('unavailable'); setPlaying(false);} });
    return () => controller.abort();
  }, [matchId, seconds]);
  useEffect(() => {
    if (!playing || !fixture || seconds >= fixture.durationSeconds) return;
    const timer = setInterval(() => {
      setSeconds(previous => Math.min(previous + speed, fixture.durationSeconds));
    }, 1000);
    return () => clearInterval(timer);
  }, [playing, fixture, speed, seconds]);
  const selectTime = (time: number) => {contextVersion.current++; setSeconds(time); setEvidence(null); setExplanation(null); setFailedAction(null); setError(currentError => currentError === 'action-unavailable' ? '' : currentError);};
  const current = data?.state.clockSeconds === seconds ? data : null;
  const insight = current?.insights.at(-1);
  function retryFixtures() {
    setLoadingFixtures(true);
    setFixtures([]);
    setMatchId('');
    setData(null);
    setError('');
    setRetryVersion(value => value + 1);
  }
  async function showEvidence() {
    if (!insight) return;
    setPlaying(false); setBusy(true); setError(''); setFailedAction(null);
    const version = contextVersion.current;
    try {const result = await request<Evidence>(`matches/${matchId}/evidence/${insight.id}?atSeconds=${seconds}`); if (version === contextVersion.current) {setEvidence(result); setError('');}}
    catch {if (version === contextVersion.current) {setError('action-unavailable'); setFailedAction('evidence');}}
    finally {setBusy(false);}
  }
  async function askAnalyst() {
    if (!insight) return;
    setPlaying(false); setBusy(true); setError(''); setFailedAction(null);
    const version = contextVersion.current;
    try {const result = await request<Explanation>(`matches/${matchId}/analyst`, undefined, {atSeconds: seconds, insightId: insight.id, audience}); if (version === contextVersion.current) {setExplanation(result); setError('');}}
    catch {if (version === contextVersion.current) {setError('action-unavailable'); setFailedAction('analyst');}}
    finally {setBusy(false);}
  }
  function skipToMain(event: MouseEvent<HTMLAnchorElement>) {
    event.preventDefault();
    document.getElementById('main-content')?.focus();
  }
  function navigate(event: MouseEvent<HTMLAnchorElement>, path: SectionPath) {
    if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
    event.preventDefault();
    window.history.pushState({}, '', `#${path}`);
    setPathname(path);
  }

  return <><a href="#main-content" className="skip-link" onClick={skipToMain}>Skip to main content</a><header className="app-header"><a className="brand" href="#/home" onClick={event => navigate(event, '/home')}><span aria-hidden="true">◈</span> PitchMind</a><span className="tag">Simulated matches</span></header><nav className="primary-nav" aria-label="Primary navigation">{sections.map(item => <a key={item.path} href={`#${item.path}`} onClick={event => navigate(event, item.path)} aria-current={section?.path === item.path ? 'page' : undefined}>{item.label}</a>)}</nav><main id="main-content" tabIndex={-1}>
    <div className="intro"><div><p className="eyebrow">Football intelligence, grounded in evidence</p><h1 ref={headingRef} tabIndex={-1}>{section?.question ?? 'This page isn’t available'}</h1></div>{isMatchRoute && fixtures.length > 0 && <select aria-label="Select simulated match" value={matchId} onChange={e => {setMatchId(e.target.value); selectTime(0); setPlaying(false);}}>{fixtures.map(f => <option key={f.matchId} value={f.matchId}>{f.home.name} vs {f.away.name}</option>)}</select>}</div>
    {!section && <section className="route-state" aria-labelledby="route-state-title"><div className="route-mark" aria-hidden="true">—</div><p className="eyebrow">Page unavailable</p><h2 id="route-state-title">This PitchMind page could not be found</h2><p>The address may be outdated or mistyped. Use Home to return to the available match views.</p><a className="primary-action" href="#/home" onClick={event => navigate(event, '/home')}>Go to Home</a></section>}
    {section && !isMatchRoute && <section className="route-state" aria-labelledby="route-state-title"><div className="route-mark" aria-hidden="true">—</div><p className="eyebrow">{section.label}</p><h2 id="route-state-title">{loadingFixtures ? 'Finding a simulated match' : fixtures.length ? 'Choose a match to begin' : 'Match data is not available yet'}</h2><p>{loadingFixtures ? 'Match details will appear when a fixture is available.' : fixtures.length ? 'Open Match to inspect the selected fixture. This view will follow the same match context when its data is available.' : 'Scores, events and analysis stay hidden until a simulated match can be loaded.'}</p>{!loadingFixtures && <a className="primary-action" href="#/match" onClick={event => navigate(event, '/match')}>{fixtures.length ? 'Open match' : 'Check match availability'}</a>}</section>}
    {isMatchRoute && loadingFixtures && <div role="status" className="availability-state"><span className="status-mark" aria-hidden="true"/><div><h2>Finding a simulated match</h2><p>Match details will appear when a fixture is available.</p></div></div>}
    {isMatchRoute && error === 'unavailable' && <section role="alert" className="availability-state unavailable"><span className="status-mark" aria-hidden="true"/><div><p className="eyebrow">Match availability</p><h2>Match data can’t be loaded right now</h2><p>Scores, events and analysis stay hidden until match data is available.</p><button onClick={retryFixtures}>Try again</button></div></section>}
    {isMatchRoute && error === 'action-unavailable' && <div role="alert" className="notice error">This part of the match isn’t available right now. <button onClick={() => {if (failedAction === 'evidence') void showEvidence(); else if (failedAction === 'analyst') void askAnalyst();}}>Try again</button></div>}
    {isMatchRoute && !fixture && !error && !loadingFixtures && <div role="status" className="availability-state"><span className="status-mark" aria-hidden="true"/><div><h2>No simulated matches are available</h2><p>Try again later to check for a fixture.</p><button onClick={retryFixtures}>Try again</button></div></div>}
    {isMatchRoute && fixture && error !== 'unavailable' && <><section className="scoreboard" aria-label="Match score"><div className="team">{fixture.home.name}<p>{fixture.home.formation}</p></div><span className="score">{current?.state.homeScore ?? '—'}</span><div className="clock">{clock(seconds)}<small>{current ? current.state.status : 'Loading selected moment…'}</small></div><span className="score">{current?.state.awayScore ?? '—'}</span><div className="team">{fixture.away.name}<p>{fixture.away.formation}</p></div></section>
    <section className="story"><div><span className="badge">{current?.insights.at(-1)?.classification ?? 'Simulated telemetry'}</span><p>{current?.insights.at(-1)?.title ?? 'Watch the match unfold. Tactical insights appear when the evidence supports them.'}</p></div><button disabled={!current?.insights.length || busy} onClick={showEvidence}>Show me why →</button></section>
    <div className="dashboard"><section className="panel"><div className="panel-head"><h2>Intelligent Pitch</h2><p>Sampled tracking frame {current ? clock(current.state.frame.clockSeconds) : 'loading…'} · Selected {clock(seconds)}</p></div>{current && <svg className="pitch" viewBox={`-3 -3 ${pitchLength + 6} ${pitchWidth + 6}`} role="img" aria-label={`Simulated player and ball positions at ${clock(current.state.frame.clockSeconds)}`}><g fill="none" stroke="#c1dfcc" strokeWidth={pitchWidth * .005}><rect width={pitchLength} height={pitchWidth}/><path d={`M${pitchLength / 2} 0v${pitchWidth}`}/><circle cx={pitchLength / 2} cy={pitchWidth / 2} r="9.15"/><rect data-marking="penalty-left" x="0" y={(pitchWidth - penaltyAreaWidth) / 2} width={penaltyAreaDepth} height={penaltyAreaWidth}/><rect data-marking="penalty-right" x={pitchLength - penaltyAreaDepth} y={(pitchWidth - penaltyAreaWidth) / 2} width={penaltyAreaDepth} height={penaltyAreaWidth}/><rect data-marking="goal-left" x="0" y={(pitchWidth - goalAreaWidth) / 2} width={goalAreaDepth} height={goalAreaWidth}/><rect data-marking="goal-right" x={pitchLength - goalAreaDepth} y={(pitchWidth - goalAreaWidth) / 2} width={goalAreaDepth} height={goalAreaWidth}/></g>{current.state.frame.players.map(position => <g key={position.playerId}><circle cx={position.x} cy={position.y} r={pitchWidth * .028} fill={position.teamId === fixture.home.id ? '#fa697b' : '#6ba5ff'}/><text x={position.x} y={position.y + pitchWidth * .0125} textAnchor="middle">{[...fixture.home.players, ...fixture.away.players].find(p => p.id === position.playerId)?.shirtNumber}</text></g>)}<circle cx={current.state.frame.ballX} cy={current.state.frame.ballY} r={pitchWidth * .013} fill="white" stroke="#07131c" strokeWidth={pitchWidth * .0044}/></svg>}<div className="legend"><span><i className="dot home"/>{fixture.home.name}</span><span><i className="dot away"/>{fixture.away.name}</span><span>○ Ball</span><span>{fixture.pitchLengthMetres} × {fixture.pitchWidthMetres} m · fixed stadium coordinates</span></div></section>
    <section className="panel"><h2>Match Timeline</h2><p>Choose an event to inspect its moment</p><div className="timeline">{current?.events.slice(-20).reverse().map(event => <button className="event" key={event.event.eventId} onClick={() => {setPlaying(false); selectTime(event.event.clockSeconds);}}><span className="minute">{clock(event.event.clockSeconds)}</span><span><strong>{event.action.replaceAll('-', ' ')}</strong><small>{event.teamId === fixture.home.id ? fixture.home.name : event.teamId === fixture.away.id ? fixture.away.name : 'Match'} · {event.outcome}</small></span></button>)}{!current?.events.length && <p className="empty">No events available at this moment.</p>}</div></section>
    <div className="side"><section className="panel"><h2>Why it matters</h2><span className="badge">{insight?.classification ?? 'Awaiting evidence'}</span><p>{current?.insights.at(-1)?.summary ?? 'Replay the fixture to discover a supported pressing sequence. No tactical conclusion is assumed before it occurs.'}</p>{evidence && <><h3>Evidence</h3><ul className="evidence-list">{evidence.conditions.map(condition => <li key={condition}>{condition}</li>)}</ul><p className="badge">{evidence.events.length} source events · {evidence.frames.length} tracking frames</p><details><summary>Source IDs and limitations</summary><ul className="evidence-list">{evidence.insight.eventIds.map(id => <li key={id}>{id}</li>)}{evidence.limitations.map(item => <li key={item}>{item}</li>)}</ul></details></>}</section>
    <section className="panel"><h2>Match Analyst</h2><p>Explain the selected insight with the same evidence.</p><div className="mode">{['fan','analyst'].map(mode => <button key={mode} className={mode === audience ? 'active' : ''} aria-pressed={mode === audience} onClick={() => {contextVersion.current++; setAudience(mode); setExplanation(null);}}>{mode === 'fan' ? 'Fan' : 'Analyst'}</button>)}</div><button disabled={!current?.insights.length || busy} onClick={askAnalyst}>{busy ? 'Loading…' : 'Explain this moment →'}</button>{explanation && <><p>{explanation.text}</p><p className="badge">{explanation.source} · {clock(explanation.clockSeconds)}</p><p>{explanation.limitations.join(' ')}</p></>}</section></div></div>
    <section className="panel replay"><div className="replay-head"><div><h2>Match Replay</h2><p>Every view follows the selected timestamp. Playback advances {speed} match seconds per second.</p></div><select aria-label="Simulation playback speed" value={speed} onChange={e => setSpeed(Number(e.target.value))}><option value="1">1× speed</option><option value="15">15× speed</option><option value="60">60× speed</option></select><button className={playing ? 'active' : ''} onClick={() => {contextVersion.current++; setEvidence(null); setExplanation(null); setPlaying(!playing);}} disabled={seconds >= fixture.durationSeconds}>{seconds >= fixture.durationSeconds ? 'Simulation finished' : playing ? 'Pause' : 'Play simulation'}</button></div><input aria-label="Selected match timestamp in seconds" type="range" min="0" max={fixture.durationSeconds} value={seconds} onChange={e => {setPlaying(false); selectTime(Number(e.target.value));}}/><div className="bounds"><span>Kick-off · 00:00</span><span aria-live="off">Selected {clock(seconds)}</span><span>Full time · {clock(fixture.durationSeconds)}</span></div></section>
    <footer>Synthetic fixture {fixture.version} · Seed {fixture.seed}. All displayed football facts come from the match API. Tracking frames are sampled; positions do not imply an unobserved tactical intent.</footer></>}
  </main></>;
}
