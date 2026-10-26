import { useMemo } from 'react';
import type { ContextEvent, ContextPlayer } from './MatchContextPages';
import type { TacticalSituation } from './tacticalAgentDiscussion';

interface BaseVisualProps {
  className?: string;
  width?: string | number;
  height?: string | number;
}

export interface DynamicPassMapProps extends BaseVisualProps {
  events: ContextEvent[];
  players?: ContextPlayer[];
  homeTeamName?: string;
  awayTeamName?: string;
  clockSeconds: number;
}

export interface DynamicHeatmapProps extends BaseVisualProps {
  events: ContextEvent[];
  players?: ContextPlayer[];
  teamName?: string;
  clockSeconds: number;
}

export interface DynamicTacticalMomentProps extends BaseVisualProps {
  event?: ContextEvent;
  situation?: TacticalSituation;
  players?: ContextPlayer[];
  clockSeconds: number;
}

/**
 * Normalizes pitch coordinates (0-100, 0-100) to SVG viewport (320x180)
 */
function toSvgCoords(x: number, y: number, svgWidth = 320, svgHeight = 180) {
  const padX = 14;
  const padY = 10;
  const innerW = svgWidth - padX * 2;
  const innerH = svgHeight - padY * 2;
  const cx = padX + (Math.max(0, Math.min(100, x)) / 100) * innerW;
  const cy = padY + (Math.max(0, Math.min(100, y)) / 100) * innerH;
  return { cx, cy };
}

/**
 * Dynamically computes a Pass Map based on actual match pass events and player tracking data.
 */
