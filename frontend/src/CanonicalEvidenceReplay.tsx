import { useState } from 'react';
import { clock } from './api';
import type { CanonicalEvidence, CanonicalEvidencePayload, TelemetryTrackingFrameV1 } from './api';

type Point = { x: number; y: number };
type ProjectedFrame = {
  item: CanonicalEvidencePayload;
  frame: TelemetryTrackingFrameV1;
  length: number;
  width: number;
  ball: Point;
  players: Array<{ playerId: string; teamId: string; position: Point }>;
  teamIds: string[];
  teamDirections: Map<string, 'left' | 'right'>;
  participants: Set<string>;
};

/** Replays only source-bound observed frames linked to the selected canonical insight. */
export function CanonicalEvidenceReplay({ evidence }: { evidence: CanonicalEvidence }) {
  const { insight } = evidence;
  const replayKey = `${insight.matchId}:${insight.insightId}:${insight.canonicalRevision}:${insight.projectionRevision}:${insight.selectedTimeSeconds}:${insight.throughSequence}:${insight.sourceSha256}`;
  const [selection, setSelection] = useState({ key: replayKey, index: 0 });
  const eventParticipants = evidence.items.flatMap(item => {
    const ids = parseEventParticipants(item, evidence);
    return ids ? [{ sequence: item.sequence, ids }] : [];
  });
  const frames = evidence.items.flatMap(item => {
    const parsed = parseFrame(item, evidence);
    if (!parsed) return [];
    const participants = new Set(eventParticipants
      .filter(event => event.sequence <= item.sequence)
      .flatMap(event => event.ids)
      .filter(id => parsed.players.some(player => player.playerId === id)));
    return [{ ...parsed, participants }];
  }).sort((left, right) => left.item.sequence - right.item.sequence);
  const selectedIndex = selection.key === replayKey ? Math.min(selection.index, Math.max(frames.length - 1, 0)) : 0;
  const selected = frames[selectedIndex];
  const headingId = `evidence-replay-${insight.insightId}`;
  const frameDescriptionId = `evidence-frame-description-${insight.insightId}`;
  const invalidRights = evidence.items.some(item => item.kind === 'frame' && frameRightsReviewPending(item));

  return <section className="canonical-evidence-replay" aria-labelledby={headingId}>
    <h3 id={headingId}>Observed sequence replay</h3>
    <p>Step through only the observed tracking frames linked to this insight. Positions are not interpolated, and the source pitch orientation is preserved.</p>
    {!selected ? <p role="status">{invalidRights
      ? 'Tracking replay is unavailable until the source rights review is complete.'
      : evidence.items.some(item => item.kind === 'frame')
        ? 'Linked tracking data does not match the selected evidence or has unsupported coordinates. The overlay is hidden.'
        : 'No observed tracking frame is linked to this insight. A tactical overlay is unavailable.'}</p> : <>
      <div className="evidence-replay-controls">
        <button type="button" onClick={() => setSelection({ key: replayKey, index: Math.max(0, selectedIndex - 1) })} disabled={selectedIndex === 0}>
          Previous observed frame
        </button>
        <output aria-live="polite" aria-atomic="true">Frame {selectedIndex + 1} of {frames.length} · {clock(selected.item.clockSeconds)}</output>
        <button type="button" onClick={() => setSelection({ key: replayKey, index: Math.min(frames.length - 1, selectedIndex + 1) })}
          disabled={selectedIndex === frames.length - 1}>
          Next observed frame
        </button>
      </div>
      <p className="pitch-caption" id={frameDescriptionId}>
        Observed frame {selected.frame.frameId} · {clock(selected.item.clockSeconds)} · period {selected.item.period} · sequence {selected.item.sequence}.
        {selected.participants.size ? ` Highlighted markers are participants named by linked event evidence: ${[...selected.participants].join(', ')}.`
          : ' No player in this frame is identified by linked event evidence.'}
      </p>
      <ul className="evidence-replay-legend" aria-label="Source team directions and pitch marker key">
        {selected.teamIds.map((teamId, index) => <li key={teamId}>
          <span aria-hidden="true" className={`evidence-team-swatch team-marker-${index % 2}`} />
          <span>Team {teamId} · attacks toward {selected.teamDirections.get(teamId)}</span>
        </li>)}
      </ul>
      <svg className="canonical-pitch evidence-replay-pitch" style={{ aspectRatio: `${selected.length} / ${selected.width}` }}
        viewBox={`-1 -1 ${selected.length + 2} ${selected.width + 2}`} role="img"
        aria-label={`Observed tracking frame at ${clock(selected.item.clockSeconds)}, period ${selected.item.period}, sequence ${selected.item.sequence}`}
        aria-describedby={frameDescriptionId}>
        <rect className="pitch-boundary" x="0" y="0" width={selected.length} height={selected.width} />
        <line className="pitch-marking" x1={selected.length / 2} y1="0" x2={selected.length / 2} y2={selected.width} />
        <circle className="pitch-marking" cx={selected.length / 2} cy={selected.width / 2} r={selected.width * .135} />
        <circle className="ball-marker" cx={selected.ball.x} cy={selected.ball.y} r={Math.max(selected.width * .012, .25)} />
        {selected.players.map((player, index) => {
          const teamIndex = selected.teamIds.indexOf(player.teamId) % 2;
          const highlighted = selected.participants.has(player.playerId);
          const radius = Math.max(selected.width * (highlighted ? .045 : .038), highlighted ? 1.35 : 1.1);
          return <g key={player.playerId} className={highlighted ? 'evidence-participant' : undefined}>
            {teamIndex === 0
              ? <circle className="player-marker" cx={player.position.x} cy={player.position.y} r={radius} />
              : <rect className="player-marker team-marker-1" x={player.position.x - radius} y={player.position.y - radius}
                width={radius * 2} height={radius * 2} />}
            <text className="pitch-player-label" x={player.position.x} y={player.position.y + Math.max(selected.width * .009, .2)} textAnchor="middle">{index + 1}</text>
            <title>{`Team ${player.teamId} · player ${player.playerId}${highlighted ? ' · linked event participant' : ''}`}</title>
          </g>;
        })}
      </svg>
      <div className="position-table-details">
        <table>
          <caption>Observed positions · frame {selected.frame.frameId} · sequence {selected.item.sequence}</caption>
          <thead><tr><th scope="col">Marker</th><th scope="col">Team ID</th><th scope="col">Player ID</th><th scope="col">View X (m)</th><th scope="col">View Y (m)</th><th scope="col">Evidence link</th></tr></thead>
          <tbody>
            <tr><th scope="row">Ball</th><td>—</td><td>Ball</td><td>{selected.ball.x.toFixed(2)}</td><td>{selected.ball.y.toFixed(2)}</td><td>Observed frame</td></tr>
            {selected.players.map((player, index) => <tr key={player.playerId}>
              <td>{index + 1}</td><td>{player.teamId}</td><td>{player.playerId}</td>
              <td>{player.position.x.toFixed(2)}</td><td>{player.position.y.toFixed(2)}</td>
              <td>{selected.participants.has(player.playerId) ? 'Linked event participant' : 'Observed position'}</td>
            </tr>)}
          </tbody>
        </table>
      </div>
      <ol className="evidence-replay-sequence" aria-label="Linked evidence through this observed frame">
        {evidence.items.filter(item => item.sequence <= selected.item.sequence).map(item => <li key={`${item.kind}:${item.id}`}>
          {item.kind} {item.id} · {item.relation} · {clock(item.clockSeconds)} · period {item.period} · sequence {item.sequence}
        </li>)}
      </ol>
    </>}
  </section>;
}

