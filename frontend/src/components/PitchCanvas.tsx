import React, { useState, useRef, useMemo, useCallback } from 'react';
import { getObservedPositionMetrics } from '../services/playerPositionMetrics';
import { TURF_PALETTES, ZONE_DEFINITIONS, type TurfTone, type RegionMode, type ZoneDefinition } from './pitchConstants';

export { TURF_PALETTES, ZONE_DEFINITIONS };
export type { ZoneDefinition, TurfTone, RegionMode };

export interface PlayerPosition {
  playerName: string;
  teamName: string;
  teamId: string;
  jerseyNumber: number;
  position: string;
  x: number;
  y: number;
  speed?: number;             // km/h at this instant
  distanceCovered?: number;   // km covered so far
  heatIndex?: number;         // 0-1 activity intensity
  passAccuracy?: number;     // % completion
  touches?: number;          // total match touches
  stamina?: number;          // % remaining stamina
  heartRate?: number;        // bpm instantaneous heart rate
}

export interface PitchMatchEvent {
  eventId: string;
  matchId: string;
  sequence: number;
  eventType: string;
  clockSeconds: number;
  period: number;
  teamId?: string;
  teamName?: string;
  playerName?: string;
  receiverName?: string;
  pitchX?: number;
  pitchY?: number;
  endX?: number;
  endY?: number;
  outcome?: string;
  ballX?: number;
  ballY?: number;
  metadata?: Record<string, string>;
  playerPositions?: PlayerPosition[];
  occurredAt?: string;
}

export type MatchEvent = PitchMatchEvent;

export type StrategyTruthClass = 'ObservedFact' | 'DeterministicDerivation' | 'SupportedInterpretation' | 'Hypothesis';

export interface TacticalPitchPoint { x: number; y: number; }

export interface TacticalStrategyGeometry {
  id: string;
  kind: 'Lane';
  points: TacticalPitchPoint[];
}

export interface TacticalStrategyObservation {
  schemaVersion: number;
  observationId: string;
  matchId: string;
  strategyType: string;
  teamId: string;
  windowStartClockSeconds: number;
  windowEndClockSeconds: number;
  selectedClockSeconds: number;
  truthClass: StrategyTruthClass;
  confidence: number;
  geometry: TacticalStrategyGeometry[];
  participantIds: string[];
  evidenceEventIds: string[];
  detectorName: string;
  detectorVersion: string;
  provenance: 'Canonical' | 'SyntheticSimulation';
  limitations: string[];
  contextRevision: number;
}

const HOME_TEAM_ID = 'team-home';
const HOME_COLOR = '#dc2626';       // Arsenal red
const HOME_COLOR_DARK = '#991b1b';
const AWAY_COLOR = '#0284c7';       // Man City sky blue
const AWAY_COLOR_DARK = '#0369a1';

export interface PitchCanvasProps {
  events: MatchEvent[];
  selectedEventId?: string | null;
  onSelectEvent?: (event: MatchEvent) => void;
  showZones?: boolean;
  homeTeamName?: string;
  awayTeamName?: string;
  homeTeamColor?: string;
  awayTeamColor?: string;
  compact?: boolean;
  isLive?: boolean;
  playerPositionsOverride?: readonly PlayerPosition[];
  tacticalStrategyObservation?: TacticalStrategyObservation | null;
  initialTurfTone?: TurfTone;
  initialRegionMode?: RegionMode;
}

interface TooltipState {
  player: PlayerPosition;
  svgX: number;
  svgY: number;
}

