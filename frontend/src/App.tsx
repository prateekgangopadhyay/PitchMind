import { useEffect, useRef, useState, useSyncExternalStore } from 'react';
import { clock, DEFAULT_ANALYST_AVATAR, getHostAccessToken, getStoredAuthSession, logoutSimulated, request, subscribeAuth } from './api';
import type { Evidence, Explanation, Fixture, Insight, MatchEvent, Snapshot } from './api';
import { RoutePage } from './ShellPages';
import { CanonicalMatchPage } from './CanonicalMatchPage';
import { RealtimeAuthProvider } from './RealtimeMatchUpdates';
import { contextHref, currentLocation, replaceCurrentMatchContext, resolveRoute, selectedTimeFromSearch, subscribeLocation } from './routes';
import { CandidateLiveMatchPage } from './CandidateLiveMatchPage';
import { toggleCandidateStream, useCandidateStreamStatus } from './candidateLiveStatus';

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
  const location = useSyncExternalStore(subscribeLocation, currentLocation, () => '/home');
  const authSession = useSyncExternalStore(subscribeAuth, getStoredAuthSession, () => null);
  const [showUserMenu, setShowUserMenu] = useState(false);

  const separator = location.indexOf('?');
  const pathname = separator === -1 ? location : location.slice(0, separator);
  const search = separator === -1 ? '' : location.slice(separator);
  const route = resolveRoute(pathname);
  const candidateStream = useCandidateStreamStatus(route.kind !== 'welcome');
  const params = new URLSearchParams(search);
  const matchId = route.kind === 'matches' ? route.matchId ?? params.get('matchId') ?? undefined : params.get('matchId') ?? undefined;
  const selectedSeconds = params.has('atSeconds') ? selectedTimeFromSearch(search) : undefined;
  const selectedAtSeconds = selectedSeconds ?? (route.kind === 'matches' ? 5400 : 0);
  const realtimeMode = params.get('mode') === 'live' || selectedSeconds === undefined ? 'LIVE' : 'HISTORICAL';
  const revisionValue = Number(params.get('revision'));
  const revision = Number.isInteger(revisionValue) && revisionValue > 0 ? revisionValue : undefined;
  const links = [
    { label: 'Home', path: '/home', active: route.kind === 'home' },
    { label: 'Match', path: '/candidate-live', active: route.kind === 'candidate-live' || route.kind === 'matches' || route.kind === 'demo-match' },
    { label: 'Teams', path: '/teams', active: route.kind === 'teams' },
    { label: 'Players', path: '/players', active: route.kind === 'players' },
    { label: 'Insights', path: '/insights', active: route.kind === 'insights' },
    { label: 'AI Analyst', path: '/analyst', active: route.kind === 'analyst' }
  ];
  const contextMode = realtimeMode === 'LIVE' ? 'live' : undefined;

  const activeUser = authSession?.user ?? {
    name: 'Prateek Gangopadhyay',
    email: 'prateek.gangopadhyay@gmail.com',
    role: 'Lead Tactical Analyst',
    permissions: ['MatchRead', 'MatchReplay', 'AnalystAction'],
    avatarUrl: DEFAULT_ANALYST_AVATAR,
    provider: 'google' as const
  };

  return <div className="app-shell">
    <a href="#main-content" className="skip-link">Skip to main content</a>
    <header className="site-header">
      <div className="header-brand-nav-group">
        <a className="brand" href={contextHref('/home', matchId, selectedSeconds, revision, contextMode)} aria-label="PitchMind home">
          <img className="brand-logo-image" src="/brand/pitchmind-symbol.svg" width="32" height="32" alt="" aria-hidden="true" />
          <span className="brand-name">PitchMind</span>
        </a>
        {route.kind !== 'welcome' && (
          <nav className="main-navigation" aria-label="Main navigation">
            {links.map(link => <a key={link.label} href={contextHref(link.path, matchId, selectedSeconds, revision, contextMode)} aria-current={link.active ? 'page' : undefined}>{link.label}</a>)}
          </nav>
        )}
      </div>
      {route.kind === 'welcome' ? (
        <div className="welcome-header-auth-cta">
          <span className="new-to-pitchmind">New to PitchMind?</span>
          <span role="button" tabIndex={0} className="btn-request-access">Request access</span>
        </div>
      ) : (
        <div className="header-right-controls">
          <span role="button" tabIndex={0} className="header-icon-btn" aria-label="Search" title="Search">
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/></svg>
          </span>
          <div className="league-selector-pill">
            <svg className="league-crest-symbol-svg" width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="#4aefac" strokeWidth="2.2" aria-hidden="true">
              <path d="M6 9H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h2M18 9h2a2 2 0 0 0 2-2V5a2 2 0 0 0-2-2h-2M4 22h16M10 14.66V17c0 .55-.45 1-1 1H7v2h10v-2h-2c-.55 0-1-.45-1-1v-2.34M12 2a8 8 0 0 0-8 8c0 4.42 5 7.6 8 10 3-2.4 8-5.58 8-10a8 8 0 0 0-8-8z"/>
            </svg>
            <span className="league-name-text">Premier League</span>
            <span className="dropdown-caret">▾</span>
          </div>
          <button type="button" className={`live-status-pill ${candidateStream.status?.isStreaming ? 'is-streaming' : ''}`}
            aria-pressed={candidateStream.status?.isStreaming ?? false}
            aria-label={candidateStream.status?.isStreaming ? 'Pause candidate live simulation' : 'Resume candidate live simulation'}
            title={candidateStream.error || (candidateStream.status?.isStreaming ? 'Pause live simulation' : 'Resume live simulation')}
            disabled={candidateStream.pending || !candidateStream.status?.matchId}
            onClick={() => { void toggleCandidateStream(); }}>
            <span className="live-status-dot" aria-hidden="true"></span>
            <span>{candidateStream.status?.isStreaming ? 'Live · Pause' : candidateStream.status ? 'Live · Resume' : 'Live unavailable'}</span>
          </button>
          <span role="button" tabIndex={0} className="header-icon-btn" aria-label="Settings" title="Settings">
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"/></svg>
          </span>

          {/* Logged in analyst user info with profile picture in right corner */}
          <div className="header-user-profile-group">
            <div className="header-user-info-text">
              <span className="header-user-name">{activeUser.name}</span>
              <span className="header-user-role">{activeUser.role}</span>
            </div>
            <div
              className="header-user-avatar-btn"
              role="button"
              tabIndex={0}
              title={`${activeUser.name} · ${activeUser.role} (${activeUser.email})`}
              onClick={() => setShowUserMenu(!showUserMenu)}
              onKeyDown={e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); setShowUserMenu(!showUserMenu); } }}
              aria-haspopup="true"
              aria-expanded={showUserMenu}
            >
              <img
                src={activeUser.avatarUrl || DEFAULT_ANALYST_AVATAR}
                alt={`${activeUser.name} profile`}
                className="header-user-avatar-image"
              />
              <span className="user-online-pip" aria-hidden="true"></span>
            </div>

            {showUserMenu && (
              <div className="header-user-popover-menu" role="menu">
                <div className="popover-user-summary">
                  <img src={activeUser.avatarUrl || DEFAULT_ANALYST_AVATAR} alt="" className="popover-avatar" />
                  <div className="popover-meta">
                    <strong>{activeUser.name}</strong>
                    <small>{activeUser.email}</small>
                    <span className="popover-provider-pill">{activeUser.provider === 'google' ? 'Google Account' : 'Analyst Account'}</span>
                  </div>
                </div>
                <div className="popover-menu-divider"></div>
                <button
                  type="button"
                  className="popover-signout-btn"
                  onClick={() => {
                    setShowUserMenu(false);
                    logoutSimulated();
                    window.history.pushState({}, '', '/welcome');
                    window.dispatchEvent(new CustomEvent('pitchmind:locationchange'));
                  }}
                >
                  Sign out
                </button>
              </div>
            )}
          </div>
        </div>
      )}
      <DeploymentVersionBanner />
      <span className="tag sr-only">{route.kind === 'demo-match' ? 'Simulated match · Demo fixture' : 'Match data with source details'}</span>
    </header>
    <main id="main-content" tabIndex={-1}>
      {route.kind === 'candidate-live' ? <CandidateLiveMatchPage />
      : route.kind === 'matches' && matchId && !matchId.startsWith('sim-')
        ? <RealtimeAuthProvider getAccessToken={getHostAccessToken}>
          {/* Preserve replay state as the selected revision changes between seconds. */}
          <CanonicalMatchPage key={`${matchId}:${route.replay}`} matchId={matchId} selectedSeconds={selectedAtSeconds}
            requestedRevision={revision} replay={route.replay} initialMode={realtimeMode} />
        </RealtimeAuthProvider>
        : route.kind === 'demo-match'
          ? <MatchExperience key={`${route.matchId ?? ''}:${route.replay}`} expectedMatchId={route.matchId} initialSeconds={selectedAtSeconds} replay={route.replay} />
        : <RoutePage route={route} search={search} />}
    </main>
  </div>;
}

function DeploymentVersionBanner() {
  const revision = import.meta.env.VITE_DEPLOYMENT_VERSION?.trim() || 'local';
  const buildId = import.meta.env.VITE_BUILD_ID?.trim();
  const label = revision === 'local' ? 'Local · unversioned' : `${revision.slice(0, 12)}${buildId ? ` · build ${buildId}` : ''}`;
  const details = `Deployment version ${revision}${buildId ? `, build ${buildId}` : ''}`;

  return <div className="deployment-version-banner" role="group" aria-label={details} title={details}>
    <span aria-hidden="true">Deployment</span>
    <code>{label}</code>
  </div>;
}
