import { useEffect, useState } from 'react';
import type { ReactNode } from 'react';
import { ApiRequestError, clock, request } from './api';
import type { CanonicalEventPage, CanonicalInsightPage, CanonicalMatchPage, CanonicalMatchSnapshot, CanonicalTrackingFramePage } from './api';
import type { AppRoute } from './routes';
import { contextHref } from './routes';

type CandidateStatus = { matchId: string | null; currentSequence: number | null; currentClockValue: number | null; sourceSha256: string | null };
type CandidateEvent = { eventId: string; matchId: string; sequence: number; clockSeconds: number; period: number; eventType: string; rawPayload: Record<string, unknown> };
type ContextPlayer = { id: string; teamId: string; name?: string; x?: number; y?: number; active?: boolean; dismissed?: boolean; yellowCards?: number; metrics?: Record<string, unknown> };
type ContextTeam = { id: string; name?: string; activePlayers: number };
type ContextEvent = { id: string; type: string; sequence: number; clockSeconds: number; teamId?: string; playerId?: string };
type ContextInsight = { id: string; truthClass: string; summary: string; evidence: Array<{ kind: string; id: string; relation: string }>; limitations: string[] };
type MatchContext = {
  matchId: string; selectedSeconds: number; revision?: number; throughSequence: number; provenance: string;
  teams: ContextTeam[]; players: ContextPlayer[]; events: ContextEvent[]; insights: ContextInsight[];
  candidate: boolean; sourceTimeLabel?: string;
};

const isObject = (value: unknown): value is Record<string, unknown> => Boolean(value && typeof value === 'object' && !Array.isArray(value));
const asText = (value: unknown): string | undefined => typeof value === 'string' && value.trim() ? value : undefined;
const asNumber = (value: unknown): number | undefined => typeof value === 'number' && Number.isFinite(value) ? value : undefined;

function adaptCanonical(snapshot: CanonicalMatchSnapshot, events: CanonicalEventPage, frames: CanonicalTrackingFramePage | null,
  insights: CanonicalInsightPage, selectedSeconds: number): MatchContext {
  const frame = frames?.items.at(-1)?.frame;
  const playerStates = snapshot.players;
  const players: ContextPlayer[] = (frame?.players ?? []).map(player => {
    const state = playerStates.find(item => item.playerId === player.playerId);
    return { id: player.playerId, teamId: player.teamId, x: player.position.x, y: player.position.y,
      active: state?.active, dismissed: state?.dismissed, yellowCards: state?.yellowCards };
  });
  const teams = [...new Set([...playerStates.map(player => player.teamId), ...players.map(player => player.teamId)])].sort().map(id => ({
    id, name: undefined, activePlayers: playerStates.filter(player => player.teamId === id && player.active).length
  }));
  return {
    matchId: snapshot.matchId, selectedSeconds, revision: snapshot.projectionRevision, throughSequence: snapshot.throughSequence,
    provenance: snapshot.provenance, teams, players,
    events: events.items.map(event => ({ id: event.eventId, type: event.type, sequence: event.sequence, clockSeconds: event.clockSeconds })),
    insights: insights.items.map(insight => ({ id: insight.insightId, truthClass: insight.truthClass, summary: insight.summary,
      evidence: insight.evidence, limitations: insight.limitations ?? [] })), candidate: false
  };
}

