import { useState, useRef, useEffect, useCallback, useMemo } from 'react';
import type { ReactElement } from 'react';
import { clock } from './api';
import { contextHref, handleLinkClick } from './routes';

export interface LinearTimelineEvent {
  id: string;
  type: string;
  sequence: number;
  clockSeconds: number;
  teamId?: string;
  teamName?: string;
  playerId?: string;
  playerName?: string;
  description?: string;
}

export interface LinearEventsTimelineProps {
  events: LinearTimelineEvent[];
  matchId?: string;
  matchTitle?: string;
  selectedSeconds?: number;
  selectedEventId?: string;
  onSelectEvent?: (event: LinearTimelineEvent) => void;
  loading?: boolean;
  vertical?: boolean;
}

export function getEventIcon(type: string): { symbol: string; label: string; themeClass: string; svg: ReactElement } {
  const normalized = type.replace(/[^a-zA-Z]/g, '').toLowerCase();

  if (normalized.includes('goal')) {
    return {
      symbol: '⚽',
      label: 'Goal',
      themeClass: 'icon-goal',
      svg: (
        <svg viewBox="0 0 24 24" width="20" height="20" fill="currentColor" aria-hidden="true">
          <circle cx="12" cy="12" r="10" fill="none" stroke="currentColor" strokeWidth="2" />
          <polygon points="12,7 16,10 14.5,15 9.5,15 8,10" fill="currentColor" />
          <line x1="12" y1="7" x2="12" y2="2" stroke="currentColor" strokeWidth="1.5" />
          <line x1="16" y1="10" x2="21" y2="9" stroke="currentColor" strokeWidth="1.5" />
          <line x1="14.5" y1="15" x2="18" y2="20" stroke="currentColor" strokeWidth="1.5" />
          <line x1="9.5" y1="15" x2="6" y2="20" stroke="currentColor" strokeWidth="1.5" />
          <line x1="8" y1="10" x2="3" y2="9" stroke="currentColor" strokeWidth="1.5" />
        </svg>
      )
    };
  }

  if (normalized.includes('shot')) {
    return {
      symbol: '🎯',
      label: 'Shot',
      themeClass: 'icon-shot',
      svg: (
        <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
          <circle cx="12" cy="12" r="10" />
          <circle cx="12" cy="12" r="6" />
          <circle cx="12" cy="12" r="2" fill="currentColor" />
        </svg>
      )
    };
  }

  if (normalized.includes('pass')) {
    return {
      symbol: '↗️',
      label: 'Pass',
      themeClass: 'icon-pass',
      svg: (
        <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <line x1="7" y1="17" x2="17" y2="7" />
          <polyline points="7 7 17 7 17 17" />
        </svg>
      )
    };
  }

  if (normalized.includes('tackle') || normalized.includes('challenge') || normalized.includes('interception')) {
    return {
      symbol: '🛑',
      label: 'Tackle / Interception',
      themeClass: 'icon-tackle',
      svg: (
        <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />
          <line x1="9" y1="12" x2="15" y2="12" />
        </svg>
      )
    };
  }

  if (normalized.includes('yellowcard')) {
    return {
      symbol: '🟨',
      label: 'Yellow Card',
      themeClass: 'icon-yellow-card',
      svg: (
        <svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true">
          <rect x="5" y="3" width="14" height="18" rx="2" fill="#eab308" stroke="#ca8a04" strokeWidth="1" />
        </svg>
      )
    };
  }

  if (normalized.includes('redcard')) {
    return {
      symbol: '🟥',
      label: 'Red Card',
      themeClass: 'icon-red-card',
      svg: (
        <svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true">
          <rect x="5" y="3" width="14" height="18" rx="2" fill="#ef4444" stroke="#dc2626" strokeWidth="1" />
        </svg>
      )
    };
  }

  if (normalized.includes('foul')) {
    return {
      symbol: '⚠️',
      label: 'Foul',
      themeClass: 'icon-foul',
      svg: (
        <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />
          <line x1="12" y1="9" x2="12" y2="13" />
          <line x1="12" y1="17" x2="12.01" y2="17" />
        </svg>
      )
    };
  }

  if (normalized.includes('ballrecovery') || normalized.includes('recovery')) {
    return {
      symbol: '🔄',
      label: 'Ball Recovery',
      themeClass: 'icon-recovery',
      svg: (
        <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <polyline points="23 4 23 10 17 10" />
          <path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10" />
        </svg>
      )
    };
  }

  if (normalized.includes('possessionlost') || normalized.includes('turnover')) {
    return {
      symbol: '📉',
      label: 'Possession Lost',
      themeClass: 'icon-turnover',
      svg: (
        <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <polyline points="23 18 13.5 8.5 8.5 13.5 1 6" />
          <polyline points="17 18 23 18 23 12" />
        </svg>
      )
    };
  }

  if (normalized.includes('substitution')) {
    return {
      symbol: '🔁',
      label: 'Substitution',
      themeClass: 'icon-substitution',
      svg: (
        <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <polyline points="17 1 21 5 17 9" />
          <path d="M3 11V9a4 4 0 0 1 4-4h14" />
          <polyline points="7 23 3 19 7 15" />
          <path d="M21 13v2a4 4 0 0 1-4 4H3" />
        </svg>
      )
    };
  }

  if (normalized.includes('kickoff') || normalized.includes('halftime') || normalized.includes('fulltime')) {
    return {
      symbol: '⏱️',
      label: 'Period / Whistle',
      themeClass: 'icon-period',
      svg: (
        <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <circle cx="12" cy="12" r="10" />
          <polyline points="12 6 12 12 16 14" />
        </svg>
      )
    };
  }

  if (normalized.includes('corner') || normalized.includes('setpiece')) {
    return {
      symbol: '🚩',
      label: 'Set Piece',
      themeClass: 'icon-setpiece',
      svg: (
        <svg viewBox="0 0 24 24" width="20" height="20" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
          <path d="M4 15s1-1 4-1 5 2 8 2 4-1 4-1V3s-1 1-4 1-5-2-8-2-4 1-4 1z" />
          <line x1="4" y1="22" x2="4" y2="15" />
        </svg>
      )
    };
  }

  return {
    symbol: '⚡',
    label: type,
    themeClass: 'icon-default',
    svg: (
      <svg viewBox="0 0 24 24" width="20" height="20" fill="currentColor" aria-hidden="true">
        <polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2" />
      </svg>
    )
  };
}

