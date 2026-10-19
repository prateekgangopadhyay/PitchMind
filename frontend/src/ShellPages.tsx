
import { useEffect, useRef, useState } from 'react';
import {
  clock,
  provenanceLabel,
  request,
  loginSimulated,
  loginWithGoogleSimulated,
  getStoredAuthSession,
  setStoredAuthSession
} from './api';
import { isEntraAuthConfigured, isEntraAuthMode, signInWithMicrosoft } from './entraAuth';
import type {
  CanonicalMatchPage,
  AuthSession
} from './api';
import type { AppRoute } from './routes';
import { contextHref, replacePhaseFilterContext, selectedTimeFromSearch } from './routes';
import { MatchContextAnalystPage, MatchContextInsightsPage, MatchContextPlayersPage, MatchContextTeamsPage, MatchSelectionPage } from './MatchContextPages';

const CANONICAL_PHASES = ['notstarted', 'firsthalf', 'halftime', 'secondhalf', 'fulltime'];

function phaseLabel(phase: string) {
  const labels: Record<string, string> = {
    notstarted: 'Not started', firsthalf: 'First half', halftime: 'Half-time',
    secondhalf: 'Second half', fulltime: 'Full-time'
  };
  return labels[phase.toLowerCase()] ?? phase.replace(/([a-z])([A-Z])/g, '$1 $2').replaceAll('-', ' ').replaceAll('_', ' ');
}

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
  const retry = () => setAttempt(a => a + 1);
  const loadMore = async () => {
    if (!page?.nextMatchId || loadingMore) return;
    const generation = requestGeneration.current;
    const controller = new AbortController();
    pageController.current = controller;
    setLoadingMore(true);
    setMoreError('');
    try {
      const next = await request<CanonicalMatchPage>(`canonical/fixtures?limit=20&afterMatchId=${encodeURIComponent(page.nextMatchId)}`, controller.signal);
      if (generation === requestGeneration.current) {
        setPage(current => current ? { ...next, items: [...current.items, ...next.items] } : next);
      }
    } catch {
      if (generation === requestGeneration.current) {
        setMoreError('Could not load additional matches. Try again.');
      }
    } finally {
      if (generation === requestGeneration.current) {
        setLoadingMore(false);
        pageController.current = null;
      }
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

/* =========================================================================
   SCREEN 01: WELCOME / LOGIN (01-login.jpg)
   ========================================================================= */
export function WelcomePage() {
  const entraMode = isEntraAuthMode();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [remember, setRemember] = useState(true);
  const [busy, setBusy] = useState(false);
  const [session, setSession] = useState<AuthSession | null>(null);
  const [authError, setAuthError] = useState('');

  const handleGoogleSignIn = async () => {
    setBusy(true);
    setAuthError('');
    try {
      const result = await loginWithGoogleSimulated();
      setSession(result);
      setStoredAuthSession(result);
      window.history.pushState({}, '', '/home');
      window.dispatchEvent(new CustomEvent('pitchmind:locationchange'));
    } catch (error) {
      setAuthError(error instanceof Error ? error.message : 'Google sign-in is unavailable. Please retry.');
    } finally {
      setBusy(false);
    }
  };

  const handleMicrosoftSignIn = async () => {
    setBusy(true);
    setAuthError('');
    try {
      await signInWithMicrosoft();
      const result = getStoredAuthSession();
      if (!result) throw new Error('Microsoft sign-in completed without an account session.');
      setSession(result);
      window.history.pushState({}, '', '/home');
      window.dispatchEvent(new CustomEvent('pitchmind:locationchange'));
    } catch (error) {
      setAuthError(error instanceof Error ? error.message : 'Microsoft sign-in is unavailable. Please retry.');
    } finally {
      setBusy(false);
    }
  };

  const signIn = async (action: () => Promise<AuthSession>) => {
    setBusy(true);
    setAuthError('');
    try {
      const result = await action();
      setStoredAuthSession(result);
      setSession(result);
      window.history.pushState({}, '', '/home');
      window.dispatchEvent(new CustomEvent('pitchmind:locationchange'));
    } catch (error) {
      setAuthError(error instanceof Error ? error.message : 'Sign in is unavailable. Please try again.');
    } finally {
      setBusy(false);
    }
  };

  const handleSignIn = (event: React.FormEvent) => {
    event.preventDefault();
    void signIn(() => loginSimulated({ email, password }));
  };

  return <section className="content-page welcome-screen-root">
    <div className="welcome-split-layout">
      <div className="welcome-left-col">
        <h1 className="welcome-hero-headline">Football intelligence, grounded in evidence.</h1>
        <p>Choose a match to explore the data available from its connected feed.</p>
        <a className="primary-link" href="/home">Browse available matches</a>
      </div>
      <div className="welcome-right-col">
        <div className="welcome-auth-card">
          <div className="auth-card-brand">
            <img className="auth-brand-logo" src="/brand/pitchmind-symbol.svg" width="48" height="48" alt="" aria-hidden="true" />
            <h2>PitchMind</h2>
          </div>
          <h3 className="auth-heading">Sign in to your account</h3>
          <p className="auth-subtext">Access match analysis for the match selected in your workspace.</p>
          {authError && <div className="notice error" role="alert">{authError}</div>}
          {session && <div className="notice success" role="status">Signed in as <strong>{session.user.name}</strong></div>}
            {entraMode ? <div className="auth-form-fields">
              <p className="auth-subtext">Use your organization account. PitchMind never stores your Microsoft access token.</p>
              {!isEntraAuthConfigured() && <p className="notice error" role="status">Sign-in is awaiting the Entra SPA client, API permission, and this site’s redirect URI.</p>}
              <button type="button" className="auth-submit-btn" onClick={() => { void handleMicrosoftSignIn(); }} disabled={busy || !isEntraAuthConfigured()}>
                {busy ? 'Connecting…' : 'Sign in with Microsoft'}
              </button>
            </div> : <form onSubmit={handleSignIn} className="auth-form-fields">
              <div className="auth-field-group">
                <label htmlFor="welcome-email">Email address</label>
                <div className="auth-input-wrap">
                  <span className="input-icon" aria-hidden="true">
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                      <rect x="2" y="4" width="20" height="16" rx="2" /><path d="m22 7-10 6L2 7" />
                    </svg>
                  </span>
                  <input
                    id="welcome-email"
                    type="email"
                    autoComplete="email"
                    required
                    placeholder="you@yourclub.com"
                    value={email}
                    onChange={e => setEmail(e.target.value)}
                  />
                </div>
              </div>

              <div className="auth-field-group">
                <label htmlFor="welcome-password">Password</label>
                <div className="auth-input-wrap">
                  <span className="input-icon" aria-hidden="true">
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                      <rect x="3" y="11" width="18" height="11" rx="2" ry="2" /><path d="M7 11V7a5 5 0 0 1 10 0v4" />
                    </svg>
                  </span>
                  <input
                    id="welcome-password"
                    type={showPassword ? 'text' : 'password'}
                    autoComplete="current-password"
                    required
                    placeholder="Enter your password"
                    value={password}
                    onChange={e => setPassword(e.target.value)}
                  />
                  <button
                    type="button"
                    className="password-toggle-btn"
                    onClick={() => setShowPassword(!showPassword)}
                    aria-label={showPassword ? 'Hide password' : 'Show password'}
                  >
                    {showPassword ? (
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
                        <path d="M9.88 9.88a3 3 0 1 0 4.24 4.24M10.73 5.08A10.43 10.43 0 0 1 12 5c7 0 10 7 10 7a13.16 13.16 0 0 1-1.67 2.68M6.61 6.61A13.526 13.526 0 0 0 2 12s3 7 10 7a9.74 9.74 0 0 0 5.39-1.61M2 2l20 20" />
                      </svg>
                    ) : (
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
                        <path d="M2 12s3-7 10-7 10 7 10 7-3 7-10 7-10-7-10-7Z" /><circle cx="12" cy="12" r="3" />
                      </svg>
                    )}
                  </button>
                </div>
              </div>

              <div className="auth-options-row">
                <label className="checkbox-wrap">
                  <input
                    type="checkbox"
                    checked={remember}
                    onChange={e => setRemember(e.target.checked)}
                  />
                  <span>Remember me</span>
                </label>
                <a href="#forgot" className="forgot-link">Forgot password?</a>
              </div>

              <button type="submit" className="auth-submit-btn" disabled={busy}>
                {busy ? 'Signing in…' : 'Sign in →'}
              </button>

              <div className="auth-divider">
                <span>or continue with</span>
              </div>

              <div className="auth-social-buttons-stack">
                <button
                  type="button"
                  className="auth-google-btn"
                  onClick={handleGoogleSignIn}
                  disabled={busy}
                  aria-label="Continue with Google using the demo sign-in flow"
                >
                  <svg className="google-g-icon" width="18" height="18" viewBox="0 0 24 24" aria-hidden="true">
                    <path fill="#4285F4" d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92c-.26 1.37-1.04 2.53-2.21 3.31v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.09z" />
                    <path fill="#34A853" d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z" />
                    <path fill="#FBBC05" d="M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.06H2.18C1.43 8.55 1 10.22 1 12s.43 3.45 1.18 4.94l2.85-2.22.81-.63z" />
                    <path fill="#EA4335" d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.06l3.66 2.84c.87-2.6 3.3-4.52 6.16-4.52z" />
                  </svg>
                  <span>Google</span>
                  <small className="provider-option-status">Demo</small>
                </button>

                <button type="button" className="auth-provider-btn" disabled aria-label="Apple sign-in coming soon">
                  <svg className="provider-brand-icon" viewBox="0 0 24 24" aria-hidden="true">
                    <path fill="currentColor" d="M12.152 6.896c-.948 0-2.415-1.078-3.96-1.04c-2.04.027-3.91 1.183-4.961 3.014c-2.117 3.675-.546 9.103 1.519 12.09c1.013 1.454 2.208 3.09 3.792 3.039c1.52-.065 2.09-.987 3.935-.987c1.831 0 2.35.987 3.96.948c1.637-.026 2.676-1.48 3.676-2.948c1.156-1.688 1.636-3.325 1.662-3.415c-.039-.013-3.182-1.221-3.22-4.857c-.026-3.04 2.48-4.494 2.597-4.559c-1.429-2.09-3.623-2.324-4.39-2.376c-2-.156-3.675 1.09-4.61 1.09zM15.53 3.83c.843-1.012 1.4-2.427 1.245-3.83c-1.207.052-2.662.805-3.532 1.818c-.78.896-1.454 2.338-1.273 3.714c1.338.104 2.715-.688 3.559-1.701z" />
                  </svg>
                  <span>Apple</span>
                  <small className="provider-option-status">Soon</small>
                </button>

                <button type="button" className="auth-provider-btn" disabled aria-label="Microsoft Entra ID sign-in coming soon">
                  <svg width="18" height="18" viewBox="0 0 21 21" aria-hidden="true">
                    <rect x="1" y="1" width="9" height="9" fill="#f25022" />
                    <rect x="11" y="1" width="9" height="9" fill="#7fba00" />
                    <rect x="1" y="11" width="9" height="9" fill="#00a4ef" />
                    <rect x="11" y="11" width="9" height="9" fill="#ffb900" />
                  </svg>
                  <span>Microsoft</span>
                  <small className="provider-option-status">Soon</small>
                  <span className="sr-only">Sign in with Microsoft Entra ID</span>
                </button>

                <button type="button" className="auth-provider-btn" disabled aria-label="X sign-in coming soon">
                  <svg className="provider-brand-icon" viewBox="0 0 24 24" aria-hidden="true">
                    <path fill="currentColor" d="M18.901 1.153h3.68l-8.04 9.19L24 22.846h-7.406l-5.8-7.584-6.638 7.584H.474l8.6-9.83L0 1.154h7.594l5.243 6.932ZM17.61 20.644h2.039L6.486 3.24H4.298Z" />
                  </svg>
                  <span>X</span>
                  <small className="provider-option-status">Soon</small>
                </button>

                <button type="button" className="auth-provider-btn" disabled aria-label="GitHub sign-in coming soon">
                  <svg className="provider-brand-icon" viewBox="0 0 24 24" aria-hidden="true">
                    <path fill="currentColor" d="M12 .297c-6.63 0-12 5.373-12 12 0 5.303 3.438 9.8 8.205 11.385.6.113.82-.258.82-.577 0-.285-.01-1.04-.015-2.04-3.338.724-4.042-1.61-4.042-1.61C4.422 18.07 3.633 17.7 3.633 17.7c-1.087-.744.084-.729.084-.729 1.205.084 1.838 1.236 1.838 1.236 1.07 1.835 2.809 1.305 3.495.998.108-.776.417-1.305.76-1.605-2.665-.3-5.466-1.332-5.466-5.93 0-1.31.465-2.38 1.235-3.22-.135-.303-.54-1.523.105-3.176 0 0 1.005-.322 3.3 1.23.96-.267 1.98-.399 3-.405 1.02.006 2.04.138 3 .405 2.28-1.552 3.285-1.23 3.285-1.23.645 1.653.24 2.873.12 3.176.765.84 1.23 1.91 1.23 3.22 0 4.61-2.805 5.625-5.475 5.92.42.36.81 1.096.81 2.22 0 1.606-.015 2.896-.015 3.286 0 .315.21.69.825.57C20.565 22.092 24 17.592 24 12.297c0-6.627-5.373-12-12-12z" />
                  </svg>
                  <span>GitHub</span>
                  <small className="provider-option-status">Soon</small>
                </button>

                <button type="button" className="auth-provider-btn" disabled aria-label="Facebook sign-in coming soon">
                  <svg className="provider-brand-icon" viewBox="0 0 512 512" aria-hidden="true">
                    <path fill="#0866FF" d="M504 256C504 119 393 8 256 8S8 119 8 256c0 123.78 90.69 226.38 209.25 245V327.69h-63V256h63v-54.64c0-62.15 37-96.48 93.67-96.48 27.14 0 55.52 4.84 55.52 4.84v61h-31.27c-30.81 0-40.42 19.12-40.42 38.73V256h68.78l-11 71.69h-57.78V501C413.31 482.38 504 379.78 504 256z" />
                  </svg>
                  <span>Facebook</span>
                  <small className="provider-option-status">Soon</small>
                </button>
              </div>
            </form>}
        </div>
      </div>
    </div>
  </section>;
}

/* =========================================================================
   SCREEN 02: HOME / MATCH LIBRARY
   ========================================================================= */
export function HomePage({ search = '', selectedMatchId, selectedSeconds }: {
  search?: string; selectedMatchId?: string; selectedSeconds?: number;
}) {
  const { page, error, loading, loadingMore, moreError, retry, loadMore } = useCanonicalFixtures();
  const matches = page?.items ?? [];
  const requestedPhase = new URLSearchParams(search).get('phase') ?? 'all';
  const phases = [...new Set([...CANONICAL_PHASES, ...matches.map(match => match.phase)])].sort();
  const phaseFilter = requestedPhase === 'all' || phases.includes(requestedPhase) ? requestedPhase : 'all';
  const visibleMatches = phaseFilter === 'all' ? matches : matches.filter(match => match.phase === phaseFilter);

  return <section className="content-page screen-page home-page">
    <div className="home-heading-row">
      <PageHeading eyebrow="Home · match library" title="Choose a match" question="Scores and match phases come from canonical match records." />
      {!loading && !error && matches.length > 0 && <label className="phase-filter">Filter by phase
        <select aria-label="Filter matches by phase" value={phaseFilter} onChange={event => replacePhaseFilterContext(event.target.value)}>
          <option value="all">All phases</option>{phases.map(phase => <option key={phase} value={phase}>{phaseLabel(phase)}</option>)}
        </select>
      </label>}
      {!loading && !error && page && <button className="refresh-matches" type="button" onClick={retry}>Refresh matches</button>}
    </div>
    {loading ? <PageState loading empty="" />
      : error ? <div className="page-state error-copy" role="alert"><p>{error}</p><button type="button" onClick={retry}>Try loading matches again</button></div>
        : matches.length === 0 ? <PageState loading={false} empty="No canonical matches are available yet." />
          : visibleMatches.length === 0 ? <p className="page-state" role="status">No matches are available in this phase.</p>
            : <><p className="match-count" role="status" aria-live="polite">{visibleMatches.length} {visibleMatches.length === 1 ? 'match' : 'matches'}</p>
              <ul className="match-list" aria-label="Matches">{visibleMatches.map(match => {
                const matchPath = `/matches/${encodeURIComponent(match.matchId)}`;
                return <li className="match-list-item" key={match.matchId}><article className="match-card match-row">
                  <div className="match-card-copy"><div className="match-card-topline"><span className="badge">{provenanceLabel(match.provenance)}</span><span className="phase-tag">{phaseLabel(match.phase)}</span></div>
                    <h2><span className="match-side-label">Home</span><span className="match-score">{match.homeScore}–{match.awayScore}</span><span className="match-side-label">Away</span></h2>
                    <p>Recorded at {clock(match.asOfSeconds)}</p>
                    <details className="match-source-details"><summary>Match and source details</summary>
                      <p className="match-id">Match ID <code translate="no">{match.matchId}</code></p><p>Revision {match.canonicalRevision} · source {match.provenance} · algorithm {match.algorithmVersion}</p>
                      <p>Source hash {match.sourceSha256}</p></details>
                  </div>
                  <a className="primary-link" href={contextHref(matchPath, match.matchId,
                    (selectedMatchId === match.matchId ? selectedSeconds : undefined) ?? match.asOfSeconds, match.projectionRevision)}>Open match</a>
                </article></li>;
              })}</ul>
            </>}
    {!loading && !error && page?.hasMore && <div className="load-more-wrap">
      {moreError && <p className="error-copy" role="alert">{moreError}</p>}
      <button className="load-more" type="button" onClick={loadMore} disabled={loadingMore || !page.nextMatchId}>{loadingMore ? 'Loading more matches…' : 'More matches'}</button>
    </div>}
    {!loading && !error && matches.length > 0 && <p className="home-contract-limit">Only match facts supplied by the selected data source are shown here.</p>}
  </section>;
}

/* =========================================================================
   MAIN ROUTE DISPATCHER
   ========================================================================= */
export function RoutePage({ route, search }: { route: AppRoute; search: string }) {
  const params = new URLSearchParams(search);
  const selectedMatchId = params.get('matchId') ?? undefined;
  const selectedSeconds = selectedTimeFromSearch(search);
  const hasSelectedTime = params.has('atSeconds');
  const revisionValue = Number(params.get('revision'));
  const revision = Number.isInteger(revisionValue) && revisionValue > 0 ? revisionValue : undefined;

  if (route.kind === 'welcome') return <WelcomePage />;

  if (route.kind === 'home') return <HomePage search={search} selectedMatchId={selectedMatchId} selectedSeconds={hasSelectedTime ? selectedSeconds : undefined} />;

  if (route.kind === 'matches') return <MatchSelectionPage route={route} />;

  if (route.kind === 'teams') return <MatchContextTeamsPage route={route} matchId={selectedMatchId} seconds={selectedSeconds} revision={revision} />;

  if (route.kind === 'players') return <MatchContextPlayersPage route={route} matchId={selectedMatchId} seconds={selectedSeconds} revision={revision} playerId={route.playerId} />;

  if (route.kind === 'insights') return <MatchContextInsightsPage route={route} matchId={selectedMatchId} seconds={selectedSeconds} revision={revision} insightId={route.insightId} />;

  if (route.kind === 'analyst') return <MatchContextAnalystPage route={route} matchId={selectedMatchId} seconds={selectedSeconds} revision={revision} />;

  return (
    <section className="content-page">
      <PageHeading eyebrow="PitchMind" title="This page is unavailable" question="Choose a section from the main navigation." />
      <a className="primary-link" href="/home">Go to available matches</a>
    </section>
  );
}