async function loadCandidateContext(matchId: string, seconds: number, signal: AbortSignal): Promise<MatchContext | null> {
  const status = await request<CandidateStatus>('simulation/candidate/status', signal).catch(() => null);
  if (!status || status.matchId !== matchId) return null;
  const fetched: CandidateEvent[] = [];
  let afterSequence = -1;
  let hasMore = true;
  for (let pageNumber = 0; hasMore && pageNumber < 12; pageNumber++) {
    const query = new URLSearchParams({ afterSequence: String(afterSequence), limit: '499' });
    const page = await request<{ matchId: string; sourceSha256: string; items: CandidateEvent[]; hasMore: boolean }>(
      `simulation/candidate/matches/${encodeURIComponent(matchId)}/events?${query}`, signal);
    if (page.matchId !== matchId || page.sourceSha256 !== status.sourceSha256 || !Array.isArray(page.items))
      throw new Error('Candidate match feed does not match the selected match context.');
    for (const event of page.items) {
      if (event.matchId !== matchId || !Number.isSafeInteger(event.sequence) || event.sequence <= afterSequence || !isObject(event.rawPayload))
        throw new Error('Candidate match event data failed validation.');
      fetched.push(event);
      afterSequence = event.sequence;
    }
    hasMore = page.hasMore;
    if (hasMore && page.items.length === 0) throw new Error('Candidate match history did not advance.');
  }
  const atTime = fetched.filter(event => Number.isFinite(event.clockSeconds) && event.clockSeconds <= seconds);
  const selectedEvent = atTime.at(-1);
  const rawPlayers = Array.isArray(selectedEvent?.rawPayload.playerPositions)
    ? selectedEvent.rawPayload.playerPositions.filter(isObject) : [];
  const teamMap = new Map<string, { id: string; name?: string; activePlayers: number }>();
  const players: ContextPlayer[] = [];
  for (const raw of rawPlayers) {
    const teamId = asText(raw.teamId) ?? asText(raw.teamName);
    if (!teamId) continue;
    const teamName = asText(raw.teamName);
    const team = teamMap.get(teamId) ?? { id: teamId, name: teamName, activePlayers: 0 };
    team.activePlayers++;
    teamMap.set(teamId, team);
    const name = asText(raw.playerName);
    const playerId = asText(raw.playerId) ?? name;
    if (!playerId) continue;
    const x = asNumber(raw.x);
    const y = asNumber(raw.y);
    const withinSourceRange = x !== undefined && y !== undefined && x >= 0 && x <= 100 && y >= 0 && y <= 100;
    players.push({ id: playerId, teamId, name,
      ...(withinSourceRange ? { x, y } : {}) });
  }
  const events = atTime.map(event => ({ id: event.eventId, type: event.eventType, sequence: event.sequence,
    clockSeconds: event.clockSeconds, teamId: asText(event.rawPayload.teamId), playerId: asText(event.rawPayload.playerId) ?? asText(event.rawPayload.playerName) }));
  return { matchId, selectedSeconds: seconds, throughSequence: selectedEvent?.sequence ?? -1,
    provenance: 'Candidate feed · not yet canonical', teams: [...teamMap.values()], players, events, insights: [], candidate: true,
    sourceTimeLabel: 'Source clock value' };
}

function ContextState({ loading, error, empty }: { loading: boolean; error?: string; empty: string }) {
  if (loading) return <p className="page-state" role="status">Loading selected match data…</p>;
  if (error) return <p className="page-state error-copy" role="alert">{error}</p>;
  return <p className="page-state">{empty}</p>;
}

