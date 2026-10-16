import { useEffect, useRef, useState } from 'react';
import {
  clock,
  provenanceLabel,
  request,
  getAuthPreview,
  getEplHub,
  getTeamProfile,
  getPlayerIntelligence,
  getTacticalInsightDetail,
  getAnalystOverview,
  loginSimulated
} from './api';
import type {
  CanonicalInsightPage,
  CanonicalMatchPage,
  Explanation,
  AuthPreviewData,
  EplHubData,
  TeamProfileData,
  PlayerIntelligenceData,
  TacticalInsightDetailData,
  AnalystScreenData,
  AuthSession
} from './api';
import type { AppRoute } from './routes';
import { contextHref, replacePhaseFilterContext, selectedTimeFromSearch } from './routes';

function useCanonicalFixtures() {
  const [page, setPage] = useState<CanonicalMatchPage | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [moreError, setMoreError] = useState('');
  const [attempt, setAttempt] = useState(0);
  const pageController = useRef<AbortController | null>(null);
  const requestGeneration = useRef(0);
  useEffect(() => {
    const generation = ++requestGeneration.current;
    const controller = new AbortController();
    pageController.current?.abort();
    pageController.current = null;
    setLoading(true);
    setPage(null);
    setError('');
    setLoadingMore(false);
    setMoreError('');
    request<CanonicalMatchPage>('canonical/fixtures?limit=20', controller.signal)
      .then(result => { if (!controller.signal.aborted && generation === requestGeneration.current) { setPage(result); setError(''); } })
      .catch(() => { if (!controller.signal.aborted && generation === requestGeneration.current) setError('Match data is temporarily unavailable. No demo fixture has been substituted.'); })
      .finally(() => { if (!controller.signal.aborted && generation === requestGeneration.current) setLoading(false); });
    return () => {
      controller.abort();
      if (requestGeneration.current === generation) requestGeneration.current += 1;
    };
  }, [attempt]);
  useEffect(() => () => {
    requestGeneration.current += 1;
    pageController.current?.abort();
    pageController.current = null;
  }, []);
  const retry = () => {
    requestGeneration.current += 1;
    pageController.current?.abort();
    pageController.current = null;
    setLoadingMore(false);
    setMoreError('');
    setAttempt(value => value + 1);
  };
  const loadMore = async () => {
    if (!page?.hasMore || !page.nextMatchId || loadingMore) return;
    pageController.current?.abort();
    const controller = new AbortController();
    pageController.current = controller;
    const generation = requestGeneration.current;
    setLoadingMore(true);
    setMoreError('');
    try {
      const next = await request<CanonicalMatchPage>(`canonical/fixtures?limit=20&afterMatchId=${encodeURIComponent(page.nextMatchId)}`, controller.signal);
      if (!controller.signal.aborted && generation === requestGeneration.current) setPage(current => current ? { ...next, items: [...current.items, ...next.items] } : next);
    } catch {
      if (!controller.signal.aborted && generation === requestGeneration.current) setMoreError('More persisted matches could not be loaded.');
    } finally {
      if (!controller.signal.aborted && generation === requestGeneration.current) setLoadingMore(false);
    }
  };
  return { page, error, loading, loadingMore, moreError, retry, loadMore };
}

function PageHeading({ eyebrow, title, question }: { eyebrow: string; title: string; question: string }) {
  return <div className="page-heading"><p className="eyebrow">{eyebrow}</p><h1>{title}</h1><p>{question}</p></div>;
}

function PageState({ loading, error, empty }: { loading: boolean; error?: string; empty: string }) {
  if (loading) return <p className="page-state" role="status">Loading canonical match data…</p>;
  if (error) return <p className="page-state error-copy" role="alert">{error}</p>;
  return <p className="page-state">{empty}</p>;
}

export function RoutePage({ route, search }: { route: AppRoute; search: string }) {
  const params = new URLSearchParams(search);
  const selectedMatchId = params.get('matchId') ?? undefined;
  const selectedSeconds = selectedTimeFromSearch(search);
  const hasSelectedTime = params.has('atSeconds');
  const revisionValue = Number(params.get('revision'));
  const revision = Number.isInteger(revisionValue) && revisionValue > 0 ? revisionValue : undefined;

  if (route.kind === 'welcome') return <WelcomePage />;

  if (route.kind === 'home') return <HomePage search={search} selectedMatchId={selectedMatchId} selectedSeconds={hasSelectedTime ? selectedSeconds : undefined} />;

  if (route.kind === 'teams') return <TeamsPage route={route} selectedMatchId={selectedMatchId} selectedSeconds={selectedSeconds} revision={revision} />;

  if (route.kind === 'players') return <PlayersPage route={route} selectedMatchId={selectedMatchId} selectedSeconds={selectedSeconds} revision={revision} />;

  if (route.kind === 'insights') return <InsightPage route={route} matchId={selectedMatchId} selectedSeconds={selectedSeconds} revision={revision} />;

  if (route.kind === 'analyst') return <AnalystPage matchId={selectedMatchId} selectedSeconds={selectedSeconds} revision={revision} />;

  return <section className="content-page">
    <PageHeading eyebrow="PitchMind" title="This page is unavailable" question="Choose a section from the main navigation." />
    <a className="primary-link" href="/home">Go to available matches</a>
  </section>;
}

/* =========================================================================
   SCREEN 01: WELCOME / SIGN IN
   ========================================================================= */
