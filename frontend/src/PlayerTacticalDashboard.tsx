import { useEffect, useMemo, useState } from 'react';
import type { PlayerIntelligenceData } from './api';
import { getPlayerIntelligence, getPlayersIntelligenceList, calculateAiPlayerKeyQuote } from './api';
import { resolveClubLogo } from './clubLogos';
import { contextHref } from './routes';

export interface PlayerTacticalDashboardProps {
  playerId?: string;
  matchId?: string;
  seconds: number;
  revision?: number;
  onSelectPlayer?: (playerId: string) => void;
  onBackToPlayers?: () => void;
  liveMatchPlayers?: Array<{ id: string; name?: string; teamId: string; active?: boolean; yellowCards?: number; jerseyNumber?: number }>;
  liveMatchTeams?: Array<{ id: string; name: string; score?: number }>;
}

export function PlayerTacticalDashboard({
  playerId,
  matchId,
  seconds,
  revision,
  onSelectPlayer,
  onBackToPlayers,
  liveMatchPlayers,
  liveMatchTeams
}: PlayerTacticalDashboardProps) {
  const [intelligence, setIntelligence] = useState<PlayerIntelligenceData | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);
  const [activeTab, setActiveTab] = useState<'touch' | 'heatmap' | 'actions'>('touch');
  const [selectedImpactIndex, setSelectedImpactIndex] = useState<number>(2); // 56' card active as in screenshot
  const [isPlayingAudio, setIsPlayingAudio] = useState<boolean>(false);
  const [availablePlayers, setAvailablePlayers] = useState<PlayerIntelligenceData[]>([]);

  // Load available players list for dropdown & roster
  useEffect(() => {
    let cancelled = false;
    getPlayersIntelligenceList({ matchId, seconds })
      .then(list => {
        if (!cancelled && list.length > 0) {
          setAvailablePlayers(list);
        }
      })
      .catch(() => {
        // Non-blocking fallback
      });
    return () => {
      cancelled = true;
    };
  }, [matchId, seconds]);

  // Load specific player intelligence when playerId changes
  useEffect(() => {
    if (!playerId) {
      setIntelligence(null);
      setLoading(false);
      return;
    }

    let cancelled = false;
    setLoading(true);
    setError(null);

    getPlayerIntelligence(playerId, { matchId, seconds })
      .then(data => {
        if (!cancelled) {
          setIntelligence(data);
          setLoading(false);
        }
      })
      .catch(err => {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Failed to retrieve player intelligence');
          setLoading(false);
        }
      });

    return () => {
      cancelled = true;
    };
  }, [playerId, matchId, seconds]);

  // Toggle audio speech synthesis for the AI Analyst section
  const handleToggleAudio = () => {
    if (typeof window === 'undefined' || !('speechSynthesis' in window)) {
      setIsPlayingAudio(!isPlayingAudio);
      return;
    }

    if (isPlayingAudio) {
      window.speechSynthesis.cancel();
      setIsPlayingAudio(false);
    } else {
      const textToSpeak = intelligence?.analystAssessment || 'No analyst assessment available.';
      const utterance = new SpeechSynthesisUtterance(textToSpeak);
      utterance.rate = 1.0;
      utterance.pitch = 1.0;
      utterance.onend = () => setIsPlayingAudio(false);
      utterance.onerror = () => setIsPlayingAudio(false);
      window.speechSynthesis.speak(utterance);
      setIsPlayingAudio(true);
    }
  };

  if (loading) {
    return (
      <div className="player-intel-loading" role="status" aria-live="polite">
        <div className="intel-spinner" />
        <p className="intel-loading-text">Computing player tactical intelligence via AI agent…</p>
        <p className="intel-loading-sub">Analyzing touch clusters, progressive carries, receiving zones, and match impact</p>
      </div>
    );
  }

  if (error || !intelligence) {
    return (
      <div className="player-intel-error" role="alert">
        <div className="error-icon">⚠️</div>
        <h3>Unable to load tactical intelligence</h3>
        <p>{error ?? 'No intelligence data returned for this player.'}</p>
        {onBackToPlayers && (
          <button type="button" className="action-btn back-btn" onClick={onBackToPlayers}>
            ← Back to all players
          </button>
        )}
      </div>
    );
  }

  // Format match clock display (e.g. "67:24")
  const clockDisplay = intelligence.matchContext?.clock ?? `${Math.floor(seconds / 60).toString().padStart(2, '0')}:${(seconds % 60).toString().padStart(2, '0')}`;
  const periodDisplay = intelligence.matchContext?.period ?? (seconds >= 2700 ? 'Second Half' : 'First Half');

  // Home / Away match details
  const home = intelligence.matchContext?.homeTeam ?? {
    name: liveMatchTeams?.[0]?.name ?? 'Home',
    formation: '4-2-3-1',
    score: liveMatchTeams?.[0]?.score ?? 2,
    logo: resolveClubLogo(liveMatchTeams?.[0]?.name ?? '') || ''
  };
  const away = intelligence.matchContext?.awayTeam ?? {
    name: liveMatchTeams?.[1]?.name ?? 'Away',
    formation: '4-3-3',
    score: liveMatchTeams?.[1]?.score ?? 1,
    logo: resolveClubLogo(liveMatchTeams?.[1]?.name ?? '') || ''
  };

  const playerCrest = intelligence.teamLogo || resolveClubLogo(intelligence.club) || '';
  const opponentCrest = resolveClubLogo(intelligence.opponentClub || '') || '';

  const xAVal = intelligence.matchStats?.xA ?? intelligence.matchStats?.xa ?? 0.32;
  const carryCount = intelligence.progressiveCarriesStat?.count ?? intelligence.matchStats?.progressiveCarries ?? 0;
  const carryAvg = intelligence.progressiveCarriesStat?.vsSeasonAvg ?? '+50% vs season avg';
  const chanceCount = intelligence.chanceCreationStat?.count ?? intelligence.matchStats?.chancesCreated ?? 0;
  const chanceAvg = intelligence.chanceCreationStat?.vsSeasonAvg ?? '+60% vs season avg';

  return (
    <div className="player-tactical-dashboard" aria-label={`Tactical Intelligence for ${intelligence.name}`}>
      {/* =========================================================================
          TOP SECTION: PLAYER HERO BANNER
          ========================================================================= */}
      <section className="player-hero-card" aria-label="Player profile and match status">
        {/* Giant watermark shirt number in background */}
        <div className="watermark-number" aria-hidden="true">
          {intelligence.number}
        </div>

        {/* Left: Cutout, Badge, Name, Role, Chips */}
        <div className="player-hero-identity">
          <div className="player-cutout-wrapper">
            <div className="cutout-aura" />
            <img
              src={intelligence.photoUrl || intelligence.photo || `/players/${intelligence.id}.jpg`}
              alt={intelligence.name}
              className="player-cutout-img"
              onError={(e) => {
                // Graceful fallback if player portrait is not found
                (e.target as HTMLImageElement).src = intelligence.teamLogo || playerCrest || '/crests/manchester-united.svg';
              }}
            />
          </div>

          <div className="player-info-meta">
            <div className="player-name-row">
              <img src={playerCrest} alt={intelligence.club} className="player-club-crest" />
              <h1 className="player-fullname">{intelligence.name}</h1>
              <span className="verified-badge" title="Verified match player" aria-label="Verified player">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none">
                  <circle cx="12" cy="12" r="10" fill="#00f59b" />
                  <path d="M7 12.5L10.5 16L17 9" stroke="#06121e" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round" />
                </svg>
              </span>
            </div>

            <p className="player-club-role">
              {intelligence.club} • {intelligence.role}
            </p>

            <div className="player-tags-row">
              <span className="player-tag-country">
                <span className="flag-icon" role="img" aria-label={intelligence.nationality}>
                  🏴󠁧󠁢󠁥󠁮󠁧󠁿
                </span>
                {intelligence.nationality}
              </span>
              <span className="player-chip">#{intelligence.number}</span>
              <span className="player-chip">{intelligence.role.includes('(') ? intelligence.role.split('(')[1].replace(')', '') : 'RW'}</span>
              <span className="player-chip">{intelligence.preferredFoot ?? 'Right foot'}</span>
            </div>
          </div>
        </div>

        {/* Middle: 5 Primary Stats + Star Tactical Quote */}
        <div className="player-hero-stats-center">
          <div className="hero-metrics-grid">
            <div className="hero-metric-item">
              <span className="metric-val">{intelligence.minutesPlayed}</span>
              <span className="metric-lbl">Minutes played</span>
            </div>
            <div className="hero-metric-item">
              <span className="metric-val">{intelligence.matchStats?.chancesCreated ?? 0}</span>
              <span className="metric-lbl">Chances created</span>
            </div>
            <div className="hero-metric-item">
              <span className="metric-val">{intelligence.matchStats?.progressiveCarries ?? 0}</span>
              <span className="metric-lbl">Progressive carries</span>
            </div>
            <div className="hero-metric-item">
              <span className="metric-val">{xAVal.toFixed(2)}</span>
              <span className="metric-lbl">xA contribution</span>
            </div>
            <div className="hero-metric-item">
              <span className="metric-val">{intelligence.matchStats?.duelsWon ?? '50%'}</span>
              <span className="metric-lbl">Duels won</span>
            </div>
          </div>

          <div className="hero-star-quote">
            <span className="star-icon" aria-hidden="true">★</span>
            <p className="quote-text">
              {intelligence.keyQuote || calculateAiPlayerKeyQuote(intelligence, { seconds })}
            </p>
          </div>
        </div>

        {/* Right: Player Switcher & Roster Navigation (Scoreboard removed from Players tab) */}
        <div className="player-hero-scoreboard-panel">
          <div className="player-actions-row">
            {availablePlayers.length > 0 ? (
              <div className="change-player-dropdown-wrapper">
                <select
                  aria-label="Change player"
                  className="change-player-select"
                  value={intelligence.id}
                  onChange={(e) => {
                    const nextId = e.target.value;
                    if (onSelectPlayer) {
                      onSelectPlayer(nextId);
                    } else if (typeof window !== 'undefined') {
                      window.location.href = contextHref(`/players/${nextId}`, matchId, seconds, revision);
                    }
                  }}
                >
                  <option value="" disabled>Change player ▾</option>
                  {availablePlayers.map(p => (
                    <option key={p.id} value={p.id}>
                      {p.name} (#{p.number} · {p.role})
                    </option>
                  ))}
                </select>
              </div>
            ) : (
              <button
                type="button"
                className="change-player-btn"
                onClick={() => onBackToPlayers && onBackToPlayers()}
              >
                Change player ▾
              </button>
            )}

            {onBackToPlayers && (
              <button
                type="button"
                className="view-roster-btn"
                onClick={onBackToPlayers}
                title="View all match players"
              >
                All Players
              </button>
            )}
          </div>
        </div>
      </section>

      {/* =========================================================================
          MIDDLE ROW: INVOLVEMENT MAP | PROGRESSIVE CARRIES | CHANCE CREATION | WHY IT MATTERS
          ========================================================================= */}
      <section className="tactical-quad-row middle-row" aria-label="Involvement and chance metrics">
        {/* CARD 1: Involvement Map */}
        <article className="tactical-card involvement-card">
          <header className="card-header">
            <div className="header-titles">
              <div className="title-with-icon">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" className="card-icon">
                  <path d="M4 6H20M4 12H20M4 18H20" stroke="#00f59b" strokeWidth="2" strokeLinecap="round" />
                  <circle cx="9" cy="6" r="2" fill="#00f59b" />
                  <circle cx="15" cy="12" r="2" fill="#00f59b" />
                  <circle cx="8" cy="18" r="2" fill="#00f59b" />
                </svg>
                <h2 className="card-title">Involvement Map</h2>
              </div>
              <p className="card-subtitle">Touches, carries and key actions</p>
            </div>

            <div className="tab-pill-group" role="tablist">
              <button
                type="button"
                role="tab"
                aria-selected={activeTab === 'touch'}
                className={`tab-pill ${activeTab === 'touch' ? 'active' : ''}`}
                onClick={() => setActiveTab('touch')}
              >
                Touch map
              </button>
              <button
                type="button"
                role="tab"
                aria-selected={activeTab === 'heatmap'}
                className={`tab-pill ${activeTab === 'heatmap' ? 'active' : ''}`}
                onClick={() => setActiveTab('heatmap')}
              >
                Heatmap
              </button>
              <button
                type="button"
                role="tab"
                aria-selected={activeTab === 'actions'}
                className={`tab-pill ${activeTab === 'actions' ? 'active' : ''}`}
                onClick={() => setActiveTab('actions')}
              >
                Actions
              </button>
            </div>
          </header>

          <div className="pitch-canvas-wrapper">
            <svg viewBox="0 0 105 68" className="tactical-pitch-svg" preserveAspectRatio="xMidYMid meet">
              <defs>
                <linearGradient id="pitchGrassGrad" x1="0%" y1="0%" x2="100%" y2="100%">
                  <stop offset="0%" stopColor="#0b241c" />
                  <stop offset="100%" stopColor="#081a14" />
                </linearGradient>
                <radialGradient id="heatSpot" cx="50%" cy="50%" r="50%">
                  <stop offset="0%" stopColor="#00f59b" stopOpacity="0.7" />
                  <stop offset="50%" stopColor="#00f59b" stopOpacity="0.3" />
                  <stop offset="100%" stopColor="#00f59b" stopOpacity="0" />
                </radialGradient>
                <marker id="carryArrow" viewBox="0 0 10 10" refX="6" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse">
                  <path d="M 0 1 L 8 5 L 0 9 z" fill="#38bdf8" />
                </marker>
                <marker id="passArrow" viewBox="0 0 10 10" refX="6" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse">
                  <path d="M 0 1 L 8 5 L 0 9 z" fill="#f8fafc" />
                </marker>
              </defs>

              {/* Pitch Boundaries and Markings */}
              <rect x="0" y="0" width="105" height="68" fill="url(#pitchGrassGrad)" rx="3" />
              {/* Outer touchlines */}
              <rect x="3" y="3" width="99" height="62" fill="none" stroke="#1f4b39" strokeWidth="0.8" />
              {/* Halfway line */}
              <line x1="52.5" y1="3" x2="52.5" y2="65" stroke="#1f4b39" strokeWidth="0.8" />
              {/* Centre circle */}
              <circle cx="52.5" cy="34" r="9.15" fill="none" stroke="#1f4b39" strokeWidth="0.8" />
              <circle cx="52.5" cy="34" r="0.6" fill="#1f4b39" />

              {/* Left Penalty Area */}
              <rect x="3" y="14" width="16.5" height="40" fill="none" stroke="#1f4b39" strokeWidth="0.8" />
              <rect x="3" y="24.5" width="5.5" height="19" fill="none" stroke="#1f4b39" strokeWidth="0.8" />
              {/* Left Goal */}
              <rect x="0.5" y="30.34" width="2.5" height="7.32" fill="none" stroke="#1f4b39" strokeWidth="0.8" />
              <circle cx="14" cy="34" r="0.6" fill="#1f4b39" />

              {/* Right Penalty Area */}
              <rect x="85.5" y="14" width="16.5" height="40" fill="none" stroke="#1f4b39" strokeWidth="0.8" />
              <rect x="96.5" y="24.5" width="5.5" height="19" fill="none" stroke="#1f4b39" strokeWidth="0.8" />
              <path d="M 85.5 27.5 A 9.15 9.15 0 0 0 85.5 40.5" fill="none" stroke="#1f4b39" strokeWidth="0.8" />
              {/* Right Goal */}
              <rect x="102" y="30.34" width="2.5" height="7.32" fill="none" stroke="#1f4b39" strokeWidth="0.8" />
              <circle cx="91" cy="34" r="0.6" fill="#1f4b39" />

              {/* Dynamic Overlay Rendering based on activeTab */}
              {activeTab === 'heatmap' ? (
                <g className="heatmap-overlay">
                  <circle cx="75" cy="22" r="16" fill="url(#heatSpot)" />
                  <circle cx="88" cy="20" r="14" fill="url(#heatSpot)" />
                  <circle cx="68" cy="28" r="12" fill="url(#heatSpot)" />
                  <circle cx="82" cy="32" r="11" fill="url(#heatSpot)" />
                  <circle cx="55" cy="20" r="9" fill="url(#heatSpot)" />
                </g>
              ) : activeTab === 'actions' ? (
                <g className="actions-overlay">
                  {/* Key actions with trajectories */}
                  {intelligence.progressiveCarries.slice(0, 4).map((c, i) => (
                    <line
                      key={`act-c-${i}`}
                      x1={c.startX}
                      y1={c.startY}
                      x2={c.endX}
                      y2={c.endY}
                      stroke="#38bdf8"
                      strokeWidth="1.6"
                      strokeDasharray="2,1"
                      markerEnd="url(#carryArrow)"
                    />
                  ))}
                  {intelligence.chanceCreation.map((cc, i) => (
                    <g key={`act-cc-${i}`}>
                      <line
                        x1={cc.fromX}
                        y1={cc.fromY}
                        x2={cc.toX}
                        y2={cc.toY}
                        stroke="#f8fafc"
                        strokeWidth="1.6"
                        strokeDasharray="2.5,1.5"
                        markerEnd="url(#passArrow)"
                      />
                      <circle cx={cc.toX} cy={cc.toY} r="2.2" fill="#f59e0b" />
                    </g>
                  ))}
                </g>
              ) : (
                /* Default 'touch' tab with full points */
                <g className="touch-overlay">
                  {/* Touch Points */}
                  {intelligence.touches.map((t, idx) => (
                    <circle
                      key={`touch-${idx}`}
                      cx={t.x}
                      cy={t.y}
                      r="1.4"
                      fill="#00f59b"
                      opacity="0.85"
                    />
                  ))}

                  {/* Progressive Carries */}
                  {intelligence.progressiveCarries.map((c, idx) => (
                    <g key={`carry-${idx}`}>
                      <line
                        x1={c.startX}
                        y1={c.startY}
                        x2={c.endX}
                        y2={c.endY}
                        stroke="#38bdf8"
                        strokeWidth="1.4"
                        strokeDasharray="2.5,1"
                        markerEnd="url(#carryArrow)"
                      />
                      <circle cx={c.startX} cy={c.startY} r="1" fill="#38bdf8" />
                    </g>
                  ))}

                  {/* Chances created (Gold Stars) */}
                  {intelligence.chanceCreation.map((cc, idx) => (
                    <g key={`chance-${idx}`}>
                      <line
                        x1={cc.fromX}
                        y1={cc.fromY}
                        x2={cc.toX}
                        y2={cc.toY}
                        stroke="#f8fafc"
                        strokeWidth="1.2"
                        strokeDasharray="2,1.5"
                      />
                      <polygon
                        points={`${cc.toX},${cc.toY - 2.8} ${cc.toX + 0.9},${cc.toY - 0.9} ${cc.toX + 2.8},${cc.toY - 0.9} ${cc.toX + 1.4},${cc.toY + 0.5} ${cc.toX + 1.9},${cc.toY + 2.4} ${cc.toX},${cc.toY + 1.3} ${cc.toX - 1.9},${cc.toY + 2.4} ${cc.toX - 1.4},${cc.toY + 0.5} ${cc.toX - 2.8},${cc.toY - 0.9} ${cc.toX - 0.9},${cc.toY - 0.9}`}
                        fill="#f59e0b"
                      />
                    </g>
                  ))}

                  {/* Shots (Red concentric rings) */}
                  <g className="shot-marker">
                    <circle cx="89" cy="30" r="2.4" fill="none" stroke="#f43f5e" strokeWidth="1.2" />
                    <circle cx="89" cy="30" r="1.1" fill="#f43f5e" />
                  </g>
                </g>
              )}
            </svg>
          </div>

          <footer className="pitch-legend-row">
            <span className="legend-item"><span className="legend-dot green" /> Touch</span>
            <span className="legend-item"><span className="legend-dot blue" /> Progressive carry</span>
            <span className="legend-item"><span className="legend-dot white-ring" /> Pass</span>
            <span className="legend-item"><span className="legend-dot gold-star">★</span> Chance created</span>
            <span className="legend-item"><span className="legend-dot red-ring">⭘</span> Shot</span>
          </footer>
        </article>

        {/* CARD 2: Progressive Carries */}
        <article className="tactical-card carries-card">
          <header className="card-header">
            <div className="header-titles">
              <div className="title-with-icon">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" className="card-icon">
                  <path d="M13 5L20 12L13 19M5 5L12 12L5 19" stroke="#00f59b" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
                </svg>
                <h2 className="card-title">Progressive Carries</h2>
              </div>
              <p className="card-subtitle">Carries that move the ball closer to goal</p>
            </div>
          </header>

          <div className="metric-callout-row">
            <div className="callout-main">
              <span className="callout-num">{carryCount}</span>
              <span className="callout-lbl">progressive carries</span>
            </div>
            <span className="trend-badge positive">{carryAvg}</span>
          </div>

          <div className="mini-pitch-wrapper">
            <svg viewBox="0 0 105 68" className="mini-pitch-svg">
              <rect x="0" y="0" width="105" height="68" fill="#081813" rx="2" />
              <rect x="3" y="3" width="99" height="62" fill="none" stroke="#153e2e" strokeWidth="0.7" />
              <line x1="52.5" y1="3" x2="52.5" y2="65" stroke="#153e2e" strokeWidth="0.7" />
              <circle cx="52.5" cy="34" r="8" fill="none" stroke="#153e2e" strokeWidth="0.7" />
              <rect x="86" y="15" width="16" height="38" fill="none" stroke="#153e2e" strokeWidth="0.7" />

              {/* Carry arrows moving right */}
              {intelligence.progressiveCarries.map((c, i) => (
                <g key={`carry-vec-${i}`}>
                  <line
                    x1={c.startX}
                    y1={c.startY}
                    x2={c.endX}
                    y2={c.endY}
                    stroke="#00f59b"
                    strokeWidth="1.6"
                    strokeDasharray="2.5,1"
                  />
                  <circle cx={c.startX} cy={c.startY} r="1.5" fill="none" stroke="#00f59b" strokeWidth="1" />
                  <circle cx={c.endX} cy={c.endY} r="1.5" fill="#00f59b" />
                  <polygon
                    points={`${c.endX},${c.endY} ${c.endX - 3},${c.endY - 1.8} ${c.endX - 3},${c.endY + 1.8}`}
                    fill="#00f59b"
                  />
                </g>
              ))}
            </svg>
          </div>

          <footer className="pitch-legend-row mini-legend">
            <span className="legend-item"><span className="legend-dot green-ring" /> Carry start</span>
            <span className="legend-item"><span className="legend-dot green-solid" /> Carry end</span>
            <span className="legend-item"><span className="legend-arrow green">➔</span> Progressive carry</span>
          </footer>
        </article>

        {/* CARD 3: Chance Creation */}
        <article className="tactical-card chance-card">
          <header className="card-header">
            <div className="header-titles">
              <div className="title-with-icon">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" className="card-icon">
                  <circle cx="12" cy="12" r="9" stroke="#00f59b" strokeWidth="2" />
                  <path d="M12 7V17M7 12H17" stroke="#00f59b" strokeWidth="2" strokeLinecap="round" />
                </svg>
                <h2 className="card-title">Chance Creation</h2>
              </div>
              <p className="card-subtitle">Key passes and open-play chances</p>
            </div>
          </header>

          <div className="metric-callout-row multi-stat">
            <div className="callout-pair">
              <div className="callout-main">
                <span className="callout-num">{chanceCount}</span>
                <span className="callout-lbl">chances created</span>
              </div>
              <div className="callout-main">
                <span className="callout-num">{xAVal.toFixed(2)}</span>
                <span className="callout-lbl">xA contribution</span>
              </div>
            </div>
            <span className="trend-badge positive">{chanceAvg}</span>
          </div>

          <div className="mini-pitch-wrapper">
            <svg viewBox="0 0 105 68" className="mini-pitch-svg">
              <rect x="0" y="0" width="105" height="68" fill="#081813" rx="2" />
              <rect x="3" y="3" width="99" height="62" fill="none" stroke="#153e2e" strokeWidth="0.7" />
              <line x1="52.5" y1="3" x2="52.5" y2="65" stroke="#153e2e" strokeWidth="0.7" />
              <circle cx="52.5" cy="34" r="8" fill="none" stroke="#153e2e" strokeWidth="0.7" />
              <rect x="86" y="15" width="16" height="38" fill="none" stroke="#153e2e" strokeWidth="0.7" />

              {/* Passes into box */}
              {intelligence.chanceCreation.map((cc, i) => (
                <g key={`cc-vec-${i}`}>
                  <line
                    x1={cc.fromX}
                    y1={cc.fromY}
                    x2={cc.toX}
                    y2={cc.toY}
                    stroke="#38bdf8"
                    strokeWidth="1.6"
                    strokeDasharray="2.5,1.5"
                  />
                  <circle cx={cc.fromX} cy={cc.fromY} r="1.8" fill="#38bdf8" />
                  <circle cx={cc.toX} cy={cc.toY} r="2.8" fill="none" stroke="#38bdf8" strokeWidth="1.2" />
                  <circle cx={cc.toX} cy={cc.toY} r="1.4" fill="#00f59b" />
                </g>
              ))}
            </svg>
          </div>

          <footer className="pitch-legend-row mini-legend">
            <span className="legend-item"><span className="legend-dot blue" /> Key pass</span>
            <span className="legend-item"><span className="legend-dot blue-ring" /> Open play chance</span>
            <span className="legend-item"><span className="legend-arrow dashed">--➔</span> Pass trajectory</span>
          </footer>
        </article>

        {/* CARD 4: Why it matters */}
        <article className="tactical-card why-matters-card">
          <header className="card-header">
            <div className="header-titles">
              <div className="title-with-icon">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" className="card-icon">
                  <path d="M9 5H7C5.89543 5 5 5.89543 5 7V19C5 20.1046 5.89543 21 7 21H17C18.1046 21 19 20.1046 19 19V7C19 5.89543 18.1046 5 17 5H15M9 5C9 6.10457 9.89543 7 11 7H13C14.1046 7 15 6.10457 15 5M9 5C9 3.89543 9.89543 3 11 3H13C14.1046 3 15 3.89543 15 5" stroke="#00f59b" strokeWidth="2" strokeLinecap="round" />
                </svg>
                <h2 className="card-title">Why it matters</h2>
              </div>
            </div>
            <span className="derived-badge">
              <span className="badge-sparkle">✦</span> DERIVED
            </span>
          </header>

          <ol className="why-matters-list">
            {(intelligence.whyItMatters && intelligence.whyItMatters.length > 0 ? intelligence.whyItMatters : [
              "Controls transition tempo and stretches opponent defensive structure.",
              "Delivers high-value distribution into key attacking zones, increasing team chance volume.",
              "Drives progression and duel success, forcing defensive rotations and creating space.",
              "High defensive work rate helps sustain pressing shape and ball recoveries in advanced areas."
            ]).slice(0, 4).map((text, idx) => (
              <li key={`why-${idx}`} className="why-item">
                <span className="item-number-badge">{idx + 1}</span>
                <p className="item-text">{text}</p>
              </li>
            ))}
          </ol>
        </article>
      </section>

      {/* =========================================================================
          LOWER ROW: RECEIVING ZONES | DEFENSIVE WORK | EXPECTED ASSISTS (xA) | PLAYER COMPARISON
          ========================================================================= */}
      <section className="tactical-quad-row lower-row" aria-label="Receiving and defensive metrics">
        {/* CARD 1: Receiving Zones */}
        <article className="tactical-card receiving-card">
          <header className="card-header">
            <div className="header-titles">
              <div className="title-with-icon">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" className="card-icon">
                  <rect x="3" y="3" width="18" height="18" rx="2" stroke="#00f59b" strokeWidth="2" />
                  <line x1="9" y1="3" x2="9" y2="21" stroke="#00f59b" strokeWidth="1.5" />
                  <line x1="15" y1="3" x2="15" y2="21" stroke="#00f59b" strokeWidth="1.5" />
                  <line x1="3" y1="9" x2="21" y2="9" stroke="#00f59b" strokeWidth="1.5" />
                  <line x1="3" y1="15" x2="21" y2="15" stroke="#00f59b" strokeWidth="1.5" />
                </svg>
                <h2 className="card-title">Receiving Zones</h2>
              </div>
              <p className="card-subtitle">Where {intelligence.name.split(' ').pop()} receives the ball</p>
            </div>
          </header>

          <div className="receiving-zones-grid-wrapper">
            <div className="tactical-pitch-zones">
              {/* 3x4 Tactical Grid */}
              <div className="zones-3x4-grid">
                {/* Zone percentages matching screenshot */}
                {[
                  { pct: 2, label: 'Def Left' }, { pct: 8, label: 'Mid Left' }, { pct: 18, label: 'Att Left' }, { pct: 22, label: 'Fin Right Wing' },
                  { pct: 1, label: 'Def Cen' }, { pct: 6, label: 'Mid Cen' }, { pct: 15, label: 'Att Cen' }, { pct: 14, label: 'Fin Half Space' },
                  { pct: 1, label: 'Def Right' }, { pct: 4, label: 'Mid Right' }, { pct: 6, label: 'Att Right' }, { pct: 3, label: 'Fin Central' }
                ].map((z, idx) => {
                  const dynamicPct = intelligence.receivingZones[idx]?.pct ?? z.pct;
                  const opacity = Math.min(0.85, Math.max(0.08, dynamicPct / 26));
                  return (
                    <div
                      key={`zone-${idx}`}
                      className="zone-cell"
                      style={{ backgroundColor: `rgba(0, 245, 155, ${opacity})` }}
                    >
                      <span className="zone-pct">{dynamicPct}%</span>
                    </div>
                  );
                })}
              </div>

              {/* Pitch markings overlay */}
              <svg viewBox="0 0 100 68" className="zone-pitch-overlay" preserveAspectRatio="none">
                <rect x="1" y="1" width="98" height="66" fill="none" stroke="#1f4b39" strokeWidth="0.8" />
                <rect x="1" y="18" width="12" height="32" fill="none" stroke="#1f4b39" strokeWidth="0.8" />
                <rect x="87" y="18" width="12" height="32" fill="none" stroke="#1f4b39" strokeWidth="0.8" />
                <circle cx="50" cy="34" r="8" fill="none" stroke="#1f4b39" strokeWidth="0.8" />
              </svg>
            </div>

            <div className="zone-direction-row">
              <span className="direction-label">Attacking ➔</span>
            </div>
          </div>
        </article>

        {/* CARD 2: Defensive Work */}
        <article className="tactical-card defensive-card">
          <header className="card-header">
            <div className="header-titles">
              <div className="title-with-icon">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" className="card-icon">
                  <path d="M12 2L4 6V12C4 17.5 7.5 21.5 12 22C16.5 21.5 20 17.5 20 12V6L12 2Z" stroke="#00f59b" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
                </svg>
                <h2 className="card-title">Defensive Work</h2>
              </div>
              <p className="card-subtitle">Pressures, tackles and defensive actions</p>
            </div>
          </header>

          <div className="defensive-metrics-summary">
            <div className="def-metric"><span className="def-num">{intelligence.defensiveWorkStats?.pressures ?? 5}</span> Pressures</div>
            <div className="def-metric"><span className="def-num">{intelligence.defensiveWorkStats?.tackles ?? 2}</span> Tackles</div>
            <div className="def-metric"><span className="def-num">{intelligence.defensiveWorkStats?.ballRecoveries ?? 3}</span> Ball recoveries</div>
          </div>

          <div className="mini-pitch-wrapper">
            <svg viewBox="0 0 105 68" className="mini-pitch-svg">
              <rect x="0" y="0" width="105" height="68" fill="#081813" rx="2" />
              <rect x="3" y="3" width="99" height="62" fill="none" stroke="#153e2e" strokeWidth="0.7" />
              <line x1="52.5" y1="3" x2="52.5" y2="65" stroke="#153e2e" strokeWidth="0.7" />
              <circle cx="52.5" cy="34" r="8" fill="none" stroke="#153e2e" strokeWidth="0.7" />
              <rect x="86" y="15" width="16" height="38" fill="none" stroke="#153e2e" strokeWidth="0.7" />

              {/* Defensive action markers */}
              {intelligence.defensiveWork.map((d, i) => {
                if (d.action.toLowerCase().includes('pressure')) {
                  return (
                    <polygon
                      key={`def-${i}`}
                      points={`${d.x},${d.y - 2.5} ${d.x + 2.5},${d.y + 2.5} ${d.x - 2.5},${d.y + 2.5}`}
                      fill="#38bdf8"
                    />
                  );
                } else if (d.action.toLowerCase().includes('tackle')) {
                  return (
                    <g key={`def-${i}`}>
                      <line x1={d.x - 2} y1={d.y - 2} x2={d.x + 2} y2={d.y + 2} stroke="#00f59b" strokeWidth="1.4" />
                      <line x1={d.x - 2} y1={d.y + 2} x2={d.x + 2} y2={d.y - 2} stroke="#00f59b" strokeWidth="1.4" />
                    </g>
                  );
                } else {
                  return (
                    <circle key={`def-${i}`} cx={d.x} cy={d.y} r="2" fill="#00f59b" />
                  );
                }
              })}
            </svg>
          </div>

          <footer className="pitch-legend-row mini-legend">
            <span className="legend-item"><span className="legend-symbol triangle">▲</span> Pressure</span>
            <span className="legend-item"><span className="legend-symbol cross">✖</span> Tackle</span>
            <span className="legend-item"><span className="legend-dot green">●</span> Ball recovery</span>
          </footer>
        </article>

        {/* CARD 3: Expected Assists (xA) */}
        <article className="tactical-card xa-card">
          <header className="card-header">
            <div className="header-titles">
              <div className="title-with-icon">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" className="card-icon">
                  <path d="M4 19L10 13L14 17L20 7" stroke="#00f59b" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
                  <path d="M14 7H20V13" stroke="#00f59b" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
                </svg>
                <h2 className="card-title">Expected Assists (xA)</h2>
              </div>
              <p className="card-subtitle">Cumulative xA in this match</p>
            </div>
          </header>

          <div className="metric-callout-row">
            <div className="callout-main">
              <span className="callout-num">{xAVal.toFixed(2)}</span>
              <span className="callout-lbl">xA contribution</span>
            </div>
            <span className="trend-badge positive">+60% vs season avg</span>
          </div>

          <div className="step-chart-wrapper">
            <svg viewBox="0 0 240 100" className="step-chart-svg" preserveAspectRatio="none">
              <defs>
                <linearGradient id="xaAreaGrad" x1="0%" y1="0%" x2="0%" y2="100%">
                  <stop offset="0%" stopColor="#00f59b" stopOpacity="0.25" />
                  <stop offset="100%" stopColor="#00f59b" stopOpacity="0.0" />
                </linearGradient>
              </defs>

              {/* Grid Lines */}
              <line x1="30" y1="15" x2="235" y2="15" stroke="#16382b" strokeWidth="0.5" strokeDasharray="2,2" />
              <line x1="30" y1="38" x2="235" y2="38" stroke="#16382b" strokeWidth="0.5" strokeDasharray="2,2" />
              <line x1="30" y1="62" x2="235" y2="62" stroke="#16382b" strokeWidth="0.5" strokeDasharray="2,2" />
              <line x1="30" y1="85" x2="235" y2="85" stroke="#16382b" strokeWidth="0.8" />

              {/* Y Axis Labels */}
              <text x="24" y="18" fill="#64748b" fontSize="8" textAnchor="end">0.45</text>
              <text x="24" y="41" fill="#64748b" fontSize="8" textAnchor="end">0.30</text>
              <text x="24" y="65" fill="#64748b" fontSize="8" textAnchor="end">0.15</text>
              <text x="24" y="88" fill="#64748b" fontSize="8" textAnchor="end">0.00</text>

              {/* Cumulative Stepped Line & Shaded Area */}
              {/* Points: (0,0) -> (28', 0.08) -> (56', 0.18) -> (64', 0.32) */}
              <path
                d="M 30 85 L 85 85 L 85 73 L 140 73 L 140 58 L 175 58 L 175 35 L 210 35 L 210 85 Z"
                fill="url(#xaAreaGrad)"
              />
              <path
                d="M 30 85 L 85 85 L 85 73 L 140 73 L 140 58 L 175 58 L 175 35 L 210 35"
                fill="none"
                stroke="#00f59b"
                strokeWidth="2"
              />

              {/* Active Current Minute Dot */}
              <circle cx="175" cy="35" r="4.5" fill="none" stroke="#00f59b" strokeWidth="1.5" />
              <circle cx="175" cy="35" r="2.5" fill="#00f59b" />

              {/* Open play baseline dashed */}
              <path
                d="M 30 85 L 85 85 L 85 76 L 140 76 L 140 64 L 175 64 L 210 64"
                fill="none"
                stroke="#38bdf8"
                strokeWidth="1.2"
                strokeDasharray="2,2"
              />

              {/* X Axis Labels */}
              <text x="30" y="96" fill="#64748b" fontSize="7.5" textAnchor="middle">0'</text>
              <text x="65" y="96" fill="#64748b" fontSize="7.5" textAnchor="middle">15'</text>
              <text x="100" y="96" fill="#64748b" fontSize="7.5" textAnchor="middle">30'</text>
              <text x="135" y="96" fill="#64748b" fontSize="7.5" textAnchor="middle">45'</text>
              <text x="170" y="96" fill="#64748b" fontSize="7.5" textAnchor="middle">60'</text>
              <text x="205" y="96" fill="#64748b" fontSize="7.5" textAnchor="middle">75'</text>
              <text x="235" y="96" fill="#64748b" fontSize="7.5" textAnchor="middle">90'</text>
            </svg>
          </div>

          <footer className="pitch-legend-row mini-legend">
            <span className="legend-item"><span className="legend-line green" /> {intelligence.name.split(' ').pop()} (xA)</span>
            <span className="legend-item"><span className="legend-line blue-dash" /> Open play xA</span>
            <span className="legend-item"><span className="legend-line dim" /> Set piece xA</span>
          </footer>
        </article>

        {/* CARD 4: Player Comparison */}
        <article className="tactical-card comparison-card">
          <header className="card-header">
            <div className="header-titles">
              <div className="title-with-icon">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" className="card-icon">
                  <path d="M16 3H21V8M8 21H3V16M21 3L13 11M3 21L11 13" stroke="#00f59b" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
                </svg>
                <h2 className="card-title">Player Comparison</h2>
              </div>
            </div>
            <span className="comparison-filter-pill">
              Premier League (Wingers) ▾
            </span>
          </header>

          <div className="head-to-head-header">
            <div className="player-col left-player">
              <img src={playerCrest} alt={intelligence.club} className="h2h-crest" />
              <div className="h2h-meta">
                <span className="h2h-name">{intelligence.name}</span>
                <span className="h2h-badge green">RW</span>
              </div>
            </div>

            <div className="player-col right-player">
              <img src={opponentCrest} alt={intelligence.comparison.opponentPlayer} className="h2h-crest" />
              <div className="h2h-meta">
                <span className="h2h-name">{intelligence.comparison.opponentPlayer}</span>
                <span className="h2h-badge blue">RW</span>
              </div>
            </div>
          </div>

          <div className="comparison-bars-list">
            {(intelligence.comparison.metrics ?? [
              { label: 'Touches (att. third)', player: 34, opponent: 28 },
              { label: 'Progressive carries', player: 6, opponent: 4 },
              { label: 'Chances created', player: 2, opponent: 2 },
              { label: 'xA (open play)', player: 0.32, opponent: 0.21 },
              { label: 'Successful dribbles', player: 4, opponent: 3 },
              { label: 'Defensive actions', player: 5, opponent: 2 }
            ]).map((metric, i) => {
              const maxVal = Math.max(metric.player, metric.opponent, 1);
              const p1Pct = Math.round((metric.player / maxVal) * 100);
              const p2Pct = Math.round((metric.opponent / maxVal) * 100);
              return (
                <div key={`comp-${i}`} className="comparison-row">
                  <div className="comp-val left">{metric.player}</div>
                  <div className="comp-bars-center">
                    <div className="bar-track left-track">
                      <div className="bar-fill green-glow" style={{ width: `${p1Pct}%` }} />
                    </div>
                    <span className="comp-label">{metric.label}</span>
                    <div className="bar-track right-track">
                      <div className="bar-fill blue-glow" style={{ width: `${p2Pct}%` }} />
                    </div>
                  </div>
                  <div className="comp-val right">{metric.opponent}</div>
                </div>
              );
            })}
          </div>
        </article>
      </section>
    </div>
  );
}
