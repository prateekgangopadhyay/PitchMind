export type AppRoute =
  | { kind: 'welcome' }
  | { kind: 'home' }
  | { kind: 'matches'; matchId?: string; replay: boolean }
  | { kind: 'demo-match'; matchId?: string; replay: boolean }
  | { kind: 'teams'; teamId?: string }
  | { kind: 'players'; playerId?: string }
  | { kind: 'insights'; insightId?: string }
  | { kind: 'analyst' }
  | { kind: 'not-found' };

export function resolveRoute(pathname: string): AppRoute {
  let parts: string[];
  try { parts = pathname.split('/').filter(Boolean).map(decodeURIComponent); }
  catch { return { kind: 'not-found' }; }
  if (parts.length === 0 || (parts.length === 1 && parts[0] === 'home')) return { kind: 'home' };
  if (parts.length === 1 && (parts[0] === 'welcome' || parts[0] === 'sign-in')) return { kind: 'welcome' };
  if (parts.length === 1 && parts[0] === 'matches') return { kind: 'matches', replay: false };
  if (parts.length === 2 && parts[0] === 'matches') return { kind: 'matches', matchId: parts[1], replay: false };
  if (parts.length === 2 && parts[0] === 'replay') return { kind: 'matches', matchId: parts[1], replay: true };
  if (parts.length === 1 && parts[0] === 'demo') return { kind: 'demo-match', replay: false };
  if (parts.length === 2 && parts[0] === 'demo' && parts[1] === 'matches') return { kind: 'demo-match', replay: false };
  if (parts.length === 3 && parts[0] === 'demo' && parts[1] === 'matches') return { kind: 'demo-match', matchId: parts[2], replay: false };
  if (parts.length === 1 && parts[0] === 'teams') return { kind: 'teams' };
  if (parts.length === 2 && parts[0] === 'teams') return { kind: 'teams', teamId: parts[1] };
  if (parts.length === 1 && parts[0] === 'players') return { kind: 'players' };
  if (parts.length === 2 && parts[0] === 'players') return { kind: 'players', playerId: parts[1] };
  if (parts.length === 1 && parts[0] === 'insights') return { kind: 'insights' };
  if (parts.length === 2 && parts[0] === 'insights') return { kind: 'insights', insightId: parts[1] };
  if (parts.length === 1 && parts[0] === 'analyst') return { kind: 'analyst' };
  return { kind: 'not-found' };
}

export function selectedTimeFromSearch(search: string): number {
  const raw = new URLSearchParams(search).get('atSeconds');
  const seconds = raw === null ? 0 : Number(raw);
  return Number.isInteger(seconds) && seconds >= 0 && seconds <= 5_400 ? seconds : 0;
}

export function contextHref(path: string, matchId?: string, selectedSeconds?: number, revision?: number): string {
  const params = new URLSearchParams();
  if (matchId) params.set('matchId', matchId);
  if (selectedSeconds !== undefined) params.set('atSeconds', String(selectedTimeFromSearch(`?atSeconds=${selectedSeconds}`)));
  if (revision && Number.isInteger(revision) && revision > 0) params.set('revision', String(revision));
  const query = params.toString();
  return query ? `${path}?${query}` : path;
}

const LOCATION_EVENT = 'pitchmind:locationchange';

export function currentLocation(): string {
  return typeof window === 'undefined' ? '/' : `${window.location.pathname}${window.location.search}`;
}

export function subscribeLocation(onChange: () => void): () => void {
  if (typeof window === 'undefined') return () => undefined;
  window.addEventListener('popstate', onChange);
  window.addEventListener(LOCATION_EVENT, onChange);
  return () => {
    window.removeEventListener('popstate', onChange);
    window.removeEventListener(LOCATION_EVENT, onChange);
  };
}

export function replaceCurrentMatchContext(matchId: string, selectedSeconds: number, revision?: number): void {
  if (typeof window === 'undefined') return;
  const pathname = window.location.pathname.startsWith('/demo')
    ? `/demo/matches/${encodeURIComponent(matchId)}`
    : window.location.pathname.startsWith('/replay/')
      ? `/replay/${encodeURIComponent(matchId)}`
      : `/matches/${encodeURIComponent(matchId)}`;
  window.history.replaceState(null, '', contextHref(pathname, matchId, selectedSeconds, revision));
  window.dispatchEvent(new Event(LOCATION_EVENT));
}

export function replacePhaseFilterContext(phase: string): void {
  if (typeof window === 'undefined') return;
  const params = new URLSearchParams(window.location.search);
  if (phase === 'all') params.delete('phase');
  else params.set('phase', phase);
  const query = params.toString();
  window.history.replaceState(null, '', `${window.location.pathname}${query ? `?${query}` : ''}`);
  window.dispatchEvent(new Event(LOCATION_EVENT));
}