export function LinearEventsTimeline({
  events,
  matchId,
  matchTitle,
  selectedSeconds,
  selectedEventId,
  onSelectEvent,
  loading = false,
  vertical = false
}: LinearEventsTimelineProps) {
  const [filterType, setFilterType] = useState<string>('all');
  const [clickedEventId, setClickedEventId] = useState<string | null>(null);
  const scrollContainerRef = useRef<HTMLDivElement>(null);
  const cardRefs = useRef<Map<string, HTMLElement>>(new Map());

  // When vertical (e.g. on Match screen), show events that have happened till now
  const eventsTillNow = vertical && selectedSeconds !== undefined
    ? events.filter(e => e.clockSeconds <= selectedSeconds)
    : events;

  const sortedEvents = [...eventsTillNow].sort((a, b) => a.clockSeconds - b.clockSeconds || a.sequence - b.sequence);

  const filteredEvents = sortedEvents.filter(event => {
    if (filterType === 'all') return true;
    const norm = event.type.toLowerCase();
    if (filterType === 'goals') return norm.includes('goal');
    if (filterType === 'shots') return norm.includes('shot');
    if (filterType === 'passes') return norm.includes('pass');
    if (filterType === 'tackles') return norm.includes('tackle') || norm.includes('interception');
    if (filterType === 'recoveries') return norm.includes('recovery');
    if (filterType === 'fouls') return norm.includes('foul') || norm.includes('card');
    return true;
  });

  const goalsCount = sortedEvents.filter(e => e.type.toLowerCase().includes('goal')).length;
  const shotsCount = sortedEvents.filter(e => e.type.toLowerCase().includes('shot')).length;
  const recoveriesCount = sortedEvents.filter(e => e.type.toLowerCase().includes('recovery')).length;

  // Compute the current relevant event:
  // 1. If selectedEventId prop is provided, look for it.
  // 2. If user clicked a card and its clockSeconds matches selectedSeconds, honor clicked card.
  // 3. If selectedSeconds is provided, find latest event at or before selectedSeconds.
  //    If selectedSeconds is earlier than all events, pick the first event.
  // 4. Otherwise, pick the latest event in the timeline.
  const currentRelevantEvent = useMemo(() => {
    if (filteredEvents.length === 0) return undefined;

    if (selectedEventId) {
      const match = filteredEvents.find(e => e.id === selectedEventId);
      if (match) return match;
    }

    if (clickedEventId) {
      const clicked = filteredEvents.find(e => e.id === clickedEventId);
      if (clicked && (selectedSeconds === undefined || clicked.clockSeconds === selectedSeconds)) {
        return clicked;
      }
    }

    if (selectedSeconds !== undefined) {
      let candidate: LinearTimelineEvent | undefined;
      for (const ev of filteredEvents) {
        if (ev.clockSeconds <= selectedSeconds) {
          candidate = ev;
        } else {
          break;
        }
      }
      if (candidate) return candidate;
      return filteredEvents[0];
    }

    return filteredEvents[filteredEvents.length - 1];
  }, [filteredEvents, selectedEventId, clickedEventId, selectedSeconds]);

  const scrollToEventCard = useCallback((eventId: string, customBehavior?: ScrollBehavior) => {
    const container = scrollContainerRef.current;
    const card = cardRefs.current.get(eventId);
    if (!container || !card) return;

    const prefersReducedMotion = typeof window !== 'undefined' && window.matchMedia?.('(prefers-reduced-motion: reduce)')?.matches;
    const behavior: ScrollBehavior = customBehavior ?? (prefersReducedMotion ? 'auto' : 'smooth');

    const containerRect = container.getBoundingClientRect();
    const cardRect = card.getBoundingClientRect();

    if (vertical) {
      const cardHeight = cardRect.height || card.offsetHeight || 120;
      const cardTop = cardRect.height > 0
        ? (container.scrollTop + (cardRect.top - containerRect.top))
        : card.offsetTop;
      const containerHeight = container.clientHeight || 400;
      const targetScrollTop = cardTop - (containerHeight / 2) + (cardHeight / 2);

      if (typeof container.scrollTo === 'function') {
        container.scrollTo({ top: Math.max(0, targetScrollTop), behavior });
      } else {
        container.scrollTop = Math.max(0, targetScrollTop);
      }
    } else {
      const cardWidth = cardRect.width || card.offsetWidth || 220;
      const cardLeft = cardRect.width > 0
        ? (container.scrollLeft + (cardRect.left - containerRect.left))
        : card.offsetLeft;
      const containerWidth = container.clientWidth || 800;
      const targetScrollLeft = cardLeft - (containerWidth / 2) + (cardWidth / 2);

      if (typeof container.scrollTo === 'function') {
        container.scrollTo({ left: Math.max(0, targetScrollLeft), behavior });
      } else {
        container.scrollLeft = Math.max(0, targetScrollLeft);
      }
    }
  }, [vertical]);

  const activeEventId = currentRelevantEvent?.id;

  // Always scroll to the current relevant event card when it changes,
  // when filter changes, when loading completes, or when new events arrive.
  useEffect(() => {
    if (!activeEventId) return;

    scrollToEventCard(activeEventId);
  }, [activeEventId, filterType, loading, filteredEvents.length, scrollToEventCard]);

  const handleSelectEvent = (event: LinearTimelineEvent) => {
    setClickedEventId(event.id);
    scrollToEventCard(event.id);
    onSelectEvent?.(event);
  };

  return (
    <section className={`home-events-timeline-section ${vertical ? 'is-vertical-timeline' : ''}`} aria-label="Linear match events timeline">
      <div className="timeline-header-row">
        <div className="timeline-title-group">
          <div className="timeline-eyebrow-row">
            <span className="timeline-pulse-dot" aria-hidden="true"></span>
            <span className="timeline-eyebrow">Linear Match Events Timeline</span>
          </div>
          <h2 className="timeline-main-title">
            {matchTitle || (matchId ? `Match #${matchId.slice(0, 8)}` : 'Live Progression')}
          </h2>
          <p className="timeline-subtitle">
            {vertical ? 'Chronological sequence of match events till now.' : 'Chronological match moments with differentiated event icons and player attributions.'}
          </p>
        </div>

        <div className="timeline-filter-chips" role="toolbar" aria-label="Filter events by type">
          <button
            type="button"
            className={`timeline-filter-chip ${filterType === 'all' ? 'active' : ''}`}
            onClick={() => setFilterType('all')}
          >
            All ({sortedEvents.length})
          </button>
          <button
            type="button"
            className={`timeline-filter-chip ${filterType === 'goals' ? 'active' : ''}`}
            onClick={() => setFilterType('goals')}
          >
            ⚽ Goals ({goalsCount})
          </button>
          <button
            type="button"
            className={`timeline-filter-chip ${filterType === 'shots' ? 'active' : ''}`}
            onClick={() => setFilterType('shots')}
          >
            🎯 Shots ({shotsCount})
          </button>
          <button
            type="button"
            className={`timeline-filter-chip ${filterType === 'recoveries' ? 'active' : ''}`}
            onClick={() => setFilterType('recoveries')}
          >
            🔄 Recoveries ({recoveriesCount})
          </button>
          <button
            type="button"
            className={`timeline-filter-chip ${filterType === 'passes' ? 'active' : ''}`}
            onClick={() => setFilterType('passes')}
          >
            ↗️ Passes
          </button>
          <button
            type="button"
            className={`timeline-filter-chip ${filterType === 'tackles' ? 'active' : ''}`}
            onClick={() => setFilterType('tackles')}
          >
            🛑 Tackles
          </button>
          <button
            type="button"
            className={`timeline-filter-chip ${filterType === 'fouls' ? 'active' : ''}`}
            onClick={() => setFilterType('fouls')}
          >
            ⚠️ Fouls
          </button>
        </div>
      </div>

      {loading ? (
        <div className="timeline-loading-state" role="status">
          <span className="timeline-spinner" aria-hidden="true"></span>
          <span>Loading chronological event timeline…</span>
        </div>
      ) : filteredEvents.length === 0 ? (
        <div className="timeline-empty-card">
          <span className="empty-icon">⏱️</span>
          <p className="empty-text">No match events match the active filter criteria.</p>
        </div>
      ) : (
        <div
          ref={scrollContainerRef}
          className={`linear-timeline-scroll-wrap ${vertical ? 'vertical-scroll-wrap' : ''}`}
          tabIndex={0}
          aria-label="Chronological event scroll container"
        >
          <div className={`linear-events-track ${vertical ? 'vertical-events-track' : ''}`}>
            {/* The continuous connecting rail across all linear events */}
            <div className={`linear-timeline-rail ${vertical ? 'vertical-timeline-rail' : ''}`} aria-hidden="true"></div>

            {filteredEvents.map((event, index) => {
              const iconData = getEventIcon(event.type);
              const isSelected = currentRelevantEvent ? event.id === currentRelevantEvent.id : false;
              const matchPath = matchId ? `/matches/${encodeURIComponent(matchId)}` : '/home';
              const targetHref = contextHref(matchPath, matchId, event.clockSeconds);

              return (
                <article
                  key={`${event.id}-${event.sequence}-${index}`}
                  ref={el => {
                    if (el) {
                      cardRefs.current.set(event.id, el);
                    } else {
                      cardRefs.current.delete(event.id);
                    }
                  }}
                  className={`linear-event-node ${iconData.themeClass} ${isSelected ? 'is-selected' : ''}`}
                  onClick={() => handleSelectEvent(event)}
                  onKeyDown={e => {
                    if (e.key === 'Enter' || e.key === ' ') {
                      e.preventDefault();
                      handleSelectEvent(event);
                    }
                  }}
                  tabIndex={0}
                  role="button"
                  data-event-id={event.id}
                  data-is-current={isSelected ? 'true' : undefined}
                  aria-current={isSelected ? 'true' : undefined}
                  aria-label={`${iconData.label} at ${clock(event.clockSeconds)}${event.playerName ? ` by ${event.playerName}` : ''}${isSelected ? ' (Current)' : ''}`}
                >
                  {/* Timeline Node Point on the continuous rail */}
                  <div className="rail-node-point" aria-hidden="true">
                    <span className="rail-node-inner"></span>
                  </div>

                  <div className="linear-event-card">
                    {/* Event Header with Icon & Time */}
                    <div className="event-card-header">
                      <div className={`event-icon-badge ${iconData.themeClass}`} title={iconData.label}>
                        {iconData.svg}
                      </div>
                      <div className="event-time-badge">
                        <div className="event-clock-row">
                          <span className="event-clock">{clock(event.clockSeconds)}</span>
                          {isSelected && (
                            <span className="event-current-badge">
                              <span className="current-pulse-dot" aria-hidden="true"></span>
                              Current
                            </span>
                          )}
                        </div>
                        <span className="event-seq-tag">#{event.sequence}</span>
                        <span className="sr-only">Event ID {event.id}</span>
                      </div>
                    </div>

                    {/* Event Body with Type & Attribution */}
                    <div className="event-card-body">
                      <strong className="event-type-label">{event.type}</strong>
                      {event.playerName && (
                        <p className="event-player-name">
                          <span className="player-icon" aria-hidden="true">👤</span>
                          <span>{event.playerName}</span>
                        </p>
                      )}
                      {event.teamName && (
                        <p className="event-team-name">
                          <span className="team-pill-indicator" aria-hidden="true"></span>
                          <span>{event.teamName}</span>
                        </p>
                      )}
                      {event.description && (
                        <p className="event-desc">{event.description}</p>
                      )}
                    </div>

                    {/* Action link */}
                    <div className="event-card-footer">
                      <a
                        href={targetHref}
                        className="event-jump-link"
                        title={`View match state at ${clock(event.clockSeconds)}`}
                        onClick={e => {
                          e.stopPropagation();
                          handleLinkClick(e, targetHref);
                          handleSelectEvent(event);
                        }}
                      >
                        Inspect at {clock(event.clockSeconds)} →
                      </a>
                    </div>
                  </div>
                </article>
              );
            })}
          </div>
        </div>
      )}
    </section>
  );
}
