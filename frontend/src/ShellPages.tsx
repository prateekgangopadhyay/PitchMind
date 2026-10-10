import { useEffect, useRef, useState } from 'react';
import { clock, provenanceLabel, request } from './api';
import type { CanonicalInsightPage, CanonicalMatchPage } from './api';
import type { AppRoute } from './routes';
import { contextHref, replacePhaseFilterContext, selectedTimeFromSearch } from './routes';

function useCanonicalFixtures() {
  const [page, setPage] = useState<CanonicalMatchPage | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [moreError, setMoreError] = useState('');
  const [attempt, setAttempt] = useState(0);
  const pageController = useRef<AbortController | null>(null);
  const requestGeneration = useRef(0);
  useEffect(() => {
    const generation = ++requestGeneration.current;
    const controller = new AbortController();
    pageController.current?.abort();
    pageController.current = null;
    setLoading(true);
    setPage(null);
    setError('');
    setLoadingMore(false);
    setMoreError('');
    request<CanonicalMatchPage>('canonical/fixtures?limit=20', controller.signal)
      .then(result => { if (!controller.signal.aborted && generation === requestGeneration.current) { setPage(result); setError(''); } })
      .catch(() => { if (!controller.signal.aborted && generation === requestGeneration.current) setError('Canonical match data is temporarily unavailable. No demo fixture has been substituted.'); })
      .finally(() => { if (!controller.signal.aborted && generation === requestGeneration.current) setLoading(false); });
    return () => {
      controller.abort();
      if (requestGeneration.current === generation) requestGeneration.current += 1;
    };
  }, [attempt]);
  useEffect(() => () => {
    requestGeneration.current += 1;
    pageController.current?.abort();
    pageController.current = null;
  }, []);
  const retry = () => {
    requestGeneration.current += 1;
    pageController.current?.abort();
    pageController.current = null;
    setLoadingMore(false);
    setMoreError('');
    setAttempt(value => value + 1);
  };
  const loadMore = async () => {
    if (!page?.hasMore || !page.nextMatchId || loadingMore) return;
    pageController.current?.abort();
    const controller = new AbortController();
    pageController.current = controller;
    const generation = requestGeneration.current;
    setLoadingMore(true);
    setMoreError('');
    try {
      const next = await request<CanonicalMatchPage>(`canonical/fixtures?limit=20&afterMatchId=${encodeURIComponent(page.nextMatchId)}`, controller.signal);
      if (!controller.signal.aborted && generation === requestGeneration.current) setPage(current => current ? { ...next, items: [...current.items, ...next.items] } : next);
    } catch {
      if (!controller.signal.aborted && generation === requestGeneration.current) setMoreError('More persisted matches could not be loaded.');
    } finally {
      if (!controller.signal.aborted && generation === requestGeneration.current) setLoadingMore(false);
    }
  };
  return { page, error, loading, loadingMore, moreError, retry, loadMore };
}

function PageHeading({ eyebrow, title, question }: { eyebrow: string; title: string; question: string }) {
  return <div className="page-heading"><p className="eyebrow">{eyebrow}</p><h1>{title}</h1><p>{question}</p></div>;
}

function PageState({ loading, error, empty }: { loading: boolean; error?: string; empty: string }) {
  if (loading) return <p className="page-state" role="status">Loading canonical match data…</p>;
  if (error) return <p className="page-state error-copy" role="alert">{error}</p>;
  return <p className="page-state">{empty}</p>;
}