export const PitchCanvas: React.FC<PitchCanvasProps> = ({
  events,
  selectedEventId,
  onSelectEvent,
  showZones = true,
  homeTeamName,
  awayTeamName,
  homeTeamColor,
  awayTeamColor,
  compact = false,
  isLive = false,
  playerPositionsOverride,
  tacticalStrategyObservation,
  initialRegionMode = '18zones',
}) => {
  const [hoveredPlayer, setHoveredPlayer] = useState<TooltipState | null>(null);
  const [isFullscreen, setIsFullscreen] = useState<boolean>(false);
  const [hoveredZoneId, setHoveredZoneId] = useState<number | string | null>(null);
  const [regionMode] = useState<RegionMode>(initialRegionMode);
  const svgRef = useRef<SVGSVGElement>(null);

  // Hard-coded pitch settings: Cinema 16:9, Huge Full Ground, 100% scale, Dark Turf
  const currentTurf = TURF_PALETTES.dark;
  const zoomScale = 1.0;
  const showTrails = false;
  const pitchScale = 'cinema';
  const cinemaMode = 'broadcast';

  // Responsive dimensions for 16:9 Cinema Huge Full Ground
  const svgW = 1600;
  const svgH = 900;
  const pitchW = 1520;
  const pitchH = 860;
  const padX = 40;
  const padY = 20;
  const goalDepth = 32;
  const maxHeight = 'none';
  const maxWidth = '100%';
  const playerRadius = 23;

  const toSvgX = useCallback((x: number) => padX + (x / 100) * pitchW, [padX, pitchW]);
  const toSvgY = useCallback((y: number) => padY + (y / 100) * pitchH, [padY, pitchH]);

  // ── Exact IFAB Pitch Dimensions (105m x 68m standard international pitch) ──
  const PITCH_LEN_M = 105;
  const PITCH_WID_M = 68;

  // Goal width centered at vertical midpoint
  const GOAL_WIDTH_M = 7.32;
  const GOAL_TOP_Y = 50 - ((GOAL_WIDTH_M / 2) / PITCH_WID_M) * 100;     // 44.618%
  const GOAL_BOT_Y = 50 + ((GOAL_WIDTH_M / 2) / PITCH_WID_M) * 100;     // 55.382%

  // 6-Yard Box: extends 5.5m into pitch, 18.32m wide (5.5m each side of 7.32m goal)
  const SIX_YARD_DIST_M = 5.5;
  const SIX_YARD_WIDTH_M = 18.32;
  const SIX_YARD_TOP_Y = 50 - ((SIX_YARD_WIDTH_M / 2) / PITCH_WID_M) * 100; // 36.529%
  const SIX_YARD_BOT_Y = 50 + ((SIX_YARD_WIDTH_M / 2) / PITCH_WID_M) * 100; // 63.471%
  const SIX_YARD_LEN_PX = (SIX_YARD_DIST_M / PITCH_LEN_M) * pitchW;

  // 18-Yard Box (Penalty Area): extends 16.5m into pitch, 40.32m wide (16.5m each side of 7.32m goal)
  const PENALTY_AREA_DIST_M = 16.5;
  const PENALTY_AREA_WIDTH_M = 40.32;
  const PENALTY_TOP_Y = 50 - ((PENALTY_AREA_WIDTH_M / 2) / PITCH_WID_M) * 100; // 20.353%
  const PENALTY_BOT_Y = 50 + ((PENALTY_AREA_WIDTH_M / 2) / PITCH_WID_M) * 100; // 79.647%
  const PENALTY_AREA_LEN_PX = (PENALTY_AREA_DIST_M / PITCH_LEN_M) * pitchW;

  // Penalty Spot: 11.0m from goal line
  const PENALTY_SPOT_DIST_M = 11.0;
  const PENALTY_SPOT_LEFT_X = padX + (PENALTY_SPOT_DIST_M / PITCH_LEN_M) * pitchW;
  const PENALTY_SPOT_RIGHT_X = padX + pitchW - (PENALTY_SPOT_DIST_M / PITCH_LEN_M) * pitchW;

  // Penalty Arc ("The D"): 9.15m radius from penalty spot outside penalty box
  // Since scaleX === scaleY (isotropic 105:68), radius in pixels = (9.15 / 105) * pitchW
  const ARC_R = (9.15 / PITCH_LEN_M) * pitchW;
  const ARC_DY_M = Math.sqrt(9.15 * 9.15 - 5.5 * 5.5); // 7.3125m
  const ARC_TOP_Y = 50 - (ARC_DY_M / PITCH_WID_M) * 100; // 39.246%
  const ARC_BOT_Y = 50 + (ARC_DY_M / PITCH_WID_M) * 100; // 60.754%

  // Corner Arc: 1.0m radius
  const CORNER_ARC_R = (1.0 / PITCH_LEN_M) * pitchW;

  // Get currently active/selected event
  const activeEvent = selectedEventId
    ? events.find(e => e.eventId === selectedEventId)
    : events[events.length - 1];

  // Filter strictly to on-pitch active players (substitutes do NOT appear on the playing turf)
  const onPitchPlayers = useMemo(() => {
    const list = playerPositionsOverride ?? activeEvent?.playerPositions ?? [];
    return list.filter(p => p.position !== 'SUB');
  }, [playerPositionsOverride, activeEvent?.playerPositions]);
  const observedPositionMetrics = useMemo(() => hoveredPlayer
    ? getObservedPositionMetrics(hoveredPlayer.player)
    : [], [hoveredPlayer]);

  // Identify whether active event is a goal
  const isGoalEvent = Boolean(activeEvent && (activeEvent.eventType === 'Goal' || activeEvent.outcome === 'Goal'));

  // ── Goal Build-Up 10-Pass Sequence with Opacity Progression ─────────────────
  // When active event is a Goal, extract the last 10 passes leading directly to it.
  // 10th pass back is faint in color (0.15 opacity), and opacity increases with each count to 1.0 for the assist.
  const goalPassSequence = useMemo(() => {
    if (!isGoalEvent || !activeEvent) return [];

    const currentIdx = events.findIndex(e => e.eventId === activeEvent.eventId);
    const searchSlice = currentIdx >= 0 ? events.slice(0, currentIdx) : events;

    const candidatePasses: MatchEvent[] = [];
    const passTypes = ['Pass', 'ProgressivePass', 'LineBreakingPass', 'Cross', 'SwitchOfPlay', 'ThroughBall', 'FreeKick'];

    for (let i = searchSlice.length - 1; i >= 0 && candidatePasses.length < 10; i--) {
      const ev = searchSlice[i];
      const isPass = passTypes.includes(ev.eventType) ||
        (ev.endX != null && ev.endY != null && (ev.endX !== ev.pitchX || ev.endY !== ev.pitchY));
      if (isPass && ev.pitchX != null && ev.pitchY != null) {
        candidatePasses.push(ev);
      }
    }

    // Chronological order: candidatePasses[0] is oldest (10th back), candidatePasses[N-1] is final assist
    candidatePasses.reverse();
    const N = candidatePasses.length;
    if (N === 0) return [];

    return candidatePasses.map((ev, idx) => {
      // 10th pass is faint (0.15), increasing steadily to 1.0 for the final assist
      const opacity = N === 1 ? 1.0 : Number((0.15 + (0.85 * (idx / (N - 1)))).toFixed(3));
      const sx = toSvgX(ev.pitchX ?? 50);
      const sy = toSvgY(ev.pitchY ?? 50);
      const ex = toSvgX(ev.endX ?? (ev.pitchX ?? 50) + 8);
      const ey = toSvgY(ev.endY ?? (ev.pitchY ?? 50));

      const dxM = ((ev.endX ?? 0) - (ev.pitchX ?? 0)) * 1.05;
      const dyM = ((ev.endY ?? 0) - (ev.pitchY ?? 0)) * 0.68;
      const distM = Math.hypot(dxM, dyM).toFixed(1);
      const angle = Math.atan2(ey - sy, ex - sx);

      return {
        event: ev,
        index: idx,
        count: idx + 1,
        totalCount: N,
        opacity,
        startX: sx,
        startY: sy,
        endX: ex,
        endY: ey,
        distM,
        angle,
        isAssist: idx === N - 1,
      };
    });
  }, [isGoalEvent, activeEvent, events, toSvgX, toSvgY]);

  // Identify whether active event is a pass or directional action
  const isPassEvent = Boolean(activeEvent && (
    ['Pass', 'ProgressivePass', 'LineBreakingPass', 'FreeKick', 'Cross',
      'SwitchOfPlay', 'ThroughBall'].includes(activeEvent.eventType)
  ));

  // Identify passer player (from playerPositions or matching name)
  const passerPlayer = onPitchPlayers.find(p =>
    activeEvent?.playerName && p.playerName.toLowerCase() === activeEvent.playerName.toLowerCase()
  ) ?? (activeEvent?.pitchX != null && activeEvent?.pitchY != null
    ? onPitchPlayers.find(p => Math.hypot(p.x - activeEvent.pitchX!, p.y - activeEvent.pitchY!) < 6)
    : undefined);

  // Identify receiver player
  const receiverPlayer = onPitchPlayers.find(p =>
    activeEvent?.receiverName && p.playerName.toLowerCase() === activeEvent.receiverName.toLowerCase()
  ) ?? (activeEvent?.endX != null && activeEvent?.endY != null
    ? onPitchPlayers.find(p => p !== passerPlayer && Math.hypot(p.x - activeEvent.endX!, p.y - activeEvent.endY!) < 6)
    : undefined);

  // Dynamic pass coordinates
  const passStartX = passerPlayer ? toSvgX(passerPlayer.x) : (activeEvent?.pitchX != null ? toSvgX(activeEvent.pitchX) : toSvgX(50));
  const passStartY = passerPlayer ? toSvgY(passerPlayer.y) : (activeEvent?.pitchY != null ? toSvgY(activeEvent.pitchY) : toSvgY(50));
  const passEndX = receiverPlayer ? toSvgX(receiverPlayer.x) : (activeEvent?.endX != null ? toSvgX(activeEvent.endX) : passStartX + 120);
  const passEndY = receiverPlayer ? toSvgY(receiverPlayer.y) : (activeEvent?.endY != null ? toSvgY(activeEvent.endY) : passStartY);

  // Calculate pass distance in meters (standard pitch is 105m x 68m)
  const dxMeters = activeEvent?.pitchX != null && activeEvent?.endX != null
    ? (activeEvent.endX - activeEvent.pitchX) * 1.05 : 0;
  const dyMeters = activeEvent?.pitchY != null && activeEvent?.endY != null
    ? (activeEvent.endY - activeEvent.pitchY) * 0.68 : 0;
  const passDistanceM = Math.hypot(dxMeters, dyMeters).toFixed(1);

  // Player circle sizing: dynamic per view mode
  const PLAYER_RADIUS = playerRadius;

  const effHomeTeamName = homeTeamName || 'Arsenal';
  const effAwayTeamName = awayTeamName || 'Man City';
  const effHomeColor = homeTeamColor || HOME_COLOR;
  const effAwayColor = awayTeamColor || AWAY_COLOR;

  const teamColor = (teamId: string, dark = false) => {
    const isHome = teamId === HOME_TEAM_ID ||
      (effHomeTeamName && teamId.toLowerCase().includes(effHomeTeamName.toLowerCase().slice(0, 3))) ||
      teamId.toLowerCase().includes('home');
    if (isHome) return dark ? HOME_COLOR_DARK : effHomeColor;
    return dark ? AWAY_COLOR_DARK : effAwayColor;
  };

  const ballCarrierName = activeEvent?.playerName;

  // Active ball / play tactical zone lookup
  const activeZone = useMemo(() => {
    if (!showZones || activeEvent?.pitchX == null || activeEvent?.pitchY == null) return null;
    const px = activeEvent.pitchX;
    const py = activeEvent.pitchY;
    return ZONE_DEFINITIONS.find(z => px >= z.x && px <= z.x + z.w && py >= z.y && py <= z.y + z.h) || null;
  }, [activeEvent?.pitchX, activeEvent?.pitchY, showZones]);

  return (
    <div className="pitch-canvas" style={isFullscreen ? {
      position: 'fixed',
      top: 0,
      left: 0,
      width: '100vw',
      height: '100vh',
      zIndex: 9999,
      background: '#07100d',
      overflow: 'hidden',
      borderRadius: 0,
      border: 'none',
      boxShadow: 'none',
      display: 'flex',
      flexDirection: 'column',
      alignItems: 'center',
      justifyContent: 'center',
      padding: '0',
    } : {
      position: 'relative',
      width: '100%',
      borderRadius: '16px',
      overflow: 'hidden',
      border: '1px solid #1e293b',
      boxShadow: '0 12px 40px rgba(0,0,0,0.3)',
      background: '#07100d',
      display: 'flex',
      flexDirection: 'column',
    }}>
      {/* ── Single Fullscreen Icon Button (Only icon on the pitch view) ── */}
      <button
        type="button"
        id="pitch-fullscreen-btn"
        onClick={() => setIsFullscreen(!isFullscreen)}
        aria-pressed={isFullscreen}
        aria-label={isFullscreen ? 'Exit fullscreen pitch' : 'Make pitch full screen'}
        title={isFullscreen ? 'Exit fullscreen' : 'Make pitch full screen'}
        style={{
          position: 'absolute',
          top: 14,
          right: 14,
          zIndex: 35,
          width: 38,
          height: 38,
          borderRadius: 8,
          border: '1px solid rgba(52, 211, 153, 0.4)',
          background: 'rgba(11, 29, 22, 0.88)',
          backdropFilter: 'blur(8px)',
          color: '#34d399',
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'center',
          cursor: 'pointer',
          padding: 0,
          boxShadow: '0 4px 14px rgba(0, 0, 0, 0.45)',
          transition: 'all 0.2s ease',
        }}
      >
        {isFullscreen ? (
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="M8 3v3a2 2 0 0 1-2 2H3m18 0h-3a2 2 0 0 1-2-2V3m0 18v-3a2 2 0 0 1 2-2h3M3 16h3a2 2 0 0 1 2 2v3" />
          </svg>
        ) : (
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="M8 3H5a2 2 0 0 0-2 2v3m18 0V5a2 2 0 0 0-2-2h-3m0 18h3a2 2 0 0 0 2-2v-3M3 16v3a2 2 0 0 0 2 2h3" />
          </svg>
        )}
      </button>

      {/* ── SVG Pitch Area ───────────────────────────────────────── */}
      <div style={{
        position: 'relative',
        width: '100%',
        maxWidth: maxWidth,
        margin: '0 auto',
        display: 'flex',
        justifyContent: 'center',
        background: currentTurf.apron,
        boxShadow: 'inset 0 0 100px rgba(0,0,0,0.3)',
        transition: 'max-width 0.25s ease',
        overflow: 'hidden',
      }}>
        {/* Floating Goal Build-Up 10-Pass HUD Banner */}
        {!compact && isGoalEvent && goalPassSequence.length > 0 && (
          <div style={{
            position: 'absolute',
            top: 14,
            left: '50%',
            transform: 'translateX(-50%)',
            background: 'rgba(15, 23, 42, 0.92)',
            backdropFilter: 'blur(10px)',
            color: '#ffffff',
            padding: '8px 20px',
            borderRadius: '30px',
            border: '1.5px solid #f59e0b',
            boxShadow: '0 8px 28px rgba(245, 158, 11, 0.45)',
            display: 'flex',
            alignItems: 'center',
            gap: '12px',
            zIndex: 30,
            fontSize: '12px',
            fontWeight: '700',
            pointerEvents: 'none',
            whiteSpace: 'nowrap',
          }}>
            <span>
              <strong style={{ color: '#fbbf24' }}>GOAL BUILD-UP SEQUENCE:</strong> Last {goalPassSequence.length} passes leading to goal
            </span>
            <div style={{
              display: 'inline-flex',
              alignItems: 'center',
              gap: '8px',
              fontSize: '11px',
              color: '#cbd5e1',
              background: 'rgba(255,255,255,0.08)',
              padding: '3px 12px',
              borderRadius: '16px',
            }}>
              <span style={{ opacity: 0.35, color: '#38bdf8', fontWeight: '800' }}>● Pass #1 (15% faint)</span>
              <span style={{ color: '#94a3b8' }}>→</span>
              <span style={{ opacity: 1.0, color: '#fbbf24', fontWeight: '900' }}>● Pass #{goalPassSequence.length} (Assist 100%)</span>
            </div>
          </div>
        )}

        <div style={{
          width: zoomScale > 1 ? `${zoomScale * 100}%` : '100%',
          minWidth: zoomScale > 1 ? `${zoomScale * 100}%` : '100%',
          display: 'flex',
          justifyContent: 'center',
          transition: 'width 0.2s ease',
        }}>
          <svg
            ref={svgRef}
            role="group"
            aria-label={`Tactical pitch showing ${effHomeTeamName} and ${effAwayTeamName} player positions`}
            viewBox={`0 0 ${svgW} ${svgH}`}
            style={{
              width: '100%',
              height: 'auto',
              maxHeight: maxHeight,
              display: 'block',
              objectFit: 'contain',
            }}
          >
            <defs>
              {/* Realistic Light Football Field Grass Pattern */}
              <pattern id="pitch-grass-stripes" width="120" height={svgH} patternUnits="userSpaceOnUse">
                <rect width="60" height={svgH} fill={currentTurf.stripeA} />
                <rect x="60" width="60" height={svgH} fill={currentTurf.stripeB} />
              </pattern>

              {/* Pass Arrow Markers */}
              <marker id="arrow-pass-head" viewBox="0 0 12 12" refX="10" refY="6"
                markerWidth="7" markerHeight="7" orient="auto-start-reverse">
                <path d="M 0 1.5 L 11 6 L 0 10.5 z" fill="#ea580c" />
              </marker>
              <marker id="arrow-switch-head" viewBox="0 0 12 12" refX="10" refY="6"
                markerWidth="7" markerHeight="7" orient="auto-start-reverse">
                <path d="M 0 1.5 L 11 6 L 0 10.5 z" fill="#9333ea" />
              </marker>
              <marker id="arrow-carry-head" viewBox="0 0 12 12" refX="10" refY="6"
                markerWidth="7" markerHeight="7" orient="auto-start-reverse">
                <path d="M 0 1.5 L 11 6 L 0 10.5 z" fill="#0284c7" />
              </marker>

              {/* Player Drop Shadows */}
              <filter id="player-glow-shadow" x="-40%" y="-40%" width="180%" height="180%">
                <feDropShadow dx="0" dy="3" stdDeviation="3.5" floodColor="rgba(0,0,0,0.35)" />
              </filter>

              {/* Glowing Ball Filter */}
              <filter id="ball-glow-filter" x="-80%" y="-80%" width="260%" height="260%">
                <feGaussianBlur stdDeviation="5" result="blur" />
                <feMerge>
                  <feMergeNode in="blur" />
                  <feMergeNode in="SourceGraphic" />
                </feMerge>
              </filter>

              {/* Pass aura gradient */}
              <radialGradient id="passer-aura-grad" cx="50%" cy="50%" r="50%">
                <stop offset="0%" stopColor="#f97316" stopOpacity="0.8" />
                <stop offset="100%" stopColor="#f97316" stopOpacity="0" />
              </radialGradient>
              <radialGradient id="target-aura-grad" cx="50%" cy="50%" r="50%">
                <stop offset="0%" stopColor="#10b981" stopOpacity="0.8" />
                <stop offset="100%" stopColor="#10b981" stopOpacity="0" />
              </radialGradient>

              {/* Authentic Football Goal Net Pattern */}
              <pattern id="goal-net-pattern" width="8" height="8" patternUnits="userSpaceOnUse">
                <rect width="8" height="8" fill="rgba(255,255,255,0.08)" />
                <path d="M 0 0 L 8 0 M 0 0 L 0 8" stroke="rgba(255,255,255,0.4)" strokeWidth="0.9" />
              </pattern>
            </defs>

            {/* ── Pitch Turf & Border ──────────────────────────────── */}
            <rect x={0} y={0} width={svgW} height={svgH} fill={currentTurf.apron} />
            <rect
              x={padX} y={padY} width={pitchW} height={pitchH}
              fill="url(#pitch-grass-stripes)"
              stroke="#ffffff"
              strokeWidth="3.5"
              rx="0"
            />

            {/* ── Official Pitch Markings in Crisp White ──────────── */}
            <g stroke="#ffffff" strokeWidth="2.8" fill="none" opacity="0.95">
              {/* Halfway line */}
              <line x1={toSvgX(50)} y1={padY} x2={toSvgX(50)} y2={padY + pitchH} />

              {/* Centre circle & spot */}
              <circle cx={toSvgX(50)} cy={toSvgY(50)} r={ARC_R} />
              <circle cx={toSvgX(50)} cy={toSvgY(50)} r="4.5" fill="#ffffff" />

              {/* Left Penalty Box (Arsenal Goal Area - 18 yards) */}
              <rect
                x={padX}
                y={toSvgY(PENALTY_TOP_Y)}
                width={PENALTY_AREA_LEN_PX}
                height={toSvgY(PENALTY_BOT_Y) - toSvgY(PENALTY_TOP_Y)}
              />
              {/* Left 6-Yard Box */}
              <rect
                x={padX}
                y={toSvgY(SIX_YARD_TOP_Y)}
                width={SIX_YARD_LEN_PX}
                height={toSvgY(SIX_YARD_BOT_Y) - toSvgY(SIX_YARD_TOP_Y)}
              />
              {/* Left Penalty Spot & Arc */}
              <circle cx={PENALTY_SPOT_LEFT_X} cy={toSvgY(50)} r="4" fill="#ffffff" />
              <path d={`M ${padX + PENALTY_AREA_LEN_PX} ${toSvgY(ARC_TOP_Y)} A ${ARC_R} ${ARC_R} 0 0 1 ${padX + PENALTY_AREA_LEN_PX} ${toSvgY(ARC_BOT_Y)}`} />

              {/* Right Penalty Box (Man City Goal Area - 18 yards) */}
              <rect
                x={padX + pitchW - PENALTY_AREA_LEN_PX}
                y={toSvgY(PENALTY_TOP_Y)}
                width={PENALTY_AREA_LEN_PX}
                height={toSvgY(PENALTY_BOT_Y) - toSvgY(PENALTY_TOP_Y)}
              />
              {/* Right 6-Yard Box */}
              <rect
                x={padX + pitchW - SIX_YARD_LEN_PX}
                y={toSvgY(SIX_YARD_TOP_Y)}
                width={SIX_YARD_LEN_PX}
                height={toSvgY(SIX_YARD_BOT_Y) - toSvgY(SIX_YARD_TOP_Y)}
              />
              {/* Right Penalty Spot & Arc */}
              <circle cx={PENALTY_SPOT_RIGHT_X} cy={toSvgY(50)} r="4" fill="#ffffff" />
              <path d={`M ${padX + pitchW - PENALTY_AREA_LEN_PX} ${toSvgY(ARC_TOP_Y)} A ${ARC_R} ${ARC_R} 0 0 0 ${padX + pitchW - PENALTY_AREA_LEN_PX} ${toSvgY(ARC_BOT_Y)}`} />

              {/* Corner Arcs */}
              <path d={`M ${padX + CORNER_ARC_R} ${padY} A ${CORNER_ARC_R} ${CORNER_ARC_R} 0 0 0 ${padX} ${padY + CORNER_ARC_R}`} />
              <path d={`M ${padX} ${padY + pitchH - CORNER_ARC_R} A ${CORNER_ARC_R} ${CORNER_ARC_R} 0 0 0 ${padX + CORNER_ARC_R} ${padY + pitchH}`} />
              <path d={`M ${padX + pitchW - CORNER_ARC_R} ${padY} A ${CORNER_ARC_R} ${CORNER_ARC_R} 0 0 1 ${padX + pitchW} ${padY + CORNER_ARC_R}`} />
              <path d={`M ${padX + pitchW} ${padY + pitchH - CORNER_ARC_R} A ${CORNER_ARC_R} ${CORNER_ARC_R} 0 0 1 ${padX + pitchW - CORNER_ARC_R} ${padY + pitchH}`} />
            </g>

            {/* ── Authentic Goal Cages, Nets, Posts & Goal Lines ─────── */}
            {/* Left Goal Cage (Behind Left Goal Line) */}
            <rect
              x={padX - goalDepth}
              y={toSvgY(GOAL_TOP_Y)}
              width={goalDepth}
              height={toSvgY(GOAL_BOT_Y) - toSvgY(GOAL_TOP_Y)}
              fill="url(#goal-net-pattern)"
              stroke="#ffffff"
              strokeWidth="2.5"
            />
            {/* Left Goal Depth Perspective Lines */}
            <line x1={padX} y1={toSvgY(GOAL_TOP_Y)} x2={padX - goalDepth} y2={toSvgY(GOAL_TOP_Y) + 5} stroke="#ffffff" strokeWidth="1.8" />
            <line x1={padX} y1={toSvgY(GOAL_BOT_Y)} x2={padX - goalDepth} y2={toSvgY(GOAL_BOT_Y) - 5} stroke="#ffffff" strokeWidth="1.8" />
            <line x1={padX - goalDepth} y1={toSvgY(GOAL_TOP_Y) + 5} x2={padX - goalDepth} y2={toSvgY(GOAL_BOT_Y) - 5} stroke="#ffffff" strokeWidth="2" />

            {/* Right Goal Cage (Behind Right Goal Line) */}
            <rect
              x={padX + pitchW}
              y={toSvgY(GOAL_TOP_Y)}
              width={goalDepth}
              height={toSvgY(GOAL_BOT_Y) - toSvgY(GOAL_TOP_Y)}
              fill="url(#goal-net-pattern)"
              stroke="#ffffff"
              strokeWidth="2.5"
            />
            {/* Right Goal Depth Perspective Lines */}
            <line x1={padX + pitchW} y1={toSvgY(GOAL_TOP_Y)} x2={padX + pitchW + goalDepth} y2={toSvgY(GOAL_TOP_Y) + 5} stroke="#ffffff" strokeWidth="1.8" />
            <line x1={padX + pitchW} y1={toSvgY(GOAL_BOT_Y)} x2={padX + pitchW + goalDepth} y2={toSvgY(GOAL_BOT_Y) - 5} stroke="#ffffff" strokeWidth="1.8" />
            <line x1={padX + pitchW + goalDepth} y1={toSvgY(GOAL_TOP_Y) + 5} x2={padX + pitchW + goalDepth} y2={toSvgY(GOAL_BOT_Y) - 5} stroke="#ffffff" strokeWidth="2" />

            {/* Left & Right Official Goal Lines (between posts) */}
            <line
              x1={padX} y1={toSvgY(GOAL_TOP_Y)}
              x2={padX} y2={toSvgY(GOAL_BOT_Y)}
              stroke="#ffffff" strokeWidth="4.5"
            />
            <line
              x1={padX + pitchW} y1={toSvgY(GOAL_TOP_Y)}
              x2={padX + pitchW} y2={toSvgY(GOAL_BOT_Y)}
              stroke="#ffffff" strokeWidth="4.5"
            />

            {/* 3D Goal Posts (Solid white discs with crisp border) */}
            <circle cx={padX} cy={toSvgY(GOAL_TOP_Y)} r="4.5" fill="#ffffff" stroke="#cbd5e1" strokeWidth="1.5" />
            <circle cx={padX} cy={toSvgY(GOAL_BOT_Y)} r="4.5" fill="#ffffff" stroke="#cbd5e1" strokeWidth="1.5" />
            <circle cx={padX + pitchW} cy={toSvgY(GOAL_TOP_Y)} r="4.5" fill="#ffffff" stroke="#cbd5e1" strokeWidth="1.5" />
            <circle cx={padX + pitchW} cy={toSvgY(GOAL_BOT_Y)} r="4.5" fill="#ffffff" stroke="#cbd5e1" strokeWidth="1.5" />

            {/* Cinema 16:9 Broadcast Embellishments (Technical areas, LED boards) */}
            {pitchScale === 'cinema' && cinemaMode === 'broadcast' && (
              <g opacity="0.95">
                {/* Pitchside LED advertising ribbon along top */}
                <rect x={padX} y={Math.max(2, padY - 14)} width={pitchW} height="10" rx="2" fill="#020617" stroke="#38bdf8" strokeWidth="0.8" />
                <text x={padX + pitchW / 2} y={Math.max(2, padY - 14) + 7.5} textAnchor="middle" fill="#38bdf8" fontSize="6.5" fontWeight="900" fontFamily="'JetBrains Mono', monospace" letterSpacing="0.1em">
                  {`${isLive ? 'PREMIER LEAGUE LIVE' : 'MATCH REPLAY'} · ${effHomeTeamName.toUpperCase()} vs ${effAwayTeamName.toUpperCase()} · SPATIAL VIEW`}
                </text>

                {/* Pitchside LED advertising ribbon along bottom */}
                <rect x={padX} y={padY + pitchH + 4} width={pitchW} height="10" rx="2" fill="#020617" stroke="#34d399" strokeWidth="0.8" />
                <text x={padX + pitchW / 2} y={padY + pitchH + 4 + 7.5} textAnchor="middle" fill="#34d399" fontSize="6.5" fontWeight="900" fontFamily="'JetBrains Mono', monospace" letterSpacing="0.1em">
                  {`PITCHMIND · ${isLive ? 'LIVE SPATIAL VIEW' : 'MATCH SPATIAL REPLAY'} · ${effHomeTeamName.toUpperCase()} vs ${effAwayTeamName.toUpperCase()}`}
                </text>

                {/* Home Team Technical Box (Top Left) */}
                <rect x={padX + 20} y={Math.max(2, padY - 16)} width="130" height="13" rx="3" fill="rgba(220,38,38,0.2)" stroke={effHomeColor} strokeWidth="1.2" strokeDasharray="3,2" />
                <text x={padX + 85} y={Math.max(2, padY - 16) + 9.5} textAnchor="middle" fill={effHomeColor} fontSize="7.5" fontWeight="800" fontFamily="'JetBrains Mono', monospace">{`${effHomeTeamName.toUpperCase()} BENCH`}</text>

                {/* Away Team Technical Box (Top Right) */}
                <rect x={padX + pitchW - 150} y={Math.max(2, padY - 16)} width="130" height="13" rx="3" fill="rgba(2,132,199,0.2)" stroke={effAwayColor} strokeWidth="1.2" strokeDasharray="3,2" />
                <text x={padX + pitchW - 85} y={Math.max(2, padY - 16) + 9.5} textAnchor="middle" fill={effAwayColor} fontSize="7.5" fontWeight="800" fontFamily="'JetBrains Mono', monospace">{`${effAwayTeamName.toUpperCase()} BENCH`}</text>
              </g>
            )}

            {/* Corner Flags for All Modes (Authentic Corner Flags with team colors) */}
            <g opacity="0.95">
              {/* Top-Left Corner Flag (Arsenal Red) */}
              <line x1={padX} y1={padY} x2={padX - 8} y2={padY - 14} stroke="#fbbf24" strokeWidth="2.2" strokeLinecap="round" />
              <polygon points={`${padX - 8},${padY - 14} ${padX},${padY - 18} ${padX - 8},${padY - 22}`} fill="#dc2626" stroke="#991b1b" strokeWidth="0.8" />

              {/* Top-Right Corner Flag (Man City Blue) */}
              <line x1={padX + pitchW} y1={padY} x2={padX + pitchW + 8} y2={padY - 14} stroke="#fbbf24" strokeWidth="2.2" strokeLinecap="round" />
              <polygon points={`${padX + pitchW + 8},${padY - 14} ${padX + pitchW},${padY - 18} ${padX + pitchW + 8},${padY - 22}`} fill="#0284c7" stroke="#0369a1" strokeWidth="0.8" />

              {/* Bottom-Left Corner Flag (Arsenal Red) */}
              <line x1={padX} y1={padY + pitchH} x2={padX - 8} y2={padY + pitchH + 14} stroke="#fbbf24" strokeWidth="2.2" strokeLinecap="round" />
              <polygon points={`${padX - 8},${padY + pitchH + 14} ${padX},${padY + pitchH + 18} ${padX - 8},${padY + pitchH + 22}`} fill="#dc2626" stroke="#991b1b" strokeWidth="0.8" />

              {/* Bottom-Right Corner Flag (Man City Blue) */}
              <line x1={padX + pitchW} y1={padY + pitchH} x2={padX + pitchW + 8} y2={padY + pitchH + 14} stroke="#fbbf24" strokeWidth="2.2" strokeLinecap="round" />
              <polygon points={`${padX + pitchW + 8},${padY + pitchH + 14} ${padX + pitchW},${padY + pitchH + 18} ${padX + pitchW + 8},${padY + pitchH + 22}`} fill="#0284c7" stroke="#0369a1" strokeWidth="0.8" />
            </g>

            {/* ── Comprehensive Tactical Pitch Regions Overlay ─────── */}
            {showZones && regionMode !== 'off' && (
              <g id="tactical-regions-overlay">
                {/* 1. 18 Tactical Positional Play Zones (Pep Guardiola / Arteta / Juego de Posición) */}
                {regionMode === '18zones' && (
                  <g id="tactical-18-zones">
                    {ZONE_DEFINITIONS.map((zone) => {
                      const zx = toSvgX(zone.x);
                      const zy = toSvgY(zone.y);
                      const zw = (zone.w / 100) * pitchW;
                      const zh = (zone.h / 100) * pitchH;
                      const cx = zx + zw / 2;
                      const cy = zy + zh / 2;
                      const isHovered = hoveredZoneId === zone.id;
                      const isActiveBall = activeZone?.id === zone.id;
                      const isZone14 = zone.id === 14;
                      const isBox = zone.id === 15;

                      return (
                        <g
                          key={`zone-${zone.id}`}
                          style={{ cursor: 'pointer' }}
                          onMouseEnter={() => setHoveredZoneId(zone.id)}
                          onMouseLeave={() => setHoveredZoneId(null)}
                        >
                          {/* Zone background tile */}
                          <rect
                            x={zx}
                            y={zy}
                            width={zw}
                            height={zh}
                            fill={
                              isActiveBall
                                ? (isZone14 ? 'rgba(245, 158, 11, 0.38)' : isBox ? 'rgba(239, 68, 68, 0.35)' : 'rgba(16, 185, 129, 0.28)')
                                : isHovered
                                  ? 'rgba(255, 255, 255, 0.28)'
                                  : zone.color
                            }
                            stroke={
                              isActiveBall
                                ? '#fbbf24'
                                : isHovered
                                  ? '#ffffff'
                                  : zone.stroke
                            }
                            strokeWidth={isActiveBall ? 2.8 : isHovered ? 2.2 : 1.3}
                            strokeDasharray={isActiveBall ? undefined : '5,4'}
                            rx="4"
                          />

                          {/* Zone center badge */}
                          <g transform={`translate(${cx}, ${cy})`}>
                            <rect
                              x={isZone14 ? -52 : isBox ? -50 : -42}
                              y="-10"
                              width={isZone14 ? 104 : isBox ? 100 : 84}
                              height="20"
                              rx="5"
                              fill={
                                isActiveBall
                                  ? '#0f172a'
                                  : isZone14
                                    ? 'rgba(30, 41, 59, 0.88)'
                                    : isBox
                                      ? 'rgba(30, 41, 59, 0.85)'
                                      : 'rgba(15, 23, 42, 0.70)'
                              }
                              stroke={
                                isActiveBall
                                  ? '#fbbf24'
                                  : isZone14
                                    ? '#f59e0b'
                                    : isBox
                                      ? '#ef4444'
                                      : 'rgba(255, 255, 255, 0.45)'
                              }
                              strokeWidth={isActiveBall || isZone14 ? 1.5 : 0.8}
                            />
                            <text
                              x="0"
                              y="4"
                              textAnchor="middle"
                              fill={
                                isActiveBall
                                  ? '#fbbf24'
                                  : isZone14
                                    ? '#fef08a'
                                    : isBox
                                      ? '#fca5a5'
                                      : '#ffffff'
                              }
                              fontSize="9"
                              fontWeight={isZone14 || isActiveBall ? '900' : '700'}
                              fontFamily="'JetBrains Mono', monospace"
                            >
                              {zone.tag}
                            </text>
                          </g>
                        </g>
                      );
                    })}
                  </g>
                )}

                {/* 2. Five Continuous Lateral Channels */}
                {regionMode === 'channels' && (
                  <g id="tactical-channels">
                    {/* Channel horizontal dividers */}
                    <line x1={padX} y1={toSvgY(18)} x2={padX + pitchW} y2={toSvgY(18)} stroke="rgba(255,255,255,0.75)" strokeWidth="1.8" strokeDasharray="6,4" />
                    <line x1={padX} y1={toSvgY(36.5)} x2={padX + pitchW} y2={toSvgY(36.5)} stroke="rgba(255,255,255,0.75)" strokeWidth="1.8" strokeDasharray="6,4" />
                    <line x1={padX} y1={toSvgY(63.5)} x2={padX + pitchW} y2={toSvgY(63.5)} stroke="rgba(255,255,255,0.75)" strokeWidth="1.8" strokeDasharray="6,4" />
                    <line x1={padX} y1={toSvgY(82)} x2={padX + pitchW} y2={toSvgY(82)} stroke="rgba(255,255,255,0.75)" strokeWidth="1.8" strokeDasharray="6,4" />

                    {/* Channel translucent fills */}
                    <rect x={padX} y={padY} width={pitchW} height={(18 / 100) * pitchH} fill="rgba(148,163,184,0.08)" />
                    <text x={padX + 24} y={toSvgY(9) + 4} fill="#ffffff" fontSize="11" fontWeight="800" fontFamily="'JetBrains Mono', monospace" stroke="rgba(0,0,0,0.6)" strokeWidth="2.5" paintOrder="stroke">
                      CHANNEL 1 · LEFT FLANK / WING
                    </text>

                    <rect x={padX} y={toSvgY(18)} width={pitchW} height={(18.5 / 100) * pitchH} fill="rgba(6,182,212,0.12)" />
                    <text x={padX + 24} y={toSvgY(27) + 4} fill="#7dd3fc" fontSize="11" fontWeight="800" fontFamily="'JetBrains Mono', monospace" stroke="rgba(0,0,0,0.6)" strokeWidth="2.5" paintOrder="stroke">
                      CHANNEL 2 · LEFT HALF-SPACE (ARTETA OVERLOAD CORRIDOR)
                    </text>

                    <rect x={padX} y={toSvgY(36.5)} width={pitchW} height={(27 / 100) * pitchH} fill="rgba(16,185,129,0.10)" />
                    <text x={padX + 24} y={toSvgY(50) + 4} fill="#a7f3d0" fontSize="11" fontWeight="800" fontFamily="'JetBrains Mono', monospace" stroke="rgba(0,0,0,0.6)" strokeWidth="2.5" paintOrder="stroke">
                      CHANNEL 3 · CENTRAL SPINE (PIVOT AXIS & GOALMOUTH)
                    </text>

                    <rect x={padX} y={toSvgY(63.5)} width={pitchW} height={(18.5 / 100) * pitchH} fill="rgba(6,182,212,0.12)" />
                    <text x={padX + 24} y={toSvgY(73) + 4} fill="#7dd3fc" fontSize="11" fontWeight="800" fontFamily="'JetBrains Mono', monospace" stroke="rgba(0,0,0,0.6)" strokeWidth="2.5" paintOrder="stroke">
                      CHANNEL 4 · RIGHT HALF-SPACE (SAKA & DE BRUYNE CORRIDOR)
                    </text>

                    <rect x={padX} y={toSvgY(82)} width={pitchW} height={(18 / 100) * pitchH} fill="rgba(148,163,184,0.08)" />
                    <text x={padX + 24} y={toSvgY(91) + 4} fill="#ffffff" fontSize="11" fontWeight="800" fontFamily="'JetBrains Mono', monospace" stroke="rgba(0,0,0,0.6)" strokeWidth="2.5" paintOrder="stroke">
                      CHANNEL 5 · RIGHT FLANK / WING
                    </text>

                    {/* Golden Zone 14 Overlay */}
                    <rect
                      x={toSvgX(66.67)} y={toSvgY(36.5)}
                      width={(16.83 / 100) * pitchW} height={(27 / 100) * pitchH}
                      fill="rgba(245,158,11,0.24)" stroke="#f59e0b" strokeWidth="2.5" strokeDasharray="6,4" rx="6"
                    />
                    <text x={toSvgX(75)} y={toSvgY(50) + 4} textAnchor="middle" fill="#fef08a" fontSize="12" fontWeight="900" fontFamily="'JetBrains Mono', monospace" stroke="rgba(0,0,0,0.7)" strokeWidth="3" paintOrder="stroke">
                      ★ ZONE 14
                    </text>
                  </g>
                )}

                {/* 3. Three Pitch Thirds */}
                {regionMode === 'thirds' && (
                  <g id="tactical-thirds">
                    <line x1={toSvgX(33.33)} y1={padY} x2={toSvgX(33.33)} y2={padY + pitchH} stroke="rgba(255,255,255,0.85)" strokeWidth="2.2" strokeDasharray="8,5" />
                    <line x1={toSvgX(66.67)} y1={padY} x2={toSvgX(66.67)} y2={padY + pitchH} stroke="rgba(255,255,255,0.85)" strokeWidth="2.2" strokeDasharray="8,5" />

                    <rect x={padX} y={padY} width={(33.33 / 100) * pitchW} height={pitchH} fill="rgba(99,102,241,0.09)" />
                    <rect x={toSvgX(33.33)} y={padY} width={(33.34 / 100) * pitchW} height={pitchH} fill="rgba(16,185,129,0.09)" />
                    <rect x={toSvgX(66.67)} y={padY} width={(33.33 / 100) * pitchW} height={pitchH} fill="rgba(239,68,68,0.09)" />

                    <g transform={`translate(${toSvgX(16.67)}, ${padY + 24})`}>
                      <rect x="-95" y="-12" width="190" height="24" rx="6" fill="rgba(15,23,42,0.85)" stroke="#818cf8" strokeWidth="1.2" />
                      <text x="0" y="4" textAnchor="middle" fill="#c7d2fe" fontSize="10.5" fontWeight="900" fontFamily="'JetBrains Mono', monospace">
                        🛡️ DEFENSIVE THIRD (0-35m)
                      </text>
                    </g>

                    <g transform={`translate(${toSvgX(50)}, ${padY + 24})`}>
                      <rect x="-95" y="-12" width="190" height="24" rx="6" fill="rgba(15,23,42,0.85)" stroke="#34d399" strokeWidth="1.2" />
                      <text x="0" y="4" textAnchor="middle" fill="#a7f3d0" fontSize="10.5" fontWeight="900" fontFamily="'JetBrains Mono', monospace">
                        ⚔️ MIDDLE THIRD (35-70m)
                      </text>
                    </g>

                    <g transform={`translate(${toSvgX(83.33)}, ${padY + 24})`}>
                      <rect x="-95" y="-12" width="190" height="24" rx="6" fill="rgba(15,23,42,0.85)" stroke="#f87171" strokeWidth="1.2" />
                      <text x="0" y="4" textAnchor="middle" fill="#fca5a5" fontSize="10.5" fontWeight="900" fontFamily="'JetBrains Mono', monospace">
                        🎯 ATTACKING THIRD (70-105m)
                      </text>
                    </g>

                    {/* Zone 14 Overlay */}
                    <rect
                      x={toSvgX(66.67)} y={toSvgY(36.5)}
                      width={(16.83 / 100) * pitchW} height={(27 / 100) * pitchH}
                      fill="rgba(245,158,11,0.24)" stroke="#f59e0b" strokeWidth="2.5" strokeDasharray="6,4" rx="6"
                    />
                    <text x={toSvgX(75)} y={toSvgY(50) + 4} textAnchor="middle" fill="#fef08a" fontSize="12" fontWeight="900" fontFamily="'JetBrains Mono', monospace" stroke="rgba(0,0,0,0.7)" strokeWidth="3" paintOrder="stroke">
                      ★ ZONE 14
                    </text>
                  </g>
                )}
              </g>
            )}

            {tacticalStrategyObservation?.geometry
              .filter(shape => shape.kind === 'Lane' && shape.points.length === 2
                && shape.points.every(point => Number.isFinite(point.x) && point.x >= 0 && point.x <= 100
                  && Number.isFinite(point.y) && point.y >= 0 && point.y <= 100))
              .map(shape => {
                const [start, end] = shape.points;
                const x1 = toSvgX(start.x);
                const y1 = toSvgY(start.y);
                const x2 = toSvgX(end.x);
                const y2 = toSvgY(end.y);
                const angle = Math.atan2(y2 - y1, x2 - x1);
                const baseX = x2 - Math.cos(angle) * 20;
                const baseY = y2 - Math.sin(angle) * 20;
                const wing = 9;
                const left = `${baseX + Math.sin(angle) * wing},${baseY - Math.cos(angle) * wing}`;
                const right = `${baseX - Math.sin(angle) * wing},${baseY + Math.cos(angle) * wing}`;
                const strategyLabel = tacticalStrategyObservation.strategyType.split('_').join(' ');
                return <g key={shape.id} role="img"
                  aria-label={`Evidence-backed ${strategyLabel} lane from ${start.x}, ${start.y} to ${end.x}, ${end.y}; event evidence ${tacticalStrategyObservation.evidenceEventIds.join(', ')}`}>
                  <line x1={x1} y1={y1} x2={x2} y2={y2} stroke="#312e81" strokeWidth="11" strokeLinecap="round" opacity="0.55" />
                  <line x1={x1} y1={y1} x2={x2} y2={y2} stroke="#c4b5fd" strokeWidth="6" strokeDasharray="12 8" strokeLinecap="round" />
                  <circle cx={x1} cy={y1} r="11" fill="#ffffff" stroke="#312e81" strokeWidth="4" />
                  <path d={`M ${x2} ${y2} L ${left} L ${right} Z`} fill="#312e81" stroke="#ffffff" strokeWidth="2" />
                </g>;
              })}

            {/* ── Historical Faded Trajectories (Recent 15 Events - Hidden by default) ── */}
            {showTrails && events.slice(-15).map((evt) => {
              if (evt.pitchX == null || evt.pitchY == null || evt.eventId === activeEvent?.eventId) return null;
              const hasEnd = evt.endX != null && evt.endY != null;
              const sx = toSvgX(evt.pitchX);
              const sy = toSvgY(evt.pitchY);
              const ex = hasEnd ? toSvgX(evt.endX!) : sx;
              const ey = hasEnd ? toSvgY(evt.endY!) : sy;
              const isGoal = evt.eventType === 'Goal';

              return (
                <g key={`trail-${evt.eventId || evt.sequence}`}
                  style={{ cursor: 'pointer', opacity: 0.35 }}
                  onClick={() => onSelectEvent?.(evt)}
                >
                  {hasEnd && (
                    <line x1={sx} y1={sy} x2={ex} y2={ey}
                      stroke={isGoal ? '#ef4444' : '#ffffff'}
                      strokeWidth="1.8"
                      strokeDasharray="4,3"
                    />
                  )}
                  <circle cx={sx} cy={sy} r={isGoal ? 7 : 3.5}
                    fill={isGoal ? '#ef4444' : '#ffffff'}
                    stroke="#1f2937" strokeWidth="1"
                  />
                </g>
              );
            })}

            {/* ── GOAL BUILD-UP 10-PASS SEQUENCE WITH DIRECTION & OPACITY PROGRESSION ── */}
            {!compact && isGoalEvent && goalPassSequence.length > 0 && (
              <g id="goal-10-pass-buildup-layer">
                {goalPassSequence.map((gp) => {
                  // Arrowhead wings at destination
                  const arrowLen = gp.isAssist ? 16 : 13;
                  const arrowWid = gp.isAssist ? 6.5 : 5;
                  const tipX = gp.endX;
                  const tipY = gp.endY;
                  const leftX = gp.endX - arrowLen * Math.cos(gp.angle) + arrowWid * Math.sin(gp.angle);
                  const leftY = gp.endY - arrowLen * Math.sin(gp.angle) - arrowWid * Math.cos(gp.angle);
                  const rightX = gp.endX - arrowLen * Math.cos(gp.angle) - arrowWid * Math.sin(gp.angle);
                  const rightY = gp.endY - arrowLen * Math.sin(gp.angle) + arrowWid * Math.cos(gp.angle);
                  const passColor = gp.isAssist ? '#fbbf24' : (gp.event.eventType === 'SwitchOfPlay' ? '#c084fc' : '#38bdf8');
                  const strokeWidth = gp.isAssist ? 5 : (2.2 + gp.index * 0.25);

                  return (
                    <g key={`goal-pass-${gp.event.eventId || gp.index}`} opacity={gp.opacity}>
                      {/* Shadow line for pitch visibility */}
                      <line
                        x1={gp.startX} y1={gp.startY}
                        x2={gp.endX} y2={gp.endY}
                        stroke="rgba(0,0,0,0.35)"
                        strokeWidth={strokeWidth + 2.5}
                        strokeLinecap="round"
                      />

                      {/* Directional Pass Vector Line */}
                      <line
                        x1={gp.startX} y1={gp.startY}
                        x2={gp.endX} y2={gp.endY}
                        stroke={passColor}
                        strokeWidth={strokeWidth}
                        strokeDasharray={gp.isAssist ? '10,5' : '7,4'}
                        strokeLinecap="round"
                      >
                        <animate attributeName="stroke-dashoffset" values="30;0" dur={gp.isAssist ? '0.8s' : '1.3s'} repeatCount="indefinite" />
                      </line>

                      {/* Directional Arrowhead polygon pointing from start to end */}
                      <polygon
                        points={`${tipX},${tipY} ${leftX},${leftY} ${rightX},${rightY}`}
                        fill={passColor}
                        stroke="#ffffff"
                        strokeWidth="0.8"
                      />

                      {/* Pass Origin Badge (#1 faint 15% -> #10 assist 100%) */}
                      <g transform={`translate(${gp.startX}, ${gp.startY})`}>
                        <circle
                          r={gp.isAssist ? 13 : 10.5}
                          fill={gp.isAssist ? '#f59e0b' : '#0284c7'}
                          stroke="#ffffff"
                          strokeWidth="2"
                          filter="url(#player-glow-shadow)"
                        />
                        <text
                          y="3.5"
                          textAnchor="middle"
                          fill="#ffffff"
                          fontSize={gp.isAssist ? '9.5' : '8.5'}
                          fontWeight="900"
                          fontFamily="'JetBrains Mono', monospace"
                        >
                          #{gp.count}
                        </text>
                      </g>

                      {/* Midpoint Pill: Player Name & Distance with Opacity Progression */}
                      <g transform={`translate(${(gp.startX + gp.endX) / 2}, ${(gp.startY + gp.endY) / 2 - (gp.isAssist ? 16 : 10)})`}>
                        <rect
                          x="-48" y="-10" width="96" height="20" rx="10"
                          fill={gp.isAssist ? '#d97706' : '#0f172a'}
                          stroke={gp.isAssist ? '#fef08a' : '#38bdf8'}
                          strokeWidth={gp.isAssist ? 2 : 1}
                          filter="url(#player-glow-shadow)"
                        />
                        <text
                          x="0" y="3.5" textAnchor="middle"
                          fill="#ffffff"
                          fontSize="9"
                          fontWeight="800"
                          fontFamily="'Inter', sans-serif"
                        >
                          {gp.isAssist ? `★ ASSIST · ${gp.distM}m` : `${gp.event.playerName ? gp.event.playerName.split(' ').pop() : 'Pass'} · ${gp.distM}m`}
                        </text>
                      </g>
                    </g>
                  );
                })}

                {/* Goal Celebration Burst at Target */}
                {activeEvent && (
                  <g transform={`translate(${toSvgX(activeEvent.pitchX ?? 92)}, ${toSvgY(activeEvent.pitchY ?? 50)})`}>
                    <circle r="26" fill="none" stroke="#f59e0b" strokeWidth="3" opacity="0.8">
                      <animate attributeName="r" values="16;38;16" dur="1.8s" repeatCount="indefinite" />
                      <animate attributeName="opacity" values="0.9;0.1;0.9" dur="1.8s" repeatCount="indefinite" />
                    </circle>
                    <circle r="16" fill="#ef4444" stroke="#ffffff" strokeWidth="2.5" filter="url(#player-glow-shadow)" />
                    <text y="5" textAnchor="middle" fontSize="13">⚽</text>
                    <g transform="translate(0, -24)">
                      <rect x="-42" y="-11" width="84" height="22" rx="11" fill="#ef4444" stroke="#fef08a" strokeWidth="1.5" />
                      <text y="4" textAnchor="middle" fill="#ffffff" fontSize="10.5" fontWeight="900" fontFamily="'Inter', sans-serif">
                        GOAL!
                      </text>
                    </g>
                  </g>
                )}
              </g>
            )}

            {/* ── PASS PLAYED: FOR NON-GOAL EVENTS ── */}
            {!isGoalEvent && isPassEvent && activeEvent && (
              <g id="pass-played-layer">
                {/* 1. Passer highlight aura */}
                <circle
                  cx={passStartX} cy={passStartY}
                  r={PLAYER_RADIUS + 14}
                  fill="url(#passer-aura-grad)"
                >
                  <animate attributeName="r" values={`${PLAYER_RADIUS + 8};${PLAYER_RADIUS + 18};${PLAYER_RADIUS + 8}`} dur="1.8s" repeatCount="indefinite" />
                  <animate attributeName="opacity" values="0.8;0.3;0.8" dur="1.8s" repeatCount="indefinite" />
                </circle>
                <circle
                  cx={passStartX} cy={passStartY}
                  r={PLAYER_RADIUS + 7}
                  fill="none" stroke="#ea580c" strokeWidth="2.5" strokeDasharray="5,3"
                >
                  <animateTransform attributeName="transform" type="rotate" from={`0 ${passStartX} ${passStartY}`} to={`360 ${passStartX} ${passStartY}`} dur="5s" repeatCount="indefinite" />
                </circle>

                {/* 2. Target / Receiver highlight aura */}
                <circle
                  cx={passEndX} cy={passEndY}
                  r={PLAYER_RADIUS + 14}
                  fill="url(#target-aura-grad)"
                >
                  <animate attributeName="r" values={`${PLAYER_RADIUS + 8};${PLAYER_RADIUS + 18};${PLAYER_RADIUS + 8}`} dur="1.8s" repeatCount="indefinite" />
                  <animate attributeName="opacity" values="0.8;0.3;0.8" dur="1.8s" repeatCount="indefinite" />
                </circle>
                <circle
                  cx={passEndX} cy={passEndY}
                  r={PLAYER_RADIUS + 7}
                  fill="none" stroke="#10b981" strokeWidth="2.5" strokeDasharray="5,3"
                >
                  <animateTransform attributeName="transform" type="rotate" from={`360 ${passEndX} ${passEndY}`} to={`0 ${passEndX} ${passEndY}`} dur="5s" repeatCount="indefinite" />
                </circle>

                {/* 3. Shadow under Pass Line */}
                <line
                  x1={passStartX} y1={passStartY}
                  x2={passEndX} y2={passEndY}
                  stroke="rgba(0,0,0,0.3)"
                  strokeWidth="6"
                />

                {/* 4. Pass Line Vector from Passer Player to Receiver Player */}
                <line
                  x1={passStartX} y1={passStartY}
                  x2={passEndX} y2={passEndY}
                  stroke={activeEvent?.eventType === 'SwitchOfPlay' ? '#9333ea' : '#f97316'}
                  strokeWidth="4"
                  strokeDasharray="8,5"
                  markerEnd={activeEvent?.eventType === 'SwitchOfPlay' ? 'url(#arrow-switch-head)' : 'url(#arrow-pass-head)'}
                >
                  <animate attributeName="stroke-dashoffset" values="26;0" dur="0.9s" repeatCount="indefinite" />
                </line>

                {/* 5. Midpoint distance pill badge */}
                <g transform={`translate(${(passStartX + passEndX) / 2}, ${(passStartY + passEndY) / 2 - 14})`}>
                  <rect
                    x="-42" y="-12" width="84" height="24" rx="12"
                    fill="#ffffff" stroke="#ea580c" strokeWidth="2"
                    filter="url(#player-glow-shadow)"
                  />
                  <text
                    x="0" y="4" textAnchor="middle"
                    fill="#c2410c" fontSize="11" fontWeight="800"
                    fontFamily="'JetBrains Mono', monospace"
                  >
                    ⚡ {passDistanceM}m
                  </text>
                </g>

                {/* 6. Passer and Receiver Floating Badges */}
                <g transform={`translate(${passStartX}, ${passStartY - PLAYER_RADIUS - 12})`}>
                  <rect x="-34" y="-10" width="68" height="20" rx="10" fill="#ea580c" filter="url(#player-glow-shadow)" />
                  <text x="0" y="4" textAnchor="middle" fill="#ffffff" fontSize="9.5" fontWeight="900" fontFamily="'Inter', sans-serif">
                    PASSER
                  </text>
                </g>

                <g transform={`translate(${passEndX}, ${passEndY - PLAYER_RADIUS - 12})`}>
                  <rect x="-34" y="-10" width="68" height="20" rx="10" fill="#059669" filter="url(#player-glow-shadow)" />
                  <text x="0" y="4" textAnchor="middle" fill="#ffffff" fontSize="9.5" fontWeight="900" fontFamily="'Inter', sans-serif">
                    TARGET
                  </text>
                </g>
              </g>
            )}

            {/* ── ALL 22 STARTING PLAYERS ON THE PITCH (NO BENCHED SUBSTITUTES) ─────── */}
            {onPitchPlayers.map((pp: PlayerPosition, idx) => {
              const cx = toSvgX(pp.x);
              const cy = toSvgY(pp.y);
              const isHome = pp.teamId === HOME_TEAM_ID;
              const primaryColor = teamColor(pp.teamId);
              const isGK = pp.position === 'GK';
              const hasBall = pp.playerName.toLowerCase() === ballCarrierName?.toLowerCase();
              const isHovered = hoveredPlayer?.player.playerName === pp.playerName;
              const r = isGK ? PLAYER_RADIUS * 1.05 : PLAYER_RADIUS;

              return (
                <g
                  key={`player-${pp.playerName}-${idx}`}
                  className="pitch-player-marker"
                  transform={`translate(${cx}, ${cy})`}
                  role="img"
                  tabIndex={0}
                  aria-label={`${pp.playerName}, ${pp.teamName}, ${pp.position}, at ${pp.x.toFixed(1)}, ${pp.y.toFixed(1)}`}
                  aria-describedby={isHovered ? 'pitch-player-tooltip' : undefined}
                  style={{
                    cursor: 'default',
                    transition: 'transform 0.45s cubic-bezier(0.2, 0.8, 0.2, 1)',
                  }}
                  onMouseEnter={() => setHoveredPlayer({ player: pp, svgX: cx, svgY: cy })}
                  onMouseLeave={(event) => {
                    if (document.activeElement !== event.currentTarget) setHoveredPlayer(null);
                  }}
                  onFocus={() => setHoveredPlayer({ player: pp, svgX: cx, svgY: cy })}
                  onBlur={() => setHoveredPlayer(null)}
                >
                  {/* Ball Carrier Glow Ring */}
                  {hasBall && (
                    <circle cx={0} cy={0} r={r + 10}
                      fill="none"
                      stroke="#f59e0b"
                      strokeWidth="3.5"
                      strokeDasharray="6,4"
                    >
                      <animateTransform
                        attributeName="transform"
                        type="rotate"
                        from="0 0 0"
                        to="360 0 0"
                        dur="3.5s"
                        repeatCount="indefinite"
                      />
                    </circle>
                  )}

                  {/* Hover ring */}
                  {isHovered && (
                    <circle cx={0} cy={0} r={r + 7}
                      className="pitch-player-focus-ring"
                      fill="none" stroke="#ffffff" strokeWidth="3" opacity="0.95" />
                  )}

                  {/* Main Player Circle */}
                  <circle
                    cx={0} cy={0} r={r}
                    fill={primaryColor}
                    stroke="#ffffff"
                    strokeWidth={hasBall ? 3.5 : (isHovered ? 3.5 : 2.5)}
                    filter="url(#player-glow-shadow)"
                  />

                  {/* GK distinct inner rim */}
                  {isGK && (
                    <circle
                      cx={0} cy={0} r={r - 3}
                      fill="none" stroke={isHome ? '#fef08a' : '#a7f3d0'} strokeWidth="2.2"
                    />
                  )}

                  {/* Large, high-contrast Jersey Number */}
                  <text
                    x={0} y={0}
                    textAnchor="middle"
                    dominantBaseline="central"
                    fill="#ffffff"
                    fontSize={pp.jerseyNumber > 9 ? "12.5" : "14"}
                    fontWeight="900"
                    fontFamily="'Inter', -apple-system, sans-serif"
                    style={{ pointerEvents: 'none', userSelect: 'none' }}
                  >
                    {pp.jerseyNumber}
                  </text>

                  {!compact && <g style={{ pointerEvents: 'none', userSelect: 'none' }}>
                    <rect
                      x={-36}
                      y={pp.y > 84 ? -r - 18 : r + 4}
                      width="72"
                      height="15"
                      rx="4"
                      fill="rgba(255,255,255,0.94)"
                      stroke="#e2e8f0"
                      strokeWidth="1"
                    />
                    <text
                      x={0}
                      y={pp.y > 84 ? -r - 7 : r + 15}
                      textAnchor="middle"
                      fill="#0f172a"
                      fontSize="9"
                      fontWeight="800"
                      fontFamily="'Inter', -apple-system, sans-serif"
                    >
                      {pp.playerName.split(' ').pop()} · {pp.position}
                    </text>
                  </g>}
                </g>
              );
            })}

            {/* ── Football Ball with Animated Gliding Motion ────────────── */}
            {activeEvent?.ballX !== undefined && activeEvent?.ballY !== undefined && (
              <g
                transform={`translate(${toSvgX(activeEvent.ballX)}, ${toSvgY(activeEvent.ballY)})`}
                style={{
                  transition: 'transform 0.35s cubic-bezier(0.2, 0.8, 0.2, 1)',
                }}
                filter="url(#ball-glow-filter)"
              >
                <circle
                  cx={0}
                  cy={0}
                  r="10"
                  fill="#ffffff"
                  stroke="#0f172a"
                  strokeWidth="2"
                />
                <circle
                  cx={0}
                  cy={0}
                  r="4.5"
                  fill="#0f172a"
                />
              </g>
            )}

            {/* ── Goal celebration badge ──────────────────────────── */}
            {activeEvent?.eventType === 'Goal' && activeEvent?.pitchX !== undefined && (
              <g transform={`translate(${toSvgX(activeEvent.pitchX)}, ${toSvgY(activeEvent.pitchY ?? 50) - 34})`}>
                <rect x="-80" y="-18" width="160" height="36" rx="18" fill="#ef4444" stroke="#ffffff" strokeWidth="3" filter="url(#player-glow-shadow)" />
                <text x="0" y="6" textAnchor="middle" fill="#ffffff" fontSize="18" fontWeight="900" fontFamily="'Inter', sans-serif">
                  ⚽ GOAL!
                </text>
              </g>
            )}

            {/* ── Event Sequence Badge (Top Right) ────────────────── */}
            {activeEvent && (
              <g transform={`translate(${padX + pitchW - 260}, ${padY + 12})`}>
                <rect x="0" y="0" width="250" height="52" rx="10"
                  fill="rgba(255,255,255,0.96)" stroke="#cbd5e1" strokeWidth="1.5" filter="url(#player-glow-shadow)" />
                <text x="14" y="22" fill="#0f172a" fontSize="13" fontWeight="900" fontFamily="'Inter', sans-serif">
                  #{activeEvent.sequence} · {activeEvent.eventType}
                </text>
                <text x="14" y="40" fill="#475569" fontSize="11" fontWeight="600" fontFamily="'JetBrains Mono', monospace">
                  {Math.floor(activeEvent.clockSeconds / 60).toString().padStart(2, '0')}:{(activeEvent.clockSeconds % 60).toString().padStart(2, '0')} · {activeEvent.playerName ?? 'Player not recorded'} ({activeEvent.teamName ?? 'Team not recorded'})
                </text>
              </g>
            )}
          </svg>
        </div>
      </div>

      {/* ── PASS INFORMATION FOOTER BANNER ───────────────────────── */}
      {isPassEvent && activeEvent && (
        <div style={{
          background: 'linear-gradient(90deg, #fff7ed 0%, #ffffff 100%)',
          borderTop: '2px solid #f97316',
          padding: '12px 20px',
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          flexWrap: 'wrap',
          gap: '12px',
        }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '14px' }}>
            <span style={{
              background: '#ea580c', color: '#ffffff',
              padding: '4px 10px', borderRadius: '6px',
              fontSize: '11px', fontWeight: '800', textTransform: 'uppercase', letterSpacing: '0.05em'
            }}>
              {activeEvent.eventType}
            </span>
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: '13px', fontWeight: '700', color: '#0f172a' }}>
              <span style={{ color: '#ea580c' }}>
                {passerPlayer ? `${passerPlayer.playerName} (#${passerPlayer.jerseyNumber})` : activeEvent.playerName ?? 'Player not recorded'}
              </span>
              <span style={{ color: '#94a3b8' }}>➔</span>
              <span style={{ color: '#059669' }}>
                {receiverPlayer ? `${receiverPlayer.playerName} (#${receiverPlayer.jerseyNumber})` : activeEvent.receiverName ?? 'Receiver not recorded'}
              </span>
            </div>
          </div>

          <div style={{ display: 'flex', alignItems: 'center', gap: '16px', fontSize: '12px', color: '#475569' }}>
            <div>
              <span style={{ color: '#94a3b8' }}>Distance: </span>
              <strong style={{ color: '#0f172a', fontFamily: 'JetBrains Mono, monospace' }}>{passDistanceM} meters</strong>
            </div>
            {activeEvent.pitchX != null && (
              <div>
                <span style={{ color: '#94a3b8' }}>Start: </span>
                <code style={{ color: '#ea580c' }}>({activeEvent.pitchX.toFixed(1)}, {activeEvent.pitchY != null ? activeEvent.pitchY.toFixed(1) : '-'})</code>
              </div>
            )}
            {activeEvent.endX != null && (
              <div>
                <span style={{ color: '#94a3b8' }}>End: </span>
                <code style={{ color: '#059669' }}>({activeEvent.endX.toFixed(1)}, {activeEvent.endY != null ? activeEvent.endY.toFixed(1) : '-'})</code>
              </div>
            )}
            <span className="badge badge-emerald">{activeEvent.outcome ?? 'Outcome not recorded'}</span>
          </div>
        </div>
      )}

      {/* ── Observed player snapshot details ──── */}
      {hoveredPlayer && (
        <div
          id="pitch-player-tooltip"
          role="tooltip"
          style={{
            position: 'absolute',
            bottom: '24px',
            ...(hoveredPlayer.player.x < 50 ? { right: '24px' } : { left: '24px' }),
            background: 'rgba(255, 255, 255, 0.98)',
            border: `2.5px solid ${teamColor(hoveredPlayer.player.teamId)}`,
            borderRadius: '16px',
            padding: '16px 20px',
            boxShadow: '0 16px 48px rgba(0,0,0,0.18)',
            fontSize: '12px',
            zIndex: 30,
            pointerEvents: 'none',
            minWidth: '290px',
            backdropFilter: 'blur(12px)',
            transition: 'left 0.2s ease, right 0.2s ease',
          }}
        >
          {/* Card Header: Jersey, Name, Position */}
          <div style={{ display: 'flex', alignItems: 'center', gap: '12px', marginBottom: '14px' }}>
            <div style={{
              width: '42px', height: '42px', borderRadius: '50%',
              background: teamColor(hoveredPlayer.player.teamId),
              display: 'flex', alignItems: 'center', justifyContent: 'center',
              color: '#ffffff', fontWeight: '900', fontSize: '18px',
              border: `2.5px solid ${teamColor(hoveredPlayer.player.teamId, true)}`,
              boxShadow: '0 2px 8px rgba(0,0,0,0.15)',
              flexShrink: 0,
            }}>
              {hoveredPlayer.player.jerseyNumber}
            </div>
            <div>
              <div style={{ fontWeight: '900', color: '#0f172a', fontSize: '15px' }}>
                {hoveredPlayer.player.playerName}
              </div>
              <div style={{ color: '#64748b', fontSize: '12px', fontWeight: '600' }}>
                {hoveredPlayer.player.teamName} · <strong style={{ color: teamColor(hoveredPlayer.player.teamId) }}>{hoveredPlayer.player.position}</strong>
              </div>
            </div>
          </div>

          {/* Only metrics supplied by the position snapshot are displayed. */}
          <div style={{
            display: 'grid', gridTemplateColumns: 'repeat(2, 1fr)', gap: '8px'
          }}>
            {observedPositionMetrics.map(metric => (
              <div key={metric.label} style={{ background: '#f8fafc', border: '1px solid #e2e8f0', borderRadius: '10px', padding: '8px 12px' }}>
                <div style={{ color: '#64748b', fontSize: '10px', fontWeight: '800', textTransform: 'uppercase' }}>{metric.label}</div>
                <div style={{ color: '#0f172a', fontWeight: '900', fontSize: '16px', fontFamily: 'JetBrains Mono, monospace' }}>{metric.value}</div>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
};
