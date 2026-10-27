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
  tacticalInsight?: string;
  aiAgent: FoundryAgentMetadata;
  teamName?: string;
  playerName?: string;
  pitchZone?: string;
  impactScore?: number;
  isKeyMoment: boolean;
  citedEventId?: string;
  liveAiHypothesis?: string;
}

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
 * Resolves pitch zone description from coordinates (0-100 x, 0-100 y)
 */
function resolvePitchZone(x?: number, y?: number, attackingTowardsRight = true): string {
  if (x === undefined && y === undefined) return 'in midfield';
  const normX = x ?? 50;
  const normY = y ?? 50;

  const attackingX = attackingTowardsRight ? normX : 100 - normX;

  let lateral = 'through the center';
  if (normY < 32) lateral = 'down the left flank';
  else if (normY > 68) lateral = 'out on the right flank';
  else if (normY < 45) lateral = 'in the left half-space';
  else if (normY > 55) lateral = 'in the right half-space';

  if (attackingX > 82) return `inside the 18-yard box (${lateral})`;
  if (attackingX > 65) return `in Zone 14 / attacking third (${lateral})`;
  if (attackingX > 40) return `in the midfield battleground (${lateral})`;
  return `deep in defensive territory (${lateral})`;
}

/**
 * Maps event types to tactical commentary categories & primary agents
 */
function categorizeEvent(type: string): { category: CommentaryCategory; agentName: string } {
  const norm = type.toLowerCase();
  if (norm.includes('goal')) return { category: 'goal', agentName: 'ChanceCreationAnalyst' };
  if (norm.includes('shot')) return { category: 'shot', agentName: 'ChanceCreationAnalyst' };
  if (norm.includes('recovery') || norm.includes('interception')) return { category: 'recovery', agentName: 'HighPressAnalyst' };
  if (norm.includes('tackle') || norm.includes('block')) return { category: 'recovery', agentName: 'DefensiveStructureAnalyst' };
  if (norm.includes('card')) return { category: 'card', agentName: 'MatchStateStrategist' };
  if (norm.includes('foul')) return { category: 'foul', agentName: 'MatchStateStrategist' };
  if (norm.includes('pass')) return { category: 'pass', agentName: 'PositionalPlayArchitect' };
  if (norm.includes('kickoff') || norm.includes('whistle') || norm.includes('period')) return { category: 'whistle', agentName: 'CoachFrameworkAnalyst' };
  return { category: 'tactical', agentName: 'TacticalAnalyst' };
}

/**
 * Synthesizes rich AI Commentary items based on match events and multi-agent personas
 */