export function RoutePage({ route, search }: { route: AppRoute; search: string }) {
  const params = new URLSearchParams(search);
  const selectedMatchId = params.get('matchId') ?? undefined;
  const selectedSeconds = selectedTimeFromSearch(search);
  const hasSelectedTime = params.has('atSeconds');
  const revisionValue = Number(params.get('revision'));
  const revision = Number.isInteger(revisionValue) && revisionValue > 0 ? revisionValue : undefined;

  if (route.kind === 'welcome') return <section className="content-page">
    <PageHeading eyebrow="Football intelligence, grounded in evidence" title="Welcome to PitchMind" question="Explore a simulated match and inspect the evidence behind each view." />
    <p className="page-state">Sign-in is not configured in this local preview.</p>
    <a className="primary-link" href="/home">Explore available matches</a>
  </section>;

  if (route.kind === 'home') return <HomePage search={search} selectedSeconds={hasSelectedTime ? selectedSeconds : undefined} />;

  if (route.kind === 'teams') return <section className="content-page">
    <PageHeading eyebrow="Teams" title={route.teamId ? 'Team profile' : 'Team overview'} question="What team pattern is supported by canonical match evidence?" />
    <p className="page-state">Team directory and profile routes are not exposed by the current canonical API. This view does not infer team data from event payloads.</p>
    <a className="primary-link" href={contextHref('/home', selectedMatchId, selectedSeconds, revision)}>Return to available matches</a>
  </section>;

  if (route.kind === 'players') return <section className="content-page">
    <PageHeading eyebrow="Players" title={route.playerId ? 'Player profile' : 'Player overview'} question="What contribution is recorded for this player?" />
    <p className="page-state">Player directory and impact routes are not exposed by the current canonical API. No player metrics are inferred in this view.</p>
    <a className="primary-link" href={contextHref('/home', selectedMatchId, selectedSeconds, revision)}>Return to available matches</a>
  </section>;

  if (route.kind === 'insights') return <InsightPage route={route} matchId={selectedMatchId} selectedSeconds={selectedSeconds} revision={revision} />;

  if (route.kind === 'analyst') return <section className="content-page">
    <PageHeading eyebrow="AI Analyst" title="Understand a match" question="Which evidence-supported moment would you like to explore?" />
    <p className="page-state">Question answering is not connected to the current public API. Foundry candidate generation is not exposed here.</p>
    {selectedMatchId && <a className="primary-link" href={contextHref('/insights', selectedMatchId, selectedSeconds, revision)}>Inspect insights at {clock(selectedSeconds)}</a>}
    {!selectedMatchId && <a className="primary-link" href="/home">Choose a match</a>}
  </section>;

  return <section className="content-page">
    <PageHeading eyebrow="PitchMind" title="This page is unavailable" question="Choose a section from the main navigation." />
    <a className="primary-link" href="/home">Go to available matches</a>
  </section>;
}

function HomePage({ search, selectedSeconds }: { search: string; selectedSeconds?: number }) {
  const { page, error, loading, loadingMore, moreError, retry, loadMore } = useCanonicalFixtures();
  const matches = page?.items ?? [];
  const requestedPhase = new URLSearchParams(search).get('phase') ?? 'all';
  const phases = [...new Set([...CANONICAL_PHASES, ...matches.map(match => match.phase)])].sort();
  const phaseFilter = requestedPhase === 'all' || phases.includes(requestedPhase) ? requestedPhase : 'all';
  const visibleMatches = phaseFilter === 'all' ? matches : matches.filter(match => match.phase === phaseFilter);
  return <section className="content-page">
    <div className="home-heading-row">
      <PageHeading eyebrow="Home · canonical fixtures" title="Choose a match" question="Explore persisted match state. Scores and phases come from the canonical service; no demo data is mixed into this list." />
      {!loading && !error && matches.length > 0 && <label className="phase-filter">Filter by phase
        <select aria-label="Filter matches by phase" value={phaseFilter} onChange={event => replacePhaseFilterContext(event.target.value)}>
          <option value="all">All phases</option>
          {phases.map(phase => <option key={phase} value={phase}>{phaseLabel(phase)}</option>)}
        </select>
      </label>}
      {!loading && !error && page && <button className="refresh-matches" type="button" onClick={retry}>Refresh matches</button>}
    </div>
    {loading ? <PageState loading empty="" />
      : error ? <div className="page-state error-copy" role="alert"><p>{error}</p><button type="button" onClick={retry}>Retry canonical service</button></div>
        : matches.length === 0 ? <PageState loading={false} empty="No canonical matches are available yet." />
          : visibleMatches.length === 0 ? <p className="page-state" role="status">{page?.hasMore
            ? 'No match in this phase is in the loaded results. More persisted matches are available; select “More matches” to continue.'
            : 'No persisted matches are available in this phase.'}</p>
            : <>
              <p className="match-count" role="status" aria-live="polite">{visibleMatches.length} persisted {visibleMatches.length === 1 ? 'match' : 'matches'}</p>
              <div className="match-list" aria-label="Persisted matches">{visibleMatches.map(match => {
              const matchPath = `/matches/${encodeURIComponent(match.matchId)}`;
              const simulationLabel = provenanceLabel(match.provenance);
              return <article className="match-card" key={match.matchId}>
                <div className="match-card-copy">
                  <div className="match-card-topline"><span className="badge">{simulationLabel}</span><span className="phase-tag">{phaseLabel(match.phase)}</span></div>
                  <h2>Home side <span className="match-score">{match.homeScore}–{match.awayScore}</span> Away side</h2>
                  <p>Latest persisted state · {clock(match.asOfSeconds)} · revision {match.canonicalRevision}</p>
                  <p className="match-id">Match ID <code translate="no">{match.matchId}</code></p>
                  <p className="match-source">Source {match.provenance} · {match.algorithmVersion} · {match.sourceSha256.slice(0, 12)}</p>
                </div>
                <a className="primary-link" aria-label={`Open match ${match.matchId}`} href={contextHref(matchPath, match.matchId, selectedSeconds ?? match.asOfSeconds, match.canonicalRevision)}>Open match</a>
              </article>;
            })}</div>
            </>}
    {!loading && !error && page?.hasMore && <div className="load-more-wrap">
      {moreError && <p className="error-copy" role="alert">{moreError}</p>}
      <button className="load-more" type="button" onClick={loadMore} disabled={loadingMore || !page.nextMatchId}>{loadingMore ? 'Loading more matches…' : 'More matches'}</button>
    </div>}
    {!loading && !error && matches.length > 0 && <p className="home-contract-limit">Competition, team names, fixture summaries, and ranked key moments are not returned by the current canonical fixtures contract.</p>}
  </section>;
}

