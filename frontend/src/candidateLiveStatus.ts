import { useEffect, useSyncExternalStore } from 'react';
import { request } from './api';

export interface CandidateStreamStatus {
  matchId: string | null; isStreaming: boolean; currentSequence: number | null;
  currentClockValue: number | null; period: number | null; sourceSha256: string | null;
  sourceStatus: 'candidate_not_canonical'; coordinateStatus: 'unverified'; playerMetricsStatus: 'unvalidated';
}
interface Snapshot { status: CandidateStreamStatus | null; error: string; pending: boolean }
let status: CandidateStreamStatus | null = null; let error = ''; let pending = false;
let autoStarted = false; let poller: ReturnType<typeof setInterval> | null = null; let requestInFlight: Promise<void> | null = null;
let snapshot: Snapshot = { status, error, pending }; const listeners = new Set<() => void>();
function publish() { snapshot = { status, error, pending }; listeners.forEach(fn => fn()); }
export function readCandidateStatus(): Snapshot { return snapshot; }
export function subscribeCandidateStatus(fn: () => void) {
  listeners.add(fn);
  if (!poller && typeof window !== 'undefined') {
    void refreshCandidateStatus();
    poller = setInterval(() => { if (document.visibilityState !== 'hidden') void refreshCandidateStatus(); }, 2500);
  }
  return () => { listeners.delete(fn); if (!listeners.size && poller) { clearInterval(poller); poller = null; } };
}
export async function refreshCandidateStatus(autoStart = false): Promise<CandidateStreamStatus | null> {
  if (requestInFlight) { await requestInFlight; return status; }
  const task = request<CandidateStreamStatus>('simulation/candidate/status').then(async result => {
    status = result; error = ''; publish();
    if (autoStart && !autoStarted && result.matchId) {
      autoStarted = true;
      if (!result.isStreaming) {
        try { status = await request<CandidateStreamStatus>('simulation/candidate/start', undefined, {}); }
        catch { error = 'The candidate simulation could not start automatically. Use Live to retry.'; }
        publish();
      }
    }
  }).catch(() => { error = 'Candidate simulation status is unavailable.'; publish(); });
  requestInFlight = task; await task; requestInFlight = null; return status;
}
export async function toggleCandidateStream() {
  if (pending) return;
  pending = true; publish();
  try {
    const current = await refreshCandidateStatus();
    if (!current?.matchId) throw new Error('Candidate match is unavailable.');
    status = await request<CandidateStreamStatus>(current.isStreaming ? 'simulation/candidate/pause' : 'simulation/candidate/start', undefined, {});
    error = '';
  } catch { error = 'Could not change the candidate stream. Retry in a moment.'; }
  finally { pending = false; publish(); }
}
export function useCandidateStreamStatus(autoStart = true) {
  const state = useSyncExternalStore(subscribeCandidateStatus, readCandidateStatus, readCandidateStatus);
  useEffect(() => { void refreshCandidateStatus(autoStart); }, [autoStart]);
  return state;
}
