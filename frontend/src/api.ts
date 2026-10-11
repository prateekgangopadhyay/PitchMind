export interface Player { id: string; name: string; teamId: string; shirtNumber: number; role: string }
export interface Team { id: string; name: string; formation: string; players: Player[] }
export interface PitchPosition { playerId: string; teamId: string; x: number; y: number }
export interface Frame { id: string; clockSeconds: number; ballX: number; ballY: number; coordinateConvention: string; players: PitchPosition[] }
export interface MatchEvent { event: {eventId: string; matchId: string; sequence: number; type: string; clockSeconds: number; period: number; occurredAt: string}; teamId: string; playerId: string; action: string; outcome: string; relatedPlayerId: string | null }
export interface Fixture { matchId: string; version: string; seed: number; provenance: string; durationSeconds: number; halfTimeSeconds: number; pitchLengthMetres: number; pitchWidthMetres: number; home: Team; away: Team; events: MatchEvent[]; frames: Frame[] }
export interface MatchPlayerState { playerId: string; teamId: string; active: boolean; dismissed: boolean; yellowCards: number }
export interface Snapshot { matchId: string; clockSeconds: number; period: number; status: 'replay' | 'full-time'; phase: 'notstarted' | 'firsthalf' | 'halftime' | 'secondhalf' | 'fulltime'; homeScore: number; awayScore: number; provenance: string; frame: Frame; lastSequence: number; possessionTeamId: string | null; homeActivePlayers: number; awayActivePlayers: number; players: MatchPlayerState[]; competitionId: string | null; competitionRulesVersion: string | null; maximumSubstituteReentriesPerPlayer: number | null }
export interface TacticalContext { schemaVersion: 'tactical-context-v1'; scoreline: string; teamRole: string; numericalAdvantage: number; opponentPhase: string; matchPhase: string }
export interface EvidenceNode { id: string; kind: 'claim' | 'event' | 'frame' | 'metric'; truthClass: 'fact' | 'derivation' | 'pattern' | 'interpretation' | 'hypothesis'; description: string; eventId: string | null; frameId: string | null; clockSeconds: number | null; sequence: number | null; sourceVersion: string; sourceSha256: string; canonicalRevision: number | null; numericValue: number | null; unit: string | null }
export interface EvidenceEdge { fromNodeId: string; toNodeId: string; relation: 'precedes' | 'derived_from' }
export interface EvidenceCondition { id: string; passed: boolean; observed: string; threshold: string | null; sourceNodeIds: string[]; exclusionReason: string | null }
export interface EvidenceGraph { matchId: string; sourceVersion: string; sourceSha256: string; provenance: string; canonicalRevision: number | null; selectedTimeSeconds: number; throughSequence: number; detectorVersion: string; policyStatus: string; publishable: boolean; claimNodeId: string; nodes: EvidenceNode[]; edges: EvidenceEdge[]; uncertainty: string; conditions: EvidenceCondition[] }
export interface KeyMoment { id: string; matchId: string; sourceVersion: string; sourceSha256: string; provenance: string; canonicalRevision: number | null; selectedTimeSeconds: number; throughSequence: number; clockSeconds: number; sequence: number; kind: string; truthClass: string; title: string; summary: string; relevance: number; policyVersion: string; policyStatus: string; publishable: boolean; eventIds: string[]; frameIds: string[]; evidenceClaimNodeIds: string[] }
export interface MechanismReplay {
  id: string; matchId: string; sourceVersion: string; sourceSha256: string; provenance: string; canonicalRevision: number | null;
  selectedTimeSeconds: number; throughSequence: number; startSeconds: number; endSeconds: number;
  detectorVersion: string; policyStatus: string; publishable: boolean; eventIds: string[]; mechanismEventIds: string[]; frameIds: string[];
  participantIds: string[]; events: MatchEvent[]; frames: Frame[]; coordinateConvention: string; evidenceGraph: EvidenceGraph; limitations: string[];
}
export interface Insight { id: string; matchId: string; clockSeconds: number; title: string; summary: string; classification: string; detectorVersion: string; eventIds: string[]; frameIds: string[]; sourceVersion: string; sourceSha256: string; provenance: string; canonicalRevision: number | null; selectedTimeSeconds: number; throughSequence: number; policyStatus: string; publishable: boolean; conditions: string[]; context: TacticalContext | null; evidenceGraph: EvidenceGraph | null }
export interface Evidence { insight: Insight; events: MatchEvent[]; frames: Frame[]; coordinateConvention: string; conditions: string[]; limitations: string[] }
export interface Explanation { insightId: string; clockSeconds: number; audience: string; source: string; text: string; eventIds: string[]; limitations: string[] }
export interface ApiProblem { type: string; title: string; status: number; code: string; correlationId: string; detail?: string; instance?: string }
// PostgreSQL-backed BFF contracts. Kept separate from fixture/demo contracts above;
// canonical requests never fall back to the local synthetic fixture.
export interface CanonicalMatchCard {
  matchId: string; canonicalRevision: number; projectionRevision: number; asOfSeconds: number; throughSequence: number;
  phase: string; homeScore: number; awayScore: number; provenance: string;
  algorithmVersion: string; sourceSha256: string;
}
export interface CanonicalMatchPage { items: CanonicalMatchCard[]; nextMatchId: string | null; hasMore: boolean }
export interface CanonicalMatchSnapshot {
  matchId: string; canonicalRevision: number; projectionRevision: number; selectedTimeSeconds: number; asOfSeconds: number;
  throughSequence: number; algorithmVersion: string; sourceSha256: string; stateSha256: string;
  provenance: string; period: number; phase: string; homeScore: number; awayScore: number;
  possessionTeamId: string | null; homeActivePlayers: number; awayActivePlayers: number;
  players: MatchPlayerState[];
}
export interface CanonicalEventPage {
  matchId: string; canonicalRevision: number; projectionRevision: number; selectedTimeSeconds: number; throughSequence: number;
  items: CanonicalLedgerEvent[]; nextCursor: string | null; hasMore: boolean;
}
export interface CanonicalLedgerEvent {
  eventId: string; matchId: string; sequence: number; type: string;
  clockSeconds: number; period: number; occurredAt: string;
}
export interface CanonicalEvidenceReference { kind: string; id: string; relation: string }
export interface CanonicalInsight {
  insightId: string; matchId: string; canonicalRevision: number; projectionRevision: number; selectedTimeSeconds: number;
  throughSequence: number; sourceVersion: string; provenance: string; sourceSha256: string;
  detectorVersion: string; truthClass: string; summary: string;
  evidence: CanonicalEvidenceReference[];
}
export interface CanonicalInsightPage {
  matchId: string; canonicalRevision: number; projectionRevision: number; selectedTimeSeconds: number; throughSequence: number;
  items: CanonicalInsight[]; nextCursor: string | null; hasMore: boolean;
}
export interface CanonicalEvidencePayload {
  kind: 'event' | 'frame'; id: string; relation: string; sourceCanonicalRevision: number; sequence: number;
  clockSeconds: number; period: number; payload: Record<string, unknown>;
}
export interface CanonicalEvidence { insight: CanonicalInsight; items: CanonicalEvidencePayload[] }
export interface CanonicalReplay { snapshot: CanonicalMatchSnapshot; events: CanonicalEventPage }
export interface CanonicalTimelineBackfill {
  snapshot: CanonicalMatchSnapshot; events: CanonicalEventPage; revisionChanged: boolean;
  replayRequired: boolean; requestedAfterSequence: number;
}
export interface RealtimeCursorV1 {
  kind: 'matchUpdated'; matchId: string; canonicalRevision: number; lastSequence: number;
  serverUtc: string; schemaVersion: 1;
}
export class ApiRequestError extends Error {
  constructor(readonly status: number, readonly code: string, readonly correlationId: string, message: string) { super(message); this.name = 'ApiRequestError'; }
}
export function clock(seconds: number) { return `${Math.floor(seconds / 60).toString().padStart(2, '0')}:${(seconds % 60).toString().padStart(2, '0')}`; }
export async function request<T>(path: string, signal?: AbortSignal, body?: unknown): Promise<T> {
  const response = await fetch(`/api/v1/${path}`, { signal, ...(body ? {method: 'POST', headers: {'Content-Type': 'application/json'}, body: JSON.stringify(body)} : {}) });
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as ApiProblem | null;
    throw new ApiRequestError(response.status, problem?.code ?? 'REQUEST_FAILED', problem?.correlationId ?? response.headers.get('X-Correlation-ID') ?? '', problem?.title ?? `Match service returned ${response.status}. Please retry.`);
  }
  return response.json() as Promise<T>;
}