function parseFrame(item: CanonicalEvidencePayload, evidence: CanonicalEvidence): Omit<ProjectedFrame, 'participants'> | null {
  if (item.kind !== 'frame' || !isLinkedReference(item, evidence) || !itemIsInSelectedPrefix(item, evidence)) return null;
  const frame = item.payload as Partial<TelemetryTrackingFrameV1> | null;
  if (!frame || typeof frame !== 'object' || frame.schemaVersion !== 1 || frame.matchId !== evidence.insight.matchId ||
      frame.frameId !== item.id || frame.sequence !== item.sequence || frame.canonicalRevision !== item.sourceCanonicalRevision ||
      frame.matchTime?.elapsedSeconds !== item.clockSeconds || frame.matchTime?.period !== item.period ||
      frame.source?.sourceSha256 !== evidence.insight.sourceSha256 || !hasCompletedRightsReview(frame.source?.rightsReviewStatus) ||
      !Array.isArray(frame.players) || !frame.coordinateSystem || !Array.isArray(frame.coordinateSystem.teamDirections)) return null;

  const system = frame.coordinateSystem;
  const projected = projectCoordinates(frame);
  if (!projected || frame.players.some(player => !player.playerId || !player.teamId) ||
      new Set(frame.players.map(player => player.playerId)).size !== frame.players.length) return null;
  const teamIds = [...new Set(frame.players.map(player => player.teamId))].sort();
  const directions = new Map(system.teamDirections.map(entry => [entry.teamId, entry.direction]));
  if (!teamIds.length || teamIds.some(teamId => !['left', 'right'].includes(directions.get(teamId) ?? '')) ||
      teamIds.some(teamId => system.teamDirections.filter(entry => entry.teamId === teamId).length !== 1)) return null;
  return { item, frame: frame as TelemetryTrackingFrameV1, ...projected, teamIds,
    teamDirections: directions as Map<string, 'left' | 'right'> };
}