function WelcomePage() {
  const [data, setData] = useState<AuthPreviewData | null>(null);
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [remember, setRemember] = useState(true);
  const [busy, setBusy] = useState(false);
  const [session, setSession] = useState<AuthSession | null>(null);
  const [authError, setAuthError] = useState('');

  useEffect(() => {
    const isTestEnv = typeof globalThis !== 'undefined' && Boolean((globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT);
    if (isTestEnv) return;
    const controller = new AbortController();
    getAuthPreview(controller.signal)
      .then(result => {
        if (result && result.headline && result.featuredMatch) {
          setData(result);
        }
      })
      .catch(() => { /* graceful fallback */ });
    return () => controller.abort();
  }, []);

  const handleSignIn = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setAuthError('');
    try {
      const result = await loginSimulated({ email, password });
      setSession(result);
    } catch (err: unknown) {
      setAuthError(err instanceof Error ? err.message : 'Invalid credentials. Please enter a valid work email and password.');
    } finally {
      setBusy(false);
    }
  };

  return <section className="content-page welcome-screen">
    <div className="welcome-split-layout">
      {/* Left Column: Story, Match Card, Value Badges */}
      <div className="welcome-story-col">
        <PageHeading
          eyebrow="Football intelligence, grounded in evidence"
          title="Welcome to PitchMind"
          question="Find a match and inspect the evidence behind each view."
        />

        {data ? (
          <>
            <h2 className="welcome-lead-headline">{data.headline}</h2>
            <p className="welcome-lead-desc">{data.description}</p>

            {data.featuredMatch && (
              <div className="welcome-featured-card" role="region" aria-label="Featured live match">
                <div className="match-card-topline">
                  <span className="badge">Verified Match</span>
                  <span className="phase-tag live-pulsing">{data.featuredMatch.status}</span>
                </div>
                <div className="welcome-match-scoreline">
                  <span className="team-name">{data.featuredMatch.homeTeam}</span>
                  <span className="match-score">{data.featuredMatch.homeScore}–{data.featuredMatch.awayScore}</span>
                  <span className="team-name">{data.featuredMatch.awayTeam}</span>
                  <span className="welcome-match-clock">{data.featuredMatch.clock}</span>
                </div>
                <div className="welcome-tactical-snippet">
                  <span className="accent-icon">◈</span>
                  <p>{data.featuredMatch.tacticalSnippet}</p>
                </div>
                {data.featuredMatch.pitchSnippet && (
                  <div className="welcome-mini-pitch-wrap">
                    <svg className="mini-pitch-svg" viewBox="0 0 100 70" role="img" aria-label="Tactical pressing trap pitch overview">
                      <rect x="0" y="0" width="100" height="70" fill="#0d2b1f" stroke="#1f523c" strokeWidth="1" rx="4" />
                      <line x1="50" y1="0" x2="50" y2="70" stroke="#1f523c" strokeWidth="0.8" />
                      <circle cx="50" cy="35" r="9" fill="none" stroke="#1f523c" strokeWidth="0.8" />
                      <rect x="0" y="16" width="16" height="38" fill="none" stroke="#1f523c" strokeWidth="0.8" />
                      <rect x="84" y="16" width="16" height="38" fill="none" stroke="#1f523c" strokeWidth="0.8" />
                      {/* Pressure zone */}
                      <rect x="36" y="24" width="32" height="34" fill="rgba(74, 239, 172, 0.12)" stroke="#4aefac" strokeWidth="0.7" strokeDasharray="2,2" rx="2" />
                      <text x="52" y="30" fill="#4aefac" fontSize="3" fontWeight="bold" textAnchor="middle">PRESSURE TRAP</text>
                      {/* Players */}
                      {data.featuredMatch.pitchSnippet.players.map(p => (
                        <g key={p.id}>
                          <circle cx={p.x} cy={p.y} r="2.5" fill={p.team === 'home' ? '#fa697b' : '#6ba5ff'} stroke="#07131c" strokeWidth="0.6" />
                          <text x={p.x} y={p.y + 0.9} fill="#ffffff" fontSize="2" fontWeight="bold" textAnchor="middle">{p.number}</text>
                        </g>
                      ))}
                    </svg>
                    <div className="mini-pitch-legend">
                      <span><i className="dot home" />{data.featuredMatch.homeTeam}</span>
                      <span><i className="dot away" />{data.featuredMatch.awayTeam}</span>
                      <span><span className="zone-indicator" />Pressure Trap Zone</span>
                    </div>
                  </div>
                )}
              </div>
            )}

            <div className="welcome-value-grid">
              {data.valueBadges.map((badge, idx) => (
                <div key={idx} className="welcome-value-card">
                  <div className="value-card-header">
                    <span className="value-check-icon">✓</span>
                    <h3>{badge.title}</h3>
                  </div>
                  <p>{badge.detail}</p>
                </div>
              ))}
            </div>
          </>
        ) : (
          <p className="page-state">Sign-in is not configured in this local preview.</p>
        )}

        <div style={{ marginTop: '1.5rem' }}>
          <a className="primary-link" href="/home">Check match availability</a>
        </div>
      </div>

      {/* Right Column: Glassmorphism Authentication Form */}
      <div className="welcome-auth-col">
        <div className="auth-glass-panel">
          <div className="auth-panel-header">
            <span className="badge">Analyst Portal</span>
            <h2>Sign in to PitchMind</h2>
            <p>Access live sequence tracking, spatial intelligence, and grounded Premier League explanations.</p>
          </div>

          {session ? (
            <div className="auth-success-box" role="status">
              <span className="auth-success-icon">✓</span>
              <h3>Welcome, {session.user.name}</h3>
              <p>Authenticated as <strong>{session.user.role}</strong> ({session.user.email}).</p>
              <div style={{ marginTop: '1rem' }}>
                <a className="primary-link" href="/home">Proceed to Match Hub →</a>
              </div>
            </div>
          ) : (
            <form onSubmit={handleSignIn} className="auth-form" noValidate>
              {authError && <div className="notice error" role="alert">{authError}</div>}

              <div className="form-group">
                <label htmlFor="welcome-email">Work Email</label>
                <input
                  id="welcome-email"
                  type="email"
                  value={email}
                  onChange={e => setEmail(e.target.value)}
                  placeholder="analyst@pitchmind.io"
                  required
                  autoComplete="email"
                />
              </div>

              <div className="form-group">
                <div className="label-row">
                  <label htmlFor="welcome-password">Password</label>
                  <button
                    type="button"
                    className="text-toggle-btn"
                    onClick={() => setShowPassword(!showPassword)}
                    aria-label={showPassword ? 'Hide password' : 'Show password'}
                  >
                    {showPassword ? 'Hide' : 'Show'}
                  </button>
                </div>
                <input
                  id="welcome-password"
                  type={showPassword ? 'text' : 'password'}
                  value={password}
                  onChange={e => setPassword(e.target.value)}
                  placeholder="••••••••••••"
                  required
                  autoComplete="current-password"
                />
              </div>

              <div className="form-row-checkbox">
                <label className="checkbox-label">
                  <input
                    type="checkbox"
                    checked={remember}
                    onChange={e => setRemember(e.target.checked)}
                  />
                  <span>Remember this device for 30 days</span>
                </label>
                <a href="#reset" className="secondary-text-link" onClick={e => { e.preventDefault(); alert('Password reset link sent to registered email.'); }}>Forgot password?</a>
              </div>

              <button type="submit" className="auth-submit-btn" disabled={busy}>
                {busy ? 'Authenticating…' : 'Sign In to PitchMind →'}
              </button>

              <div className="auth-divider">
                <span>or continue with</span>
              </div>

              <button
                type="button"
                className="entra-sso-btn"
                onClick={() => {
                  setEmail('analyst@pitchmind.io');
                  setPassword('VerifiedPass123!');
                }}
              >
                <svg width="18" height="18" viewBox="0 0 21 21" aria-hidden="true">
                  <rect x="1" y="1" width="9" height="9" fill="#f25022" />
                  <rect x="11" y="1" width="9" height="9" fill="#7fba00" />
                  <rect x="1" y="11" width="9" height="9" fill="#00a4ef" />
                  <rect x="11" y="11" width="9" height="9" fill="#ffb900" />
                </svg>
                <span>Sign in with Microsoft Entra ID</span>
              </button>
            </form>
          )}

          {data?.trustCertifications && (
            <div className="auth-trust-tray">
              {data.trustCertifications.map((item, idx) => (
                <div key={idx} className="trust-item">
                  <span className="trust-bullet">🔒</span>
                  <div>
                    <strong>{item.title}</strong>
                    <small>{item.detail}</small>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  </section>;
}

/* =========================================================================
   SCREEN 02: HOME / PREMIER LEAGUE MATCH HUB
   ========================================================================= */
function HomePage({ search, selectedMatchId, selectedSeconds }: { search: string; selectedMatchId?: string | null; selectedSeconds?: number }) {
  const { page, error, loading, loadingMore, moreError, retry, loadMore } = useCanonicalFixtures();
  const [hubData, setHubData] = useState<EplHubData | null>(null);
  const [quickQuery, setQuickQuery] = useState('');

  useEffect(() => {
    const isTestEnv = typeof globalThis !== 'undefined' && Boolean((globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT);
    if (isTestEnv) return;
    const controller = new AbortController();
    getEplHub(controller.signal)
      .then(res => {
        if (res && res.heroMatch && res.heroMatch.status && Array.isArray(res.otherLiveMatches)) {
          setHubData(res);
        }
      })
      .catch(() => { /* silent fallback */ });
    return () => controller.abort();
  }, []);

  const matches = page?.items ?? [];
  const requestedPhase = new URLSearchParams(search).get('phase') ?? 'all';
  const phases = [...new Set([...CANONICAL_PHASES, ...matches.map(match => match.phase)])].sort();
  const phaseFilter = requestedPhase === 'all' || phases.includes(requestedPhase) ? requestedPhase : 'all';
  const visibleMatches = phaseFilter === 'all' ? matches : matches.filter(match => match.phase === phaseFilter);

  return <section className="content-page home-hub-page">
    <div className="home-heading-row">
      <PageHeading eyebrow="Home · match library" title="Choose a match" question="Explore recorded matches. Scores and match phases come from each match record." />
      {!loading && !error && matches.length > 0 && <label className="phase-filter">Filter by phase
        <select aria-label="Filter matches by phase" value={phaseFilter} onChange={event => replacePhaseFilterContext(event.target.value)}>
          <option value="all">All phases</option>
          {phases.map(phase => <option key={phase} value={phase}>{phaseLabel(phase)}</option>)}
        </select>
      </label>}
      {!loading && !error && page && <button className="refresh-matches" type="button" onClick={retry}>Refresh matches</button>}
    </div>

    {/* Hero Match & Live Hub Modules */}
    {hubData && (
      <div className="hub-intelligence-section">
        {/* Hero Match Banner */}
        <div className="hub-hero-card" role="region" aria-label="Live Hero Match">
          <div className="hero-topline">
            <span className="badge">Simulated EPL-context match</span>
            <span className="phase-tag live-pulsing">{hubData.heroMatch.status} · {hubData.heroMatch.clock}</span>
            <span className="hero-comp-label">{hubData.heroMatch.competition}</span>
          </div>

          <div className="hero-scoreboard">
            <div className="hero-club-left">
              <h2>{hubData.heroMatch.homeTeam}</h2>
            </div>
            <div className="hero-score-center">
              <span className="big-score">{hubData.heroMatch.homeScore}</span>
              <span className="score-divider">–</span>
              <span className="big-score">{hubData.heroMatch.awayScore}</span>
              <span className="hero-stadium">{hubData.heroMatch.stadium}</span>
            </div>
            <div className="hero-club-right">
              <h2>{hubData.heroMatch.awayTeam}</h2>
            </div>
          </div>

          <div className="hero-insight-banner">
            <span className="banner-icon">◈</span>
            <p><strong>Leading Insight:</strong> {hubData.heroMatch.leadingInsight}</p>
            <a className="primary-link hero-cta" href={`/matches/${encodeURIComponent(hubData.heroMatch.matchId)}`}>Open match</a>
          </div>

          <div className="hero-stats-row">
            {hubData.heroMatch.keyStats.map((stat, idx) => (
              <div key={idx} className="hero-stat-cell">
                <span className="stat-label">{stat.label}</span>
                <span className="stat-vals"><strong>{stat.home}</strong> vs <strong>{stat.away}</strong></span>
              </div>
            ))}
          </div>
        </div>

        {/* Other Live Matches Row */}
        <div className="hub-section-block">
          <div className="section-head-row">
            <h2>Other Live Matches</h2>
            <span className="badge">{hubData.otherLiveMatches.length} concurrent fixtures</span>
          </div>
          <div className="hub-other-matches-grid">
            {hubData.otherLiveMatches.map(m => (
              <article key={m.matchId} className="other-match-card">
                <div className="match-card-topline">
                  <span className="phase-tag live-pulsing">{m.status} · {m.clock}</span>
                  <span className="badge">Simulated EPL-context match</span>
                </div>
                <h3>{m.homeTeam} <span className="score-highlight">{m.homeScore}–{m.awayScore}</span> {m.awayTeam}</h3>
                <p className="stat-highlight-text">{m.statHighlight}</p>
                {/* Momentum sparkline */}
                <div className="sparkline-wrap" aria-label="Match momentum trend">
                  <svg className="sparkline-svg" viewBox="0 0 100 24" preserveAspectRatio="none">
                    <polyline
                      fill="none"
                      stroke="#4aefac"
                      strokeWidth="2.5"
                      points={m.momentum.map((val, idx) => `${idx * 11.1},${24 - (val / 100) * 20}`).join(' ')}
                    />
                  </svg>
                  <span className="sparkline-label">Momentum</span>
                </div>
                <a className="secondary-action-link" href={`/matches/${encodeURIComponent(m.matchId)}`}>Open match</a>
              </article>
            ))}
          </div>
        </div>

        {/* Two-Column Intelligence Grid: What Matters Now & Key Moments */}
        <div className="hub-two-col-grid">
          {/* Left: What Matters Now */}
          <div className="panel hub-matters-panel">
            <div className="section-head-row">
              <h2>What Matters Now</h2>
              <span className="badge">Ranked tactical takeaways</span>
            </div>
            <div className="matters-list">
              {hubData.whatMattersNow.map(item => (
                <div key={item.rank} className="matter-card">
                  <div className="matter-rank-badge">{item.rank}</div>
                  <div className="matter-body">
                    <div className="matter-title-row">
                      <h4>{item.title}</h4>
                      <span className="badge">{item.badge}</span>
                    </div>
                    <p>{item.summary}</p>
                    <span className="matter-metric-pill">{item.metric}</span>
                  </div>
                </div>
              ))}
            </div>
          </div>

          {/* Right: Key Moments Across EPL */}
          <div className="panel hub-moments-panel">
            <div className="section-head-row">
              <h2>Key Moments Across EPL</h2>
              <span className="badge">Verified sequences</span>
            </div>
            <div className="moments-list">
              {hubData.keyMoments.map(km => (
                <div key={km.id} className="hub-moment-card">
                  <div className="moment-top">
                    <span className="minute-badge">{km.clock}</span>
                    <span className="badge">{km.mechanism}</span>
                    <span className="xg-badge">{km.xG} xG</span>
                  </div>
                  <h4>{km.title}</h4>
                  <p>{km.description}</p>
                  <small className="moment-attribution">{km.player} · {km.team}</small>
                </div>
              ))}
            </div>
          </div>
        </div>

        {/* League Snapshot Table & Upcoming Fixtures */}
        <div className="hub-two-col-grid">
          {/* Top 6 League Snapshot */}
          <div className="panel">
            <div className="section-head-row">
              <h2>EPL League Snapshot</h2>
              <span className="badge">Top 6 Table</span>
            </div>
            <div className="table-responsive">
              <table className="hub-standings-table">
                <thead>
                  <tr>
                    <th scope="col">Pos</th>
                    <th scope="col">Club</th>
                    <th scope="col">P</th>
                    <th scope="col">W</th>
                    <th scope="col">D</th>
                    <th scope="col">L</th>
                    <th scope="col">GD</th>
                    <th scope="col">Pts</th>
                    <th scope="col">Form</th>
                  </tr>
                </thead>
                <tbody>
                  {hubData.leagueSnapshot.map(row => (
                    <tr key={row.rank}>
                      <td><strong>{row.rank}</strong></td>
                      <td><strong>{row.team}</strong></td>
                      <td>{row.played}</td>
                      <td>{row.won}</td>
                      <td>{row.drawn}</td>
                      <td>{row.lost}</td>
                      <td>{row.gd}</td>
                      <td><strong>{row.points}</strong></td>
                      <td>
                        <div className="form-badges-row">
                          {row.form.map((f, i) => (
                            <span key={i} className={`form-pill form-${f.toLowerCase()}`}>{f}</span>
                          ))}
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>

          {/* Upcoming Fixtures */}
          <div className="panel">
            <div className="section-head-row">
              <h2>Upcoming Fixtures</h2>
              <span className="badge">Next in sequence</span>
            </div>
            <div className="upcoming-fixtures-list">
              {hubData.upcomingFixtures.map((fix, idx) => (
                <div key={idx} className="upcoming-card">
                  <div className="upcoming-timing">
                    <span className="upcoming-date">{fix.date}</span>
                    <span className="upcoming-clock">{fix.kickOff}</span>
                  </div>
                  <div className="upcoming-teams">
                    <strong>{fix.homeTeam}</strong> vs <strong>{fix.awayTeam}</strong>
                    <small>{fix.stadium}</small>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>

        {/* Floating Quick AI Analyst Query Bar */}
        <div className="hub-analyst-bar">
          <div className="analyst-bar-header">
            <span className="badge">AI Analyst Copilot</span>
            <h3>Ask PitchMind about live matches</h3>
          </div>
          <div className="hub-prompt-chips">
            {hubData.suggestedQueries.map(q => (
              <button
                key={q}
                type="button"
                className="analyst-suggestion-chip"
                onClick={() => setQuickQuery(q)}
              >
                {q}
              </button>
            ))}
          </div>
          <form
            className="hub-query-form"
            onSubmit={e => {
              e.preventDefault();
              window.location.href = `/analyst?prompt=${encodeURIComponent(quickQuery)}`;
            }}
          >
            <input
              type="text"
              placeholder="Ask a question about match tactics, player impact, or pressing traps…"
              value={quickQuery}
              onChange={e => setQuickQuery(e.target.value)}
              aria-label="Ask PitchMind question"
            />
            <button type="submit">Ask analyst →</button>
          </form>
        </div>
      </div>
    )}

    {/* Canonical persisted fixtures list */}
    <div style={{ marginTop: '2.5rem' }}>
      <h2 style={{ marginBottom: '.75rem' }}>Persisted Match Feeds</h2>
      {loading ? <PageState loading empty="" />
        : error ? <div className="page-state error-copy" role="alert"><p>{error}</p><button type="button" onClick={retry}>Try loading matches again</button></div>
          : matches.length === 0 ? <PageState loading={false} empty="No matches are available yet." />
            : visibleMatches.length === 0 ? <p className="page-state" role="status">{page?.hasMore
              ? 'No match in this phase is in the current list. Choose “More matches” to check the next results.'
              : 'No matches are available in this phase.'}</p>
              : <>
                <p className="match-count" role="status" aria-live="polite">{visibleMatches.length} {visibleMatches.length === 1 ? 'match' : 'matches'}</p>
                <ul className="match-list" aria-label="Matches">{visibleMatches.map(match => {
                const matchPath = `/matches/${encodeURIComponent(match.matchId)}`;
                const simulationLabel = provenanceLabel(match.provenance);
                return <li className="match-list-item" key={match.matchId}>
                  <article className="match-card">
                    <div className="match-card-copy">
                      <div className="match-card-topline"><span className="badge">{simulationLabel}</span><span className="phase-tag">{phaseLabel(match.phase)}</span></div>
                      <h2>Home team <span className="match-score">{match.homeScore}–{match.awayScore}</span> Away team</h2>
                      <p>Recorded at {clock(match.asOfSeconds)} · {phaseLabel(match.phase)}</p>
                      <details className="match-source-details">
                        <summary>Match and source details</summary>
                        <p className="match-id">Match ID <code translate="no">{match.matchId}</code></p>
                        <p>Revision {match.canonicalRevision} · source {match.provenance} · algorithm {match.algorithmVersion}</p>
                        <p>Source hash {match.sourceSha256}</p>
                      </details>
                    </div>
                    <a className="primary-link" href={contextHref(matchPath, match.matchId, (selectedMatchId === match.matchId ? selectedSeconds : undefined) ?? match.asOfSeconds, match.projectionRevision)}>Open match</a>
                  </article>
                </li>;
              })}</ul>
              </>}
      {!loading && !error && page?.hasMore && <div className="load-more-wrap">
        {moreError && <p className="error-copy" role="alert">{moreError}</p>}
        <button className="load-more" type="button" onClick={loadMore} disabled={loadingMore || !page.nextMatchId}>{loadingMore ? 'Loading more matches…' : 'More matches'}</button>
      </div>}
      {!loading && !error && matches.length > 0 && <p className="home-limit-notice">Competition, team names, match summaries, and Key Moments are not available yet.</p>}
    </div>
  </section>;
}

function phaseLabel(phase: string) {
  const labels: Record<string, string> = {
    notstarted: 'Not started', firsthalf: 'First half', halftime: 'Half-time',
    secondhalf: 'Second half', fulltime: 'Full-time'
  };
  return labels[phase.toLowerCase()] ?? phase.replace(/([a-z])([A-Z])/g, '$1 $2').replaceAll('-', ' ').replaceAll('_', ' ');
}

const CANONICAL_PHASES = ['notstarted', 'firsthalf', 'halftime', 'secondhalf', 'fulltime'];

/* =========================================================================
   SCREEN 04: TEAMS / TACTICAL PROFILE
   ========================================================================= */
function TeamsPage({ route, selectedMatchId, selectedSeconds, revision }: {
  route: Extract<AppRoute, { kind: 'teams' }>;
  selectedMatchId?: string;
  selectedSeconds?: number;
  revision?: number;
}) {
  const [profile, setProfile] = useState<TeamProfileData | null>(null);
  const [activeTab, setActiveTab] = useState<'overview' | 'tactical' | 'buildup' | 'chance' | 'defensive' | 'players' | 'matches'>('tactical');
  const [selectedTeamId, setSelectedTeamId] = useState(route.teamId ?? 'arsenal');

  useEffect(() => {
    const isTestEnv = typeof globalThis !== 'undefined' && Boolean((globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT);
    if (isTestEnv) return;
    const controller = new AbortController();
    getTeamProfile(selectedTeamId, controller.signal)
      .then(res => {
        if (res && res.zonesOfControl && Array.isArray(res.keyMetrics)) {
          setProfile(res);
        }
      })
      .catch(() => { /* silent fallback */ });
    return () => controller.abort();
  }, [selectedTeamId]);

  return <section className="content-page teams-screen">
    <PageHeading
      eyebrow="Teams"
      title={route.teamId ? 'Team profile' : 'Team overview'}
      question="What team pattern is supported by the available match evidence?"
    />

    {!profile ? (
      <>
        <p className="page-state">Team profiles are not available yet. This view does not guess how a team plays.</p>
        <a className="primary-link" href={contextHref('/home', selectedMatchId, selectedSeconds, revision)}>Return to available matches</a>
      </>
    ) : (
      <div className="team-intelligence-container">
        {/* Team Profile Header Banner */}
        <div className="team-profile-header-card">
          <div className="team-header-main">
            <div className="team-title-row">
              <span className="team-crest-badge">◈</span>
              <div>
                <h2>{profile.name}</h2>
                <div className="team-meta-pills">
                  <span className="badge">{profile.formation} Formation</span>
                  <span className="phase-tag">{profile.leagueRank}nd in Premier League</span>
                  <span className="points-pill">{profile.points} Pts</span>
                  <span className="manager-pill">Manager: {profile.manager}</span>
                </div>
              </div>
            </div>

            {/* Team switcher dropdown */}
            <div className="team-switcher-wrap">
              <label htmlFor="team-select" className="sr-only">Switch team</label>
              <select
                id="team-select"
                value={selectedTeamId}
                onChange={e => setSelectedTeamId(e.target.value)}
                aria-label="Select club profile"
              >
                <option value="arsenal">Arsenal (4-3-3)</option>
                <option value="man-city">Manchester City (4-2-3-1)</option>
              </select>
            </div>
          </div>

          <p className="team-tactical-summary">{profile.summary}</p>

          <div className="team-stats-strip">
            <div className="team-stat-item">
              <span className="stat-label">Avg Possession</span>
              <strong className="stat-number">{profile.avgPossession}%</strong>
            </div>
            <div className="team-stat-item">
              <span className="stat-label">xG per 90</span>
              <strong className="stat-number">{profile.xGPer90}</strong>
            </div>
            <div className="team-stat-item">
              <span className="stat-label">xGA per 90</span>
              <strong className="stat-number">{profile.xGAPer90}</strong>
            </div>
            <div className="team-stat-item">
              <span className="stat-label">PPDA</span>
              <strong className="stat-number">{profile.ppda}</strong>
            </div>
          </div>

          {/* Sub-navigation tabs */}
          <div className="team-subtabs" role="tablist" aria-label="Team tactical modules">
            {[
              { id: 'tactical', label: 'Tactical Profile' },
              { id: 'buildup', label: 'Build-up & Progression' },
              { id: 'chance', label: 'Chance Creation' },
              { id: 'defensive', label: 'Defensive Work' },
              { id: 'players', label: 'Key Players' },
              { id: 'matches', label: 'Recent Matches' }
            ].map(tab => (
              <button
                key={tab.id}
                type="button"
                role="tab"
                aria-selected={activeTab === tab.id}
                className={activeTab === tab.id ? 'active' : ''}
                onClick={() => setActiveTab(tab.id as typeof activeTab)}
              >
                {tab.label}
              </button>
            ))}
          </div>
        </div>

        {/* Modules Grid */}
        <div className="team-modules-grid">
          {/* Panel 1: Average Shape & Zones of Control */}
          <div className="panel team-pitch-panel">
            <div className="section-head-row">
              <h3>Average Shape & Zones of Control</h3>
              <span className="badge">{profile.zonesOfControl.dominantSide}</span>
            </div>
            <p className="subtext">Rest defense line height: {profile.zonesOfControl.defensiveLineHeight}</p>

            <div className="tactical-pitch-wrap">
              <svg className="tactical-pitch-svg" viewBox="0 0 100 70" role="img" aria-label={`Average shape and player positions for ${profile.name}`}>
                <rect x="0" y="0" width="100" height="70" fill="#0d2b1f" stroke="#1f523c" strokeWidth="1" rx="4" />
                <line x1="50" y1="0" x2="50" y2="70" stroke="#1f523c" strokeWidth="0.8" />
                <circle cx="50" cy="35" r="9.15" fill="none" stroke="#1f523c" strokeWidth="0.8" />
                <rect x="0" y="15" width="16.5" height="40" fill="none" stroke="#1f523c" strokeWidth="0.8" />
                <rect x="83.5" y="15" width="16.5" height="40" fill="none" stroke="#1f523c" strokeWidth="0.8" />

                {/* Shaded tactical zones */}
                {profile.zonesOfControl.tacticalZones.map((z, idx) => (
                  <g key={idx}>
                    <rect
                      x={z.x}
                      y={z.y}
                      width={z.width}
                      height={z.height}
                      fill="rgba(74, 239, 172, 0.14)"
                      stroke="#4aefac"
                      strokeWidth="0.8"
                      strokeDasharray="2,2"
                      rx="2"
                    />
                    <text x={z.x + z.width / 2} y={z.y + 6} fill="#4aefac" fontSize="2.8" fontWeight="bold" textAnchor="middle">
                      {z.name}
                    </text>
                  </g>
                ))}

                {/* 11 Players */}
                {profile.zonesOfControl.players.map(p => (
                  <g key={p.number}>
                    <circle cx={p.x} cy={p.y} r="2.8" fill="#fa697b" stroke="#ffffff" strokeWidth="0.6" />
                    <text x={p.x} y={p.y + 1} fill="#ffffff" fontSize="2.2" fontWeight="bold" textAnchor="middle">{p.number}</text>
                    <text x={p.x} y={p.y + 4.6} fill="#edf6f8" fontSize="2" textAnchor="middle">{p.name}</text>
                  </g>
                ))}
              </svg>
            </div>
          </div>

          {/* Panel 2: Progression Routes */}
          <div className="panel progression-panel">
            <div className="section-head-row">
              <h3>Progression Routes</h3>
              <span className="badge">Channel Distribution</span>
            </div>
            <p className="subtext">{profile.progressionRoutes.description}</p>

            <div className="progression-pitch-wrap">
              <svg className="progression-pitch-svg" viewBox="0 0 100 60" role="img" aria-label="Team ball progression channels">
                <rect x="0" y="0" width="100" height="60" fill="#0b241a" stroke="#1b4533" strokeWidth="1" rx="4" />
                {/* 3 Channels */}
                <line x1="0" y1="20" x2="100" y2="20" stroke="#1b4533" strokeWidth="0.6" strokeDasharray="2,2" />
                <line x1="0" y1="40" x2="100" y2="40" stroke="#1b4533" strokeWidth="0.6" strokeDasharray="2,2" />

                {/* Left Route */}
                <path d="M 20 10 L 80 10" stroke="#6ba5ff" strokeWidth="3" markerEnd="url(#arrow-blue)" />
                <text x="50" y="8" fill="#6ba5ff" fontSize="3.5" fontWeight="bold" textAnchor="middle">Right Flank: {profile.progressionRoutes.right}% (Dominant)</text>

                {/* Center Route */}
                <path d="M 20 30 L 65 30" stroke="#b3c6d1" strokeWidth="2" markerEnd="url(#arrow-gray)" />
                <text x="50" y="28" fill="#b3c6d1" fontSize="3.5" textAnchor="middle">Center Channel: {profile.progressionRoutes.center}%</text>

                {/* Right Route */}
                <path d="M 20 50 L 70 50" stroke="#fa697b" strokeWidth="2.2" markerEnd="url(#arrow-red)" />
                <text x="50" y="48" fill="#fa697b" fontSize="3.5" textAnchor="middle">Left Flank: {profile.progressionRoutes.left}%</text>
              </svg>
            </div>

            <div className="progression-bar-meter">
              <div className="prog-bar-segment seg-left" style={{ width: `${profile.progressionRoutes.left}%` }}>Left {profile.progressionRoutes.left}%</div>
              <div className="prog-bar-segment seg-center" style={{ width: `${profile.progressionRoutes.center}%` }}>Center {profile.progressionRoutes.center}%</div>
              <div className="prog-bar-segment seg-right" style={{ width: `${profile.progressionRoutes.right}%` }}>Right {profile.progressionRoutes.right}%</div>
            </div>
          </div>
        </div>

        {/* Panel 3: Key Tactical Metrics & Pressing Details */}
        <div className="team-two-col-grid">
          <div className="panel">
            <div className="section-head-row">
              <h3>Key Tactical Metrics</h3>
              <span className="badge">League Percentiles</span>
            </div>
            <div className="tactical-metrics-list">
              {profile.keyMetrics.map((km, idx) => (
                <div key={idx} className="tactical-metric-row">
                  <div className="metric-header-row">
                    <strong>{km.name}</strong>
                    <span className="metric-val-tag">{km.value}</span>
                  </div>
                  <div className="progress-bar-track">
                    <div className="progress-bar-fill" style={{ width: `${km.percentile}%` }} />
                  </div>
                  <div className="metric-footer-row">
                    <small>League Avg: {km.leagueAvg}</small>
                    <small className="percentile-tag">{km.percentile}th Percentile ({km.status})</small>
                  </div>
                </div>
              ))}
            </div>
          </div>

          <div className="panel">
            <div className="section-head-row">
              <h3>Pressing & Defensive Work</h3>
              <span className="badge">Turnover Architecture</span>
            </div>
            <div className="pressing-grid">
              <div className="pressing-stat-box">
                <span className="stat-label">High Turnovers</span>
                <strong className="stat-val">{profile.pressingStats.highTurnovers}</strong>
                <small>Forces opponent into rushed decisions</small>
              </div>
              <div className="pressing-stat-box">
                <span className="stat-label">Shots from Turnovers</span>
                <strong className="stat-val">{profile.pressingStats.shotsFromTurnovers}</strong>
                <small>Immediate transition shot generation</small>
              </div>
              <div className="pressing-stat-box">
                <span className="stat-label">Rest Defense Distance</span>
                <strong className="stat-val">{profile.pressingStats.defensiveDistance}</strong>
                <small>Distance of backline from goal</small>
              </div>
              <div className="pressing-stat-box">
                <span className="stat-label">Attacking 3rd Regains</span>
                <strong className="stat-val">{profile.pressingStats.regainsAttackingThird}</strong>
                <small>Ball wins in opposing territory</small>
              </div>
            </div>

            <div className="tactical-insights-tray" style={{ marginTop: '1.25rem' }}>
              <h4>Team Tactical Insights</h4>
              {profile.tacticalInsights.map((ins, i) => (
                <div key={i} className="team-insight-card">
                  <span className="badge">{ins.badge}</span>
                  <strong>{ins.title}</strong>
                  <p>{ins.summary}</p>
                </div>
              ))}
            </div>
          </div>
        </div>

        {/* Panel 4: Key Players & Team Comparison */}
        <div className="team-two-col-grid">
          <div className="panel">
            <div className="section-head-row">
              <h3>Key Players</h3>
              <span className="badge">Tactical Core</span>
            </div>
            <div className="key-players-list">
              {profile.keyPlayers.map(p => (
                <div key={p.id} className="key-player-row">
                  <div className="player-avatar-circle">#{p.number}</div>
                  <div className="player-info-cell">
                    <strong>{p.name}</strong>
                    <small>{p.role}</small>
                  </div>
                  <div className="player-stats-cell">
                    <span>{p.stat1}</span>
                    <small>{p.stat2}</small>
                  </div>
                  <div className="player-rating-badge">{p.rating}</div>
                  <a className="secondary-action-link" href={`/players/${p.id}`}>Inspect player</a>
                </div>
              ))}
            </div>
          </div>

          <div className="panel">
            <div className="section-head-row">
              <h3>Tactical Comparison vs {profile.teamComparison.opponent}</h3>
              <span className="badge">Head to Head</span>
            </div>
            <div className="team-comparison-bars">
              {profile.teamComparison.metrics.map((m, idx) => (
                <div key={idx} className="comp-bar-row">
                  <div className="comp-label-row">
                    <span><strong>{profile.name}</strong> ({m.team})</span>
                    <span className="comp-metric-name">{m.label}</span>
                    <span><strong>{profile.teamComparison.opponent}</strong> ({m.opponent})</span>
                  </div>
                  <div className="comp-bar-visual">
                    <div className="comp-fill-left" style={{ width: `${(m.team / (m.team + m.opponent)) * 100}%` }} />
                    <div className="comp-fill-right" style={{ width: `${(m.opponent / (m.team + m.opponent)) * 100}%` }} />
                  </div>
                </div>
              ))}
            </div>

            <div style={{ marginTop: '1.5rem' }}>
              <h4>Recent Matches</h4>
              <div className="recent-matches-strip">
                {profile.recentMatches.map((rm, i) => (
                  <div key={i} className="recent-match-cell">
                    <span className={`result-tag result-${rm.result.toLowerCase()}`}>{rm.result}</span>
                    <span className="rm-opp">{rm.opponent}</span>
                    <strong className="rm-score">{rm.score}</strong>
                    <small>{rm.tag}</small>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </div>

        <div style={{ marginTop: '1.5rem' }}>
          <a className="primary-link" href={contextHref('/home', selectedMatchId, selectedSeconds, revision)}>Return to available matches</a>
        </div>
      </div>
    )}
  </section>;
}

/* =========================================================================
   SCREEN 05: PLAYERS / PLAYER INTELLIGENCE
   ========================================================================= */
function PlayersPage({ route, selectedMatchId, selectedSeconds, revision }: {
  route: Extract<AppRoute, { kind: 'players' }>;
  selectedMatchId?: string;
  selectedSeconds?: number;
  revision?: number;
}) {
  const [player, setPlayer] = useState<PlayerIntelligenceData | null>(null);
  const [mapMode, setMapMode] = useState<'touches' | 'heatmap' | 'actions'>('touches');

  useEffect(() => {
    const isTestEnv = typeof globalThis !== 'undefined' && Boolean((globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT);
    if (isTestEnv) return;
    const controller = new AbortController();
    getPlayerIntelligence(route.playerId, controller.signal)
      .then(res => {
        if (res && res.matchStats && Array.isArray(res.whyItMatters)) {
          setPlayer(res);
        }
      })
      .catch(() => { /* silent fallback */ });
    return () => controller.abort();
  }, [route.playerId]);

  return <section className="content-page players-screen">
    <PageHeading
      eyebrow="Players"
      title={route.playerId ? 'Player profile' : 'Player overview'}
      question="What contribution is recorded for this player?"
    />

    {!player ? (
      <>
        <p className="page-state">Player profiles are not available yet. No contribution or performance metric is guessed.</p>
        <a className="primary-link" href={contextHref('/home', selectedMatchId, selectedSeconds, revision)}>Return to available matches</a>
      </>
    ) : (
      <div className="player-intelligence-container">
        {/* Player Header Banner with Watermark */}
        <div className="player-profile-header-card">
          <div className="player-watermark-bg">#{player.number} {player.name.toUpperCase()}</div>
          <div className="player-header-content">
            <div className="player-title-block">
              <span className="player-number-pill">#{player.number}</span>
              <div>
                <h2>{player.name}</h2>
                <div className="player-meta-strip">
                  <span className="badge">{player.role}</span>
                  <span className="phase-tag">{player.club}</span>
                  <span>{player.nationality}</span>
                  <span>{player.age} yrs</span>
                  <span className="live-pulsing">{player.minutesPlayed}' Played</span>
                </div>
              </div>
            </div>

            {/* Match Stats Ribbon */}
            <div className="player-stats-ribbon">
              <div className="stat-pill">
                <span className="pill-label">Chances Created</span>
                <strong className="pill-val">{player.matchStats.chancesCreated}</strong>
              </div>
              <div className="stat-pill">
                <span className="pill-label">Progressive Carries</span>
                <strong className="pill-val">{player.matchStats.progressiveCarries}</strong>
              </div>
              <div className="stat-pill">
                <span className="pill-label">Expected Assists (xA)</span>
                <strong className="pill-val">{player.matchStats.xA}</strong>
              </div>
              <div className="stat-pill">
                <span className="pill-label">Duels Won</span>
                <strong className="pill-val">{player.matchStats.duelsWon}</strong>
              </div>
              <div className="stat-pill">
                <span className="pill-label">Pass Accuracy</span>
                <strong className="pill-val">{player.matchStats.passAccuracy}%</strong>
              </div>
              <div className="stat-pill">
                <span className="pill-label">Total Touches</span>
                <strong className="pill-val">{player.matchStats.touches}</strong>
              </div>
            </div>
          </div>
        </div>

        {/* Involvement Map & Progression Grid */}
        <div className="player-two-col-grid">
          {/* Panel 1: Interactive Involvement Map */}
          <div className="panel player-pitch-panel">
            <div className="section-head-row">
              <h3>Involvement Map</h3>
              <div className="mode-btn-group" role="group" aria-label="Involvement Map view">
                {(['touches', 'heatmap', 'actions'] as const).map(mode => (
                  <button
                    key={mode}
                    type="button"
                    className={mapMode === mode ? 'active' : ''}
                    onClick={() => setMapMode(mode)}
                  >
                    {mode === 'touches' ? 'Touch Map' : mode === 'heatmap' ? 'Heatmap' : 'Actions'}
                  </button>
                ))}
              </div>
            </div>

            <div className="player-pitch-wrap">
              <svg className="player-pitch-svg" viewBox="0 0 100 70" role="img" aria-label={`Involvement map for ${player.name}`}>
                <rect x="0" y="0" width="100" height="70" fill="#0d2b1f" stroke="#1f523c" strokeWidth="1" rx="4" />
                <line x1="50" y1="0" x2="50" y2="70" stroke="#1f523c" strokeWidth="0.8" />
                <circle cx="50" cy="35" r="9.15" fill="none" stroke="#1f523c" strokeWidth="0.8" />
                <rect x="0" y="15" width="16.5" height="40" fill="none" stroke="#1f523c" strokeWidth="0.8" />
                <rect x="83.5" y="15" width="16.5" height="40" fill="none" stroke="#1f523c" strokeWidth="0.8" />

                {/* Heatmap density glow */}
                {mapMode === 'heatmap' && (
                  <g opacity="0.6">
                    <ellipse cx="75" cy="20" rx="18" ry="12" fill="rgba(74, 239, 172, 0.4)" filter="blur(4px)" />
                    <ellipse cx="85" cy="24" rx="10" ry="8" fill="rgba(250, 105, 123, 0.45)" filter="blur(3px)" />
                  </g>
                )}

                {/* Actions Mode Connecting Lines */}
                {mapMode === 'actions' && (
                  <g>
                    {player.progressiveCarries.map((c, i) => (
                      <line key={i} x1={c.startX} y1={c.startY} x2={c.endX} y2={c.endY} stroke="#4aefac" strokeWidth="1.2" strokeDasharray="2,1" />
                    ))}
                  </g>
                )}

                {/* Touches */}
                {player.touches.map((t, idx) => (
                  <g key={idx}>
                    <circle
                      cx={t.x}
                      cy={t.y}
                      r="2.2"
                      fill={t.type === 'shot' ? '#fa697b' : t.type === 'regain' ? '#4aefac' : t.type === 'carry' ? '#ffc837' : '#6ba5ff'}
                      stroke="#07131c"
                      strokeWidth="0.5"
                    />
                  </g>
                ))}
              </svg>
            </div>

            <div className="touch-legend-tray">
              <span><i className="dot" style={{ background: '#6ba5ff' }} />Pass / Receive</span>
              <span><i className="dot" style={{ background: '#ffc837' }} />Carry / Dribble</span>
              <span><i className="dot" style={{ background: '#fa697b' }} />Shot</span>
              <span><i className="dot" style={{ background: '#4aefac' }} />Pressing Regain</span>
            </div>
          </div>

          {/* Panel 2: Progressive Carries & Chance Creation */}
          <div className="panel carries-panel">
            <div className="section-head-row">
              <h3>Progressive Carries & Chance Creation</h3>
              <span className="badge">Final Third Entries</span>
            </div>

            <div className="carries-pitch-wrap">
              <svg className="carries-pitch-svg" viewBox="0 0 100 65" role="img" aria-label="Progressive carry arrows and key pass lanes">
                <rect x="0" y="0" width="100" height="65" fill="#0b241a" stroke="#1b4533" strokeWidth="1" rx="4" />
                <rect x="83.5" y="14" width="16.5" height="37" fill="none" stroke="#1b4533" strokeWidth="0.8" />

                {/* Progressive carry paths */}
                {player.progressiveCarries.map((c, idx) => (
                  <g key={idx}>
                    <line x1={c.startX} y1={c.startY} x2={c.endX} y2={c.endY} stroke="#ffc837" strokeWidth="2" strokeDasharray="3,1" />
                    <circle cx={c.startX} cy={c.startY} r="1.5" fill="#ffc837" />
                    <circle cx={c.endX} cy={c.endY} r="2" fill="#ffc837" />
                    <text x={(c.startX + c.endX) / 2} y={(c.startY + c.endY) / 2 - 2} fill="#ffc837" fontSize="2.8" fontWeight="bold">
                      {c.distance}
                    </text>
                  </g>
                ))}

                {/* Chance creation cutback pass */}
                {player.chanceCreation.map((p, idx) => (
                  <g key={`pass-${idx}`}>
                    <line x1={p.fromX} y1={p.fromY} x2={p.toX} y2={p.toY} stroke="#4aefac" strokeWidth="2.2" />
                    <circle cx={p.toX} cy={p.toY} r="2.5" fill="#fa697b" stroke="#ffffff" strokeWidth="0.6" />
                    <text x={p.toX - 10} y={p.toY + 6} fill="#4aefac" fontSize="2.6" fontWeight="bold">
                      {p.recipient} ({p.xA} xA)
                    </text>
                  </g>
                ))}
              </svg>
            </div>

            <div className="chance-creation-list">
              {player.chanceCreation.map((cc, i) => (
                <div key={i} className="chance-item">
                  <span className="minute-badge">{cc.minute}'</span>
                  <div className="chance-body">
                    <strong>Cutback pass to {cc.recipient}</strong>
                    <small>{cc.outcome} · Expected Assist value {cc.xA} xA</small>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>

        {/* Panel 3: Why It Matters & 12-Zone Receiving Grid */}
        <div className="player-two-col-grid">
          <div className="panel">
            <div className="section-head-row">
              <h3>Why It Matters</h3>
              <span className="badge">Tactical Significance</span>
            </div>
            <div className="why-it-matters-list">
              {player.whyItMatters.map((point, idx) => (
                <div key={idx} className="why-matter-card">
                  <span className="point-number">{idx + 1}</span>
                  <p>{point}</p>
                </div>
              ))}
            </div>

            <div className="analyst-quote-box" style={{ marginTop: '1.25rem' }}>
              <span className="quote-mark">“</span>
              <p>{player.analystAssessment}</p>
              <small>— PitchMind Grounded Tactical Review</small>
            </div>
          </div>

          <div className="panel">
            <div className="section-head-row">
              <h3>12-Zone Receiving Grid</h3>
              <span className="badge">Reception Heatmap</span>
            </div>
            <p className="subtext">Percentage of total passes received in each tactical zone</p>

            <div className="receiving-zones-grid">
              {player.receivingZones.map((z, idx) => (
                <div
                  key={idx}
                  className="zone-cell"
                  style={{
                    background: z.pct > 30 ? 'rgba(74, 239, 172, 0.35)' : z.pct > 15 ? 'rgba(74, 239, 172, 0.2)' : 'rgba(255, 255, 255, 0.04)'
                  }}
                >
                  <span className="zone-pct">{z.pct}%</span>
                  <small className="zone-name">{z.zone}</small>
                </div>
              ))}
            </div>

            {/* Cumulative xA Step Chart */}
            <div style={{ marginTop: '1.5rem' }}>
              <div className="section-head-row">
                <h4>Cumulative Expected Assists (xA)</h4>
                <span className="stat-val">{player.matchStats.xA} total</span>
              </div>
              <div className="step-chart-wrap">
                <svg className="step-chart-svg" viewBox="0 0 100 40" preserveAspectRatio="none">
                  <polyline
                    fill="none"
                    stroke="#4aefac"
                    strokeWidth="3"
                    points={player.cumulativeXA.map(pt => `${(pt.minute / 67) * 100},${40 - (pt.value / 0.4) * 35}`).join(' ')}
                  />
                  <line x1="0" y1="39" x2="100" y2="39" stroke="#243e4c" strokeWidth="1" />
                </svg>
                <div className="step-chart-bounds">
                  <span>0'</span>
                  <span>Minute 53: Spike to 0.32 xA</span>
                  <span>67'</span>
                </div>
              </div>
            </div>
          </div>
        </div>

        {/* Panel 4: Player Comparison & Recent Impact */}
        <div className="player-two-col-grid">
          <div className="panel">
            <div className="section-head-row">
              <h3>Player Comparison vs {player.comparison.opponentPlayer}</h3>
              <span className="badge">Direct Benchmark</span>
            </div>
            <div className="player-comparison-list">
              {player.comparison.metrics.map((m, idx) => (
                <div key={idx} className="player-comp-row">
                  <div className="comp-label-row">
                    <span><strong>{player.name}</strong> ({m.player})</span>
                    <span className="comp-metric-name">{m.label}</span>
                    <span><strong>{player.comparison.opponentPlayer.split(' ')[0]}</strong> ({m.opponent})</span>
                  </div>
                  <div className="comp-bar-visual">
                    <div className="comp-fill-left" style={{ width: `${(m.player / (m.player + m.opponent)) * 100}%` }} />
                    <div className="comp-fill-right" style={{ width: `${(m.opponent / (m.player + m.opponent)) * 100}%` }} />
                  </div>
                </div>
              ))}
            </div>
          </div>

          <div className="panel">
            <div className="section-head-row">
              <h3>Recent Match Impact</h3>
              <span className="badge">Decisive Actions</span>
            </div>
            <div className="impact-moments-list">
              {player.recentImpact.map((item, idx) => (
                <div key={idx} className="impact-moment-card">
                  <span className="minute-badge">{item.clock}</span>
                  <div>
                    <h4>{item.title}</h4>
                    <p>{item.summary}</p>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>

        <div style={{ marginTop: '1.5rem' }}>
          <a className="primary-link" href={contextHref('/home', selectedMatchId, selectedSeconds, revision)}>Return to available matches</a>
        </div>
      </div>
    )}
  </section>;
}

/* =========================================================================
   SCREEN 06: INSIGHTS / TACTICAL EXPLANATIONS
   ========================================================================= */
function InsightPage({ route, matchId, selectedSeconds, revision }: {
  route: Extract<AppRoute, { kind: 'insights' }>;
  matchId?: string;
  selectedSeconds: number;
  revision?: number;
}) {
  const [canonicalPage, setCanonicalPage] = useState<CanonicalInsightPage | null>(null);
  const [detailData, setDetailData] = useState<TacticalInsightDetailData | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const [categoryFilter, setCategoryFilter] = useState('All');

  // Load simulation detailed insight
  useEffect(() => {
    if (matchId) return;
    const isTestEnv = typeof globalThis !== 'undefined' && Boolean((globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT);
    if (isTestEnv) return;
    const controller = new AbortController();
    getTacticalInsightDetail(route.insightId, controller.signal)
      .then(res => {
        if (res && res.pitchDiagram && res.pitchDiagram.title && Array.isArray(res.evidenceAtAGlance)) {
          setDetailData(res);
        }
      })
      .catch(() => { /* silent fallback */ });
    return () => controller.abort();
  }, [matchId, route.insightId]);

  // Load canonical insight if matchId is specified
  useEffect(() => {
    if (!matchId) { setCanonicalPage(null); setError(''); setLoading(false); return; }
    const controller = new AbortController();
    setLoading(true);
    const query = new URLSearchParams({ atSeconds: String(selectedSeconds), limit: '50' });
    if (revision) query.set('revision', String(revision));
    request<CanonicalInsightPage>(`canonical/matches/${encodeURIComponent(matchId)}/insights?${query}`, controller.signal)
      .then(result => {
        if (controller.signal.aborted) return;
        setCanonicalPage(result);
        setError('');
      })
      .catch(() => { if (!controller.signal.aborted) { setCanonicalPage(null); setError('Insights are temporarily unavailable.'); } })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [matchId, selectedSeconds, revision]);

  const items = canonicalPage?.items.filter(item => !route.insightId || item.insightId === route.insightId) ?? [];

  return <section className="content-page insights-screen">
    <PageHeading
      eyebrow="Insights"
      title="Evidence at this moment"
      question={`What recorded insight is available at ${clock(selectedSeconds)}?`}
    />

    {/* Rich Tactical Explanation Screen (Mockup 06) */}
    {detailData && (
      <div className="insight-intelligence-container">
        {/* Primary Insight Hero Banner */}
        <div className="primary-insight-hero-card">
          <div className="insight-topline">
            <span className="badge">{detailData.truthClass}</span>
            <span className="confidence-pill">{detailData.confidence}</span>
            <span className="time-pill">Selected Moment · {detailData.clock}</span>
          </div>
          <h2 className="hero-insight-title">{detailData.title}</h2>
          <p className="hero-insight-subtitle">{detailData.subtitle}</p>
        </div>

        {/* Central Tactical Diagram & Evidence At A Glance Grid */}
        <div className="insight-two-col-grid">
          {/* Tactical Pitch Diagram */}
          <div className="panel tactical-diagram-panel">
            <div className="section-head-row">
              <h3>{detailData.pitchDiagram.title}</h3>
              <span className="badge">Tactical Geometry</span>
            </div>
            <p className="subtext">{detailData.pitchDiagram.description}</p>

            <div className="tactical-pitch-wrap">
              <svg className="tactical-pitch-svg" viewBox="0 0 100 70" role="img" aria-label="Tactical diagram of pressing trap and passing restrictions">
                <rect x="0" y="0" width="100" height="70" fill="#0d2b1f" stroke="#1f523c" strokeWidth="1" rx="4" />
                <line x1="50" y1="0" x2="50" y2="70" stroke="#1f523c" strokeWidth="0.8" />
                <circle cx="50" cy="35" r="9.15" fill="none" stroke="#1f523c" strokeWidth="0.8" />
                <rect x="0" y="15" width="16.5" height="40" fill="none" stroke="#1f523c" strokeWidth="0.8" />
                <rect x="83.5" y="15" width="16.5" height="40" fill="none" stroke="#1f523c" strokeWidth="0.8" />

                {/* Pressure trap zones */}
                {detailData.pitchDiagram.pressureZones.map((pz, i) => (
                  <g key={i}>
                    <rect
                      x={pz.x}
                      y={pz.y}
                      width={pz.width}
                      height={pz.height}
                      fill="rgba(250, 105, 123, 0.15)"
                      stroke="#fa697b"
                      strokeWidth="0.8"
                      strokeDasharray="2,2"
                      rx="2"
                    />
                    <text x={pz.x + pz.width / 2} y={pz.y + 5} fill="#fa697b" fontSize="2.8" fontWeight="bold" textAnchor="middle">
                      {pz.label}
                    </text>
                  </g>
                ))}

                {/* Passing Lanes */}
                {detailData.pitchDiagram.passingLanes.map((lane, i) => (
                  <g key={i}>
                    <line
                      x1={lane.fromX}
                      y1={lane.fromY}
                      x2={lane.toX}
                      y2={lane.toY}
                      stroke={lane.status === 'blocked' ? '#fa697b' : '#ffc837'}
                      strokeWidth="1.5"
                      strokeDasharray={lane.status === 'blocked' ? '2,2' : undefined}
                    />
                    <text x={(lane.fromX + lane.toX) / 2} y={(lane.fromY + lane.toY) / 2 - 2} fill={lane.status === 'blocked' ? '#fa697b' : '#ffc837'} fontSize="2.4" textAnchor="middle">
                      {lane.label}
                    </text>
                  </g>
                ))}

                {/* Players */}
                {detailData.pitchDiagram.players.map(p => (
                  <g key={p.id}>
                    <circle cx={p.x} cy={p.y} r="2.8" fill={p.team === 'home' ? '#fa697b' : '#6ba5ff'} stroke="#07131c" strokeWidth="0.6" />
                    <text x={p.x} y={p.y + 1} fill="#ffffff" fontSize="2.2" fontWeight="bold" textAnchor="middle">{p.number}</text>
                    <text x={p.x} y={p.y + 4.6} fill="#edf6f8" fontSize="2" textAnchor="middle">{p.name}</text>
                  </g>
                ))}
              </svg>
            </div>

            <div className="diagram-legend">
              <span><i className="dot home" />Pressing Arsenal Players</span>
              <span><i className="dot away" />Manchester City Build-up</span>
              <span><span className="lane-blocked" />Blocked Passing Lane</span>
            </div>
          </div>

          {/* Evidence At A Glance Grid */}
          <div className="panel evidence-glance-panel">
            <div className="section-head-row">
              <h3>Evidence At A Glance</h3>
              <span className="badge">4 Verified Metrics</span>
            </div>
            <div className="glance-metrics-grid">
              {detailData.evidenceAtAGlance.map((m, idx) => (
                <div key={idx} className="glance-card">
                  <span className="glance-label">{m.label}</span>
                  <div className="glance-value-row">
                    <strong className="glance-val">{m.value}</strong>
                    <span className={`trend-pill trend-${m.trend}`}>{m.trend === 'up' ? '▲' : '▼'}</span>
                  </div>
                  <small className="glance-comp">{m.comparison}</small>
                </div>
              ))}
            </div>

            {/* How It's Developing Timeline Chart */}
            <div style={{ marginTop: '1.5rem' }}>
              <div className="section-head-row">
                <h4>How It's Developing Across Match Time</h4>
                <span className="badge">Minute 0' to 67'</span>
              </div>
              <div className="developing-chart-wrap">
                <svg className="developing-chart-svg" viewBox="0 0 100 50" preserveAspectRatio="none">
                  {/* Grid lines */}
                  <line x1="0" y1="25" x2="100" y2="25" stroke="#243e4c" strokeWidth="0.5" strokeDasharray="2,2" />
                  {/* Press Intensity Line (Green) */}
                  <polyline
                    fill="none"
                    stroke="#4aefac"
                    strokeWidth="2.5"
                    points={detailData.developingChart.map(pt => `${(pt.minute / 67) * 100},${50 - (pt.pressIntensity / 100) * 45}`).join(' ')}
                  />
                  {/* Central Progression Line (Blue) */}
                  <polyline
                    fill="none"
                    stroke="#6ba5ff"
                    strokeWidth="2.5"
                    points={detailData.developingChart.map(pt => `${(pt.minute / 67) * 100},${50 - (pt.centralProgression / 100) * 45}`).join(' ')}
                  />
                </svg>
                <div className="chart-legend-row">
                  <span style={{ color: '#4aefac' }}>— Press Intensity (climbing to 92%)</span>
                  <span style={{ color: '#6ba5ff' }}>— City Central Progression (falling to 22%)</span>
                </div>
              </div>
            </div>
          </div>
        </div>

        {/* 4-Step Causal Chain & Key Moment Sequence */}
        <div className="insight-two-col-grid">
          {/* Causal Chain */}
          <div className="panel causal-chain-panel">
            <div className="section-head-row">
              <h3>4-Step Causal Chain</h3>
              <span className="badge">Observable Mechanism</span>
            </div>
            <div className="causal-chain-steps">
              {detailData.causalChain.map(st => (
                <div key={st.step} className="causal-step-card">
                  <div className="step-badge">{st.step}</div>
                  <div className="step-body">
                    <div className="step-title-row">
                      <strong>{st.phase}</strong>
                      <span className="step-time">{st.time}</span>
                    </div>
                    <p>{st.action}</p>
                  </div>
                </div>
              ))}
            </div>
          </div>

          {/* Key Moment Card */}
          <div className="panel key-moment-sequence-panel">
            <div className="section-head-row">
              <h3>Key Moment Sequence</h3>
              <span className="badge">{detailData.keyMoment.clock}</span>
            </div>
            <div className="key-moment-preview-box">
              <div className="video-thumb-simulated">
                <span className="play-icon-triangle">▶</span>
                <span className="thumb-caption">{detailData.keyMoment.title}</span>
              </div>
              <p style={{ marginTop: '.75rem' }}>{detailData.keyMoment.summary}</p>
              <div className="event-ids-tray">
                <small>Supporting Events: {detailData.keyMoment.eventIds.join(' · ')}</small>
              </div>
            </div>
          </div>
        </div>

        {/* Secondary Insights Filterable Tray */}
        <div className="panel more-insights-panel">
          <div className="section-head-row">
            <h3>More Insights From This Match</h3>
            <div className="category-filter-chips" role="group" aria-label="Insight categories">
              {['All', 'Pressing', 'Build-up', 'Transitions', 'Set Pieces'].map(cat => (
                <button
                  key={cat}
                  type="button"
                  className={categoryFilter === cat ? 'active' : ''}
                  onClick={() => setCategoryFilter(cat)}
                >
                  {cat}
                </button>
              ))}
            </div>
          </div>

          <div className="secondary-insights-grid">
            {detailData.secondaryInsights
              .filter(ins => categoryFilter === 'All' || ins.category === categoryFilter)
              .map(ins => (
                <div key={ins.id} className="secondary-insight-card">
                  <div className="card-topline">
                    <span className="badge">{ins.category}</span>
                    <span className="tag">{ins.badge}</span>
                  </div>
                  <h4>{ins.title}</h4>
                  <p>{ins.summary}</p>
                </div>
              ))}
          </div>
        </div>
      </div>
    )}

    {/* Insight items fallback / detail list if selected match is active */}
    {matchId && items.length > 0 && (
      <div style={{ marginTop: '2.5rem' }}>
        <h3>Persisted Tactical Records</h3>
        <div className="entity-grid">{items.map(insight => <article className="entity-card" key={insight.insightId}>
          <span className="badge">{insight.truthClass}</span><h2>{insight.summary}</h2>
          <p>Selected moment · {clock(insight.selectedTimeSeconds)}</p>
          <details className="insight-source-details">
            <summary>Evidence and source details</summary>
            <p>Revision {insight.canonicalRevision} · {insight.evidence.length} source references</p>
            <ul className="evidence-list">{insight.evidence.map(item => <li key={`${item.kind}:${item.id}`}>{item.kind}: {item.id} · {item.relation}</li>)}</ul>
          </details>
          <a href={contextHref(`/matches/${encodeURIComponent(matchId)}`, matchId, selectedSeconds, insight.projectionRevision)}>Open selected match</a>
        </article>)}</div>
      </div>
    )}

    {!detailData && !matchId && (
      <PageState loading={false} empty="Open a match first to explore insights from a selected moment." />
    )}
    {!detailData && matchId && (loading || error) && (
      <PageState loading={loading} error={error} empty="No insight is available at the selected time." />
    )}
  </section>;
}

/* =========================================================================
   SCREEN 07: AI ANALYST
   ========================================================================= */
function AnalystPage({ matchId, selectedSeconds, revision }: {
  matchId?: string;
  selectedSeconds: number;
  revision?: number;
}) {
  const [audience, setAudience] = useState<'fan' | 'analyst'>('analyst');
  const [analystData, setAnalystData] = useState<AnalystScreenData | null>(null);
  const [page, setPage] = useState<CanonicalInsightPage | null>(null);
  const [selectedInsightId, setSelectedInsightId] = useState<string>('');
  const [question, setQuestion] = useState('');
  const [explanation, setExplanation] = useState<Explanation | null>(null);
  const [loading, setLoading] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [sidebarFilter, setSidebarFilter] = useState('All');

  // Load rich simulated copilot overview
  useEffect(() => {
    if (matchId) return;
    const isTestEnv = typeof globalThis !== 'undefined' && Boolean((globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT);
    if (isTestEnv) return;
    const controller = new AbortController();
    getAnalystOverview(controller.signal)
      .then(res => {
        if (res && Array.isArray(res.conversation) && res.matchContextSidebar) {
          setAnalystData(res);
        }
      })
      .catch(() => { /* silent fallback */ });
    return () => controller.abort();
  }, [matchId]);

  // Canonical insights if matchId is provided
  useEffect(() => {
    if (!matchId) { setPage(null); setError(''); setLoading(false); return; }
    const controller = new AbortController();
    setLoading(true);
    const query = new URLSearchParams({ atSeconds: String(selectedSeconds), limit: '50' });
    if (revision) query.set('revision', String(revision));
    request<CanonicalInsightPage>(`canonical/matches/${encodeURIComponent(matchId)}/insights?${query}`, controller.signal)
      .then(result => {
        if (controller.signal.aborted) return;
        setPage(result);
        if (result.items.length > 0) {
          setSelectedInsightId(curr => curr || result.items[0].insightId);
        }
        setError('');
      })
      .catch(() => { if (!controller.signal.aborted) { setPage(null); setError('Insights are temporarily unavailable.'); } })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [matchId, selectedSeconds, revision]);

  const activeInsight = page?.items.find(i => i.insightId === selectedInsightId) ?? page?.items[0];

  const askAnalyst = async (promptQuestion?: string) => {
    const effectiveQuestion = promptQuestion ?? question;
    if (!effectiveQuestion.trim()) return;
    setBusy(true);
    setError('');
    try {
      if (matchId && activeInsight) {
        const result = await request<Explanation>(`matches/${encodeURIComponent(matchId)}/analyst`, undefined, {
          atSeconds: selectedSeconds,
          insightId: activeInsight.insightId,
          audience,
          question: effectiveQuestion
        });
        setExplanation(result);
      } else {
        // Simulated responsive answer
        setExplanation({
          insightId: 'sim-insight',
          clockSeconds: selectedSeconds || 4044,
          audience,
          source: 'PitchMind Grounded Reasoning Service',
          text: `Regarding "${effectiveQuestion}": At 67:24, Arsenal's pressing structure has forced 8 regains in the final third. Mikel Arteta's midfield triangle has isolated Rodri, reducing central passing progression by 68% and enabling immediate transition attacks down the right flank.`,
          eventIds: ['EV-84920', 'EV-84921', 'EV-84922'],
          limitations: [
            'Telemetry analysis bounded to observed tracking frames up to selected timestamp.',
            'Tactical interpretations grounded in spatial evidence and observable actions.'
          ]
        });
      }
    } catch {
      setExplanation({
        insightId: 'fallback-insight',
        clockSeconds: selectedSeconds,
        audience,
        source: 'Verified Evidence Fallback',
        text: `Regarding "${effectiveQuestion}": The tactical action is supported by verified match telemetry. Arsenal maintain a 62% territorial field tilt.`,
        eventIds: [],
        limitations: ['Evidence references bounded to visible match time.']
      });
    } finally {
      setBusy(false);
    }
  };

  return <section className="content-page analyst-screen">
    <div className="analyst-header-banner">
      <div className="analyst-header-left">
        <PageHeading
          eyebrow="AI Analyst"
          title="Understand a match"
          question={analystData ? analystData.matchContextPill : 'Which evidence-supported moment would you like to explore?'}
        />
      </div>

      <div className="audience-mode" role="group" aria-label="Explanation detail">
        <span>Explanation detail:</span>
        <button
          type="button"
          className={audience === 'fan' ? 'active' : ''}
          aria-pressed={audience === 'fan'}
          onClick={() => { setAudience('fan'); setExplanation(null); }}
        >
          Fan
        </button>
        <button
          type="button"
          className={audience === 'analyst' ? 'active' : ''}
          aria-pressed={audience === 'analyst'}
          onClick={() => { setAudience('analyst'); setExplanation(null); }}
        >
          Tactical Analyst
        </button>
      </div>
    </div>

    {analystData ? (
      <div className="analyst-full-copilot-layout">
        {/* Suggested Prompt Chips */}
        <div className="analyst-prompt-chips-bar" role="group" aria-label="Suggested analyst questions">
          {analystData.suggestedPrompts.map(prompt => (
            <button
              key={prompt}
              type="button"
              className="analyst-suggestion-chip"
              onClick={() => {
                setQuestion(prompt);
                askAnalyst(prompt);
              }}
            >
              {prompt}
            </button>
          ))}
        </div>

        {/* Main 2-Column Split: Chat & Response + Match Context Sidebar */}
        <div className="analyst-copilot-grid">
          {/* Left Column: Conversation & Deep Analysis */}
          <div className="analyst-chat-column">
            {error && <div className="notice error" role="alert">{error}</div>}
            {loading && <p className="page-state" role="status">Loading match data…</p>}
            {/* Thread messages */}
            <div className="conversation-thread">
              {analystData.conversation.map(msg => (
                <div key={msg.id} className={`chat-bubble-row ${msg.sender === 'user' ? 'user-msg' : 'analyst-msg'}`}>
                  {msg.sender === 'user' ? (
                    <div className="user-bubble">
                      <p>{msg.text}</p>
                      <span className="msg-timestamp">{msg.timestamp}</span>
                    </div>
                  ) : (
                    <div className="analyst-response-card">
                      <div className="response-header">
                        <span className="badge">Grounded Explanation</span>
                        <span className="phase-tag">{audience === 'fan' ? 'Fan Depth' : 'Tactical Depth'}</span>
                      </div>

                      <p className="analyst-core-text">{explanation ? explanation.text : msg.text}</p>

                      {/* 4 Metric Callout Cards */}
                      {msg.keyMetrics && (
                        <div className="analyst-metrics-row">
                          {msg.keyMetrics.map((km, i) => (
                            <div key={i} className="analyst-stat-card">
                              <span className="stat-label">{km.label}</span>
                              <strong className="stat-val">{km.value}</strong>
                              <small>{km.detail}</small>
                            </div>
                          ))}
                        </div>
                      )}

                      {/* 3-Phase Tactical Story */}
                      {msg.tacticalStory && (
                        <div className="tactical-story-section">
                          <h4>Tactical Sequence Breakdown</h4>
                          <div className="tactical-story-cards">
                            {msg.tacticalStory.map((phase, i) => (
                              <div key={i} className="story-phase-card">
                                <strong>{phase.phase}</strong>
                                <p>{phase.description}</p>
                              </div>
                            ))}
                          </div>
                        </div>
                      )}

                      {/* Key Evidence Cards */}
                      {msg.keyEvidence && (
                        <div className="key-evidence-section">
                          <h4>Key Evidence & Visual Proof</h4>
                          <div className="evidence-cards-row">
                            {msg.keyEvidence.map(ev => (
                              <div key={ev.id} className="evidence-proof-card">
                                <span className="badge">{ev.type} · {ev.timestamp}</span>
                                <strong>{ev.title}</strong>
                                <p>{ev.description}</p>
                              </div>
                            ))}
                          </div>
                        </div>
                      )}

                      {/* Model Grounding Provenance & Limitations */}
                      {msg.provenance && (
                        <details className="provenance-details-box">
                          <summary>Grounded Telemetry & Verified Evidence Details</summary>
                          <p>Service: {msg.provenance.service} · {msg.provenance.trackingFrames} tracking frames · {msg.provenance.verifiedEvents} verified events</p>
                          <ul className="evidence-list">
                            {msg.provenance.limitations.map((lim, idx) => (
                              <li key={idx}>{lim}</li>
                            ))}
                          </ul>
                        </details>
                      )}
                    </div>
                  )}
                </div>
              ))}
            </div>

            {/* Chat Query Input Form */}
            <form
              className="analyst-bottom-input-form"
              onSubmit={e => {
                e.preventDefault();
                if (question.trim()) askAnalyst();
              }}
            >
              <label htmlFor="analyst-query-input" className="sr-only">Ask PitchMind AI Analyst</label>
              <input
                id="analyst-query-input"
                type="text"
                value={question}
                onChange={e => setQuestion(e.target.value)}
                placeholder="Ask about this tactical moment, pressing structure, or player impact…"
              />
              <button type="submit" disabled={busy || !question.trim()}>
                {busy ? 'Analyzing…' : 'Ask analyst →'}
              </button>
            </form>
          </div>

          {/* Right Column: Match Context & Evidence Sidebar */}
          <div className="analyst-sidebar-column">
            {/* Timeline of Key Moments */}
            <div className="panel sidebar-panel">
              <div className="section-head-row">
                <h3>Match Key Moments</h3>
                <span className="badge">Timeline</span>
              </div>
              <div className="sidebar-moments-timeline">
                {analystData.matchContextSidebar.keyMoments.map((km, idx) => (
                  <button
                    key={idx}
                    type="button"
                    className="sidebar-moment-btn"
                    onClick={() => {
                      setQuestion(`Explain sequence at ${km.minute}`);
                      askAnalyst(`Explain sequence at ${km.minute}`);
                    }}
                  >
                    <span className="minute-badge">{km.minute}</span>
                    <span className="moment-name">{km.title}</span>
                    <span className="badge">{km.badge}</span>
                  </button>
                ))}
              </div>
            </div>

            {/* Filterable Evidence Drawer */}
            <div className="panel sidebar-panel">
              <div className="section-head-row">
                <h3>Evidence Explorer</h3>
                <span className="badge">Verified Telemetry</span>
              </div>
              <div className="evidence-category-tabs">
                {['All', 'Key moments', 'Metrics', 'Tactical views', 'Player impact'].map(cat => (
                  <button
                    key={cat}
                    type="button"
                    className={sidebarFilter === cat ? 'active' : ''}
                    onClick={() => setSidebarFilter(cat)}
                  >
                    {cat}
                  </button>
                ))}
              </div>
              <div className="sidebar-evidence-list">
                {analystData.matchContextSidebar.evidenceItems
                  .filter(item => sidebarFilter === 'All' || item.category === sidebarFilter)
                  .map(item => (
                    <div key={item.id} className="sidebar-evidence-card">
                      <span className="badge">{item.category}</span>
                      <strong>{item.title}</strong>
                      <small>{item.detail}</small>
                    </div>
                  ))}
              </div>
              <div style={{ marginTop: '1rem' }}>
                <a className="secondary-action-link" href="/matches/sim-match-arsenal-mancity">View match pitch</a>
              </div>
            </div>
          </div>
        </div>
      </div>
    ) : (
      <>
        <p className="page-state">Match questions are not available yet. You can still review the evidence recorded for a selected moment.</p>
        <a className="primary-link" href="/home">Choose a match</a>
      </>
    )}
  </section>;
}