function phaseLabel(phase: string) {
  const labels: Record<string, string> = {
    notstarted: 'Not started', firsthalf: 'First half', halftime: 'Half-time',
    secondhalf: 'Second half', fulltime: 'Full-time'
  };
  return labels[phase.toLowerCase()] ?? phase.replace(/([a-z])([A-Z])/g, '$1 $2').replaceAll('-', ' ').replaceAll('_', ' ');
}

const CANONICAL_PHASES = ['notstarted', 'firsthalf', 'halftime', 'secondhalf', 'fulltime'];

function InsightPage({ route, matchId, selectedSeconds, revision }: {
  route: Extract<AppRoute, { kind: 'insights' }>;
  matchId?: string;
  selectedSeconds: number;
  revision?: number;
}) {
  const [page, setPage] = useState<CanonicalInsightPage | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  useEffect(() => {
    if (!matchId) { setPage(null); setError(''); setLoading(false); return; }
    const controller = new AbortController();
    setLoading(true);
    const query = new URLSearchParams({ atSeconds: String(selectedSeconds), limit: '50' });
    if (revision) query.set('revision', String(revision));
    request<CanonicalInsightPage>(`canonical/matches/${encodeURIComponent(matchId)}/insights?${query}`, controller.signal)
      .then(result => {
        if (controller.signal.aborted) return;
        if (result.matchId !== matchId || result.selectedTimeSeconds !== selectedSeconds ||
          (revision && result.canonicalRevision !== revision) ||
          result.items.some(item => item.matchId !== matchId || item.canonicalRevision !== result.canonicalRevision ||
            item.selectedTimeSeconds !== selectedSeconds || item.throughSequence > result.throughSequence)) {
          setError('The response does not match the selected match, time and revision.');
          setPage(null);
          return;
        }
        setPage(result);
        setError('');
      })
      .catch(() => { if (!controller.signal.aborted) { setPage(null); setError('Canonical insights are temporarily unavailable.'); } })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [matchId, selectedSeconds, revision]);
  const items = page?.items.filter(item => !route.insightId || item.insightId === route.insightId) ?? [];
  return <section className="content-page">
    <PageHeading eyebrow="Insights" title="Evidence at this moment" question={`What recorded insight is available at ${clock(selectedSeconds)}?`} />
    {!matchId ? <PageState loading={false} empty="Open a match first to inspect its selected-time insights." />
      : loading || error || !page ? <PageState loading={loading} error={error} empty="No insight is available at the selected time." />
        : items.length === 0 ? <PageState loading={false} empty={route.insightId ? 'This insight is not available at the selected time.' : 'No insight is available at the selected time.'} />
          : <div className="entity-grid">{items.map(insight => <article className="entity-card" key={insight.insightId}>
            <span className="badge">{insight.truthClass}</span><h2>{insight.summary}</h2>
            <p>Selected {clock(insight.selectedTimeSeconds)} · revision {insight.canonicalRevision} · {insight.evidence.length} evidence reference(s)</p>
            <ul className="evidence-list">{insight.evidence.map(item => <li key={`${item.kind}:${item.id}`}>{item.kind}: {item.id} · {item.relation}</li>)}</ul>
            <a href={contextHref(`/matches/${encodeURIComponent(matchId)}`, matchId, selectedSeconds, insight.canonicalRevision)}>Open selected match</a>
          </article>)}</div>}
  </section>;
}