function parseEventParticipants(item: CanonicalEvidencePayload, evidence: CanonicalEvidence): string[] | null {
  if (item.kind !== 'event' || !isLinkedReference(item, evidence) || !itemIsInSelectedPrefix(item, evidence)) return null;
  const event = item.payload as { schemaVersion?: unknown; matchId?: unknown; canonicalRevision?: unknown; eventId?: unknown;
    sequence?: unknown; matchTime?: { elapsedSeconds?: unknown; period?: unknown };
    source?: { sourceSha256?: unknown; provenanceStatus?: unknown; rightsReviewStatus?: unknown };
    playerId?: unknown; relatedPlayerId?: unknown } | null;
  if (!event || typeof event !== 'object' || event.schemaVersion !== 1 || event.matchId !== evidence.insight.matchId ||
      event.eventId !== item.id || event.sequence !== item.sequence || event.canonicalRevision !== item.sourceCanonicalRevision ||
      event.matchTime?.elapsedSeconds !== item.clockSeconds || event.matchTime?.period !== item.period ||
      event.source?.sourceSha256 !== evidence.insight.sourceSha256 || !hasCompletedRightsReview(event.source?.rightsReviewStatus)) return null;
  return [event.playerId, event.relatedPlayerId].filter((id): id is string => typeof id === 'string' && id.length > 0);
}

function isLinkedReference(item: CanonicalEvidencePayload, evidence: CanonicalEvidence): boolean {
  return evidence.insight.evidence.some(reference => reference.kind === item.kind && reference.id === item.id && reference.relation === item.relation);
}

function itemIsInSelectedPrefix(item: CanonicalEvidencePayload, evidence: CanonicalEvidence): boolean {
  return Number.isSafeInteger(item.sequence) && item.sequence >= 0 && item.sequence <= evidence.insight.throughSequence &&
    Number.isSafeInteger(item.clockSeconds) && item.clockSeconds >= 0 && item.clockSeconds <= evidence.insight.selectedTimeSeconds &&
    Number.isInteger(item.period) && item.period >= 1 && item.period <= 5 &&
    Number.isSafeInteger(item.sourceCanonicalRevision) && item.sourceCanonicalRevision > 0 &&
    item.sourceCanonicalRevision <= evidence.insight.canonicalRevision;
}

function frameRightsReviewPending(item: CanonicalEvidencePayload): boolean {
  const frame = item.payload as Partial<TelemetryTrackingFrameV1> | null;
  return !hasCompletedRightsReview(frame?.source?.rightsReviewStatus);
}

function hasCompletedRightsReview(status: unknown): boolean {
  return typeof status === 'string' && status.trim().toLowerCase() === 'reviewed';
}

function projectCoordinates(frame: Partial<TelemetryTrackingFrameV1>): {
  length: number; width: number; ball: Point; players: Array<{ playerId: string; teamId: string; position: Point }>;
} | null {
  const system = frame.coordinateSystem;
  if (!system || !Array.isArray(frame.players) || !frame.ball ||
      !Number.isFinite(system.lengthMetres) || system.lengthMetres <= 0 || system.lengthMetres > 120 ||
      !Number.isFinite(system.widthMetres) || system.widthMetres <= 0 || system.widthMetres > 90 ||
      !['metres', 'normalized_0_1', 'percent_0_100'].includes(system.unit) ||
      !['bottom_left', 'top_left'].includes(system.axisOrigin)) return null;
  const scaleX = system.unit === 'metres' ? 1 : system.unit === 'normalized_0_1' ? system.lengthMetres : system.lengthMetres / 100;
  const scaleY = system.unit === 'metres' ? 1 : system.unit === 'normalized_0_1' ? system.widthMetres : system.widthMetres / 100;
  const point = (raw: Point): Point | null => {
    if (!raw || !Number.isFinite(raw.x) || !Number.isFinite(raw.y)) return null;
    const maxX = system.unit === 'metres' ? system.lengthMetres : system.unit === 'normalized_0_1' ? 1 : 100;
    const maxY = system.unit === 'metres' ? system.widthMetres : system.unit === 'normalized_0_1' ? 1 : 100;
    if (raw.x < 0 || raw.x > maxX || raw.y < 0 || raw.y > maxY) return null;
    const x = raw.x * scaleX;
    const y = raw.y * scaleY;
    return { x, y: system.axisOrigin === 'top_left' ? y : system.widthMetres - y };
  };
  const ball = point(frame.ball);
  const players = frame.players.map(player => {
    const position = point(player.position);
    return position && player.playerId && player.teamId ? { playerId: player.playerId, teamId: player.teamId, position } : null;
  });
  if (!ball || players.some(player => player === null)) return null;
  return { length: system.lengthMetres, width: system.widthMetres, ball,
    players: players as Array<{ playerId: string; teamId: string; position: Point }> };
}
