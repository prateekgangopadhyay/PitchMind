import { useEffect, useSyncExternalStore } from 'react';
import { HttpTransportType, HubConnection, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { ApiRequestError, getHostAccessToken, getStoredAuthSession, request, subscribeAuth } from './api';

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

export interface CandidateSnapshot {
  status: CandidateStreamStatus | null;
  error: string;
  pending: boolean;
  currentEvent: CandidateEvent | null;
  events: CandidateEvent[];
  processedEvents: CandidateEvent[];
  queueLength: number;
}
export type Snapshot = CandidateSnapshot;

let status: CandidateStreamStatus | null = null;
let error = '';
let pending = false;
let autoStartRequested = false;
let autoStartAttempted = false;
let autoStartInFlight = false;
let nextAutoStartAttemptAt = 0;
let poller: ReturnType<typeof setInterval> | null = null;
let requestInFlight: Promise<void> | null = null;

let cachedEvents: CandidateEvent[] = [];
let processedEvents: CandidateEvent[] = [];
let eventQueue: CandidateEvent[] = [];
let currentLiveEvent: CandidateEvent | null = null;
let cachedMatchId: string | null = null;
let queueTicker: ReturnType<typeof setInterval> | null = null;
let syncPromise: Promise<CandidateEvent[]> | null = null;

let snapshot: CandidateSnapshot = {
  status,
  error,
  pending,
  currentEvent: currentLiveEvent,
  events: cachedEvents,
  processedEvents,
  queueLength: 0
};
const listeners = new Set<() => void>();

let globalHub: HubConnection | null = null;
let activeHubMatchId: string | null = null;
let activeHubSourceHash: string | null = null;
let isConnectingHub = false;

let syncDebounceTimer: ReturnType<typeof setTimeout> | null = null;
let syncThrottleUntil = 0;
let rateLimitBackoffUntil = 0;

function publish() {
  snapshot = {
    status,
    error,
    pending,
    currentEvent: currentLiveEvent,
    events: cachedEvents,
    processedEvents,
    queueLength: eventQueue.length
  };
  listeners.forEach(fn => fn());
}

function failureMessage(failure: unknown, fallback: string) {
  if (failure instanceof ApiRequestError && failure.status === 401) return 'Your sign-in expired. Sign in again to load the match feed.';
  if (failure instanceof ApiRequestError && failure.status === 403) return 'Your account needs the analyst or admin role to control the live simulation.';
  return fallback;
}

export function readCandidateStatus(): CandidateSnapshot {
  return snapshot;
}

export function getCachedCandidateEvents(targetMatchId?: string): CandidateEvent[] {
  if (targetMatchId && cachedMatchId !== targetMatchId) return [];
  return cachedEvents;
}

export function getProcessedCandidateEvents(targetMatchId?: string): CandidateEvent[] {
  if (targetMatchId && cachedMatchId !== targetMatchId) return [];
  return processedEvents;
}

export function getCurrentLiveCandidateEvent(targetMatchId?: string): CandidateEvent | null {
  if (targetMatchId && cachedMatchId !== targetMatchId) return null;
  return currentLiveEvent;
}

export function enqueueCandidateEvents(items: CandidateEvent[], targetMatchId: string) {
  if (cachedMatchId !== targetMatchId) {
    cachedEvents = [];
    processedEvents = [];
    eventQueue = [];
    currentLiveEvent = null;
    cachedMatchId = targetMatchId;
  }

  const existingMap = new Map<number, CandidateEvent>();
  cachedEvents.forEach(e => existingMap.set(e.sequence, e));

  const newItems: CandidateEvent[] = [];
  for (const item of items) {
    if (!existingMap.has(item.sequence)) {
      existingMap.set(item.sequence, item);
      newItems.push(item);
    }
  }

  if (newItems.length === 0) return;

  cachedEvents = [...existingMap.values()].sort((a, b) => a.sequence - b.sequence);

  if (!currentLiveEvent) {
    let targetSeq: number | null = null;
    if (status?.currentSequence !== null && status?.currentSequence !== undefined) {
      targetSeq = status.currentSequence;
    } else if (typeof window !== 'undefined') {
      try {
        const savedSeq = window.sessionStorage.getItem(`pitchmind:lastSeq:${targetMatchId}`);
        if (savedSeq !== null) {
          const parsed = Number(savedSeq);
          if (Number.isFinite(parsed) && parsed > 0) targetSeq = parsed;
        }
      } catch {
        // Ignore sessionStorage read errors
      }
    }

    let initialIndex = 0;
    if (targetSeq !== null && targetSeq > 0) {
      for (let i = cachedEvents.length - 1; i >= 0; i--) {
        if (cachedEvents[i].sequence <= targetSeq) {
          initialIndex = i;
          break;
        }
      }
    }

    currentLiveEvent = cachedEvents[initialIndex];
    processedEvents = cachedEvents.slice(0, initialIndex + 1);
    eventQueue = cachedEvents.slice(initialIndex + 1);
    status = {
      ...(status ?? {
        matchId: targetMatchId,
        isStreaming: true,
        sourceSha256: null,
        sourceStatus: 'candidate_not_canonical',
        coordinateStatus: 'unverified',
        playerMetricsStatus: 'unvalidated'
      }),
      currentSequence: currentLiveEvent.sequence,
      currentClockValue: currentLiveEvent.clockSeconds,
      period: currentLiveEvent.period,
      isStreaming: status ? status.isStreaming : true
    };
  } else {
    const existingQueueSeqs = new Set(eventQueue.map(e => e.sequence));
    const processedSeqs = new Set(processedEvents.map(e => e.sequence));
    for (const item of newItems) {
      if (
        item.sequence > currentLiveEvent.sequence &&
        !existingQueueSeqs.has(item.sequence) &&
        !processedSeqs.has(item.sequence)
      ) {
        eventQueue.push(item);
      }
    }
    eventQueue.sort((a, b) => a.sequence - b.sequence);
  }

  startQueueTicker();
  publish();
}

export function setCachedCandidateEvents(events: CandidateEvent[], targetMatchId: string) {
  enqueueCandidateEvents(events, targetMatchId);
}

function processNextQueueEvent() {
  if (!status?.isStreaming) return;

  if (eventQueue.length === 0) {
    if (status?.matchId) {
      scheduleSyncEvents(status.matchId);
    }
    return;
  }

  const nextEvent = eventQueue.shift()!;
  currentLiveEvent = nextEvent;
  if (!processedEvents.some(e => e.sequence === nextEvent.sequence)) {
    processedEvents.push(nextEvent);
  }

  if (typeof window !== 'undefined' && nextEvent.matchId) {
    try {
      window.sessionStorage.setItem(`pitchmind:lastSeq:${nextEvent.matchId}`, String(nextEvent.sequence));
      window.sessionStorage.setItem(`pitchmind:lastClock:${nextEvent.matchId}`, String(nextEvent.clockSeconds));
    } catch {
      // Ignore sessionStorage write errors
    }
  }

  status = {
    ...(status ?? {
      matchId: nextEvent.matchId,
      isStreaming: true,
      currentSequence: nextEvent.sequence,
      currentClockValue: nextEvent.clockSeconds,
      period: nextEvent.period,
      sourceSha256: null,
      sourceStatus: 'candidate_not_canonical',
      coordinateStatus: 'unverified',
      playerMetricsStatus: 'unvalidated'
    }),
    currentSequence: nextEvent.sequence,
    currentClockValue: nextEvent.clockSeconds,
    period: nextEvent.period,
    isStreaming: true
  };

  publish();
}

function startQueueTicker() {
  if (queueTicker || typeof window === 'undefined') return;
  queueTicker = setInterval(() => {
    processNextQueueEvent();
  }, 1000);
}

function stopQueueTicker() {
  if (queueTicker) {
    clearInterval(queueTicker);
    queueTicker = null;
  }
}

export function clearCandidateLiveCache() {
  if (typeof window !== 'undefined' && cachedMatchId) {
    try {
      window.sessionStorage.removeItem(`pitchmind:lastSeq:${cachedMatchId}`);
      window.sessionStorage.removeItem(`pitchmind:lastClock:${cachedMatchId}`);
    } catch {
      // Ignore sessionStorage removal errors
    }
  }
  cachedEvents = [];
  processedEvents = [];
  eventQueue = [];
  currentLiveEvent = null;
  cachedMatchId = null;
  stopGlobalSignalR();
  stopQueueTicker();
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

export function scheduleSyncEvents(targetMatchId: string) {
  if (syncDebounceTimer) return;
  const now = Date.now();
  const waitMs = Math.max(100, syncThrottleUntil - now);
  syncDebounceTimer = setTimeout(() => {
    syncDebounceTimer = null;
    syncThrottleUntil = Date.now() + 600;
    void syncCandidateEvents(targetMatchId);
  }, waitMs);
}

async function ensureGlobalSignalR(matchId: string, sourceHash: string) {
  if (typeof window === 'undefined') return;
  if (getStoredAuthSession()?.user.provider === 'guest') return;
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
    processedEvents = [];
    eventQueue = [];
    currentLiveEvent = null;
    cachedMatchId = targetMatchId;
    publish();
  }

  if (syncPromise) {
    return syncPromise;
  }

  syncPromise = (async () => {
    try {
      const highestSequence = cachedEvents.reduce((last, event) => Math.max(last, event.sequence), -1);
      const query = new URLSearchParams({ afterSequence: String(highestSequence), limit: '100' });
      const res = await request<CandidatePage>(
        `simulation/candidate/matches/${encodeURIComponent(targetMatchId)}/events?${query}`,
        signal
      );
      if (signal?.aborted) return cachedEvents;
      if (Array.isArray(res?.items) && res.items.length > 0) {
        const validItems = res.items.filter(item =>
          item.matchId === targetMatchId &&
          Number.isSafeInteger(item.sequence) &&
          item.sequence > highestSequence &&
          isObject(item.rawPayload)
        );
        if (validItems.length > 0) {
          enqueueCandidateEvents(validItems, targetMatchId);
        }
      }
    } catch (failure) {
      if (failure instanceof ApiRequestError && failure.status === 429) {
        rateLimitBackoffUntil = Date.now() + 10_000;
      }
    } finally {
      syncPromise = null;
    }
    return cachedEvents;
  })();

  return syncPromise;
}

export function subscribeCandidateStatus(fn: () => void) {
  listeners.add(fn);
  startQueueTicker();
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
    if (!listeners.size) {
      if (poller) {
        clearInterval(poller);
        poller = null;
      }
      stopQueueTicker();
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
    const started = await request<CandidateStreamStatus>('simulation/candidate/start', undefined, {});
    autoStartRequested = false;
    autoStartAttempted = true;
    nextAutoStartAttemptAt = 0;
    error = '';
    status = {
      ...started,
      currentSequence: currentLiveEvent ? currentLiveEvent.sequence : started.currentSequence,
      currentClockValue: currentLiveEvent ? currentLiveEvent.clockSeconds : started.currentClockValue,
      period: currentLiveEvent ? currentLiveEvent.period : started.period
    };
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
    error = '';
    status = {
      ...result,
      currentSequence: currentLiveEvent ? currentLiveEvent.sequence : result.currentSequence,
      currentClockValue: currentLiveEvent ? currentLiveEvent.clockSeconds : result.currentClockValue,
      period: currentLiveEvent ? currentLiveEvent.period : result.period
    };
    publish();
    if (status?.matchId && status.isStreaming) {
      if (status.sourceSha256) {
        void ensureGlobalSignalR(status.matchId, status.sourceSha256);
      }
      if (cachedEvents.length === 0 || (result.currentSequence && result.currentSequence > (cachedEvents.at(-1)?.sequence ?? -1))) {
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
    const updated = await request<CandidateStreamStatus>(current.isStreaming ? 'simulation/candidate/pause' : 'simulation/candidate/start', undefined, {});
    error = '';
    status = {
      ...updated,
      currentSequence: currentLiveEvent ? currentLiveEvent.sequence : updated.currentSequence,
      currentClockValue: currentLiveEvent ? currentLiveEvent.clockSeconds : updated.currentClockValue,
      period: currentLiveEvent ? currentLiveEvent.period : updated.period
    };
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
