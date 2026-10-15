import { createContext, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { clock } from './api';
import { markRealtimeDisconnected, returnToLive, type RealtimeTimelineState } from './realtime';
import { MatchRealtimeClient, type RealtimeAccessTokenProvider, type RealtimeStatus } from './realtimeClient';

const RealtimeAuthContext = createContext<RealtimeAccessTokenProvider | null>(null);

/** The host authentication system supplies tokens; PitchMind does not create or persist them. */
export function RealtimeAuthProvider({ getAccessToken, children }: {
  getAccessToken: () => Promise<string | null>;
  children: ReactNode;
}) {
  const provider = useMemo(() => ({ getAccessToken }), [getAccessToken]);
  return <RealtimeAuthContext.Provider value={provider}>{children}</RealtimeAuthContext.Provider>;
}

function statusText(status: RealtimeStatus): string {
  switch (status) {
    case 'connecting': return 'Connecting to match updates…';
    case 'connected': return 'Match updates connected';
    case 'reconnecting': return 'Connection interrupted. Reconnecting…';
    case 'disconnected': return 'Match updates disconnected';
    case 'unavailable': return 'Live updates unavailable. Sign in and retry.';
  }
}

/**
 * Canonical match host boundary. The fixture/demo App intentionally does not mount
 * this component; hosts must provide auth and a canonical timeline state explicitly.
 */
export function RealtimeMatchUpdates({ state, onStateChange }: {
  state: RealtimeTimelineState;
  onStateChange: (next: RealtimeTimelineState) => void;
}) {
  const authProvider = useContext(RealtimeAuthContext);
  const authGetter = useRef<RealtimeAccessTokenProvider['getAccessToken'] | null>(null);
  authGetter.current = authProvider?.getAccessToken ?? null;
  const hasAuthProvider = authProvider !== null;
  const stableAuthProvider = useRef<RealtimeAccessTokenProvider>({
    getAccessToken: () => authGetter.current ? authGetter.current() : Promise.resolve(null),
  }).current;
  const latestState = useRef(state);
  const latestChange = useRef(onStateChange);
  const client = useRef<MatchRealtimeClient | null>(null);
  const [status, setStatus] = useState<RealtimeStatus>(hasAuthProvider ? 'connecting' : 'unavailable');
  const previousTimeContext = useRef(`${state.mode}:${state.selectedTimeSeconds}`);

  latestState.current = state;
  latestChange.current = onStateChange;

  useEffect(() => {
    if (!hasAuthProvider) {
      setStatus('unavailable');
      return;
    }
    const current = new MatchRealtimeClient({
      matchId: state.matchId,
      authProvider: stableAuthProvider,
      getState: () => latestState.current,
      onStateChange: next => {
        latestState.current = next;
        latestChange.current(next);
      },
      onStatusChange: setStatus,
    });
    client.current = current;
    void current.start();
    return () => {
      if (client.current === current) client.current = null;
      void current.stop();
    };
  }, [hasAuthProvider, stableAuthProvider, state.matchId]);

  useEffect(() => {
    const currentTimeContext = `${state.mode}:${state.selectedTimeSeconds}`;
    const activeClient = client.current;
    if (currentTimeContext !== previousTimeContext.current && activeClient) {
      void activeClient.refresh().catch(() => {
        if (client.current !== activeClient) return;
        const staleState = markRealtimeDisconnected(latestState.current);
        latestState.current = staleState;
        latestChange.current(staleState);
        setStatus('disconnected');
      });
    }
    previousTimeContext.current = currentTimeContext;
  }, [state.mode, state.selectedTimeSeconds]);

  const retry = () => { void client.current?.retry(); };
  const returnLive = () => onStateChange(returnToLive(state));

  return <section className="realtime-status" aria-label="Live match update status">
    <p role="status" aria-live="polite" aria-atomic="true">{statusText(status)}</p>
    {(status === 'unavailable' || status === 'disconnected') && hasAuthProvider &&
      <button type="button" onClick={retry}>Retry live connection</button>}
    {!hasAuthProvider && <p>Live updates require the host application’s signed-in identity.</p>}
    {state.mode === 'HISTORICAL' && <div className="new-match-updates" role="status" aria-live="polite" aria-atomic="true">
      <span>{state.hasNewMatchUpdate
        ? `New match updates are available. Your selected time stays at ${clock(state.selectedTimeSeconds)}.`
        : `Historical view at ${clock(state.selectedTimeSeconds)}. Live updates will not move this selected time.`}</span>
      <button type="button" onClick={returnLive}>Return to live</button>
    </div>}
  </section>;
}