function useMatchContext(matchId: string | undefined, seconds: number, revision?: number) {
  const [context, setContext] = useState<MatchContext | null>(null);
  const [fixtures, setFixtures] = useState<CanonicalMatchPage | null>(null);
  const [candidateStatus, setCandidateStatus] = useState<CandidateStatus | null>(null);
  const [loading, setLoading] = useState(Boolean(matchId));
  const [error, setError] = useState('');

  useEffect(() => {
    const controller = new AbortController();
    setContext(null); setError('');
    if (!matchId) {
      setLoading(false);
      Promise.all([
        request<CanonicalMatchPage>('canonical/fixtures?limit=100', controller.signal).catch(() => null),
        request<CandidateStatus>('simulation/candidate/status', controller.signal).catch(() => null)
      ]).then(([page, candidate]) => {
        if (controller.signal.aborted) return;
        setFixtures(page);
        setCandidateStatus(candidate?.matchId ? candidate : null);
        if (!page && !candidate?.matchId) setError('No match context is available from the match APIs.');
      });
      return () => controller.abort();
    }

    setLoading(true);
    loadCandidateContext(matchId, seconds, controller.signal).then(async candidate => {
      if (controller.signal.aborted) return;
      if (candidate) { setContext(candidate); return; }
      const root = `canonical/matches/${encodeURIComponent(matchId)}`;
      const query = new URLSearchParams({ atSeconds: String(seconds) });
      if (revision) query.set('revision', String(revision));
      const snapshot = await request<CanonicalMatchSnapshot>(`${root}/summary?${query}`, controller.signal);
      const eventQuery = new URLSearchParams({ atSeconds: String(seconds), revision: String(snapshot.projectionRevision), limit: '100' });
      const frameQuery = new URLSearchParams({ atSeconds: String(seconds), revision: String(snapshot.projectionRevision), limit: '12' });
      const [events, frames, insights] = await Promise.all([
        request<CanonicalEventPage>(`${root}/events?${eventQuery}`, controller.signal),
        request<CanonicalTrackingFramePage>(`${root}/frames?${frameQuery}`, controller.signal).catch(() => null),
        request<CanonicalInsightPage>(`${root}/insights?${eventQuery}`, controller.signal)
      ]);
      if (controller.signal.aborted) return;
      const pages = [events, insights, ...(frames ? [frames] : [])];
      const consistent = pages.every(page => page.matchId === matchId && page.canonicalRevision === snapshot.canonicalRevision &&
        page.projectionRevision === snapshot.projectionRevision && page.selectedTimeSeconds === seconds && page.throughSequence === snapshot.throughSequence);
      if (!consistent) throw new Error('Match API data does not match the selected match and time.');
      setContext(adaptCanonical(snapshot, events, frames, insights, seconds));
    }).catch(failure => {
      if (controller.signal.aborted) return;
      setError(failure instanceof ApiRequestError && failure.status === 404
        ? 'No match data is available at this selected time.' : failure instanceof Error ? failure.message : 'Match data is unavailable.');
    }).finally(() => { if (!controller.signal.aborted) setLoading(false); });

    return () => controller.abort();
  }, [matchId, seconds, revision]);

  const selectMatch = (nextMatchId: string) => {
    const currentPath = typeof window === 'undefined' ? '/matches' : window.location.pathname;
    const pathname = currentPath === '/matches' && candidateStatus?.matchId === nextMatchId ? '/candidate-live' : currentPath;
    window.history.pushState(null, '', contextHref(pathname, nextMatchId, seconds));
    window.dispatchEvent(new Event('pitchmind:locationchange'));
  };
  const matchOptions = [
    ...(candidateStatus?.matchId ? [{ id: candidateStatus.matchId, label: `${candidateStatus.matchId} · candidate live feed` }] : []),
    ...(fixtures?.items?.map(item => ({ id: item.matchId, label: `${item.matchId} · ${item.phase}` })) ?? [])
  ].filter((item, index, all) => all.findIndex(candidate => candidate.id === item.id) === index);
  const selector = !matchId && matchOptions.length ? <label className="match-context-selector">Selected match
    <select aria-label="Select a match" value="" onChange={event => { if (event.target.value) selectMatch(event.target.value); }}>
      <option value="">Choose a match</option>{matchOptions.map(item => <option key={item.id} value={item.id}>{item.label}</option>)}
    </select>
  </label> : null;
  return { context, loading, error, selector };
}

function ContextLayout({ title, description, matchId, seconds, revision, route, children, candidate = false }: {
  title: string; description: string; matchId?: string; seconds: number; revision?: number; route: AppRoute; children: ReactNode; candidate?: boolean;
}) {
  const matchPath = candidate ? '/candidate-live' : '/matches';
  return <section className="content-page screen-page match-context-page">
    <div className="match-context-header-bar">
      <header className="page-heading">
        <p className="eyebrow">{title}</p>
        <h1>{title}</h1>
        <p>{description}</p>
      </header>
      <div className="match-context-scope-container">
        <nav className="match-context-subnav" aria-label="Selected match analysis">
          <a href={contextHref(matchPath, matchId, seconds, revision)} aria-current={route.kind === 'matches' || route.kind === 'candidate-live' ? 'page' : undefined}>Match</a>
          <a href={contextHref('/teams', matchId, seconds, revision)} aria-current={route.kind === 'teams' ? 'page' : undefined}>Teams</a>
          <a href={contextHref('/players', matchId, seconds, revision)} aria-current={route.kind === 'players' ? 'page' : undefined}>Players</a>
          <a href={contextHref('/insights', matchId, seconds, revision)} aria-current={route.kind === 'insights' ? 'page' : undefined}>Insights</a>
          <a href={contextHref('/analyst', matchId, seconds, revision)} aria-current={route.kind === 'analyst' ? 'page' : undefined}>AI Analyst</a>
        </nav>
      </div>
    </div>
    {children}
  </section>;
}