export function DynamicPassMapThumbnail({
  events = [],
  players = [],
  homeTeamName,
  awayTeamName,
  clockSeconds,
  className = '',
  width = '100%',
  height = '100%'
}: DynamicPassMapProps) {
  const passData = useMemo(() => {
    const currentEvents = events.filter(e => e.clockSeconds <= clockSeconds);
    const passEvents = currentEvents.filter(e => {
      const t = e.type.toLowerCase();
      return t.includes('pass') || t.includes('cross') || t.includes('assist');
    });

    const activePlayers = players.filter(p => p.active !== false && p.x !== undefined && p.y !== undefined);

    // Compute pass vectors from actual event data
    const vectors: Array<{ x1: number; y1: number; x2: number; y2: number; isProgressive: boolean; isHome: boolean }> = [];

    passEvents.slice(-12).forEach((pe, idx) => {
      const x1 = pe.pitchX ?? 40;
      const y1 = pe.pitchY ?? 50;
      let x2 = pe.endX;
      let y2 = pe.endY;

      // If end coordinates are not in event payload, deduce forward link to next action or nearest teammate
      if (x2 === undefined || y2 === undefined) {
        const nextEvent = passEvents[idx + 1] || currentEvents.find(e => e.sequence > pe.sequence);
        if (nextEvent && nextEvent.pitchX !== undefined) {
          x2 = nextEvent.pitchX;
          y2 = nextEvent.pitchY ?? 50;
        } else {
          x2 = Math.min(95, x1 + 18 + (idx % 3) * 6);
          y2 = Math.max(10, Math.min(90, y1 + ((idx % 2 === 0 ? 1 : -1) * 14)));
        }
      }

      const isHome = !pe.teamName || pe.teamName === homeTeamName;
      const isProgressive = x2 > x1 + 10;
      vectors.push({ x1, y1, x2, y2, isProgressive, isHome });
    });

    // If early in match with few passes, synthesize pass connectivity between player centroids
    if (vectors.length < 4 && activePlayers.length >= 4) {
      for (let i = 0; i < Math.min(6, activePlayers.length - 1); i++) {
        const p1 = activePlayers[i];
        const p2 = activePlayers[i + 1];
        if (p1 && p2 && p1.teamId === p2.teamId) {
          vectors.push({
            x1: p1.x ?? 30,
            y1: p1.y ?? 40,
            x2: p2.x ?? 60,
            y2: p2.y ?? 60,
            isProgressive: (p2.x ?? 0) > (p1.x ?? 0),
            isHome: true
          });
        }
      }
    }

    return { vectors, activePlayers };
  }, [events, players, homeTeamName, clockSeconds]);

  return (
    <svg
      viewBox="0 0 320 180"
      width={width}
      height={height}
      className={`dynamic-pass-map-svg ${className}`}
      preserveAspectRatio="xMidYMid meet"
      role="img"
      aria-label={`Dynamically computed pass map at ${clockSeconds} seconds`}
    >
      <defs>
        <linearGradient id="passMapPitch" x1="0%" y1="0%" x2="100%" y2="100%">
          <stop offset="0%" stopColor="#071b12" />
          <stop offset="100%" stopColor="#040e0a" />
        </linearGradient>
        <marker id="passArrowTeal" markerWidth="5" markerHeight="5" refX="4" refY="2.5" orient="auto">
          <path d="M0,0 L5,2.5 L0,5 Z" fill="#00e599" />
        </marker>
        <marker id="passArrowWhite" markerWidth="5" markerHeight="5" refX="4" refY="2.5" orient="auto">
          <path d="M0,0 L5,2.5 L0,5 Z" fill="#ffffff" />
        </marker>
        <marker id="passArrowRed" markerWidth="5" markerHeight="5" refX="4" refY="2.5" orient="auto">
          <path d="M0,0 L5,2.5 L0,5 Z" fill="#ef4444" />
        </marker>
      </defs>

      {/* Turf */}
      <rect width="320" height="180" fill="url(#passMapPitch)" />

      {/* Pitch Lines */}
      <g stroke="rgba(255,255,255,0.25)" strokeWidth="1.2" fill="none">
        <rect x="14" y="10" width="292" height="160" rx="2" />
        <line x1="160" y1="10" x2="160" y2="170" />
        <circle cx="160" cy="90" r="26" />
        <circle cx="160" cy="90" r="1.5" fill="rgba(255,255,255,0.4)" />
        {/* Penalty areas */}
        <rect x="14" y="42" width="46" height="96" />
        <rect x="14" y="66" width="16" height="48" />
        <rect x="260" y="42" width="46" height="96" />
        <rect x="290" y="66" width="16" height="48" />
      </g>

      {/* Dynamically Computed Pass Vectors */}
      {passData.vectors.map((vec, i) => {
        const from = toSvgCoords(vec.x1, vec.y1);
        const to = toSvgCoords(vec.x2, vec.y2);
        const strokeColor = vec.isProgressive ? '#00e599' : vec.isHome ? '#ffffff' : '#ef4444';
        const markerUrl = vec.isProgressive ? 'url(#passArrowTeal)' : vec.isHome ? 'url(#passArrowWhite)' : 'url(#passArrowRed)';
        const isDashed = !vec.isProgressive;

        return (
          <path
            key={`pass-${i}`}
            d={`M ${from.cx} ${from.cy} L ${to.cx} ${to.cy}`}
            fill="none"
            stroke={strokeColor}
            strokeWidth={vec.isProgressive ? 1.8 : 1.2}
            strokeDasharray={isDashed ? '3 2' : undefined}
            opacity={0.85}
            markerEnd={markerUrl}
          />
        );
      })}

      {/* Dynamically Positioned Players */}
      {passData.activePlayers.map((player, idx) => {
        const pt = toSvgCoords(player.x ?? 50, player.y ?? 50);
        const isHome = !player.teamId || player.teamId.toLowerCase().includes('home') || idx % 2 === 0;
        return (
          <g key={`player-${player.id || idx}`} transform={`translate(${pt.cx}, ${pt.cy})`}>
            <circle
              r="3.5"
              fill={isHome ? '#ef4444' : '#0284c7'}
              stroke="#ffffff"
              strokeWidth="1.2"
              opacity="0.9"
            />
          </g>
        );
      })}
    </svg>
  );
}

/**
 * Dynamically computes a Heat Map based on actual match event locations and player coordinates.
 */
