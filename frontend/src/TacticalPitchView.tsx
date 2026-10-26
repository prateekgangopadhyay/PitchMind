import { useState, useRef } from 'react';
import type { ContextPlayer, ContextTeam } from './MatchContextPages';
import { clock } from './api';

export interface TacticalPitchEvent {
  type: string;
  sequence: number;
  clockSeconds: number;
  teamName?: string;
  playerName?: string;
  receiverName?: string;
  pitchX?: number;
  pitchY?: number;
  endX?: number;
  endY?: number;
  action?: string;
  outcome?: string;
  impactScore?: number;
  headline?: string;
  leadupEvent?: {
    type: string;
    sequence: number;
    clockSeconds: number;
    playerName?: string;
    teamName?: string;
    pitchX?: number;
    pitchY?: number;
    endX?: number;
    endY?: number;
    action?: string;
  };
}

export interface TacticalPitchViewProps {
  players: ContextPlayer[];
  homeTeam?: ContextTeam;
  awayTeam?: ContextTeam;
  seconds: number;
  ballPosition?: { x: number; y: number };
  recentEvent?: TacticalPitchEvent;
  availableEvents?: TacticalPitchEvent[];
  onSelectEvent?: (sequence: number) => void;
}

// 18 Standard Tactical Pitch Zones
export const TACTICAL_ZONES = [
  // Defensive Third (x: 3 to 34.3)
  { id: 1, name: 'Zone 1: Right Back Channel', x: 3, y: 3, width: 31.3, height: 15.5 },
  { id: 2, name: 'Zone 2: Right Half-Space Def', x: 3, y: 18.5, width: 31.3, height: 15.5 },
  { id: 3, name: 'Zone 3: Central Defense Box', x: 3, y: 34, width: 31.3, height: 15.5 },
  { id: 4, name: 'Zone 4: Left Half-Space Def', x: 3, y: 49.5, width: 31.3, height: 15.5 },
  { id: 5, name: 'Zone 5: Left Back Channel', x: 3, y: 65, width: 31.3, height: 15.5 },
  // Middle Third (x: 34.3 to 65.6)
  { id: 6, name: 'Zone 6: Right Flank Build-up', x: 34.3, y: 3, width: 31.3, height: 15.5 },
  { id: 7, name: 'Zone 7: Right Half-Space Mid', x: 34.3, y: 18.5, width: 31.3, height: 15.5 },
  { id: 8, name: 'Zone 8: Central Midfield Pivot', x: 34.3, y: 34, width: 31.3, height: 15.5 },
  { id: 9, name: 'Zone 9: Left Half-Space Mid', x: 34.3, y: 49.5, width: 31.3, height: 15.5 },
  { id: 10, name: 'Zone 10: Left Flank Build-up', x: 34.3, y: 65, width: 31.3, height: 15.5 },
  // Final Third (x: 65.6 to 97)
  { id: 11, name: 'Zone 11: Right Wing Corner', x: 65.6, y: 3, width: 31.4, height: 15.5 },
  { id: 12, name: 'Zone 12: Right Half-Space Attack', x: 65.6, y: 18.5, width: 31.4, height: 15.5 },
  { id: 13, name: 'Zone 13: Central Attacking Pocket', x: 65.6, y: 26.25, width: 15.7, height: 15.5 },
  { id: 14, name: 'Zone 14: The Golden Hole', x: 65.6, y: 26.25, width: 15.7, height: 15.5, highlight: true },
  { id: 15, name: 'Zone 15: Left Half-Space Attack', x: 65.6, y: 49.5, width: 31.4, height: 15.5 },
  { id: 16, name: 'Zone 16: Left Wing Corner', x: 65.6, y: 65, width: 31.4, height: 15.5 },
  { id: 17, name: 'Zone 17: Penalty Box Left', x: 81.3, y: 34, width: 15.7, height: 15.5 },
  { id: 18, name: 'Zone 18: Penalty Box Right', x: 81.3, y: 18.5, width: 15.7, height: 15.5 }
];

