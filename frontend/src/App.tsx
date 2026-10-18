import { useState, useSyncExternalStore } from 'react';
import { DEFAULT_ANALYST_AVATAR, getHostAccessToken, getStoredAuthSession, logoutSimulated, subscribeAuth } from './api';
import { RoutePage } from './ShellPages';
import { CanonicalMatchPage } from './CanonicalMatchPage';
import { RealtimeAuthProvider } from './RealtimeMatchUpdates';
import { contextHref, currentLocation, resolveRoute, selectedTimeFromSearch, subscribeLocation } from './routes';
import { CandidateLiveMatchPage } from './CandidateLiveMatchPage';
import { toggleCandidateStream, useCandidateStreamStatus } from './candidateLiveStatus';

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
  const matchId = route.kind === 'matches' ? route.matchId ?? params.get('matchId') ?? undefined
    : params.get('matchId') ?? (route.kind !== 'welcome' ? candidateStream.status?.matchId ?? undefined : undefined);
  const selectedSeconds = params.has('atSeconds') ? selectedTimeFromSearch(search)
    : candidateStream.status?.matchId === matchId ? candidateStream.status?.currentClockValue ?? undefined : undefined;
  const selectedAtSeconds = selectedSeconds ?? (route.kind === 'matches' ? 5400 : 0);
  const realtimeMode = params.get('mode') === 'live' || selectedSeconds === undefined ? 'LIVE' : 'HISTORICAL';
  const revisionValue = Number(params.get('revision'));
  const revision = Number.isInteger(revisionValue) && revisionValue > 0 ? revisionValue : undefined;
  const links = [
    { label: 'Home', path: '/home', active: route.kind === 'home' },
    { label: 'Match', path: candidateStream.status?.matchId === matchId ? '/candidate-live' : '/matches', active: route.kind === 'candidate-live' || route.kind === 'matches' || route.kind === 'demo-match' },
    { label: 'Teams', path: '/teams', active: route.kind === 'teams' },
    { label: 'Players', path: '/players', active: route.kind === 'players' },
    { label: 'Insights', path: '/insights', active: route.kind === 'insights' },
    { label: 'AI Analyst', path: '/analyst', active: route.kind === 'analyst' }
  ];
  const contextMode = realtimeMode === 'LIVE' ? 'live' : undefined;

  const activeUser = authSession?.user ?? {
    name: 'Guest analyst',
    email: '',
    role: 'Guest',
    permissions: [],
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
      <span className="tag sr-only">Match data with source details</span>
    </header>
    <main id="main-content" tabIndex={-1}>
      {route.kind === 'candidate-live' || (route.kind === 'matches' && !route.matchId && !params.has('matchId') && Boolean(candidateStream.status?.matchId))
        ? <CandidateLiveMatchPage selectedSeconds={selectedSeconds} />
      : route.kind === 'matches' && matchId && !matchId.startsWith('sim-')
        ? <RealtimeAuthProvider getAccessToken={getHostAccessToken}>
          {/* Preserve replay state as the selected revision changes between seconds. */}
          <CanonicalMatchPage key={`${matchId}:${route.replay}`} matchId={matchId} selectedSeconds={selectedAtSeconds}
            requestedRevision={revision} replay={route.replay} initialMode={realtimeMode} />
        </RealtimeAuthProvider>
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