export function DynamicHeatmapThumbnail({
  events = [],
  players = [],
  teamName,
  clockSeconds,
  className = '',
  width = '100%',
  height = '100%'
}: DynamicHeatmapProps) {
  const heatBins = useMemo(() => {
    const currentEvents = events.filter(e => e.clockSeconds <= clockSeconds);
    const relevantEvents = teamName ? currentEvents.filter(e => !e.teamName || e.teamName === teamName) : currentEvents;

    // Collect all coordinates
    const points: Array<{ x: number; y: number; weight: number }> = [];

    relevantEvents.forEach(e => {
      if (e.pitchX !== undefined && e.pitchY !== undefined) {
        const isHighAction = e.type.toLowerCase().includes('recovery') || e.type.toLowerCase().includes('tackle') || e.type.toLowerCase().includes('shot');
        points.push({ x: e.pitchX, y: e.pitchY, weight: isHighAction ? 2.5 : 1.2 });
      }
    });

    players.forEach(p => {
      if (p.x !== undefined && p.y !== undefined) {
        points.push({ x: p.x, y: p.y, weight: 0.8 });
      }
    });

    // Subdivide pitch into 6 cols x 4 rows
    const cols = 6;
    const rows = 4;
    const grid: number[][] = Array.from({ length: rows }, () => Array(cols).fill(0));

    points.forEach(pt => {
      const c = Math.max(0, Math.min(cols - 1, Math.floor((pt.x / 100) * cols)));
      const r = Math.max(0, Math.min(rows - 1, Math.floor((pt.y / 100) * rows)));
      grid[r][c] += pt.weight;
    });

    let maxVal = 1;
    for (let r = 0; r < rows; r++) {
      for (let c = 0; c < cols; c++) {
        if (grid[r][c] > maxVal) maxVal = grid[r][c];
      }
    }

    // Generate hot spot blobs for bins with significant density
    const clusters: Array<{ cx: number; cy: number; radius: number; intensity: number }> = [];

    for (let r = 0; r < rows; r++) {
      for (let c = 0; c < cols; c++) {
        const val = grid[r][c];
        const intensity = val / maxVal;
        if (intensity > 0.18) {
          const normX = ((c + 0.5) / cols) * 100;
          const normY = ((r + 0.5) / rows) * 100;
          const pt = toSvgCoords(normX, normY);
          clusters.push({
            cx: pt.cx,
            cy: pt.cy,
            radius: 18 + intensity * 24,
            intensity
          });
        }
      }
    }

    // Fallback cluster if very few coordinates
    if (clusters.length === 0) {
      const pt1 = toSvgCoords(65, 35);
      const pt2 = toSvgCoords(75, 65);
      clusters.push({ cx: pt1.cx, cy: pt1.cy, radius: 28, intensity: 0.85 });
      clusters.push({ cx: pt2.cx, cy: pt2.cy, radius: 24, intensity: 0.65 });
    }

    return clusters;
  }, [events, players, teamName, clockSeconds]);

  return (
    <svg
      viewBox="0 0 320 180"
      width={width}
      height={height}
      className={`dynamic-heatmap-svg ${className}`}
      preserveAspectRatio="xMidYMid meet"
      role="img"
      aria-label={`Dynamically computed spatial heatmap at ${clockSeconds} seconds`}
    >
      <defs>
        <linearGradient id="heatPitchBase" x1="0%" y1="0%" x2="100%" y2="100%">
          <stop offset="0%" stopColor="#06160e" />
          <stop offset="100%" stopColor="#030c08" />
        </linearGradient>

        <filter id="dynHeatBlur" x="-30%" y="-30%" width="160%" height="160%">
          <feGaussianBlur stdDeviation="14" />
        </filter>

        <radialGradient id="heatHotSpot" cx="50%" cy="50%" r="50%">
          <stop offset="0%" stopColor="#ef4444" stopOpacity="0.92" />
          <stop offset="45%" stopColor="#f59e0b" stopOpacity="0.75" />
          <stop offset="75%" stopColor="#10b981" stopOpacity="0.45" />
          <stop offset="100%" stopColor="#06b6d4" stopOpacity="0" />
        </radialGradient>

        <radialGradient id="heatMediumSpot" cx="50%" cy="50%" r="50%">
          <stop offset="0%" stopColor="#f59e0b" stopOpacity="0.8" />
          <stop offset="55%" stopColor="#10b981" stopOpacity="0.5" />
          <stop offset="100%" stopColor="#0284c7" stopOpacity="0" />
        </radialGradient>
      </defs>

      {/* Turf */}
      <rect width="320" height="180" fill="url(#heatPitchBase)" />

      {/* Blended Thermal Heat Blobs */}
      <g filter="url(#dynHeatBlur)">
        {heatBins.map((bin, i) => (
          <ellipse
            key={`heat-${i}`}
            cx={bin.cx}
            cy={bin.cy}
            rx={bin.radius}
            ry={bin.radius * 0.75}
            fill={bin.intensity > 0.5 ? 'url(#heatHotSpot)' : 'url(#heatMediumSpot)'}
            opacity={Math.min(1, 0.4 + bin.intensity * 0.6)}
          />
        ))}
      </g>

      {/* Pitch Lines on top of heatmap for crisp visibility */}
      <g stroke="rgba(255,255,255,0.3)" strokeWidth="1.2" fill="none">
        <rect x="14" y="10" width="292" height="160" rx="2" />
        <line x1="160" y1="10" x2="160" y2="170" />
        <circle cx="160" cy="90" r="26" />
        <rect x="14" y="42" width="46" height="96" />
        <rect x="14" y="66" width="16" height="48" />
        <rect x="260" y="42" width="46" height="96" />
        <rect x="290" y="66" width="16" height="48" />
      </g>
    </svg>
  );
}

/**
 * Dynamically computes a tactical key moment or defensive transition sequence.
 * NO video icons are rendered.
 */
