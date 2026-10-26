import { useState, useMemo } from 'react';
import type { PlayerIntelligenceData } from './api';
import { calculateAiPlayerKeyQuote } from './api';
import { resolveClubLogo } from './clubLogos';

export interface PlayerRosterViewProps {
  players: PlayerIntelligenceData[];
  onSelectPlayer: (playerId: string) => void;
  homeTeamName?: string;
  awayTeamName?: string;
  clockDisplay?: string;
}

export function PlayerRosterView({
  players,
  onSelectPlayer,
  homeTeamName,
  awayTeamName,
  clockDisplay
}: PlayerRosterViewProps) {
  const [searchQuery, setSearchQuery] = useState('');
  const [teamFilter, setTeamFilter] = useState<'all' | string>('all');
  const [roleFilter, setRoleFilter] = useState<'all' | 'forward' | 'midfielder' | 'defender' | 'goalkeeper'>('all');
  const [sortBy, setSortBy] = useState<'impact' | 'chances' | 'carries' | 'number'>('impact');

  const effectiveHomeTeam = homeTeamName ?? 'Home Team';
  const effectiveAwayTeam = awayTeamName ?? 'Away Team';

  const filteredPlayers = useMemo(() => {
    return players
      .filter((p) => {
        // Search query
        if (searchQuery.trim()) {
          const q = searchQuery.toLowerCase();
          const matchName = p.name.toLowerCase().includes(q);
          const matchClub = p.club.toLowerCase().includes(q);
          const matchRole = p.role.toLowerCase().includes(q);
          if (!matchName && !matchClub && !matchRole) return false;
        }

        // Team filter
        if (teamFilter !== 'all') {
          if (!p.club.toLowerCase().includes(teamFilter.toLowerCase())) return false;
        }

        // Role filter
        if (roleFilter !== 'all') {
          const roleLower = p.role.toLowerCase();
          if (roleFilter === 'forward' && !roleLower.includes('winger') && !roleLower.includes('forward') && !roleLower.includes('striker')) return false;
          if (roleFilter === 'midfielder' && !roleLower.includes('midfield') && !roleLower.includes('cm') && !roleLower.includes('cam') && !roleLower.includes('cdm')) return false;
          if (roleFilter === 'defender' && !roleLower.includes('back') && !roleLower.includes('defender') && !roleLower.includes('cb')) return false;
          if (roleFilter === 'goalkeeper' && !roleLower.includes('keeper') && !roleLower.includes('gk')) return false;
        }

        return true;
      })
      .sort((a, b) => {
        const aChances = a.matchStats?.chancesCreated ?? 0;
        const bChances = b.matchStats?.chancesCreated ?? 0;
        const aCarries = a.matchStats?.progressiveCarries ?? 0;
        const bCarries = b.matchStats?.progressiveCarries ?? 0;
        const aXa = a.matchStats?.xA ?? a.matchStats?.xa ?? 0;
        const bXa = b.matchStats?.xA ?? b.matchStats?.xa ?? 0;

        if (sortBy === 'chances') {
          return bChances - aChances;
        }
        if (sortBy === 'carries') {
          return bCarries - aCarries;
        }
        if (sortBy === 'number') {
          return a.number - b.number;
        }
        // default impact: chances + carries + (xA * 10)
        const scoreA = aChances * 2 + aCarries + aXa * 10;
        const scoreB = bChances * 2 + bCarries + bXa * 10;
        return scoreB - scoreA;
      });
  }, [players, searchQuery, teamFilter, roleFilter, sortBy]);

  return (
    <div className="player-roster-view" aria-label="Premier League Players Roster">
      {/* Top Banner / Filter Toolbar */}
      <div className="roster-toolbar">
        <div className="roster-headline">
          <div className="headline-meta">
            <span className="live-pill">
              <span className="live-dot" /> LIVE INTELLIGENCE
            </span>
            {clockDisplay && <span className="clock-pill">{clockDisplay}</span>}
          </div>
          <h1 className="roster-title">Match Squad Intelligence</h1>
          <p className="roster-subtitle">
            Dynamic player tracking, involvement clusters, chance creation, and Azure AI tactical profiling. Select a player card to inspect complete in-depth intelligence.
          </p>
        </div>

        {/* Filter Bar */}
        <div className="filter-controls-bar">
          <div className="search-input-wrapper">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" className="search-icon">
              <circle cx="11" cy="11" r="8" stroke="#64748b" strokeWidth="2" />
              <line x1="21" y1="21" x2="16.65" y2="16.65" stroke="#64748b" strokeWidth="2" strokeLinecap="round" />
            </svg>
            <input
              type="text"
              className="player-search-input"
              placeholder="Search player, club, position…"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              aria-label="Filter players by name or club"
            />
            {searchQuery && (
              <button
                type="button"
                className="clear-search-btn"
                onClick={() => setSearchQuery('')}
                aria-label="Clear search"
              >
                ✕
              </button>
            )}
          </div>

          <div className="filter-group">
            <div className="filter-pills-row" role="radiogroup" aria-label="Team filter">
              <button
                type="button"
                className={`filter-pill ${teamFilter === 'all' ? 'active' : ''}`}
                onClick={() => setTeamFilter('all')}
              >
                All Teams
              </button>
              {effectiveHomeTeam && (
                <button
                  type="button"
                  className={`filter-pill ${teamFilter === effectiveHomeTeam ? 'active' : ''}`}
                  onClick={() => setTeamFilter(effectiveHomeTeam)}
                >
                  {effectiveHomeTeam}
                </button>
              )}
              {effectiveAwayTeam && (
                <button
                  type="button"
                  className={`filter-pill ${teamFilter === effectiveAwayTeam ? 'active' : ''}`}
                  onClick={() => setTeamFilter(effectiveAwayTeam)}
                >
                  {effectiveAwayTeam}
                </button>
              )}
            </div>

            <div className="filter-pills-row" role="radiogroup" aria-label="Role filter">
              <button
                type="button"
                className={`filter-pill ${roleFilter === 'all' ? 'active' : ''}`}
                onClick={() => setRoleFilter('all')}
              >
                All Roles
              </button>
              <button
                type="button"
                className={`filter-pill ${roleFilter === 'forward' ? 'active' : ''}`}
                onClick={() => setRoleFilter('forward')}
              >
                Forwards
              </button>
              <button
                type="button"
                className={`filter-pill ${roleFilter === 'midfielder' ? 'active' : ''}`}
                onClick={() => setRoleFilter('midfielder')}
              >
                Midfielders
              </button>
              <button
                type="button"
                className={`filter-pill ${roleFilter === 'defender' ? 'active' : ''}`}
                onClick={() => setRoleFilter('defender')}
              >
                Defenders
              </button>
            </div>

            <div className="sort-dropdown-wrapper">
              <label htmlFor="sort-select" className="sort-label">Sort:</label>
              <select
                id="sort-select"
                className="sort-select"
                value={sortBy}
                onChange={(e) => setSortBy(e.target.value as any)}
              >
                <option value="impact">Highest Impact</option>
                <option value="chances">Chances Created</option>
                <option value="carries">Progressive Carries</option>
                <option value="number">Shirt Number</option>
              </select>
            </div>
          </div>
        </div>
      </div>

      {/* Players Card Grid */}
      {filteredPlayers.length === 0 ? (
        <div className="no-players-found">
          <p>No players found matching "{searchQuery}". Try adjusting your filters.</p>
        </div>
      ) : (
        <div className="pro-player-cards-grid">
          {filteredPlayers.map((player) => {
            const crest = player.teamLogo || resolveClubLogo(player.club) || '/crests/manchester-united.svg';
            const playerPhoto = player.photoUrl || player.photo || `/players/${player.id}.jpg`;
            const xA = (player.matchStats?.xA ?? player.matchStats?.xa ?? 0.25).toFixed(2);
            const chances = player.matchStats?.chancesCreated ?? 0;
            const carries = player.matchStats?.progressiveCarries ?? 0;
            const duels = player.matchStats?.duelsWon ?? '50%';

            return (
              <article
                key={player.id}
                className="pro-player-card"
                onClick={() => onSelectPlayer(player.id)}
                role="button"
                tabIndex={0}
                onKeyDown={(e) => {
                  if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    onSelectPlayer(player.id);
                  }
                }}
                aria-label={`View tactical intelligence for ${player.name}`}
              >
                {/* Giant watermark number in card background */}
                <span className="card-watermark-number" aria-hidden="true">
                  {player.number}
                </span>

                {/* Card Topline: Club badge + Active status */}
                <div className="card-topline">
                  <div className="card-club-tag">
                    <img src={crest} alt={player.club} className="card-club-crest" />
                    <span className="card-club-name">{player.club}</span>
                  </div>
                  <span className="card-status-badge">
                    <span className="status-dot-pulse" /> Active
                  </span>
                </div>

                {/* Player Identity Row */}
                <div className="card-identity-row">
                  <div className="card-portrait-box">
                    <div className="portrait-glow-ring" />
                    <img
                      src={playerPhoto}
                      alt={player.name}
                      className="card-portrait-img"
                      onError={(e) => {
                        (e.target as HTMLImageElement).src = crest;
                      }}
                    />
                  </div>

                  <div className="card-player-text">
                    <div className="card-name-line">
                      <h2 className="card-player-name">{player.name}</h2>
                      <span className="card-verified-icon" title="Verified player">✓</span>
                    </div>
                    <p className="card-player-role">{player.role}</p>
                    <div className="card-meta-chips">
                      <span className="meta-chip">#{player.number}</span>
                      <span className="meta-chip">{player.nationality}</span>
                      <span className="meta-chip">{player.preferredFoot ?? 'Right foot'}</span>
                    </div>
                  </div>
                </div>

                {/* Tactical Stats Matrix */}
                <div className="card-stats-matrix">
                  <div className="stat-matrix-cell">
                    <span className="cell-num">{player.minutesPlayed}'</span>
                    <span className="cell-lbl">Minutes</span>
                  </div>
                  <div className="stat-matrix-cell">
                    <span className="cell-num">{chances}</span>
                    <span className="cell-lbl">Chances</span>
                  </div>
                  <div className="stat-matrix-cell">
                    <span className="cell-num">{carries}</span>
                    <span className="cell-lbl">Carries</span>
                  </div>
                  <div className="stat-matrix-cell">
                    <span className="cell-num">{xA}</span>
                    <span className="cell-lbl">xA</span>
                  </div>
                  <div className="stat-matrix-cell">
                    <span className="cell-num">{duels}</span>
                    <span className="cell-lbl">Duels</span>
                  </div>
                </div>

                {/* Key Tactical Trait */}
                <div className="card-trait-preview">
                  <span className="trait-star">★</span>
                  <p className="trait-text">
                    {player.keyQuote || calculateAiPlayerKeyQuote(player)}
                  </p>
                </div>

                {/* Footer Action */}
                <div className="card-footer-cta">
                  <span className="cta-action-text">View Tactical Intelligence</span>
                  <span className="cta-arrow-icon">➔</span>
                </div>
              </article>
            );
          })}
        </div>
      )}
    </div>
  );
}
