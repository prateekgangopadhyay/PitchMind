import { useEffect, useSyncExternalStore } from 'react';
import { HttpTransportType, HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { ApiRequestError, getHostAccessToken, request, subscribeAuth } from './api';

export interface CandidateStreamStatus {
  matchId: string | null; isStreaming: boolean; currentSequence: number | null;
  currentClockValue: number | null; period: number | null; sourceSha256: string | null;
  sourceStatus: 'candidate_not_canonical'; coordinateStatus: 'unverified'; playerMetricsStatus: 'unvalidated';
}

export interface CandidateEvent {
  eventId: string;
  matchId: string;
  sequence: number;
  clockSeconds: number;
  period: number;
  eventType: string;
  rawPayload: Record<string, unknown>;
}

interface CandidatePage {
  matchId: string;
  sourceSha256: string;
  items: CandidateEvent[];
  hasMore: boolean;
}

const isObject = (value: unknown): value is Record<string, unknown> =>
  Boolean(value && typeof value === 'object' && !Array.isArray(value));

interface Snapshot { status: CandidateStreamStatus | null; error: string; pending: boolean }
let status: CandidateStreamStatus | null = null; let error = ''; let pending = false;
let autoStartRequested = false; let autoStartAttempted = false; let autoStartInFlight = false;
let nextAutoStartAttemptAt = 0;
let poller: ReturnType<typeof setInterval> | null = null; let requestInFlight: Promise<void> | null = null;
let snapshot: Snapshot = { status, error, pending }; const listeners = new Set<() => void>();

let cachedEvents: CandidateEvent[] = [];
let cachedMatchId: string | null = null;
let isSyncingEvents = false;
let syncPending = false;

let globalHub: HubConnection | null = null;
let activeHubMatchId: string | null = null;
let activeHubSourceHash: string | null = null;
let isConnectingHub = false;

let syncDebounceTimer: ReturnType<typeof setTimeout> | null = null;
let syncThrottleUntil = 0;
let rateLimitBackoffUntil = 0;

export function scheduleSyncEvents(targetMatchId: string) {
  if (syncDebounceTimer) return;
  const now = Date.now();
  const waitMs = Math.max(400, syncThrottleUntil - now);
  syncDebounceTimer = setTimeout(() => {
    syncDebounceTimer = null;
    syncThrottleUntil = Date.now() + 1500;
    void syncCandidateEvents(targetMatchId);
  }, waitMs);
}

function publish() { snapshot = { status, error, pending }; listeners.forEach(fn => fn()); }
function failureMessage(failure: unknown, fallback: string) {
  if (failure instanceof ApiRequestError && failure.status === 401) return 'Your sign-in expired. Sign in again to load the match feed.';
  if (failure instanceof ApiRequestError && failure.status === 403) return 'Your account needs the analyst or admin role to control the live simulation.';
  return fallback;
}

export function readCandidateStatus(): Snapshot { return snapshot; }

export function getCachedCandidateEvents(targetMatchId?: string): CandidateEvent[] {
  if (targetMatchId && cachedMatchId !== targetMatchId) return [];
  return cachedEvents;
}

export function setCachedCandidateEvents(events: CandidateEvent[], targetMatchId: string) {
  cachedEvents = [...events];
  cachedMatchId = targetMatchId;
  publish();
}

export function clearCandidateLiveCache() {
  cachedEvents = [];
  cachedMatchId = null;
  stopGlobalSignalR();
  publish();
}

export function stopGlobalSignalR() {
  if (globalHub) {
    const hub = globalHub;
    globalHub = null;
    activeHubMatchId = null;
    activeHubSourceHash = null;
    void hub.stop().catch(() => undefined);
  }
}

async function ensureGlobalSignalR(matchId: string, sourceHash: string) {
  if (typeof window === 'undefined') return;
  if (globalHub && activeHubMatchId === matchId && activeHubSourceHash === sourceHash) {
    return;
  }
  if (isConnectingHub) return;
  isConnectingHub = true;
  try {
    if (globalHub) {
      const old = globalHub;
      globalHub = null;
      await old.stop().catch(() => undefined);
    }
    const token = await getHostAccessToken();
    const local = ['localhost', '127.0.0.1'].includes(window.location.hostname);
    const headers: Record<string, string> = { 'Content-Type': 'application/json' };
    if (token) headers.Authorization = `Bearer ${token}`;
    else if (local) {
      headers['X-Dev-Subject'] = 'local-analyst';
      headers['X-Dev-Roles'] = 'analyst';
    }
    const response = await fetch('/api/v1/simulation/candidate/realtime/negotiate', {
      method: 'POST',
      headers,
      body: JSON.stringify({ matchId, sourceSha256: sourceHash })
    });
    if (!response.ok) throw new Error('Candidate SignalR unavailable.');
    const negotiation = (await response.json()) as { transport: string; url: string; accessToken: string | null };
    const url = new URL(negotiation.url, window.location.origin);
    if (
      negotiation.transport !== 'aspnetcore-signalr' ||
      url.origin !== window.location.origin ||
      url.pathname !== '/hubs/matches' ||
      negotiation.accessToken
    ) {
      throw new Error('Unsupported SignalR endpoint.');
    }
    const hub = new HubConnectionBuilder()
      .withUrl(url.toString(), {
        accessTokenFactory: async () => (await getHostAccessToken()) ?? '',
        ...(local
          ? { headers: { 'X-Dev-Subject': 'local-analyst', 'X-Dev-Roles': 'analyst' }, transport: HttpTransportType.LongPolling }
          : {})
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Error)
      .build();

    hub.on('CandidateMatchUpdated', (cursor: {
      matchId: string;
      sourceSha256: string;
      sequence?: number;
      clockSeconds?: number;
      period?: number;
    }) => {
      if (cursor.matchId?.toLowerCase() === matchId.toLowerCase()) {
        if (status && status.matchId?.toLowerCase() === cursor.matchId.toLowerCase()) {
          const prevSeq = status.currentSequence ?? -1;
          const newSeq = cursor.sequence ?? prevSeq;
          const prevClock = status.currentClockValue;
          const newClock = cursor.clockSeconds ?? prevClock;
          status = {
            ...status,
            currentSequence: newSeq,
            currentClockValue: newClock,
            period: cursor.period ?? status.period,
            isStreaming: true
          };
          if (newSeq !== prevSeq || newClock !== prevClock) {
            publish();
          }
        }
        scheduleSyncEvents(matchId);
      }
    });

    hub.onreconnected(() => {
      void hub.invoke('SubscribeToCandidateMatch', matchId, sourceHash).catch(() => undefined);
      scheduleSyncEvents(matchId);
    });

    await hub.start();
    await hub.invoke('SubscribeToCandidateMatch', matchId, sourceHash);
    globalHub = hub;
    activeHubMatchId = matchId;
    activeHubSourceHash = sourceHash;
  } catch {
    // Fall back to polling (handled by poller interval)
  } finally {
    isConnectingHub = false;
  }
}

export async function syncCandidateEvents(targetMatchId: string, signal?: AbortSignal): Promise<CandidateEvent[]> {
  if (Date.now() < rateLimitBackoffUntil) {
    return cachedEvents;
  }
  if (cachedMatchId !== targetMatchId) {
    cachedEvents = [];
    cachedMatchId = targetMatchId;
  }
  if (isSyncingEvents) {
    syncPending = true;
    return cachedEvents;
  }
  isSyncingEvents = true;
  try {
    let after = cachedEvents.reduce((last, event) => Math.max(last, event.sequence), -1);
    let hasMore = true;
    let pages = 0;
    let addedCount = 0;
    while (hasMore && pages < 12) {
      if (Date.now() < rateLimitBackoffUntil) break;
      const query = new URLSearchParams({ afterSequence: String(after), limit: '499' });
      const res = await request<CandidatePage>(
        `simulation/candidate/matches/${encodeURIComponent(targetMatchId)}/events?${query}`,
        signal
      );
      if (signal?.aborted) return cachedEvents;
      if (!Array.isArray(res?.items)) break;
      for (const item of res.items) {
        if (
          item.matchId === targetMatchId &&
          Number.isSafeInteger(item.sequence) &&
          item.sequence > after &&
          isObject(item.rawPayload)
        ) {
          cachedEvents.push(item);
          after = item.sequence;
          addedCount++;
        }
      }
      hasMore = Boolean(res.hasMore);
      if (hasMore && res.items.length === 0) break;
      pages++;
    }
    if (addedCount > 0) {
      const map = new Map<number, CandidateEvent>();
      cachedEvents.forEach(e => map.set(e.sequence, e));
      cachedEvents = [...map.values()].sort((a, b) => a.sequence - b.sequence);
      publish();
    }
  } catch (failure) {
    if (failure instanceof ApiRequestError && failure.status === 429) {
      rateLimitBackoffUntil = Date.now() + 10_000;
    }
    // Keep cached events on transient network failure
  } finally {
    isSyncingEvents = false;
    if (syncPending && !signal?.aborted) {
      syncPending = false;
      scheduleSyncEvents(targetMatchId);
    }
  }
  return cachedEvents;
}

export function subscribeCandidateStatus(fn: () => void) {
  listeners.add(fn);
  if (!poller && typeof window !== 'undefined') {
    void refreshCandidateStatus();
    poller = setInterval(() => {
      if (document.visibilityState !== 'hidden') {
        if (!globalHub || globalHub.state !== 'Connected') {
          void refreshCandidateStatus();
        }
      }
    }, 4000);
  }
  return () => {
    listeners.delete(fn);
    if (!listeners.size && poller) {
      clearInterval(poller);
      poller = null;
      stopGlobalSignalR();
    }
  };
}

if (typeof window !== 'undefined') {
  subscribeAuth(() => {
    autoStartAttempted = false;
    nextAutoStartAttemptAt = 0;
    void refreshCandidateStatus(true);
  });
}

async function startAfterStatusIfRequested() {
  if (!autoStartRequested || autoStartInFlight || Date.now() < nextAutoStartAttemptAt) return;
  if (status?.isStreaming) {
    autoStartRequested = false;
    autoStartAttempted = true;
    return;
  }
  if (!status?.matchId || autoStartAttempted) return;
  autoStartInFlight = true;
  try {
    status = await request<CandidateStreamStatus>('simulation/candidate/start', undefined, {});
    autoStartRequested = false;
    autoStartAttempted = true;
    nextAutoStartAttemptAt = 0;
    error = '';
    if (status.isStreaming && status.matchId && status.sourceSha256) {
      void ensureGlobalSignalR(status.matchId, status.sourceSha256);
      void syncCandidateEvents(status.matchId);
    }
  } catch (failure) {
    if (failure instanceof ApiRequestError && failure.status === 403) {
      autoStartRequested = false;
      autoStartAttempted = true;
    } else {
      nextAutoStartAttemptAt = Date.now() + 2000;
    }
    error = failureMessage(failure, 'The candidate simulation could not start. Retrying automatically.');
  } finally {
    autoStartInFlight = false;
    publish();
  }
}

export async function refreshCandidateStatus(autoStart = false): Promise<CandidateStreamStatus | null> {
  if (Date.now() < rateLimitBackoffUntil) {
    return status;
  }
  if (autoStart) {
    if (!status?.isStreaming) {
      autoStartRequested = true;
      autoStartAttempted = false;
    }
  }
  if (requestInFlight) {
    await requestInFlight;
    await startAfterStatusIfRequested();
    return status;
  }
  const task = request<CandidateStreamStatus>('simulation/candidate/status').then(result => {
    if (!result || typeof result.isStreaming !== 'boolean') {
      return;
    }
    const prevSeq = status?.currentSequence ?? -1;
    const prevClock = status?.currentClockValue;
    const prevStreaming = status?.isStreaming;
    status = result;
    const hadError = Boolean(error);
    error = '';
    const hasChanged = hadError || prevSeq !== result.currentSequence || prevClock !== result.currentClockValue || prevStreaming !== result.isStreaming;
    if (hasChanged) {
      publish();
    }
    if (status?.matchId && status.isStreaming) {
      if (status.sourceSha256) {
        void ensureGlobalSignalR(status.matchId, status.sourceSha256);
      }
      if ((status.currentSequence ?? -1) > prevSeq || cachedEvents.length === 0) {
        scheduleSyncEvents(status.matchId);
      }
    }
  }).catch((failure) => {
    if (failure instanceof ApiRequestError && failure.status === 429) {
      rateLimitBackoffUntil = Date.now() + 10_000;
    }
    if (error !== 'Candidate simulation status is unavailable.') {
      error = 'Candidate simulation status is unavailable.';
      publish();
    }
  });
  requestInFlight = task; await task; requestInFlight = null;
  await startAfterStatusIfRequested();
  return status;
}

export async function toggleCandidateStream() {
  if (pending) return;
  autoStartRequested = false;
  autoStartAttempted = true;
  pending = true; publish();
  try {
    const current = await refreshCandidateStatus();
    if (!current?.matchId) throw new Error('Candidate match is unavailable.');
    status = await request<CandidateStreamStatus>(current.isStreaming ? 'simulation/candidate/pause' : 'simulation/candidate/start', undefined, {});
    error = '';
    if (!status.isStreaming) {
      stopGlobalSignalR();
    } else if (status.matchId && status.sourceSha256) {
      void ensureGlobalSignalR(status.matchId, status.sourceSha256);
      void syncCandidateEvents(status.matchId);
    }
  } catch (failure) { error = failureMessage(failure, 'Could not change the candidate stream. Retry in a moment.'); }
  finally { pending = false; publish(); }
}

export function useCandidateStreamStatus(autoStart = true) {
  const state = useSyncExternalStore(subscribeCandidateStatus, readCandidateStatus, readCandidateStatus);
  useEffect(() => { void refreshCandidateStatus(autoStart); }, [autoStart]);
  return state;
}