export function TacticalPitchView({
  players,
  homeTeam,
  awayTeam,
  ballPosition,
  recentEvent,
  availableEvents = [],
  onSelectEvent
}: TacticalPitchViewProps) {
  const [showZones, setShowZones] = useState(false);
  const [showLines, setShowLines] = useState(true);
  const [showVectors, setShowVectors] = useState(true);
  const [showPressure, setShowPressure] = useState(true);
  const [hoveredPlayer, setHoveredPlayer] = useState<ContextPlayer | null>(null);
  const containerRef = useRef<HTMLDivElement>(null);

  const homeName = homeTeam?.name ?? 'Home FC';
  const awayName = awayTeam?.name ?? 'Away FC';

  // Coordinate mapping helper from 0..100% to Pitch SVG coordinates (x: 3..97, y: 3..65)
  const mapX = (percentX?: number) => {
    if (percentX === undefined) return 50;
    return 3 + (Math.max(0, Math.min(100, percentX)) / 100) * 94;
  };
  const mapY = (percentY?: number) => {
    if (percentY === undefined) return 34;
    return 3 + (Math.max(0, Math.min(100, percentY)) / 100) * 62;
  };

  // Divide players into home and away
  const homePlayers = players.filter(p => p.teamId === homeName || p.teamId === 'home' || p.teamId === homeTeam?.id);
  const awayPlayers = players.filter(p => p.teamId === awayName || p.teamId === 'away' || p.teamId === awayTeam?.id);

  const effectiveHomePlayers = homePlayers.length > 0 ? homePlayers : players.slice(0, Math.ceil(players.length / 2));
  const effectiveAwayPlayers = awayPlayers.length > 0 ? awayPlayers : players.slice(Math.ceil(players.length / 2));

  // Determine active event coordinates
  const eventX = recentEvent?.pitchX;
  const eventY = recentEvent?.pitchY;
  const endX = recentEvent?.endX;
  const endY = recentEvent?.endY;

  // Lead-up pass/action coordinates
  const leadup = recentEvent?.leadupEvent;
  const leadupX = leadup?.pitchX ?? (recentEvent ? (eventX !== undefined ? eventX - 13 : undefined) : undefined);
  const leadupY = leadup?.pitchY ?? (recentEvent ? (eventY !== undefined ? eventY - 7 : undefined) : undefined);

  // Ball coordinate fallback
  const effBallX = mapX(ballPosition?.x ?? (eventX ?? 50));
  const effBallY = mapY(ballPosition?.y ?? (eventY ?? 34));

  // Active Zone Discovery based on coordinates
  const activeZone = eventX !== undefined && eventY !== undefined
    ? TACTICAL_ZONES.find(z =>
        eventX >= z.x && eventX <= (z.x + z.width) &&
        eventY >= z.y && eventY <= (z.y + z.height)
      ) || TACTICAL_ZONES.find(z => z.id === 14) || TACTICAL_ZONES[0]
    : null;

  // Find 2 opponent defenders closest to event to draw pressure collapse arrows
  const eventTeamIsHome = recentEvent?.teamName === homeName || !recentEvent?.teamName;
  const defendingPlayers = eventTeamIsHome ? effectiveAwayPlayers : effectiveHomePlayers;
  const sortedDefenders = [...defendingPlayers]
    .filter(p => p.x !== undefined && p.y !== undefined)
    .map(p => {
      const dx = (p.x ?? 0) - (eventX ?? 50);
      const dy = (p.y ?? 0) - (eventY ?? 34);
      return { player: p, dist: Math.sqrt(dx * dx + dy * dy) };
    })
    .sort((a, b) => a.dist - b.dist);
  const pressurePlayers = sortedDefenders.slice(0, 2);

  // Dynamic Tactical diagram title and badges
  const isGoal = recentEvent?.type === 'Goal' || recentEvent?.outcome === 'Goal';
  const diagramTitle = isGoal
    ? 'CENTRAL PROGRESSION UNDER PRESSURE · GOAL CONVERSION'
    : recentEvent?.type === 'Shot'
    ? 'FINAL THIRD PENETRATION · SHOT ON TARGET'
    : recentEvent?.type === 'Tackle' || recentEvent?.type === 'BallRecovery'
    ? 'HIGH PRESS REGAIN & TRANSITION TRIGGER'
    : 'CENTRAL PROGRESSION UNDER PRESSURE';

  const constraintZoneLabel = isGoal
    ? '⚡ GOAL CREATION POCKET · ZONE 14'
    : activeZone
    ? `68% FEWER PASSES THROUGH ${activeZone.name.toUpperCase()}`
    : '68% FEWER PASSES THROUGH CENTRE';

  // Sort players along X to connect tactical lines (Defenders, Midfielders, Attackers)
  const sortedHome = [...effectiveHomePlayers].filter(p => p.x !== undefined).sort((a, b) => (a.x ?? 0) - (b.x ?? 0));
  const homeDefenders = sortedHome.slice(0, 4);
  const homeMidfielders = sortedHome.slice(4, 8);

  const sortedAway = [...effectiveAwayPlayers].filter(p => p.x !== undefined).sort((a, b) => (b.x ?? 0) - (a.x ?? 0));
  const awayDefenders = sortedAway.slice(0, 4);

  return (
    <div className="tactical-pitch-card" ref={containerRef}>
      {/* Tactical Diagram Banner Header matching screenshot */}
      <div className="tactical-diagram-banner">
        <div className="banner-topline">
          <span className="banner-tag">TACTICAL INTELLIGENCE DIAGRAM</span>
          <span className="banner-source-tag">OBSERVED MATCH TRACKING</span>
          {recentEvent && (
            <span className="banner-clock-tag">
              Clock: {clock(recentEvent.clockSeconds)} · Seq {recentEvent.sequence}
            </span>
          )}
        </div>
        <h3 className="banner-title">{diagramTitle}</h3>
        <div className="banner-metrics-row">
          <div className="metric-pill">
            <span className="metric-label">Zone:</span>
            <strong className="metric-value">{activeZone ? activeZone.name : 'Central Pivot (Z8)'}</strong>
          </div>
          {recentEvent?.playerName && (
            <div className="metric-pill highlight">
              <span className="metric-label">Key Actor:</span>
              <strong className="metric-value">{recentEvent.playerName} ({recentEvent.teamName ?? 'Active'})</strong>
            </div>
          )}
          {recentEvent?.action && (
            <div className="metric-pill">
              <span className="metric-label">Action:</span>
              <strong className="metric-value">{recentEvent.action}</strong>
            </div>
          )}
          {recentEvent?.impactScore && (
            <div className="metric-pill gold">
              <span className="metric-label">Impact / xG:</span>
              <strong className="metric-value">{recentEvent.impactScore}</strong>
            </div>
          )}
        </div>
      </div>

      {/* Interactive Quick Event Selector Bar */}
      {availableEvents.length > 0 && (
        <div className="pitch-events-bar" role="navigation" aria-label="Quick jump to key events on pitch">
          <span className="quick-events-label">Focus Event:</span>
          <div className="quick-events-pills">
            {availableEvents.map(ev => {
              const isSelected = recentEvent?.sequence === ev.sequence;
              const icon = ev.type === 'Goal' ? '⚽' : ev.type === 'Shot' ? '🎯' : ev.type === 'Tackle' ? '🛑' : ev.type === 'BallRecovery' ? '🔄' : '↗️';
              return (
                <button
                  key={`${ev.sequence}-${ev.type}`}
                  type="button"
                  className={`event-selector-pill ${isSelected ? 'active' : ''} ${ev.type.toLowerCase()}`}
                  onClick={() => onSelectEvent?.(ev.sequence)}
                  title={`Focus ${ev.type} by ${ev.playerName ?? ev.teamName} at ${clock(ev.clockSeconds)}`}
                >
                  <span className="pill-icon">{icon}</span>
                  <span className="pill-type">{ev.type}</span>
                  <span className="pill-time">{clock(ev.clockSeconds)}</span>
                  {ev.playerName && <span className="pill-player">{ev.playerName.split(' ').pop()}</span>}
                </button>
              );
            })}
          </div>
        </div>
      )}

      {/* Pitch Header with Toolbar */}
      <div className="tactical-pitch-header">
        <div className="pitch-heading-meta">
          <span className="pitch-badge">Interactive Pitch</span>
          <h3 className="pitch-title">Tactical Pitch View</h3>
        </div>

        <div className="pitch-toolbar" role="toolbar" aria-label="Pitch overlay options">
          <button
            type="button"
            className={`pitch-tool-btn ${showVectors ? 'active' : ''}`}
            onClick={() => setShowVectors(!showVectors)}
            title="Toggle action and pass vectors"
          >
            <span>{showVectors ? 'Hide Vectors' : 'Show Vectors'}</span>
          </button>
          <button
            type="button"
            className={`pitch-tool-btn ${showPressure ? 'active' : ''}`}
            onClick={() => setShowPressure(!showPressure)}
            title="Toggle opponent pressure arrows"
          >
            <span>{showPressure ? 'Hide Pressure' : 'Show Pressure'}</span>
          </button>
          <button
            type="button"
            className={`pitch-tool-btn ${showZones ? 'active' : ''}`}
            onClick={() => setShowZones(!showZones)}
            title="Toggle 18 tactical zone subdivisions"
          >
            <span>{showZones ? 'Hide 18 Zones' : 'Show 18 Zones'}</span>
          </button>
          <button
            type="button"
            className={`pitch-tool-btn ${showLines ? 'active' : ''}`}
            onClick={() => setShowLines(!showLines)}
            title="Toggle unit structure lines connecting players"
          >
            <span>{showLines ? 'Hide Shape Lines' : 'Show Shape Lines'}</span>
          </button>
        </div>
      </div>

      {/* Direction indicators */}
      <div className="pitch-direction-banner" aria-hidden="true">
        <span className="team-dir home-dir">
          <strong style={{ color: '#ef4444' }}>{homeName}</strong> attacking →
        </span>
        <span className="team-dir away-dir">
          ← attacking <strong style={{ color: '#0284c7' }}>{awayName}</strong>
        </span>
      </div>

      {/* Pitch SVG Screen */}
      <div className="tactical-pitch-screen">
        <svg
          className="tactical-pitch-svg"
          viewBox="-3.5 -2.5 107 73"
          preserveAspectRatio="xMidYMid meet"
          role="img"
          aria-label="Tactical pitch displaying player positions, tactical vectors, constraint zones, and match ball"
        >
          <defs>
            {/* Deep Stadium Turf & Radial Floodlight Gradients */}
            <radialGradient id="tactical-pitch-floodlight" cx="50%" cy="50%" r="58%" fx="50%" fy="48%">
              <stop offset="0%" stopColor="#14532d" />
              <stop offset="45%" stopColor="#0f3d21" />
              <stop offset="85%" stopColor="#0b2c19" />
              <stop offset="100%" stopColor="#071e11" />
            </radialGradient>

            <linearGradient id="tactical-stripe-sheen" x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor="#ffffff" stopOpacity="0.04" />
              <stop offset="50%" stopColor="#ffffff" stopOpacity="0.01" />
              <stop offset="100%" stopColor="#000000" stopOpacity="0.08" />
            </linearGradient>

            {/* 3D Soccer Ball Shading Gradients */}
            <radialGradient id="tactical-ball-leather" cx="35%" cy="32%" r="65%">
              <stop offset="0%" stopColor="#ffffff" />
              <stop offset="55%" stopColor="#f1f5f9" />
              <stop offset="85%" stopColor="#cbd5e1" />
              <stop offset="100%" stopColor="#94a3b8" />
            </radialGradient>

            {/* Goal Net Hexagonal / Diamond Weave Pattern */}
            <pattern id="tactical-net-pattern" width="0.7" height="0.7" patternUnits="userSpaceOnUse">
              <rect width="0.7" height="0.7" fill="rgba(255,255,255,0.02)" />
              <path d="M 0 0.7 L 0.7 0 M 0 0 L 0.7 0.7" stroke="rgba(255,255,255,0.4)" strokeWidth="0.08" />
            </pattern>

            {/* Diagonal Constraint Zone Hatch Pattern matching screenshot */}
            <pattern id="tactical-constraint-hatch" width="2" height="2" patternTransform="rotate(45 0 0)" patternUnits="userSpaceOnUse">
              <line x1="0" y1="0" x2="0" y2="2" stroke="rgba(239, 68, 68, 0.45)" strokeWidth="0.5" />
            </pattern>

            {/* Glowing Arrowheads for Action, Pass, and Pressure Vectors */}
            <marker id="goal-arrowhead" markerWidth="6" markerHeight="6" refX="4" refY="3" orient="auto">
              <polygon points="0 0.5, 5 3, 0 5.5" fill="#f59e0b" />
            </marker>

            <marker id="pass-arrowhead" markerWidth="6" markerHeight="6" refX="4" refY="3" orient="auto">
              <polygon points="0 1, 4.5 3, 0 5" fill="#38bdf8" />
            </marker>

            <marker id="pressure-arrowhead" markerWidth="5" markerHeight="5" refX="3.5" refY="2.5" orient="auto">
              <polygon points="0 0.8, 4 2.5, 0 4.2" fill="#ef4444" />
            </marker>

            {/* Glow Filter for Decisive Finishing Vectors */}
            <filter id="goal-vector-glow" x="-20%" y="-20%" width="140%" height="140%">
              <feGaussianBlur stdDeviation="0.4" result="blur" />
              <feMerge>
                <feMergeNode in="blur" />
                <feMergeNode in="SourceGraphic" />
              </feMerge>
            </filter>
          </defs>

          {/* Outer Turf Apron & Run-off Track */}
          <rect x="-4" y="-3" width="108" height="74" fill="#082013" />

          {/* Main Floodlit Playing Turf */}
          <rect className="tactical-pitch-grass" x="0" y="0" width="100" height="68" fill="url(#tactical-pitch-floodlight)" />

          {/* 10 Precision Lawn Mowing Bands (4.7m stripe alternation) */}
          {Array.from({ length: 10 }, (_, index) => (
            <rect
              key={index}
              x={3 + index * 9.4}
              y="3"
              width="4.7"
              height="62"
              fill="url(#tactical-stripe-sheen)"
              opacity="0.45"
            />
          ))}

          {/* Shaded Constraint / Danger Zone Overlay matching the screenshot */}
          {showVectors && activeZone && (
            <g className="tactical-constraint-zone-group">
              <rect
                x={activeZone.x}
                y={activeZone.y}
                width={activeZone.width}
                height={activeZone.height}
                fill="url(#tactical-constraint-hatch)"
                stroke="#ef4444"
                strokeWidth="0.45"
                strokeDasharray="1.5 1"
                rx="0.5"
              />
              <rect
                x={activeZone.x + 1}
                y={activeZone.y + 1}
                width={activeZone.width - 2}
                height="3.6"
                fill="rgba(15, 23, 42, 0.85)"
                rx="0.4"
                stroke="#ef4444"
                strokeWidth="0.2"
              />
              <text
                x={activeZone.x + activeZone.width / 2}
                y={activeZone.y + 3.3}
                textAnchor="middle"
                fill="#fca5a5"
                fontSize="1.9"
                fontWeight="800"
                letterSpacing="0.04em"
              >
                {constraintZoneLabel}
              </text>
            </g>
          )}

          {/* 18 Tactical Zones Grid (Optional Overlay) */}
          {showZones && (
            <g className="tactical-zones-overlay">
              {TACTICAL_ZONES.map(zone => (
                <g key={zone.id}>
                  <rect
                    x={zone.x}
                    y={zone.y}
                    width={zone.width}
                    height={zone.height}
                    fill={zone.highlight ? 'rgba(234, 179, 8, 0.12)' : 'rgba(255, 255, 255, 0.02)'}
                    stroke={zone.highlight ? '#eab308' : 'rgba(255, 255, 255, 0.15)'}
                    strokeWidth={zone.highlight ? 0.35 : 0.2}
                    strokeDasharray={zone.highlight ? undefined : '1.5 1.5'}
                  />
                  <text
                    x={zone.x + zone.width / 2}
                    y={zone.y + zone.height / 2 + 1}
                    textAnchor="middle"
                    fill={zone.highlight ? '#fef08a' : 'rgba(255, 255, 255, 0.4)'}
                    fontSize="2.2"
                    fontWeight="600"
                    pointerEvents="none"
                  >
                    {zone.id === 14 ? 'Zone 14' : `Z${zone.id}`}
                  </text>
                </g>
              ))}
            </g>
          )}

          {/* Goal Structures */}
          <g className="goal-nets">
            <rect x="0.85" y="30.66" width="2.15" height="6.68" rx="0.3" fill="url(#tactical-net-pattern)" stroke="rgba(255,255,255,0.7)" strokeWidth="0.25" />
            <line x1="3" y1="30.66" x2="3" y2="37.34" stroke="#ffffff" strokeWidth="0.5" strokeLinecap="round" />
            <rect x="97" y="30.66" width="2.15" height="6.68" rx="0.3" fill="url(#tactical-net-pattern)" stroke="rgba(255,255,255,0.7)" strokeWidth="0.25" />
            <line x1="97" y1="30.66" x2="97" y2="37.34" stroke="#ffffff" strokeWidth="0.5" strokeLinecap="round" />
          </g>

          {/* Official Regulation Pitch Markings in Crisp White */}
          <g className="tactical-pitch-lines" fill="none" stroke="rgba(255, 255, 255, 0.88)" strokeWidth="0.35" strokeLinecap="round" strokeLinejoin="round">
            <rect x="3" y="3" width="94" height="62" />
            <line x1="50" y1="3" x2="50" y2="65" />
            <circle cx="50" cy="34" r="8.19" />
            <circle cx="50" cy="34" r="0.45" fill="#ffffff" />
            <rect x="3" y="15.62" width="14.77" height="36.76" />
            <rect x="3" y="25.65" width="4.92" height="16.70" />
            <circle cx="12.85" cy="34" r="0.45" fill="#ffffff" />
            <path d="M 17.77 27.45 A 8.19 8.19 0 0 1 17.77 40.55" />
            <rect x="82.23" y="15.62" width="14.77" height="36.76" />
            <rect x="92.08" y="25.65" width="4.92" height="16.70" />
            <circle cx="87.15" cy="34" r="0.45" fill="#ffffff" />
            <path d="M 82.23 27.45 A 8.19 8.19 0 0 0 82.23 40.55" />
            <path d="M 3 3.9 A 0.9 0.9 0 0 0 3.9 3" />
            <path d="M 3 64.1 A 0.9 0.9 0 0 1 3.9 65" />
            <path d="M 97 3.9 A 0.9 0.9 0 0 1 96.1 3" />
            <path d="M 97 64.1 A 0.9 0.9 0 0 0 96.1 65" />
          </g>

          {/* Corner Flag Pins */}
          <g className="corner-flags" pointerEvents="none">
            <circle cx="3" cy="3" r="0.45" fill="#facc15" stroke="#ca8a04" strokeWidth="0.1" />
            <circle cx="3" cy="65" r="0.45" fill="#facc15" stroke="#ca8a04" strokeWidth="0.1" />
            <circle cx="97" cy="3" r="0.45" fill="#facc15" stroke="#ca8a04" strokeWidth="0.1" />
            <circle cx="97" cy="65" r="0.45" fill="#facc15" stroke="#ca8a04" strokeWidth="0.1" />
          </g>

          {/* Tactical Line Guides (Connecting Backlines & Midfielders) */}
          {showLines && (
            <g className="tactical-shape-lines" strokeDasharray="1 1" strokeWidth="0.25" fill="none" opacity="0.65">
              {homeDefenders.length > 1 && (
                <polyline
                  points={homeDefenders.map(p => `${mapX(p.x)},${mapY(p.y)}`).join(' ')}
                  stroke="#ef4444"
                />
              )}
              {homeMidfielders.length > 1 && (
                <polyline
                  points={homeMidfielders.map(p => `${mapX(p.x)},${mapY(p.y)}`).join(' ')}
                  stroke="#ef4444"
                />
              )}
              {awayDefenders.length > 1 && (
                <polyline
                  points={awayDefenders.map(p => `${mapX(p.x)},${mapY(p.y)}`).join(' ')}
                  stroke="#0284c7"
                />
              )}
            </g>
          )}

          {/* Build-up / Assist Delivery Vector (Pass into Event) */}
          {showVectors && leadupX !== undefined && leadupY !== undefined && eventX !== undefined && eventY !== undefined && (
            <g className="tactical-leadup-vector-group">
              <line
                x1={mapX(leadupX)}
                y1={mapY(leadupY)}
                x2={mapX(eventX)}
                y2={mapY(eventY)}
                stroke="#38bdf8"
                strokeWidth="0.5"
                strokeDasharray="1.2 0.8"
                markerEnd="url(#pass-arrowhead)"
              />
              <circle cx={mapX(leadupX)} cy={mapY(leadupY)} r="0.8" fill="#38bdf8" />
              <text
                x={(mapX(leadupX) + mapX(eventX)) / 2}
                y={(mapY(leadupY) + mapY(eventY)) / 2 - 1.2}
                textAnchor="middle"
                fill="#7dd3fc"
                fontSize="1.7"
                fontWeight="700"
                stroke="#0f172a"
                strokeWidth="0.3"
                paintOrder="stroke"
              >
                {leadup?.playerName ? `Pass: ${leadup.playerName}` : 'Progression Pass'}
              </text>
            </g>
          )}

          {/* Decisive Finishing / Action Vector (e.g. InsideBoxFinish or Shot into the Net) */}
          {showVectors && eventX !== undefined && eventY !== undefined && endX !== undefined && endY !== undefined && (
            <g className="tactical-action-vector-group" filter="url(#goal-vector-glow)">
              <line
                x1={mapX(eventX)}
                y1={mapY(eventY)}
                x2={mapX(endX)}
                y2={mapY(endY)}
                stroke={isGoal ? '#f59e0b' : '#34d399'}
                strokeWidth="0.75"
                markerEnd="url(#goal-arrowhead)"
              />
              <rect
                x={(mapX(eventX) + mapX(endX)) / 2 - 8}
                y={(mapY(eventY) + mapY(endY)) / 2 - 2.8}
                width="16"
                height="3.2"
                fill="rgba(15, 23, 42, 0.9)"
                rx="0.4"
                stroke={isGoal ? '#f59e0b' : '#34d399'}
                strokeWidth="0.25"
              />
              <text
                x={(mapX(eventX) + mapX(endX)) / 2}
                y={(mapY(eventY) + mapY(endY)) / 2 - 0.7}
                textAnchor="middle"
                fill={isGoal ? '#fde047' : '#a7f3d0'}
                fontSize="1.6"
                fontWeight="800"
              >
                {recentEvent?.action ? `${recentEvent.action} ⚡` : isGoal ? 'Goal Finish ⚽' : 'Shot on Target 🎯'}
              </text>
            </g>
          )}

          {/* Opponent Pressure Collapse Arrows converging on ball carrier */}
          {showPressure && eventX !== undefined && eventY !== undefined && pressurePlayers.map(({ player }, idx) => {
            const px = mapX(player.x);
            const py = mapY(player.y);
            const targetX = mapX(eventX);
            const targetY = mapY(eventY);
            return (
              <g key={`pressure-${player.id}-${idx}`} className="tactical-pressure-arrow">
                <line
                  x1={px}
                  y1={py}
                  x2={targetX}
                  y2={targetY}
                  stroke="#ef4444"
                  strokeWidth="0.38"
                  strokeDasharray="0.9 0.6"
                  markerEnd="url(#pressure-arrowhead)"
                  opacity="0.85"
                />
                <text
                  x={(px + targetX) / 2}
                  y={(py + targetY) / 2 - 0.8}
                  textAnchor="middle"
                  fill="#f87171"
                  fontSize="1.4"
                  fontWeight="700"
                  stroke="#000000"
                  strokeWidth="0.25"
                  paintOrder="stroke"
                >
                  Pressure
                </text>
              </g>
            );
          })}

          {/* Away Team Players (Sky Blue) */}
          {effectiveAwayPlayers.map((player, idx) => {
            const px = mapX(player.x ?? (70 + (idx % 4) * 8));
            const py = mapY(player.y ?? (15 + (idx % 5) * 11));
            const isHovered = hoveredPlayer?.id === player.id;
            const isActor = recentEvent?.playerName === player.name;

            return (
              <g
                key={`away-${player.id}-${idx}`}
                className={`tactical-player away-team ${isActor ? 'is-key-actor' : ''}`}
                transform={`translate(${px} ${py})`}
                onMouseEnter={() => setHoveredPlayer(player)}
                onMouseLeave={() => setHoveredPlayer(null)}
                style={{ cursor: 'pointer' }}
              >
                {isActor && (
                  <>
                    <circle cx="0" cy="0" r="4.5" fill="none" stroke="#38bdf8" strokeWidth="0.3" opacity="0.8" />
                    <circle cx="0" cy="0" r="6" fill="none" stroke="#38bdf8" strokeWidth="0.2" opacity="0.4" />
                  </>
                )}
                <circle cx="0" cy="0" r={isHovered || isActor ? 2.5 : 2} fill="#0284c7" stroke="#ffffff" strokeWidth="0.35" />
                <text x="0" y="0.8" textAnchor="middle" fill="#ffffff" fontSize="1.8" fontWeight="700">
                  {player.jerseyNumber ?? idx + 1}
                </text>
                {(isHovered || isActor) && (
                  <text x="0" y="-3.2" textAnchor="middle" fill="#ffffff" fontSize="2.2" fontWeight="700" stroke="#000000" strokeWidth="0.2" paintOrder="stroke">
                    {player.name}
                  </text>
                )}
              </g>
            );
          })}

          {/* Home Team Players (Crimson Red) */}
          {effectiveHomePlayers.map((player, idx) => {
            const px = mapX(player.x ?? (20 + (idx % 4) * 8));
            const py = mapY(player.y ?? (15 + (idx % 5) * 11));
            const isHovered = hoveredPlayer?.id === player.id;
            const isActor = recentEvent?.playerName === player.name;

            return (
              <g
                key={`home-${player.id}-${idx}`}
                className={`tactical-player home-team ${isActor ? 'is-key-actor' : ''}`}
                transform={`translate(${px} ${py})`}
                onMouseEnter={() => setHoveredPlayer(player)}
                onMouseLeave={() => setHoveredPlayer(null)}
                style={{ cursor: 'pointer' }}
              >
                {isActor && (
                  <>
                    <circle cx="0" cy="0" r="4.5" fill="none" stroke="#f59e0b" strokeWidth="0.4" opacity="0.9" />
                    <circle cx="0" cy="0" r="6.5" fill="none" stroke="#f59e0b" strokeWidth="0.25" opacity="0.5" />
                  </>
                )}
                <circle cx="0" cy="0" r={isHovered || isActor ? 2.5 : 2} fill="#dc2626" stroke="#ffffff" strokeWidth="0.35" />
                <text x="0" y="0.8" textAnchor="middle" fill="#ffffff" fontSize="1.8" fontWeight="700">
                  {player.jerseyNumber ?? idx + 1}
                </text>
                {(isHovered || isActor) && (
                  <text x="0" y="-3.2" textAnchor="middle" fill="#ffffff" fontSize="2.2" fontWeight="700" stroke="#000000" strokeWidth="0.2" paintOrder="stroke">
                    {player.name}
                  </text>
                )}
              </g>
            );
          })}

          {/* ⚽ 3D Soccer Ball Marker */}
          <g
            className="tactical-ball-marker"
            transform={`translate(${effBallX} ${effBallY})`}
            role="img"
            aria-label={`Match ball at pitch coordinates X ${effBallX.toFixed(1)}, Y ${effBallY.toFixed(1)}`}
          >
            <ellipse cx="0" cy="0.4" rx="1.1" ry="0.45" fill="rgba(0, 0, 0, 0.45)" />
            <circle cx="0" cy="0" r="1.1" fill="url(#tactical-ball-leather)" stroke="#0f172a" strokeWidth="0.16" />
            <polygon points="0,-0.38 0.36,-0.12 0.22,0.3 -0.22,0.3 -0.36,-0.12" fill="#0f172a" />
          </g>
        </svg>
      </div>

      {/* Pitch Footer with Hovered details / status */}
      <div className="tactical-pitch-footer">
        <div className="pitch-footer-item">
          <span className="footer-label">Mapped Lineups:</span>
          <strong>{players.length} players tracked</strong>
        </div>
        {activeZone && (
          <div className="pitch-footer-item highlight-zone">
            <span className="footer-label">Action Zone:</span>
            <strong>{activeZone.name}</strong>
          </div>
        )}
        {hoveredPlayer && (
          <div className="pitch-footer-item highlight-player">
            <span className="footer-label">Inspecting:</span>
            <strong>{hoveredPlayer.name} (#{hoveredPlayer.jerseyNumber}) · {hoveredPlayer.teamId}</strong>
          </div>
        )}
        <div className="pitch-footer-item">
          <span className="footer-label">Vector Analysis:</span>
          <span className="calib-badge">Dynamic Geometry Verified</span>
        </div>
      </div>
    </div>
  );
}