function matchSource(context: MatchContext) {
  return <p className="context-provenance">Match {context.matchId} · {context.candidate ? 'candidate source data' : `revision ${context.revision}`} · through sequence {context.throughSequence} · {context.provenance}</p>;
}

export function MatchSelectionPage({ route }: { route: AppRoute }) {
  const data = useMatchContext(undefined, 0);
  return <ContextLayout title="Match" description="Choose a match from the available API feeds to open its live view and timestamp replay." route={route} seconds={0}>
    {data.selector}
    {!data.selector && <ContextState loading={data.loading} error={data.error} empty="No matches are currently available from the APIs." />}
  </ContextLayout>;
}

export function MatchContextTeamsPage({ route, matchId, seconds, revision }: { route: AppRoute; matchId?: string; seconds: number; revision?: number }) {
  const data = useMatchContext(matchId, seconds, revision);
  return <ContextLayout title="Teams" description={`Team information derived from the selected match at ${clock(seconds)}.`} matchId={matchId} seconds={seconds} revision={revision} route={route} candidate={data.context?.candidate}>
    {data.selector}
    {!matchId ? <ContextState loading={data.loading} error={data.error} empty="Choose a match to view team data." />
      : data.loading || data.error || !data.context ? <ContextState loading={data.loading} error={data.error} empty="No team data is available." />
        : data.context.teams.length ? <><div className="entity-grid team-entity-grid">{data.context.teams.map(team => {
          const events = data.context!.events.filter(event => event.teamId === team.id);
          return <article className="entity-card team-card" key={team.id}>
            <div className="entity-card-topline"><span className="badge">Team in selected match</span></div>
            <h2>{team.name ?? team.id}</h2>
            <dl>
              {team.activePlayers > 0 && <div><dt>Active players in match state</dt><dd>{team.activePlayers}</dd></div>}
              {events.length > 0 && <div><dt>Events attributed by feed</dt><dd>{events.length}</dd></div>}
            </dl>
          </article>;
        })}</div>{matchSource(data.context)}</> : <ContextState loading={false} empty="Team identity is not included in the selected match data." />}
  </ContextLayout>;
}

export function MatchContextPlayersPage({ route, matchId, seconds, revision, playerId }: {
  route: AppRoute; matchId?: string; seconds: number; revision?: number; playerId?: string;
}) {
  const data = useMatchContext(matchId, seconds, revision);
  const players = data.context?.players.filter(player => !playerId || player.id === playerId) ?? [];
  return <ContextLayout title="Players" description={`Player details from observed match data at ${clock(seconds)}. Metrics are shown only when supplied by the match feed.`} matchId={matchId} seconds={seconds} revision={revision} route={route} candidate={data.context?.candidate}>
    {data.selector}
    {!matchId ? <ContextState loading={data.loading} error={data.error} empty="Choose a match to view player data." />
      : data.loading || data.error || !data.context ? <ContextState loading={data.loading} error={data.error} empty="No player data is available." />
        : players.length ? <><div className="entity-grid player-entity-grid">{players.map(player => <article className="entity-card player-card" key={player.id}>
          <div className="entity-card-topline">
            <span className="badge">{player.active === undefined ? 'Observed in selected frame' : player.active ? 'Active' : player.dismissed ? 'Dismissed' : 'Not active'}</span>
          </div>
          <h2>{player.name ?? player.id}</h2>
          <p className="entity-subtitle">Team {data.context!.teams.find(team => team.id === player.teamId)?.name ?? player.teamId}</p>
          <dl>
            {data.context!.candidate && player.x !== undefined && <div><dt>Source coordinate x · unit unverified</dt><dd>{player.x}</dd></div>}
            {data.context!.candidate && player.y !== undefined && <div><dt>Source coordinate y · unit unverified</dt><dd>{player.y}</dd></div>}
            {player.yellowCards !== undefined && <div><dt>Yellow cards</dt><dd>{player.yellowCards}</dd></div>}
          </dl>
          {data.context!.candidate && <p className="entity-note">Player metrics and coordinate orientation are not validated, so no performance ratings or pitch position are inferred.</p>}
        </article>)}</div>{matchSource(data.context)}</> : <ContextState loading={false} empty={playerId ? 'This player is not present in the selected match data at this time.' : 'No player positions are available at this selected time.'} />}
  </ContextLayout>;
}

