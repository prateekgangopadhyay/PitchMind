import { useState, useRef, useEffect, useMemo, useCallback } from 'react';
import { clock, askCandidateAnalyst } from './api';
import type { CandidateAnalystAnswer } from './api';
import { contextHref } from './routes';
import { FOUNDRY_AGENTS } from './tacticalAgentDiscussion';
import type { FoundryAgentMetadata } from './tacticalAgentDiscussion';

export interface CommentaryEventInput {
  id: string;
  type: string;
  sequence: number;
  clockSeconds: number;
  period?: number;
  teamId?: string;
  teamName?: string;
  playerId?: string;
  playerName?: string;
  receiverName?: string;
  pitchX?: number;
  pitchY?: number;
  endX?: number;
  endY?: number;
  action?: string;
  outcome?: string;
  impactScore?: number;
  headline?: string;
}

export type CommentaryCategory = 'goal' | 'shot' | 'tactical' | 'recovery' | 'pass' | 'foul' | 'card' | 'whistle' | 'general';

export interface AiCommentaryItem {
  id: string;
  sequence: number;
  clockSeconds: number;
  timeFormatted: string;
  period: number;
  category: CommentaryCategory;
  headline: string;
  commentaryText: string;
  aiAgent: FoundryAgentMetadata;
  teamName?: string;
  playerName?: string;
  impactScore?: number;
  isKeyMoment: boolean;
  citedEventId?: string;
}

type AgentReportState =
  | { status: 'generating' }
  | { status: 'ready'; answer: string; evidenceIds: string[] }
  | { status: 'unavailable' };

export interface AiCommentaryTimelineProps {
  events: CommentaryEventInput[];
  matchId?: string;
  selectedSeconds?: number;
  homeTeam?: string;
  awayTeam?: string;
  homeScore?: number;
  awayScore?: number;
  loading?: boolean;
  onTimeSelect?: (seconds: number) => void;
}

/**
 * Maps event types to tactical commentary categories & primary agents
 */
function categorizeEvent(type: string): { category: CommentaryCategory; agentName: string } {
  const norm = type.toLowerCase();
  if (norm.includes('goal')) return { category: 'goal', agentName: 'ChanceCreationAnalyst' };
  if (norm.includes('shot') || norm.includes('penalty')) return { category: 'shot', agentName: 'ChanceCreationAnalyst' };
  if (norm.includes('recovery') || norm.includes('interception') || norm.includes('possessionlost')) return { category: 'recovery', agentName: 'HighPressAnalyst' };
  if (norm.includes('tackle') || norm.includes('block')) return { category: 'recovery', agentName: 'DefensiveStructureAnalyst' };
  if (norm.includes('card') || norm.includes('substitution')) return { category: 'card', agentName: 'MatchStateStrategist' };
  if (norm.includes('foul')) return { category: 'foul', agentName: 'MatchStateStrategist' };
  if (norm.includes('pass')) return { category: 'pass', agentName: 'PositionalPlayArchitect' };
  if (norm.includes('kickoff') || norm.includes('whistle') || norm.includes('period')) return { category: 'whistle', agentName: 'CoachFrameworkAnalyst' };
  return { category: 'tactical', agentName: 'TacticalAnalyst' };
}

/**
 * Creates event cards from observed match data. Narrative text is intentionally
 * left empty until the configured Foundry agent returns a validated answer.
 */
export function generateAiCommentaryFromEvents(events: CommentaryEventInput[]): AiCommentaryItem[] {
  if (!events || events.length === 0) {
    return [];
  }

  // Filter to significant moments to produce high-value football commentary
  const sorted = [...events].sort((a, b) => a.clockSeconds - b.clockSeconds || a.sequence - b.sequence);

  // We ensure goals, shots, cards, high recoveries, and key progressive passes are highlighted
  const items: AiCommentaryItem[] = [];
  let lastCommentarySecond = -60;

  sorted.forEach((event, idx) => {
    const { category, agentName } = categorizeEvent(event.type);
    const isGoal = category === 'goal';
    const isShot = category === 'shot';
    const isCard = category === 'card';
    const isWhistle = category === 'whistle';
    const isHighRecovery = category === 'recovery' && ((event.pitchX ?? 0) > 55);
    const isSignificantPass = category === 'pass' && ((event.endX ?? 0) > 65 || (event.impactScore ?? 0) > 70);

    // Keep all key moments; sample regular passes/tackles every ~40-60 seconds to prevent spam
    const timeDelta = event.clockSeconds - lastCommentarySecond;
    const normalizedType = event.type.toLowerCase().replace(/[^a-z]/g, '');
    const isKeyMoment = isGoal || isShot || isCard || isWhistle || category === 'foul' ||
      ['penalty', 'substitution', 'ballrecovery', 'possessionlost'].includes(normalizedType);
    const shouldInclude = isKeyMoment || isHighRecovery || isSignificantPass || timeDelta >= 45;

    if (!shouldInclude && idx !== 0 && idx !== sorted.length - 1) {
      return;
    }

    lastCommentarySecond = event.clockSeconds;

    const agent = FOUNDRY_AGENTS.find(a => a.name === agentName) || FOUNDRY_AGENTS[0];
    const team = event.teamName;
    const player = event.playerName;
    const clockFormatted = clock(event.clockSeconds);
    const eventLabel = [event.type, player, team].filter(Boolean).join(' · ');

    items.push({
      id: `comm-${event.id}-${event.sequence}`,
      sequence: event.sequence,
      clockSeconds: event.clockSeconds,
      timeFormatted: clockFormatted,
      period: event.period ?? (event.clockSeconds >= 2700 ? 2 : 1),
      category,
      headline: event.headline || eventLabel,
      commentaryText: '',
      aiAgent: agent,
      teamName: team,
      playerName: player,
      impactScore: event.impactScore,
      isKeyMoment,
      citedEventId: event.id
    });
  });

  return items.sort((a, b) => a.clockSeconds - b.clockSeconds || a.sequence - b.sequence);
}

