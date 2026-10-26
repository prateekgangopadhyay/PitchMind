import { useEffect, useState } from 'react';
import { clock } from './api';
import { resolveClubLogo, clubInitials, KNOWN_MATCH_FIXTURES } from './clubLogos';
import { contextHref } from './routes';
import { useMatchContext, phaseLabel } from './MatchContextPages';

export interface TopMatchScoreboardAndSliderProps {
  matchId?: string;
  selectedSeconds?: number;
  revision?: number;
  pathname: string;
  isLiveCandidate?: boolean;
}

export function TopMatchScoreboardAndSlider({
  matchId,
  selectedSeconds = 0,
  revision,
  pathname,
  isLiveCandidate = false
}: TopMatchScoreboardAndSliderProps) {
  const [localSeconds, setLocalSeconds] = useState(selectedSeconds);

  useEffect(() => {
    setLocalSeconds(selectedSeconds);
  }, [selectedSeconds]);

  const selectTime = (nextSeconds: number) => {
    const currentPath = typeof window === 'undefined' ? (pathname || '/matches') : window.location.pathname;
    window.history.replaceState(null, '', contextHref(currentPath, matchId, nextSeconds, revision));
    window.dispatchEvent(new Event('pitchmind:locationchange'));
  };

  const handleSliderChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const next = Number(e.target.value);
    setLocalSeconds(next);
    selectTime(next);
  };

  // Avoid firing duplicate canonical BFF network calls when on canonical /matches routes
  const isCanonicalRoute = Boolean(
    (pathname.startsWith('/matches') || pathname.startsWith('/replay')) &&
    matchId && !matchId.startsWith('sim-')
  );
  const contextData = useMatchContext(isCanonicalRoute ? undefined : matchId, selectedSeconds, revision);
  const context = isCanonicalRoute ? null : contextData.context;

  // Derive authentic team details from match context or known fixture mapping (NEVER hardcoded fake clubs)
  const known = matchId ? KNOWN_MATCH_FIXTURES[matchId] : undefined;
  const homeName = context?.homeTeam?.name || known?.home || 'Home FC';
  const awayName = context?.awayTeam?.name || known?.away || 'Away FC';

  // Dynamic scores mapped to elapsed time
  const hasLoadedMatch = Boolean(context || (matchId && isLiveCandidate));
  const homeScore = context?.homeTeam?.score !== undefined ? context.homeTeam.score : (hasLoadedMatch ? 0 : '—');
  const awayScore = context?.awayTeam?.score !== undefined ? context.awayTeam.score : (hasLoadedMatch ? 0 : '—');

  // Resolve club crests
  const homeLogo = context?.homeTeam?.logoUrl || resolveClubLogo(homeName);
  const awayLogo = context?.awayTeam?.logoUrl || resolveClubLogo(awayName);

  // Dynamic venue and match metadata
  const venue = known?.venue || (homeName && homeName !== 'Home FC' ? `${homeName} Ground` : 'Premier League Venue');
  const currentClockFormatted = clock(localSeconds);
  const phaseText = context?.phase ? phaseLabel(context.phase) : (localSeconds >= 2700 ? 'Second Half' : 'First Half');
  const effectiveMaxSeconds = 5400;
  const progressPct = Math.min(100, Math.max(0, (localSeconds / effectiveMaxSeconds) * 100));

  const jumpToLive = () => {
    const currentPath = typeof window === 'undefined' ? (pathname || '/matches') : window.location.pathname;
    window.history.replaceState(null, '', contextHref(currentPath, matchId, undefined, revision, 'live'));
    window.dispatchEvent(new Event('pitchmind:locationchange'));
  };

  return (
    <section className="universal-top-scoreboard-banner" aria-label="Live Match Scoreboard and Time Scrubber">
      <div className="scoreboard-main-container">
        {/* Left Side: Home Club with Crest & Name */}
        <div className="scoreboard-team-box home-side">
          <div className="team-crest-wrap">
            {homeLogo ? (
              <img src={homeLogo} alt={`${homeName} crest`} className="scoreboard-big-crest" />
            ) : (
              <span className="scoreboard-monogram home">{clubInitials(homeName)}</span>
            )}
          </div>
          <div className="team-meta">
            <span className="team-role-tag">HOME</span>
            <h2 className="team-name">{homeName}</h2>
          </div>
        </div>

        {/* Center: Match Score, Live Clock, and Phase */}
        <div className="scoreboard-center-box">
          <div className="scoreboard-meta-topline">
            <span className="competition-pill">
              <span className="pill-icon" aria-hidden="true">🏆</span> Premier League
            </span>
            {hasLoadedMatch && (
              <span className="venue-pill">
                <span className="pill-icon" aria-hidden="true">📍</span> {venue}
              </span>
            )}
          </div>

          <div className="score-and-clock-row">
            <span className="score-digit home" aria-label={`${homeName} score: ${homeScore}`}>
              {homeScore}
            </span>

            <div className="clock-phase-cluster">
              <div
                className="clock-time-badge"
                role="button"
                tabIndex={0}
                onClick={jumpToLive}
                onKeyDown={e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); jumpToLive(); } }}
                title={isLiveCandidate ? 'Live stream synchronized · Click to jump to live' : 'Play time'}
                style={{ cursor: isLiveCandidate ? 'pointer' : 'default' }}
              >
                <span className={`live-pulse-dot ${isLiveCandidate ? 'pulse' : ''}`} aria-hidden="true"></span>
                <span className="clock-digits">{currentClockFormatted}</span>
              </div>
              <span className="match-phase-label">{phaseText}</span>
            </div>

            <span className="score-digit away" aria-label={`${awayName} score: ${awayScore}`}>
              {awayScore}
            </span>
          </div>
        </div>

        {/* Right Side: Away Club with Crest & Name */}
        <div className="scoreboard-team-box away-side">
          <div className="team-meta">
            <span className="team-role-tag">AWAY</span>
            <h2 className="team-name">{awayName}</h2>
          </div>
          <div className="team-crest-wrap">
            {awayLogo ? (
              <img src={awayLogo} alt={`${awayName} crest`} className="scoreboard-big-crest" />
            ) : (
              <span className="scoreboard-monogram away">{clubInitials(awayName)}</span>
            )}
          </div>
        </div>

        {/* Far Right: Clean View Navigation Controls */}
        <div className="scoreboard-actions-group">
          <a
            href={contextHref('/candidate-live', matchId, localSeconds, revision)}
            className={`scoreboard-action-pill ${pathname === '/candidate-live' || pathname === '/matches' ? 'active' : ''}`}
            aria-label="Open Match View"
          >
            <span className="action-pill-icon" aria-hidden="true">⚽</span>
            <span>Match View</span>
          </a>
          <a
            href={contextHref('/analyst', matchId, localSeconds, revision)}
            className={`scoreboard-action-pill ${pathname === '/analyst' ? 'active' : ''}`}
            aria-label="Open AI Analyst"
          >
            <span className="action-pill-icon" aria-hidden="true">✦</span>
            <span>AI Analyst</span>
          </a>
        </div>
      </div>

      {/* Universal Continuous Time Scrubber Slider (Maps with elapsed time across all pages) */}
      <div className="universal-time-scrubber-bar" aria-label="Match timeline scrubber">
        <div className="scrubber-header-row">
          <span className="scrubber-legend-start">00:00 (Kick-off)</span>
          <span className="scrubber-current-callout">
            <span className="callout-clock-dot" aria-hidden="true"></span>
            Elapsed {currentClockFormatted} · {phaseText}
          </span>
          <span className="scrubber-legend-end">{clock(effectiveMaxSeconds)} (Full-Time)</span>
        </div>
        <div className="scrubber-slider-container">
          <label htmlFor="universal-match-slider" className="sr-only">
            Match elapsed time scrubber
          </label>
          <input
            id="universal-match-slider"
            type="range"
            min="0"
            max={effectiveMaxSeconds}
            step="1"
            value={localSeconds}
            aria-valuetext={currentClockFormatted}
            onChange={handleSliderChange}
            className="universal-time-slider"
            style={{ '--progress': `${progressPct}%` } as React.CSSProperties}
          />
        </div>
      </div>
    </section>
  );
}