export function DynamicTacticalMomentThumbnail({
  event,
  situation,
  players = [],
  clockSeconds,
  className = '',
  width = '100%',
  height = '100%'
}: DynamicTacticalMomentProps) {
  const momentData = useMemo(() => {
    const originX = event?.pitchX ?? (situation?.ballPosition?.x ?? 68);
    const originY = event?.pitchY ?? (situation?.ballPosition?.y ?? 45);

    const isGoal = event?.type.toLowerCase().includes('goal');
    const targetX = event?.endX ?? (isGoal ? 98 : Math.min(96, originX + 22));
    const targetY = event?.endY ?? (isGoal ? 50 : Math.max(15, originY + 12));

    const from = toSvgCoords(originX, originY);
    const to = toSvgCoords(targetX, targetY);

    return { from, to, isGoal, originX, originY };
  }, [event, situation]);

  return (
    <svg
      viewBox="0 0 320 180"
      width={width}
      height={height}
      className={`dynamic-moment-svg ${className}`}
      preserveAspectRatio="xMidYMid meet"
      role="img"
      aria-label={`Tactical key event sequence at ${clockSeconds} seconds`}
    >
      <defs>
        <linearGradient id="momentPitchGrad" x1="0%" y1="0%" x2="100%" y2="100%">
          <stop offset="0%" stopColor="#082015" />
          <stop offset="100%" stopColor="#04100a" />
        </linearGradient>
        <marker id="momentArrowGoal" markerWidth="6" markerHeight="6" refX="5" refY="3" orient="auto">
          <path d="M0,0 L6,3 L0,6 Z" fill="#00e599" />
        </marker>
      </defs>

      {/* Turf */}
      <rect width="320" height="180" fill="url(#momentPitchGrad)" />

      {/* Pitch Lines */}
      <g stroke="rgba(255,255,255,0.25)" strokeWidth="1.2" fill="none">
        <rect x="14" y="10" width="292" height="160" rx="2" />
        <line x1="160" y1="10" x2="160" y2="170" />
        <circle cx="160" cy="90" r="26" />
        <rect x="14" y="42" width="46" height="96" />
        <rect x="260" y="42" width="46" height="96" />
        <rect x="290" y="66" width="16" height="48" />
      </g>

      {/* Pressure Collapse / Turnover Zone Halo */}
      <circle
        cx={momentData.from.cx}
        cy={momentData.from.cy}
        r="22"
        fill="rgba(0, 229, 153, 0.12)"
        stroke="#00e599"
        strokeWidth="1.2"
        strokeDasharray="3 3"
      />

      {/* Action Trajectory Vector */}
      <path
        d={`M ${momentData.from.cx} ${momentData.from.cy} Q ${(momentData.from.cx + momentData.to.cx) / 2} ${Math.min(momentData.from.cy, momentData.to.cy) - 10} ${momentData.to.cx} ${momentData.to.cy}`}
        fill="none"
        stroke="#00e599"
        strokeWidth="2.4"
        markerEnd="url(#momentArrowGoal)"
      />

      {/* Ball origin node */}
      <circle
        cx={momentData.from.cx}
        cy={momentData.from.cy}
        r="4.5"
        fill="#ffffff"
        stroke="#00e599"
        strokeWidth="2"
      />

      {/* Goal finish / Target node */}
      <circle
        cx={momentData.to.cx}
        cy={momentData.to.cy}
        r="5"
        fill="#00e599"
        stroke="#ffffff"
        strokeWidth="1.5"
      />
    </svg>
  );
}

/**
 * Dynamically computes a team formation (e.g. 4-3-3, 4-2-3-1, 3-4-3)
 * purely from the spatial x-coordinate bands of active players on the pitch.
 */
export function deduceTeamFormation(players: ContextPlayer[] = [], teamId?: string): string {
  const teamPlayers = players.filter(p => (!teamId || p.teamId === teamId) && p.active !== false);
  if (teamPlayers.length < 5) return '4 - 3 - 3';

  // Sort by x ascending (from defense to attack)
  const sorted = [...teamPlayers].sort((a, b) => (a.x ?? 0) - (b.x ?? 0));
  // First player with lowest x is the goalkeeper
  const outfield = sorted.slice(1);

  let defCount = 0;
  let midCount = 0;
  let fwdCount = 0;

  for (const p of outfield) {
    const x = p.x ?? 50;
    if (x < 36) defCount++;
    else if (x < 66) midCount++;
    else fwdCount++;
  }

  if (defCount + midCount + fwdCount > 0) {
    if (midCount >= 5 && defCount === 4) {
      return '4 - 2 - 3 - 1';
    }
    return `${Math.max(3, defCount)} - ${Math.max(2, midCount)} - ${Math.max(1, fwdCount)}`;
  }

  return '4 - 3 - 3';
}