export function AiCommentaryTimeline({
  events,
  matchId,
  selectedSeconds = 0,
  homeTeam = 'Home Team',
  awayTeam = 'Away Team',
  loading = false,
  onTimeSelect
}: AiCommentaryTimelineProps) {
  const [agentReports, setAgentReports] = useState<Record<string, AgentReportState>>({});
  const [activeCardId, setActiveCardId] = useState<string | null>(null);
  const requestedItemIdsRef = useRef(new Set<string>());
  const queuedItemsRef = useRef<Array<{ matchId: string; item: AiCommentaryItem }>>([]);
  const processingQueueRef = useRef(false);
  const observedMatchRef = useRef<{ matchId?: string; initialized: boolean; lastSequence: number }>({ matchId, initialized: false, lastSequence: -1 });
  const componentActiveRef = useRef(true);

  const scrollContainerRef = useRef<HTMLDivElement>(null);
  const cardElementsRef = useRef<Map<string, HTMLElement>>(new Map());

  // Generate full commentary items from match events
  const allCommentaryItems = useMemo(() => {
    return generateAiCommentaryFromEvents(events);
  }, [events]);

  // Find the commentary item closest to current selectedSeconds
  const currentCommentaryItem = useMemo(() => {
    if (!allCommentaryItems.length) return null;
    const pastItems = allCommentaryItems.filter(item => item.clockSeconds <= selectedSeconds);
    return pastItems.length > 0 ? pastItems[pastItems.length - 1] : allCommentaryItems[0];
  }, [allCommentaryItems, selectedSeconds]);

  // Keep the view focused on events that have happened by the selected match time.
  const visibleItems = useMemo(
    () => allCommentaryItems.filter(item => item.clockSeconds <= selectedSeconds),
    [allCommentaryItems, selectedSeconds]
  );

  // Jump to specific timestamp and sync with UX timeline
  const handleSelectTime = useCallback((targetSeconds: number, cardId?: string) => {
    if (cardId) setActiveCardId(cardId);
    if (onTimeSelect) {
      onTimeSelect(targetSeconds);
    } else {
      const currentPath = typeof window === 'undefined' ? '/home' : window.location.pathname;
      const href = contextHref(currentPath, matchId, targetSeconds);
      window.history.replaceState(null, '', href);
      window.dispatchEvent(new Event('pitchmind:locationchange'));
    }
  }, [matchId, onTimeSelect]);

  const requestAgentCommentary = useCallback(async (requestMatchId: string, item: AiCommentaryItem) => {
    setAgentReports(previous => ({ ...previous, [item.id]: { status: 'generating' } }));
    try {
      const answer: CandidateAnalystAnswer = await askCandidateAnalyst({
        matchId: requestMatchId,
        selectedSourceClockValue: item.clockSeconds,
        audience: 'analyst',
        question: `Write concise match commentary for source event ${item.citedEventId} at sequence ${item.sequence}, source clock ${item.clockSeconds}. Use only the supplied records. Clearly distinguish observed facts from cautious interpretation. Cite the supporting event IDs. Do not infer intent, causation, or unobserved facts.`,
        agentName: item.aiAgent.name
      });
      if (componentActiveRef.current && observedMatchRef.current.matchId === requestMatchId) {
        setAgentReports(previous => ({
          ...previous,
          [item.id]: answer.status === 'answered'
            ? { status: 'ready', answer: answer.answer, evidenceIds: answer.claims.flatMap(claim => claim.evidenceIds) }
            : { status: 'ready', answer: answer.answer, evidenceIds: [] }
        }));
      }
    } catch {
      if (componentActiveRef.current && observedMatchRef.current.matchId === requestMatchId)
        setAgentReports(previous => ({ ...previous, [item.id]: { status: 'unavailable' } }));
    }
  }, []);

  const enqueueAgentCommentary = useCallback((items: AiCommentaryItem[]) => {
    for (const item of items) {
      if (requestedItemIdsRef.current.has(item.id)) continue;
      requestedItemIdsRef.current.add(item.id);
      if (matchId) queuedItemsRef.current.push({ matchId, item });
    }
    if (processingQueueRef.current) return;
    processingQueueRef.current = true;
    void (async () => {
      while (queuedItemsRef.current.length > 0 && componentActiveRef.current) {
        const queued = queuedItemsRef.current.shift();
        if (queued) await requestAgentCommentary(queued.matchId, queued.item);
      }
      processingQueueRef.current = false;
    })();
  }, [matchId, requestAgentCommentary]);

  useEffect(() => {
    componentActiveRef.current = true;
    return () => { componentActiveRef.current = false; };
  }, []);

  useEffect(() => {
    if (observedMatchRef.current.matchId === matchId) return;
    observedMatchRef.current = { matchId, initialized: false, lastSequence: -1 };
    requestedItemIdsRef.current.clear();
    queuedItemsRef.current = [];
    setAgentReports({});
  }, [matchId]);

  // On first load analyze only the latest few visible moments; then analyze new
  // timeline moments as the live event stream advances. Calls are serialized to
  // stay within the analyst API's rate limit.
  useEffect(() => {
    if (!matchId || visibleItems.length === 0) return;
    const observed = observedMatchRef.current;
    const highestSequence = Math.max(...visibleItems.map(item => item.sequence));
    if (!observed.initialized) {
      observed.initialized = true;
      observed.lastSequence = highestSequence;
      enqueueAgentCommentary(visibleItems.filter(item => item.isKeyMoment).slice(-3));
      return;
    }
    if (highestSequence <= observed.lastSequence) return;
    const newItems = visibleItems.filter(item => item.sequence > observed.lastSequence && item.isKeyMoment);
    observed.lastSequence = highestSequence;
    enqueueAgentCommentary(newItems);
  }, [matchId, visibleItems, enqueueAgentCommentary]);

  const retryAgentCommentary = (item: AiCommentaryItem) => {
    requestedItemIdsRef.current.delete(item.id);
    enqueueAgentCommentary([item]);
  };

  const generateAgentCommentary = (item: AiCommentaryItem) => enqueueAgentCommentary([item]);

  // Auto-scroll to active commentary card when selectedSeconds changes
  useEffect(() => {
    if (!currentCommentaryItem) return;

    const el = cardElementsRef.current.get(currentCommentaryItem.id);
    const container = scrollContainerRef.current;
    if (el && container) {
      const elTop = el.offsetTop - container.offsetTop;
      const targetScroll = Math.max(0, elTop - 120);
      if (typeof container.scrollTo === 'function') {
        container.scrollTo({ top: targetScroll, behavior: 'smooth' });
      } else {
        container.scrollTop = targetScroll;
      }
    }
  }, [currentCommentaryItem, selectedSeconds]);

  return (
    <section className="ai-commentary-timeline-container" aria-label="Match commentary timeline">
      <div className="commentary-header-bar">
        <div className="commentary-brand-group">
          <div className="commentary-eyebrow-row">
            <span className="synced-badge">
              <span className="synced-pulse-dot" aria-hidden="true"></span>
              AI agent commentary
            </span>
          </div>
          <h2 className="commentary-title">
            Match commentary
          </h2>
          <p className="commentary-subtitle">
            {homeTeam} vs {awayTeam} · {visibleItems.length} moments through {clock(selectedSeconds)}
          </p>
        </div>
      </div>

      {/* Main Synced Vertical Commentary Rail */}
      <div
        ref={scrollContainerRef}
        className="commentary-scroll-track-wrapper"
        tabIndex={0}
        aria-label="Commentary scroll list"
      >
        {loading ? (
          <div className="commentary-loading-state" role="status">
            <span className="commentary-spinner" aria-hidden="true"></span>
            <span>Synthesizing match events into AI tactical commentary…</span>
          </div>
        ) : visibleItems.length === 0 ? (
          <div className="commentary-empty-state">
            <span className="empty-symbol" aria-hidden="true">⏱️</span>
            <p>No match events are available through {clock(selectedSeconds)}.</p>
          </div>
        ) : (
          <div className="commentary-vertical-rail-container">
            {/* The vertical electrical connection rail */}
            <div className="commentary-vertical-axis-rail" aria-hidden="true"></div>

            {visibleItems.map(item => {
              const isCurrent = currentCommentaryItem?.id === item.id;
              const isPast = item.clockSeconds <= selectedSeconds;
              const report = agentReports[item.id];

              return (
                <article
                  key={item.id}
                  ref={el => {
                    if (el) cardElementsRef.current.set(item.id, el);
                    else cardElementsRef.current.delete(item.id);
                  }}
                  className={`commentary-node-card ${item.category} ${isCurrent || activeCardId === item.id ? 'is-synced-current' : ''} ${isPast ? 'is-past-moment' : 'is-future-moment'}`}
                  onClick={() => handleSelectTime(item.clockSeconds, item.id)}
                  tabIndex={0}
                  role="button"
                  aria-current={isCurrent ? 'time' : undefined}
                  aria-label={`${item.timeFormatted}: ${item.headline}`}
                  onKeyDown={e => {
                    if (e.key === 'Enter' || e.key === ' ') {
                      e.preventDefault();
                      handleSelectTime(item.clockSeconds, item.id);
                    }
                  }}
                >
                  {/* Pin Node on the vertical continuous rail */}
                  <div className="rail-pin-anchor" aria-hidden="true">
                    <span className={`rail-pin-dot ${isCurrent ? 'is-glowing' : ''}`}>
                      {item.category === 'goal' ? '⚽' : item.category === 'shot' ? '🎯' : item.category === 'recovery' ? '⚡' : item.category === 'card' ? '🟨' : '●'}
                    </span>
                  </div>

                  {/* Commentary Card Content Body */}
                  <div className="commentary-card-inner">
                    <div className="card-topline-row">
                      <div className="time-badge-group">
                        <span className="commentary-time-pill">
                          {item.timeFormatted}
                        </span>
                        <span className="commentary-period-tag">
                          {item.period === 1 ? '1st Half' : '2nd Half'}
                        </span>
                        {isCurrent && (
                          <span className="live-current-pill">
                            <span className="current-pulse-dot" aria-hidden="true"></span>
                            ACTIVE MATCH TIME
                          </span>
                        )}
                      </div>

                      {/* AI Agent Persona Badge */}
                      <div
                        className="commentary-agent-badge"
                        style={{ borderColor: item.aiAgent.badgeColor }}
                        title={`${item.aiAgent.name} (v${item.aiAgent.version}): ${item.aiAgent.specialty}`}
                      >
                        <span className="agent-icon" aria-hidden="true">{item.aiAgent.icon}</span>
                        <span className="agent-name" style={{ color: item.aiAgent.badgeColor }}>
                          {item.aiAgent.name}
                        </span>
                        <span className="agent-role-pill">AI Specialist</span>
                      </div>
                    </div>

                    {/* Headline and Team/Player Context */}
                    <h3 className="commentary-card-headline">
                      {item.headline}
                    </h3>

                    {/* Editorial play-by-play narrative */}
                    <p className="commentary-narrative-text" aria-live="polite">
                      {report?.status === 'ready' ? report.answer
                        : report?.status === 'generating' ? 'The selected specialist is analyzing the match evidence…'
                            : report?.status === 'unavailable' ? 'AI commentary is unavailable for this event right now.'
                            : 'Generate a source-grounded report with the selected specialist.'}
                    </p>

                    {report?.status === 'ready' && report.evidenceIds.length > 0 && (
                      <div className="live-ai-answer-callout" role="region" aria-label="AI Foundry response">
                        <div className="live-ai-header">
                          <span className="live-sparkle" aria-hidden="true">⚡</span>
                          <strong>Agent report · evidence references</strong>
                        </div>
                        <p className="live-ai-text">{[...new Set(report.evidenceIds)].join(' · ')}</p>
                      </div>
                    )}

                    {/* Selecting a moment seeks the shared match timeline. */}
                    <div className="commentary-card-footer">
                      {matchId && (!report || report.status === 'unavailable') && (
                        <button
                          type="button"
                          className="query-ai-analyst-btn"
                          onClick={e => {
                            e.stopPropagation();
                            if (report?.status === 'unavailable') retryAgentCommentary(item);
                            else generateAgentCommentary(item);
                          }}
                          title="Generate a match evidence report with this specialist agent"
                        >
                          <span>{report?.status === 'unavailable' ? 'Retry agent commentary' : 'Generate agent commentary'}</span>
                        </button>
                      )}

                      {item.impactScore !== undefined && (
                        <span className="impact-score-badge">
                          Impact Rating: <strong>{item.impactScore}</strong>/100
                        </span>
                      )}
                    </div>
                  </div>
                </article>
              );
            })}
          </div>
        )}
      </div>
    </section>
  );
}
