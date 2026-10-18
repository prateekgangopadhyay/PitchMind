import { useEffect, useSyncExternalStore } from 'react';
import { request } from './api';

export interface CandidateStreamStatus {
  matchId: string | null; isStreaming: boolean; currentSequence: number | null;
  currentClockValue: number | null; period: number | null; sourceSha256: string | null;
  sourceStatus: 'candidate_not_canonical'; coordinateStatus: 'unverified'; playerMetricsStatus: 'unvalidated';
}
interface Snapshot { status: CandidateStreamStatus | null; error: string; pending: boolean }
let status: CandidateStreamStatus | null = null; let error = ''; let pending = false;
let autoStartRequested = false; let autoStartAttempted = false;
let poller: ReturnType<typeof setInterval> | null = null; let requestInFlight: Promise<void> | null = null;
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
async function startAfterStatusIfRequested() {
  if (!autoStartRequested) return;
  if (status?.isStreaming) {
    autoStartRequested = false;
    autoStartAttempted = true;
    return;
  }
  if (!status?.matchId || autoStartAttempted) return;
  autoStartRequested = false;
  autoStartAttempted = true;
  try { status = await request<CandidateStreamStatus>('simulation/candidate/start', undefined, {}); }
  catch { error = 'The candidate simulation could not start automatically. Use Live to retry.'; }
  publish();
}
export async function refreshCandidateStatus(autoStart = false): Promise<CandidateStreamStatus | null> {
  if (autoStart && !autoStartAttempted) autoStartRequested = true;
  if (requestInFlight) {
    await requestInFlight;
    await startAfterStatusIfRequested();
    return status;
  }
  const task = request<CandidateStreamStatus>('simulation/candidate/status').then(result => {
    status = result; error = ''; publish();
  }).catch(() => { error = 'Candidate simulation status is unavailable.'; publish(); });
  requestInFlight = task; await task; requestInFlight = null;
  await startAfterStatusIfRequested();
  return status;
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