export function MatchContextInsightsPage({ route, matchId, seconds, revision, insightId }: {
  route: AppRoute; matchId?: string; seconds: number; revision?: number; insightId?: string;
}) {
  const data = useMatchContext(matchId, seconds, revision);
  const items = data.context?.insights.filter(item => !insightId || item.id === insightId) ?? [];
  return <ContextLayout title="Insights" description={`Evidence-supported match insights at ${clock(seconds)}.`} matchId={matchId} seconds={seconds} revision={revision} route={route} candidate={data.context?.candidate}>
    {data.selector}
    {!matchId ? <ContextState loading={data.loading} error={data.error} empty="Choose a match to view insights." />
      : data.loading || data.error || !data.context ? <ContextState loading={data.loading} error={data.error} empty="No insight is available at this selected time." />
        : items.length ? <><div className="entity-grid insight-entity-grid">{items.map(insight => <article className="entity-card insight-entity-card" key={insight.id}>
          <span className="badge">{insight.truthClass}</span><h2>{insight.summary}</h2><details><summary>Evidence and limitations</summary>
            <ul>{insight.evidence.map(item => <li key={`${item.kind}:${item.id}`}>{item.kind}: {item.id} · {item.relation}</li>)}</ul>
            {insight.limitations.map(item => <p key={item}>{item}</p>)}</details>
        </article>)}</div>{matchSource(data.context)}</> : data.context.candidate
          ? <p className="page-state">Candidate events are not canonical insights. This feed has no approved insight records to display.</p>
          : <ContextState loading={false} empty={insightId ? 'This insight is not available in the selected match context.' : 'No canonical insight is available at this time.'} />}
  </ContextLayout>;
}

export function MatchContextAnalystPage({ route, matchId, seconds, revision }: { route: AppRoute; matchId?: string; seconds: number; revision?: number }) {
  const data = useMatchContext(matchId, seconds, revision);
  const insights = data.context?.insights ?? [];
  return <ContextLayout title="AI Analyst" description={`Verified match evidence available to the analyst at ${clock(seconds)}.`} matchId={matchId} seconds={seconds} revision={revision} route={route} candidate={data.context?.candidate}>
    {data.selector}
    {!matchId ? <ContextState loading={data.loading} error={data.error} empty="Choose a match before opening the analyst." />
      : data.loading || data.error || !data.context ? <ContextState loading={data.loading} error={data.error} empty="Match context is unavailable." />
        : <><section className="panel"><h2>Selected match evidence</h2>
          <p>{data.context.events.length} events from match {data.context.matchId} through sequence {data.context.throughSequence}.</p>
          {insights.length ? <ul className="context-insight-list">{insights.map(item => <li key={item.id}><span className="badge">{item.truthClass}</span><p>{item.summary}</p>
            <small>{item.evidence.length} evidence references · {item.limitations.length} stated limitations</small></li>)}</ul>
            : <p role="status">{data.context.candidate ? 'The candidate feed has no canonical insights; analysis is withheld until the facts are admitted and verified.' : 'No saved evidence-backed insight is available at this time.'}</p>}
        </section><p className="page-state" role="status">A match-scoped generated-answer API is not currently available. No sample question or generated answer is substituted.</p>{matchSource(data.context)}</>}
  </ContextLayout>;
}