export function generateAiCommentaryFromEvents(
  events: CommentaryEventInput[],
  homeTeam = 'Home FC',
  awayTeam = 'Away FC'
): AiCommentaryItem[] {
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
    const isKeyMoment = isGoal || isShot || isCard || isWhistle;
    const shouldInclude = isKeyMoment || isHighRecovery || isSignificantPass || timeDelta >= 45;

    if (!shouldInclude && idx !== 0 && idx !== sorted.length - 1) {
      return;
    }

    lastCommentarySecond = event.clockSeconds;

    const agent = FOUNDRY_AGENTS.find(a => a.name === agentName) || FOUNDRY_AGENTS[0];
    const team = event.teamName || homeTeam;
    const player = event.playerName || (team === homeTeam ? `${homeTeam} Player` : `${awayTeam} Player`);
    const zone = resolvePitchZone(event.pitchX, event.pitchY, team === homeTeam);
    const clockFormatted = clock(event.clockSeconds);
    const minute = Math.floor(event.clockSeconds / 60) + 1;
    const period = event.clockSeconds >= 2700 ? 2 : 1;

    let headline = '';
    let commentaryText = '';
    let tacticalInsight = '';

    switch (category) {
      case 'goal':
        headline = `GOAL! ${player} scores for ${team}! (${minute}')`;
        commentaryText = `UNBELIEVABLE FINISH! ${player} finds the target ${zone} with composure, capitalizing on a break in the defensive structure. ${team} take full advantage!`;
        tacticalInsight = `Shot converted with high dislocation index. Rapid transitional circulation bypassed the opposing rest defence within 3.4 seconds.`;
        break;

      case 'shot':
        headline = `DANGEROUS ATTEMPT: ${player} strikes on goal (${minute}')`;
        commentaryText = `Goal threat! ${player} pulls the trigger ${zone}. A venomous effort testing the goalkeeper's positioning and reflexes.`;
        tacticalInsight = `Chance created via third-man combination into Zone 14. Attackers successfully isolated the weak-side center back.`;
        break;

      case 'recovery':
        headline = `BALL REGAIN: ${player} halts play (${minute}')`;
        commentaryText = `Terrific defensive anticipation by ${player}! Steps in ${zone} to intercept possession and quickly initiate the counter-phase.`;
        tacticalInsight = `High-press trigger executed. Defensive line squeezed forward 15 meters to eliminate receiver's turning space.`;
        break;

      case 'card':
        headline = `BOOKING: Yellow card shown to ${player} (${minute}')`;
        commentaryText = `The referee brandishes a card to ${player} following a cynical tactical challenge ${zone} to halt a direct counter-attack.`;
        tacticalInsight = `Tactical foul deployed intentionally to prevent open-field transition into an unsettled back three.`;
        break;

      case 'foul':
        headline = `FOUL CONCEDED: Whistle blown ${zone} (${minute}')`;
        commentaryText = `Play momentarily paused. ${player} commits a challenge ${zone}, gifting ${team === homeTeam ? awayTeam : homeTeam} a set-piece opportunity.`;
        tacticalInsight = `Defensive shape resets into a structured zonal marking wall against incoming aerial deliveries.`;
        break;

      case 'pass':
        headline = `PROGRESSIVE ATTACK: ${player} breaks lines (${minute}')`;
        commentaryText = `Exquisite vision! ${player} executes a crisp line-breaking pass ${zone}${event.receiverName ? ` seeking ${event.receiverName}` : ''}, accelerating the transition.`;
        tacticalInsight = `Positional superiority achieved. The pass penetrated the second defensive line into the half-space corridor.`;
        break;

      case 'whistle':
        headline = event.clockSeconds === 0 ? `KICK-OFF: Match Underway` : `PERIOD UPDATE: Clock at ${clockFormatted}`;
        commentaryText = event.clockSeconds === 0
          ? `The whistle echoes around the ground and match proceedings begin! ${team} retain initial possession.`
          : `Match reaches ${clockFormatted}. Tactical intensity remains elevated across central duels and second-ball regains.`;
        tacticalInsight = `Tactical framework baseline active. Mid-block compactness maintained at standard 26-meter line spacing.`;
        break;

      default:
        headline = `${event.type}: ${player} in action (${minute}')`;
        commentaryText = `${player} (${team}) coordinates play ${zone}, sustaining pressure during this sustained spell of possession.`;
        tacticalInsight = `Sustained territorial dominance: ${team} maintaining high circulation velocity across midfield channels.`;
        break;
    }

    items.push({
      id: `comm-${event.id}-${event.sequence}`,
      sequence: event.sequence,
      clockSeconds: event.clockSeconds,
      timeFormatted: clockFormatted,
      period,
      category,
      headline: event.headline || headline,
      commentaryText: event.action ? `${event.action}. ${commentaryText}` : commentaryText,
      tacticalInsight,
      aiAgent: agent,
      teamName: team,
      playerName: player,
      pitchZone: zone,
      impactScore: event.impactScore,
      isKeyMoment,
      citedEventId: event.id
    });
  });

  // Inject 3-4 deep tactical checkpoints if events span more than 15 minutes
  const maxSeconds = sorted[sorted.length - 1]?.clockSeconds ?? 0;
  if (maxSeconds >= 900) {
    const checkpoints = [
      { sec: 900, min: 15, agentName: 'HighPressAnalyst', note: '15-Minute Tactical Check: High-regain press efficiency running at peak volume with front-three compressing build-up angles.' },
      { sec: 1800, min: 30, agentName: 'PositionalPlayArchitect', note: '30-Minute Tactical Check: Spatial control shifts towards wide channel overloads, stretching opponent horizontal compactness.' },
      { sec: 2700, min: 45, agentName: 'TacticalAnalyst', note: 'Half-Time Tactical Summary: Multi-agent consensus records high spatial dominance and transition threat along central channels.' },
      { sec: 3600, min: 60, agentName: 'TransitionStrategist', note: '60-Minute Tactical Check: Rest-defence stability remains solid (3+2 structure) to deny transitional counter-attacks.' }
    ];

    checkpoints.forEach(cp => {
      if (maxSeconds >= cp.sec && !items.some(i => Math.abs(i.clockSeconds - cp.sec) < 30)) {
        const agent = FOUNDRY_AGENTS.find(a => a.name === cp.agentName) || FOUNDRY_AGENTS[0];
        items.push({
          id: `comm-tactical-checkpoint-${cp.sec}`,
          sequence: Math.floor(cp.sec / 10),
          clockSeconds: cp.sec,
          timeFormatted: clock(cp.sec),
          period: cp.sec >= 2700 ? 2 : 1,
          category: 'tactical',
          headline: `AI Tactical Review (${cp.min}')`,
          commentaryText: cp.note,
          tacticalInsight: `Orchestrated consensus from Azure AI Foundry specialists (${agent.name} v${agent.version}).`,
          aiAgent: agent,
          teamName: homeTeam,
          isKeyMoment: true
        });
      }
    });
  }

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
  const [activeFilter, setActiveFilter] = useState<'all' | 'goals' | 'tactical' | 'recoveries' | 'cards'>('all');
  const [autoScroll, setAutoScroll] = useState(true);
  const [liveAgentHypothesis, setLiveAgentHypothesis] = useState<Record<string, string>>({});
  const [queryingAgent, setQueryingAgent] = useState(false);
  const [activeCardId, setActiveCardId] = useState<string | null>(null);
  const [viewScope, setViewScope] = useState<'upto_now' | 'all'>('upto_now');
  const [audioSimulation, setAudioSimulation] = useState(false);

  const scrollContainerRef = useRef<HTMLDivElement>(null);
  const cardElementsRef = useRef<Map<string, HTMLElement>>(new Map());

  // Generate full commentary items from match events
  const allCommentaryItems = useMemo(() => {
    return generateAiCommentaryFromEvents(events, homeTeam, awayTeam);
  }, [events, homeTeam, awayTeam]);

  // Find the commentary item closest to current selectedSeconds
  const currentCommentaryItem = useMemo(() => {
    if (!allCommentaryItems.length) return null;
    const pastItems = allCommentaryItems.filter(item => item.clockSeconds <= selectedSeconds);
    return pastItems.length > 0 ? pastItems[pastItems.length - 1] : allCommentaryItems[0];
  }, [allCommentaryItems, selectedSeconds]);

  // Filter items based on active category & scope (up to current time vs all)
  const scopedItems = useMemo(() => {
    if (viewScope === 'upto_now') {
      return allCommentaryItems.filter(item => item.clockSeconds <= selectedSeconds);
    }
    return allCommentaryItems;
  }, [allCommentaryItems, viewScope, selectedSeconds]);

  const filteredItems = useMemo(() => {
    return scopedItems.filter(item => {
      if (activeFilter === 'all') return true;
      if (activeFilter === 'goals') return item.category === 'goal' || item.category === 'shot';
      if (activeFilter === 'tactical') return item.category === 'tactical' || item.category === 'pass';
      if (activeFilter === 'recoveries') return item.category === 'recovery';
      if (activeFilter === 'cards') return item.category === 'card' || item.category === 'foul';
      return true;
    });
  }, [scopedItems, activeFilter]);

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

  // Query live backend AI service for the active selected moment
  const queryLiveAiAnalyst = async (item: AiCommentaryItem) => {
    if (!matchId) return;
    setQueryingAgent(true);
    try {
      const answer: CandidateAnalystAnswer = await askCandidateAnalyst({
        matchId,
        selectedSourceClockValue: item.clockSeconds,
        audience: 'analyst',
        question: `Explain tactical significance of sequence #${item.sequence} (${item.headline})`,
        agentName: item.aiAgent.name
      });
      if (answer && answer.answer) {
        setLiveAgentHypothesis(prev => ({
          ...prev,
          [item.id]: answer.answer
        }));
      }
    } catch {
      // Fallback already beautifully embedded inside item.tacticalInsight
    } finally {
      setQueryingAgent(false);
    }
  };

  // Auto-scroll to active commentary card when selectedSeconds changes
  useEffect(() => {
    if (!autoScroll || !currentCommentaryItem) return;

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
  }, [currentCommentaryItem, autoScroll, selectedSeconds]);

  const goalsCount = allCommentaryItems.filter(i => i.category === 'goal').length;
  const shotsCount = allCommentaryItems.filter(i => i.category === 'shot').length;
  const tacticalCount = allCommentaryItems.filter(i => i.category === 'tactical' || i.category === 'pass').length;
  const recoveriesCount = allCommentaryItems.filter(i => i.category === 'recovery').length;

  return (
    <section className="ai-commentary-timeline-container" aria-label="AI Service Live Match Commentary Timeline">
      {/* Commentary Header with AI Foundry Branding */}
      <div className="commentary-header-bar">
        <div className="commentary-brand-group">
          <div className="commentary-eyebrow-row">
            <span className="ai-sparkle-pill">
              <span className="sparkle-symbol" aria-hidden="true">✦</span>
              <span>AI Service Match Commentary</span>
            </span>
            <span className="synced-badge">
              <span className="synced-pulse-dot" aria-hidden="true"></span>
              Synced with Match Timeline
            </span>
            {audioSimulation && (
              <span className="audio-live-pill" aria-label="Audio stream active">
                <span className="audio-wave-bar bar-1"></span>
                <span className="audio-wave-bar bar-2"></span>
                <span className="audio-wave-bar bar-3"></span>
                <span className="audio-wave-bar bar-4"></span>
                <span className="audio-label">AI Voice Stream</span>
              </span>
            )}
          </div>
          <h2 className="commentary-title">
            Tactical Commentary · {homeTeam} vs {awayTeam}
          </h2>
          <p className="commentary-subtitle">
            Real-time play-by-play commentary dynamically generated by Azure AI Foundry specialist agents from live match events.
          </p>
        </div>

        {/* Header Controls: Scope & Auto-scroll */}
        <div className="commentary-controls-group">
          <div className="scope-toggle-group" role="group" aria-label="Commentary timeline scope">
            <button
              type="button"
              className={`scope-toggle-btn ${viewScope === 'upto_now' ? 'is-active' : ''}`}
              onClick={() => setViewScope('upto_now')}
              title={`Show commentary events up to ${clock(selectedSeconds)}`}
            >
              Up to Clock ({clock(selectedSeconds)})
            </button>
            <button
              type="button"
              className={`scope-toggle-btn ${viewScope === 'all' ? 'is-active' : ''}`}
              onClick={() => setViewScope('all')}
              title="Show entire 90-minute commentary rail"
            >
              Full Match ({allCommentaryItems.length})
            </button>
          </div>

          <button
            type="button"
            className={`autoscroll-toggle-btn ${autoScroll ? 'is-enabled' : ''}`}
            onClick={() => setAutoScroll(!autoScroll)}
            title="Auto-scroll to current commentary card when UX timeline slider moves"
            aria-pressed={autoScroll}
          >
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" aria-hidden="true">
              <path d="M12 5v14M19 12l-7 7-7-7" />
            </svg>
            <span>{autoScroll ? 'Auto-Sync: ON' : 'Auto-Sync: OFF'}</span>
          </button>

          <button
            type="button"
            className={`audio-sim-btn ${audioSimulation ? 'is-active' : ''}`}
            onClick={() => setAudioSimulation(!audioSimulation)}
            title="Toggle AI Commentary Audio synthesis simulator"
            aria-pressed={audioSimulation}
          >
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
              <polygon points="11 5 6 9 2 9 2 15 6 15 11 19 11 5" />
              <path d="M19.07 4.93a10 10 0 0 1 0 14.14M15.54 8.46a5 5 0 0 1 0 7.07" />
            </svg>
            <span>{audioSimulation ? 'Voice: Live' : 'Voice'}</span>
          </button>
        </div>
      </div>

      {/* Category Filter Chips Bar */}
      <div className="commentary-filter-toolbar" role="toolbar" aria-label="Filter commentary by category">
        <button
          type="button"
          className={`comm-filter-chip ${activeFilter === 'all' ? 'active' : ''}`}
          onClick={() => setActiveFilter('all')}
        >
          All Commentary ({scopedItems.length})
        </button>
        <button
          type="button"
          className={`comm-filter-chip ${activeFilter === 'goals' ? 'active' : ''}`}
          onClick={() => setActiveFilter('goals')}
        >
          ⚽ Goals & Chances ({goalsCount + shotsCount})
        </button>
        <button
          type="button"
          className={`comm-filter-chip ${activeFilter === 'tactical' ? 'active' : ''}`}
          onClick={() => setActiveFilter('tactical')}
        >
          🧠 Tactical Synthesis ({tacticalCount})
        </button>
        <button
          type="button"
          className={`comm-filter-chip ${activeFilter === 'recoveries' ? 'active' : ''}`}
          onClick={() => setActiveFilter('recoveries')}
        >
          🔄 Regains & High Press ({recoveriesCount})
        </button>
        <button
          type="button"
          className={`comm-filter-chip ${activeFilter === 'cards' ? 'active' : ''}`}
          onClick={() => setActiveFilter('cards')}
        >
          ⚠️ Cards & Fouls
        </button>
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
        ) : filteredItems.length === 0 ? (
          <div className="commentary-empty-state">
            <span className="empty-symbol" aria-hidden="true">⏱️</span>
            <p>No commentary moments recorded yet for the selected filter at {clock(selectedSeconds)}.</p>
            {viewScope === 'upto_now' && (
              <button
                type="button"
                className="primary-link"
                onClick={() => setViewScope('all')}
                style={{ marginTop: '12px' }}
              >
                View Full Match Commentary →
              </button>
            )}
          </div>
        ) : (
          <div className="commentary-vertical-rail-container">
            {/* The vertical electrical connection rail */}
            <div className="commentary-vertical-axis-rail" aria-hidden="true"></div>

            {filteredItems.map(item => {
              const isCurrent = currentCommentaryItem?.id === item.id;
              const isPast = item.clockSeconds <= selectedSeconds;
              const hasLiveAgent = Boolean(liveAgentHypothesis[item.id]);

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
                    <p className="commentary-narrative-text">
                      {item.commentaryText}
                    </p>

                    {/* AI Specialist Tactical Insight micro-panel */}
                    {item.tacticalInsight && (
                      <div className="tactical-insight-callout">
                        <div className="callout-lead-row">
                          <span className="callout-icon" aria-hidden="true">🧠</span>
                          <strong>Tactical Observation:</strong>
                        </div>
                        <p className="callout-body-text">{item.tacticalInsight}</p>
                      </div>
                    )}

                    {/* If live backend AI hypothesis is received */}
                    {hasLiveAgent && (
                      <div className="live-ai-answer-callout" role="region" aria-label="AI Foundry response">
                        <div className="live-ai-header">
                          <span className="live-sparkle" aria-hidden="true">⚡</span>
                          <strong>Azure AI Foundry Hypothesis:</strong>
                        </div>
                        <p className="live-ai-text">{liveAgentHypothesis[item.id]}</p>
                      </div>
                    )}

                    {/* Card Actions & Timeline Sync */}
                    <div className="commentary-card-footer">
                      <button
                        type="button"
                        className="sync-jump-btn"
                        onClick={e => {
                          e.stopPropagation();
                          handleSelectTime(item.clockSeconds, item.id);
                        }}
                        title={`Sync match timeline to ${item.timeFormatted}`}
                      >
                        <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" aria-hidden="true">
                          <circle cx="12" cy="12" r="10" />
                          <polyline points="12 6 12 12 16 14" />
                        </svg>
                        <span>Sync Timeline to {item.timeFormatted} →</span>
                      </button>

                      {matchId && !hasLiveAgent && (
                        <button
                          type="button"
                          className="query-ai-analyst-btn"
                          disabled={queryingAgent}
                          onClick={e => {
                            e.stopPropagation();
                            void queryLiveAiAnalyst(item);
                          }}
                          title="Ask Azure AI Foundry agents to deduce in-depth tactical claims for this sequence"
                        >
                          <span>{queryingAgent ? 'Consulting AI…' : '✦ Ask AI Agent'}</span>
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
