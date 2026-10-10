export interface Player { id: string; name: string; teamId: string; shirtNumber: number; role: string }
export interface Team { id: string; name: string; formation: string; players: Player[] }
export interface PitchPosition { playerId: string; teamId: string; x: number; y: number }
export interface Frame { id: string; clockSeconds: number; ballX: number; ballY: number; coordinateConvention: string; players: PitchPosition[] }
export interface MatchEvent { event: {eventId: string; matchId: string; sequence: number; type: string; clockSeconds: number; period: number; occurredAt: string}; teamId: string; playerId: string; action: string; outcome: string; relatedPlayerId: string | null }
export interface Fixture { matchId: string; version: string; seed: number; provenance: string; durationSeconds: number; halfTimeSeconds: number; pitchLengthMetres: number; pitchWidthMetres: number; home: Team; away: Team; events: MatchEvent[]; frames: Frame[] }
export interface MatchPlayerState { playerId: string; teamId: string; active: boolean; dismissed: boolean; yellowCards: number }
export interface Snapshot { matchId: string; clockSeconds: number; period: number; status: 'replay' | 'full-time'; phase: 'notstarted' | 'firsthalf' | 'halftime' | 'secondhalf' | 'fulltime'; homeScore: number; awayScore: number; provenance: string; frame: Frame; lastSequence: number; possessionTeamId: string | null; homeActivePlayers: number; awayActivePlayers: number; players: MatchPlayerState[] }
export interface Insight { id: string; matchId: string; clockSeconds: number; title: string; summary: string; classification: string; detectorVersion: string; eventIds: string[]; frameIds: string[]; sourceVersion: string; canonicalRevision: number | null; selectedTimeSeconds: number; throughSequence: number; policyStatus: string; publishable: boolean; conditions: string[] }
export interface Evidence { insight: Insight; events: MatchEvent[]; frames: Frame[]; coordinateConvention: string; conditions: string[]; limitations: string[] }
export interface Explanation { insightId: string; clockSeconds: number; audience: string; source: string; text: string; eventIds: string[]; limitations: string[] }
export interface ApiProblem { type: string; title: string; status: number; code: string; correlationId: string; detail?: string; instance?: string }
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
