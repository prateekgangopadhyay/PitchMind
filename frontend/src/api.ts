export interface Player { id: string; name: string; teamId: string; shirtNumber: number; role: string }
export interface Team { id: string; name: string; formation: string; players: Player[] }
export interface Fixture { matchId: string; version: string; seed: number; provenance: string; durationSeconds: number; home: Team; away: Team }
export interface Frame { id: string; clockSeconds: number; ballX: number; ballY: number; players: {playerId: string; teamId: string; x: number; y: number}[] }
export interface Snapshot { matchId: string; clockSeconds: number; period: number; status: string; homeScore: number; awayScore: number; provenance: string; frame: Frame; lastSequence: number }
export interface MatchEvent { event: {eventId: string; clockSeconds: number; sequence: number}; teamId: string; playerId: string; action: string; outcome: string }
export interface Insight { id: string; clockSeconds: number; title: string; summary: string; classification: string; detectorVersion: string; eventIds: string[]; frameIds: string[] }
export interface Evidence { insight: Insight; events: MatchEvent[]; frames: Frame[]; coordinateConvention: string; conditions: string[]; limitations: string[] }
export interface Explanation { insightId: string; clockSeconds: number; audience: string; source: string; text: string; eventIds: string[]; limitations: string[] }
export function clock(seconds: number) { return `${Math.floor(seconds / 60).toString().padStart(2, '0')}:${(seconds % 60).toString().padStart(2, '0')}`; }
export async function request<T>(path: string, signal?: AbortSignal, body?: unknown): Promise<T> {
  const response = await fetch(`/api/v1/${path}`, { signal, ...(body ? {method: 'POST', headers: {'Content-Type': 'application/json'}, body: JSON.stringify(body)} : {}) });
  if (!response.ok) throw new Error(`Match service returned ${response.status}. Please retry.`);
  return response.json() as Promise<T>;
}
