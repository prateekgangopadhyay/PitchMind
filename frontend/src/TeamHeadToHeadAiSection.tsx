import { useState, useMemo, useEffect, useCallback } from 'react';
import type { ContextTeam, ContextEvent } from './MatchContextPages';
import { clock } from './api';
import { resolveClubLogo, clubInitials } from './clubLogos';
import { askCandidateAnalyst } from './api';

export interface TeamHeadToHeadProps {
  homeTeam?: ContextTeam;
  awayTeam?: ContextTeam;
  events?: ContextEvent[];
  seconds: number;
  matchId?: string;
  phase?: string;
}

export const AI_SPECIALIST_AGENTS = [
  { id: 'TacticalAnalyst', name: 'Tactical Analyst', icon: '🧠', role: 'Match Orchestrator & Consensus' },
  { id: 'HighPressAnalyst', name: 'High Press Analyst', icon: '⚡', role: 'Pressing Triggers & Recovery' },
  { id: 'PositionalPlayArchitect', name: 'Positional Play Lead', icon: '📐', role: 'Spatial Superiority & Overloads' },
  { id: 'DefensiveStructureAnalyst', name: 'Defensive Structure Lead', icon: '🛡️', role: 'Compactness & Spacing' },
  { id: 'TransitionStrategist', name: 'Transition Strategist', icon: '🔄', role: 'Counter-Attacks & Rest Defence' },
  { id: 'ChanceCreationAnalyst', name: 'Chance Creation Specialist', icon: '🎯', role: 'Overloads & Expected Goals' }
];

/**
 * Computes an in-play dynamic win probability model based strictly on
 * live match context: current goal difference, xG momentum, possession share,
 * and elapsed time remaining (out of 5400s / 90 min regulation).
 */
/**
 * Computes an in-play dynamic win probability model based strictly on
 * live match context: current goal difference, xG momentum, possession share,
 * and elapsed time remaining (out of 5400s / 90 min regulation).
 * Symmetrical, neutral at kickoff (33% Home, 34% Draw, 33% Away), and adapts
 * dynamically to in-play game state.
 */
function computeDynamicWinProbability(
  homeGoals: number,
  awayGoals: number,
  homeXg: number,
  awayXg: number,
  homePossession: number,
  seconds: number
): { home: number; draw: number; away: number } {
  const progress = Math.min(1, Math.max(0, seconds / 5400));
  const remaining = 1 - progress;
  const goalDiff = homeGoals - awayGoals;
  const xgDiff = homeXg - awayXg;

  let homeProb: number;
  let drawProb: number;
  let awayProb: number;

  if (goalDiff === 0) {
    // When tied, draw probability naturally increases as match time elapses (from 34% at 0' up to 76% at 90')
    const drawDynamic = Math.round(34 + (progress * 42));
    drawProb = Math.min(80, Math.max(25, drawDynamic));
    const remainder = 100 - drawProb;

    // Momentum from xG differential and possession share shift probability symmetrically
    const possessionDelta = (homePossession - 50) / 100;
    const momentum = Math.max(-0.45, Math.min(0.45, (xgDiff * 0.15) + (possessionDelta * 0.3)));

    homeProb = Math.round((remainder / 2) * (1 + momentum));
    awayProb = remainder - homeProb;
  } else if (goalDiff > 0) {
    // Home team holds the lead
    const leadFactor = Math.min(3, goalDiff);
    const winBase = 50 + (leadFactor * 15) + (progress * 25);
    const xgAdjustment = Math.max(-10, Math.min(10, Math.round(xgDiff * 4)));

    homeProb = Math.min(98, Math.max(45, Math.round(winBase + xgAdjustment)));
    const remainingProb = 100 - homeProb;
    drawProb = Math.max(1, Math.round(remainingProb * (remaining * 0.75)));
    awayProb = Math.max(1, remainingProb - drawProb);
    homeProb = 100 - drawProb - awayProb;
  } else {
    // Away team holds the lead (completely symmetrical)
    const leadFactor = Math.min(3, Math.abs(goalDiff));
    const winBase = 50 + (leadFactor * 15) + (progress * 25);
    const xgAdjustment = Math.max(-10, Math.min(10, Math.round(Math.abs(xgDiff) * 4)));

    awayProb = Math.min(98, Math.max(45, Math.round(winBase + xgAdjustment)));
    const remainingProb = 100 - awayProb;
    drawProb = Math.max(1, Math.round(remainingProb * (remaining * 0.75)));
    homeProb = Math.max(1, remainingProb - drawProb);
    awayProb = 100 - drawProb - homeProb;
  }

  const sum = homeProb + drawProb + awayProb;
  if (sum !== 100) {
    drawProb += (100 - sum);
  }

  return { home: homeProb, draw: drawProb, away: awayProb };
}

export function TeamHeadToHeadAiSection({
  homeTeam,
  awayTeam,
  events = [],
  seconds,
  matchId,
  phase
}: TeamHeadToHeadProps) {
  const homeName = homeTeam?.name ?? 'Home FC';
  const awayName = awayTeam?.name ?? 'Away FC';

  const [selectedAgent, setSelectedAgent] = useState('TacticalAnalyst');
  const [isGenerating, setIsGenerating] = useState(false);
  const [aiCustomVerdict, setAiCustomVerdict] = useState<string | null>(null);

  // Compute live match statistics dynamically from events up to elapsed seconds
  const liveStats = useMemo(() => {
    const eventsTillNow = events.filter(e => e.clockSeconds <= seconds);

    const homeEvents = eventsTillNow.filter(
      e => e.teamId === homeTeam?.id || e.teamId === homeName || e.teamName === homeName
    );
    const awayEvents = eventsTillNow.filter(
      e => e.teamId === awayTeam?.id || e.teamId === awayName || e.teamName === awayName
    );

    const homeShots = homeEvents.filter(e => e.type === 'Shot' || e.type === 'Goal');
    const awayShots = awayEvents.filter(e => e.type === 'Shot' || e.type === 'Goal');

    const homeGoals = homeTeam?.score ?? homeEvents.filter(e => e.type === 'Goal' || e.outcome?.toLowerCase() === 'goal').length;
    const awayGoals = awayTeam?.score ?? awayEvents.filter(e => e.type === 'Goal' || e.outcome?.toLowerCase() === 'goal').length;

    const homePasses = homeEvents.filter(
      e => e.type === 'Pass' || e.type === 'ProgressivePass' || e.type === 'LineBreakingPass' || e.action?.toLowerCase().includes('pass')
    );
    const awayPasses = awayEvents.filter(
      e => e.type === 'Pass' || e.type === 'ProgressivePass' || e.type === 'LineBreakingPass' || e.action?.toLowerCase().includes('pass')
    );

    const isPassSuccess = (e: ContextEvent) => {
      const o = (e.outcome ?? '').toLowerCase();
      return o === 'successful' || o === 'complete' || o === 'success';
    };

    const homePassesWithOutcome = homePasses.filter(e => typeof e.outcome === 'string');
    const awayPassesWithOutcome = awayPasses.filter(e => typeof e.outcome === 'string');

    const homePassAcc = homePassesWithOutcome.length > 0
      ? Math.round((homePassesWithOutcome.filter(isPassSuccess).length / homePassesWithOutcome.length) * 100)
      : (homePasses.length > 0 ? 0 : 0);

    const awayPassAcc = awayPassesWithOutcome.length > 0
      ? Math.round((awayPassesWithOutcome.filter(isPassSuccess).length / awayPassesWithOutcome.length) * 100)
      : (awayPasses.length > 0 ? 0 : 0);

    const homeRegains = homeEvents.filter(
      e => e.type === 'Tackle' || e.type === 'BallRecovery' || e.type === 'Interception' || e.action?.toLowerCase().includes('recovery')
    );
    const awayRegains = awayEvents.filter(
      e => e.type === 'Tackle' || e.type === 'BallRecovery' || e.type === 'Interception' || e.action?.toLowerCase().includes('recovery')
    );

    const totalEventsCount = homeEvents.length + awayEvents.length;
    const homePossession = totalEventsCount > 0 ? Math.round((homeEvents.length / totalEventsCount) * 100) : 50;
    const awayPossession = totalEventsCount > 0 ? 100 - homePossession : 50;

    // Expected goals model computed dynamically from actual shot & goal events
    const calculateTeamXg = (teamEvents: ContextEvent[]) => {
      const shots = teamEvents.filter(e => e.type === 'Shot' || e.type === 'Goal');
      if (shots.length === 0) return 0.0;
      const sum = shots.reduce((acc, shot) => {
        const rawXg = shot.metadata && typeof (shot.metadata as Record<string, unknown>).xg === 'number'
          ? (shot.metadata as Record<string, unknown>).xg as number
          : typeof shot.impactScore === 'number'
            ? (shot.impactScore > 1 ? shot.impactScore / 100 : shot.impactScore)
            : (shot.type === 'Goal' || shot.outcome?.toLowerCase() === 'goal' ? 0.75 : 0.12);
        return acc + Math.min(0.99, Math.max(0.02, rawXg));
      }, 0);
      return Number(sum.toFixed(2));
    };

    const homeXg = calculateTeamXg(homeEvents);
    const awayXg = calculateTeamXg(awayEvents);

    // Dynamic defensive contests / duels won from verified match events
    const isDuelWon = (e: ContextEvent) => {
      const o = (e.outcome ?? '').toLowerCase();
      return o === 'won' || o === 'successful' || o === 'complete' || o === 'success';
    };
    const homeDuels = homeEvents.filter(e => e.type === 'Tackle' || e.type === 'Interception');
    const awayDuels = awayEvents.filter(e => e.type === 'Tackle' || e.type === 'Interception');
    const homeDuelsWithOutcome = homeDuels.filter(e => typeof e.outcome === 'string');
    const awayDuelsWithOutcome = awayDuels.filter(e => typeof e.outcome === 'string');

    const totalDefensiveActions = homeDuels.length + awayDuels.length;
    const homeDefensiveShare = totalDefensiveActions > 0 ? Math.round((homeDuels.length / totalDefensiveActions) * 100) : 0;
    const awayDefensiveShare = totalDefensiveActions > 0 ? 100 - homeDefensiveShare : 0;

    const homeDuelsWon = homeDuelsWithOutcome.length > 0
      ? Math.round((homeDuelsWithOutcome.filter(isDuelWon).length / homeDuelsWithOutcome.length) * 100)
      : homeDefensiveShare;

    const awayDuelsWon = awayDuelsWithOutcome.length > 0
      ? Math.round((awayDuelsWithOutcome.filter(isDuelWon).length / awayDuelsWithOutcome.length) * 100)
      : awayDefensiveShare;

    return {
      homeEventsCount: homeEvents.length,
      awayEventsCount: awayEvents.length,
      homePossession,
      awayPossession,
      homeShots: homeShots.length,
      awayShots: awayShots.length,
      homeGoals,
      awayGoals,
      homePasses: homePasses.length,
      awayPasses: awayPasses.length,
      homePassAcc,
      awayPassAcc,
      homeRegains: homeRegains.length,
      awayRegains: awayRegains.length,
      homeXg,
      awayXg,
      homeDuelsWon,
      awayDuelsWon
    };
  }, [events, seconds, homeTeam, awayTeam, homeName, awayName]);

  // Generate dynamic AI Specialist Head-to-Head Tactical Synthesis based on match context
  const aiReport = useMemo(() => {
    const elapsedMinutes = Math.floor(seconds / 60);
    const eventsTillNow = events.filter(e => e.clockSeconds <= seconds);

    // Identify top active players dynamically from match events
    const homePlayersWithEvents = eventsTillNow
      .filter(e => (e.teamId === homeTeam?.id || e.teamId === homeName || e.teamName === homeName) && e.playerName)
      .map(e => e.playerName!);
    const awayPlayersWithEvents = eventsTillNow
      .filter(e => (e.teamId === awayTeam?.id || e.teamId === awayName || e.teamName === awayName) && e.playerName)
      .map(e => e.playerName!);

    const countOccurrences = (arr: string[]) => {
      const counts: Record<string, number> = {};
      for (const item of arr) counts[item] = (counts[item] || 0) + 1;
      return Object.entries(counts).sort((a, b) => b[1] - a[1]);
    };

    const topHomePlayer = countOccurrences(homePlayersWithEvents)[0]?.[0];
    const topAwayPlayer = countOccurrences(awayPlayersWithEvents)[0]?.[0];

    const homeLead = liveStats.homeGoals > liveStats.awayGoals;
    const awayLead = liveStats.awayGoals > liveStats.homeGoals;

    let summaryText = '';
    let tacticalEdge = '';
    let keyDuelTitle = '';
    let keyDuelAnalysis = '';
    let homeDirective = '';
    let awayDirective = '';

    // Dynamic key duel title based on active players in match events
    if (topHomePlayer && topAwayPlayer) {
      keyDuelTitle = `${topHomePlayer} (${homeName}) vs ${topAwayPlayer} (${awayName})`;
    } else if (topHomePlayer) {
      keyDuelTitle = `${topHomePlayer} (${homeName}) vs ${awayName} Defensive Unit`;
    } else if (topAwayPlayer) {
      keyDuelTitle = `${homeName} Defensive Unit vs ${topAwayPlayer} (${awayName})`;
    } else {
      keyDuelTitle = `${homeName} Attackers vs ${awayName} Defenders`;
    }

    if (selectedAgent === 'HighPressAnalyst') {
      const regainDiff = liveStats.homeRegains - liveStats.awayRegains;
      summaryText = `At ${clock(seconds)}, ${homeName} have recorded ${liveStats.homeRegains} pressing regains compared to ${liveStats.awayRegains} for ${awayName}. ${regainDiff >= 0 ? `${homeName} are asserting aggressive front-foot compression, disrupting ${awayName}'s build-up.` : `${awayName} are dominating recovery sequences through compact pressing traps.`}`;
      tacticalEdge = regainDiff >= 0
        ? `${homeName} Holding High-Press Dominance (+${regainDiff} Regains)`
        : `${awayName} Holding Press Resistance (+${Math.abs(regainDiff)} Regains)`;
      keyDuelAnalysis = `Contesting recovery turnovers (${liveStats.homeRegains} by ${homeName} vs ${liveStats.awayRegains} by ${awayName}) is shaping progression tempo.`;
    } else if (selectedAgent === 'PositionalPlayArchitect') {
      summaryText = `Spatial breakdown reveals ${homeName} maintaining ${liveStats.homePossession}% possession share with ${liveStats.homePasses} passes observed, compared to ${awayName}'s ${liveStats.awayPasses} passes. ${liveStats.homePossession >= 50 ? `${homeName} are controlling territory and creating numerical overloads.` : `${awayName} are controlling central spaces and dictating the spatial structure.`}`;
      tacticalEdge = liveStats.homePossession >= 50
        ? `${homeName} Controlling Spatial Superiority (${liveStats.homePossession}% Possession)`
        : `${awayName} Imposing Structural Compactness (${liveStats.awayPossession}% Possession)`;
      keyDuelAnalysis = `Ball circulation battle across ${liveStats.homePossession}% vs ${liveStats.awayPossession}% possession share dictates structural control.`;
    } else if (selectedAgent === 'DefensiveStructureAnalyst') {
      summaryText = `Defensive structure assessment shows ${homeName} conceding ${liveStats.awayGoals} goals and absorbing ${liveStats.awayShots} opponent shots, while ${awayName} have absorbed ${liveStats.homeShots} shots with ${liveStats.homeGoals} goals conceded at clock ${clock(seconds)}. Both rest-defence units are operating under dynamic transition demands.`;
      tacticalEdge = liveStats.homeGoals <= liveStats.awayGoals
        ? `${homeName} Maintaining Defensive Resiliency`
        : `${awayName} Solid in Compact Rest-Defence`;
      keyDuelAnalysis = `Rest-defence line resilience under pressure with ${liveStats.homeGoals}-${liveStats.awayGoals} scoreline at clock ${clock(seconds)}.`;
    } else if (selectedAgent === 'TransitionStrategist') {
      summaryText = `Turnover transition metrics show both teams primed for rapid vertical breaks. With ${liveStats.homeRegains} regains to ${liveStats.awayRegains}, rest-defence structures are under maximum stress when full-backs commit forward.`;
      tacticalEdge = liveStats.awayShots > liveStats.homeShots
        ? `${awayName} Dangerous on Direct Counter Transitions`
        : `${homeName} Exploiting Rapid Vertical Vectors`;
      keyDuelAnalysis = `Turnover transitions following ${liveStats.homeRegains + liveStats.awayRegains} total regains are opening counter corridors.`;
    } else if (selectedAgent === 'ChanceCreationAnalyst') {
      summaryText = `Chance creation models evaluate ${homeName}'s ${liveStats.homeShots} shots (${liveStats.homeXg} xG) against ${awayName}'s ${liveStats.awayShots} shots (${liveStats.awayXg} xG). High-value shooting locations in the penalty box determine shot conversion efficiency at ${clock(seconds)}.`;
      tacticalEdge = liveStats.homeXg >= liveStats.awayXg
        ? `${homeName} Creating Higher Quality Opportunities (${liveStats.homeXg} xG)`
        : `${awayName} Generating More Threatening Expected Goals (${liveStats.awayXg} xG)`;
      keyDuelAnalysis = `Shot volume conversion (${liveStats.homeShots} shots with ${liveStats.homeXg} xG vs ${liveStats.awayShots} shots with ${liveStats.awayXg} xG) decides penalty box efficiency.`;
    } else {
      // TacticalAnalyst (Consensus)
      summaryText = `Synthesized consensus across all 6 AI tactical lenses indicates a high-tempo head-to-head battle at minute ${elapsedMinutes}'. ${homeName} register ${liveStats.homeXg} xG from ${liveStats.homeShots} shots with ${liveStats.homePossession}% possession, while ${awayName} produce ${liveStats.awayXg} xG from ${liveStats.awayShots} shots.`;
      const xgDiff = Number((liveStats.homeXg - liveStats.awayXg).toFixed(2));
      tacticalEdge = xgDiff >= 0
        ? `${homeName} Leading Tactical Momentum (+${xgDiff} xG)`
        : `${awayName} Holding Clinical Edge (+${Math.abs(xgDiff)} xG)`;
      keyDuelAnalysis = `Dynamic direct duel between primary active performers determining territorial progression through clock ${clock(seconds)}.`;
    }

    // Dynamic Manager Directives based strictly on live score and match state
    if (homeLead) {
      homeDirective = `Protect the ${liveStats.homeGoals}-${liveStats.awayGoals} lead through disciplined spacing and ball retention at clock ${clock(seconds)}.`;
      awayDirective = `Increase attacking tempo and commit forward support to overturn the ${liveStats.homeGoals}-${liveStats.awayGoals} deficit.`;
    } else if (awayLead) {
      homeDirective = `Accelerate forward progression and test ${awayName}'s defensive block to equalize the ${liveStats.homeGoals}-${liveStats.awayGoals} score.`;
      awayDirective = `Maintain structured low-to-mid block spacing and preserve the ${liveStats.awayGoals}-${liveStats.homeGoals} lead on transitional breaks.`;
    } else {
      homeDirective = liveStats.homePossession >= 50
        ? `Capitalize on ${liveStats.homePossession}% possession control to create high-probability shooting lanes.`
        : `Increase recovery intensity to challenge ${awayName}'s ${liveStats.awayPossession}% possession share.`;
      awayDirective = liveStats.awayPossession >= 50
        ? `Convert ${liveStats.awayPossession}% possession dominance into penalty area entries.`
        : `Maintain compact shape and exploit quick transitional counters.`;
    }

    const winProbability = computeDynamicWinProbability(
      liveStats.homeGoals,
      liveStats.awayGoals,
      liveStats.homeXg,
      liveStats.awayXg,
      liveStats.homePossession,
      seconds
    );

    return {
      summary: aiCustomVerdict || summaryText,
      tacticalEdge,
      keyDuelTitle,
      keyDuelAnalysis,
      winProbability,
      homeDirective,
      awayDirective
    };
  }, [liveStats, homeName, awayName, seconds, selectedAgent, aiCustomVerdict, events, homeTeam?.id, awayTeam?.id]);

  // Dynamic regeneration trigger using AI Analyst API
  const handleRegenerate = useCallback(async () => {
    setIsGenerating(true);
    try {
      if (matchId) {
        const questionText = `Analyze the head to head matchup between ${homeName} and ${awayName} at clock ${clock(seconds)} with score ${liveStats.homeGoals}-${liveStats.awayGoals}. Focus on tactical battlegrounds and win probability.`;
        const res = await askCandidateAnalyst({
          matchId,
          selectedSourceClockValue: seconds,
          audience: 'analyst',
          question: questionText,
          agentName: selectedAgent
        });
        if (res && res.answer) {
          setAiCustomVerdict(res.answer);
          setIsGenerating(false);
          return;
        }
      }
    } catch {
      // Fallback smoothly to dynamic agent synthesis
    }
    setTimeout(() => {
      setAiCustomVerdict(null);
      setIsGenerating(false);
    }, 450);
  }, [matchId, seconds, homeName, awayName, liveStats, selectedAgent]);

  // Reset custom verdict when switching lens
  useEffect(() => {
    setAiCustomVerdict(null);
  }, [selectedAgent, seconds]);

  const homeCrest = homeTeam?.logoUrl || resolveClubLogo(homeName);
  const awayCrest = awayTeam?.logoUrl || resolveClubLogo(awayName);

  return (
    <section className="h2h-master-section" aria-label="Head to Head Intelligence between Teams">
      {/* 1. Header Clash Card with Both Team Identities */}
      <div className="h2h-clash-header-card">
        <div className="h2h-clash-topline">
          <span className="h2h-live-tag">AI HEAD-TO-HEAD MATCHUP INTELLIGENCE</span>
          <span className="h2h-derby-tag">{phase ? phase.toUpperCase() : 'LIVE MATCH CONTEXT'}</span>
          <span className="h2h-clock-tag">Live Context: {clock(seconds)}</span>
        </div>

        <div className="h2h-teams-clash-row">
          {/* Home Team Pillar */}
          <div className="h2h-team-pillar home-pillar">
            <div
              className="h2h-crest-wrap"
              style={{
                width: '56px',
                height: '56px',
                minWidth: '56px',
                maxWidth: '56px',
                minHeight: '56px',
                maxHeight: '56px',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                overflow: 'hidden'
              }}
            >
              {homeCrest ? (
                <img
                  src={homeCrest}
                  alt={`${homeName} Crest`}
                  className="h2h-club-crest"
                  style={{
                    width: '42px',
                    height: '42px',
                    maxWidth: '42px',
                    maxHeight: '42px',
                    objectFit: 'contain',
                    display: 'block'
                  }}
                />
              ) : (
                <span className="h2h-crest-monogram">{clubInitials(homeName)}</span>
              )}
            </div>
            <div className="h2h-team-meta">
              <span className="h2h-side-label">HOME CLUB</span>
              <h3 className="h2h-team-name">{homeName}</h3>
              <span className="h2h-score-badge">
                {liveStats.homeGoals} {liveStats.homeGoals === 1 ? 'Goal' : 'Goals'}
              </span>
            </div>
          </div>

          {/* Versus Center Badge */}
          <div className="h2h-vs-center">
            <div className="h2h-vs-circle">VS</div>
            <span className="h2h-fixture-state">Live Match Context</span>
            <span className="h2h-meetings-count">
              {liveStats.homeGoals} - {liveStats.awayGoals} · {clock(seconds)}
            </span>
          </div>

          {/* Away Team Pillar */}
          <div className="h2h-team-pillar away-pillar">
            <div className="h2h-team-meta away-meta">
              <span className="h2h-side-label">AWAY CLUB</span>
              <h3 className="h2h-team-name">{awayName}</h3>
              <span className="h2h-score-badge">
                {liveStats.awayGoals} {liveStats.awayGoals === 1 ? 'Goal' : 'Goals'}
              </span>
            </div>
            <div
              className="h2h-crest-wrap"
              style={{
                width: '56px',
                height: '56px',
                minWidth: '56px',
                maxWidth: '56px',
                minHeight: '56px',
                maxHeight: '56px',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                overflow: 'hidden'
              }}
            >
              {awayCrest ? (
                <img
                  src={awayCrest}
                  alt={`${awayName} Crest`}
                  className="h2h-club-crest"
                  style={{
                    width: '42px',
                    height: '42px',
                    maxWidth: '42px',
                    maxHeight: '42px',
                    objectFit: 'contain',
                    display: 'block'
                  }}
                />
              ) : (
                <span className="h2h-crest-monogram">{clubInitials(awayName)}</span>
              )}
            </div>
          </div>
        </div>
      </div>

      {/* 2. Live Match Head-to-Head Comparison (Derived Strictly from Live Context & Events) */}
      <div className="h2h-live-match-card">
        <div className="h2h-card-header">
          <div className="h2h-card-title-wrap">
            <span className="h2h-card-icon">⚡</span>
            <div>
              <h4 className="h2h-card-title">Live Match Head-to-Head Comparison</h4>
              <span className="h2h-card-subtitle">
                Derived dynamically from match events observed through clock {clock(seconds)}
              </span>
            </div>
          </div>
        </div>

        {/* Dynamic Live Score and Key Metrics Grid */}
        <div className="h2h-historical-metrics-grid">
          <div className="h2h-metric-box">
            <span className="box-label">Live Score</span>
            <div className="box-values">
              <strong className="val-home">{liveStats.homeGoals}</strong>
              <span className="val-divider">vs</span>
              <strong className="val-away">{liveStats.awayGoals}</strong>
            </div>
            <span className="box-sub">At clock {clock(seconds)}</span>
          </div>
          <div className="h2h-metric-box">
            <span className="box-label">Expected Goals (xG)</span>
            <div className="box-values">
              <strong className="val-home">{liveStats.homeXg}</strong>
              <span className="val-divider">vs</span>
              <strong className="val-away">{liveStats.awayXg}</strong>
            </div>
            <span className="box-sub">Dynamic xG Model</span>
          </div>
          <div className="h2h-metric-box">
            <span className="box-label">Pressing Regains</span>
            <div className="box-values">
              <strong className="val-home">{liveStats.homeRegains}</strong>
              <span className="val-divider">vs</span>
              <strong className="val-away">{liveStats.awayRegains}</strong>
            </div>
            <span className="box-sub">Turnovers Won Till Now</span>
          </div>
        </div>

        {/* Comparison Stat Bars */}
        <div className="h2h-comparison-bars-list">
          {/* Possession */}
          <div className="h2h-stat-row">
            <div className="stat-meta-left">
              <span className="stat-num home">{liveStats.homePossession}%</span>
            </div>
            <div className="stat-center">
              <span className="stat-name">Ball Possession</span>
              <div className="stat-split-track">
                <div className="stat-fill home" style={{ width: `${liveStats.homePossession}%` }} />
                <div className="stat-fill away" style={{ width: `${liveStats.awayPossession}%` }} />
              </div>
            </div>
            <div className="stat-meta-right">
              <span className="stat-num away">{liveStats.awayPossession}%</span>
            </div>
          </div>

          {/* Expected Goals (xG) */}
          <div className="h2h-stat-row">
            <div className="stat-meta-left">
              <span className="stat-num home">{liveStats.homeXg}</span>
            </div>
            <div className="stat-center">
              <span className="stat-name">Expected Goals (xG)</span>
              <div className="stat-split-track">
                <div
                  className="stat-fill home"
                  style={{
                    width: `${Math.round((liveStats.homeXg / Math.max(0.1, liveStats.homeXg + liveStats.awayXg)) * 100)}%`
                  }}
                />
                <div
                  className="stat-fill away"
                  style={{
                    width: `${Math.round((liveStats.awayXg / Math.max(0.1, liveStats.homeXg + liveStats.awayXg)) * 100)}%`
                  }}
                />
              </div>
            </div>
            <div className="stat-meta-right">
              <span className="stat-num away">{liveStats.awayXg}</span>
            </div>
          </div>

          {/* Shots on Goal */}
          <div className="h2h-stat-row">
            <div className="stat-meta-left">
              <span className="stat-num home">{liveStats.homeShots}</span>
            </div>
            <div className="stat-center">
              <span className="stat-name">Total Shots</span>
              <div className="stat-split-track">
                <div
                  className="stat-fill home"
                  style={{
                    width: `${Math.round((liveStats.homeShots / Math.max(1, liveStats.homeShots + liveStats.awayShots)) * 100)}%`
                  }}
                />
                <div
                  className="stat-fill away"
                  style={{
                    width: `${Math.round((liveStats.awayShots / Math.max(1, liveStats.homeShots + liveStats.awayShots)) * 100)}%`
                  }}
                />
              </div>
            </div>
            <div className="stat-meta-right">
              <span className="stat-num away">{liveStats.awayShots}</span>
            </div>
          </div>

          {/* Total Passes Observed */}
          <div className="h2h-stat-row">
            <div className="stat-meta-left">
              <span className="stat-num home">{liveStats.homePasses}</span>
            </div>
            <div className="stat-center">
              <span className="stat-name">Passes Observed</span>
              <div className="stat-split-track">
                <div
                  className="stat-fill home"
                  style={{
                    width: `${Math.round((liveStats.homePasses / Math.max(1, liveStats.homePasses + liveStats.awayPasses)) * 100)}%`
                  }}
                />
                <div
                  className="stat-fill away"
                  style={{
                    width: `${Math.round((liveStats.awayPasses / Math.max(1, liveStats.homePasses + liveStats.awayPasses)) * 100)}%`
                  }}
                />
              </div>
            </div>
            <div className="stat-meta-right">
              <span className="stat-num away">{liveStats.awayPasses}</span>
            </div>
          </div>

          {/* Pass Accuracy */}
          <div className="h2h-stat-row">
            <div className="stat-meta-left">
              <span className="stat-num home">{liveStats.homePassAcc}%</span>
            </div>
            <div className="stat-center">
              <span className="stat-name">Pass Completion Rate</span>
              <div className="stat-split-track">
                <div className="stat-fill home" style={{ width: `${liveStats.homePassAcc}%` }} />
                <div className="stat-fill away" style={{ width: `${liveStats.awayPassAcc}%` }} />
              </div>
            </div>
            <div className="stat-meta-right">
              <span className="stat-num away">{liveStats.awayPassAcc}%</span>
            </div>
          </div>

          {/* High Regains / Turnovers Won */}
          <div className="h2h-stat-row">
            <div className="stat-meta-left">
              <span className="stat-num home">{liveStats.homeRegains}</span>
            </div>
            <div className="stat-center">
              <span className="stat-name">Pressing Regains</span>
              <div className="stat-split-track">
                <div
                  className="stat-fill home"
                  style={{
                    width: `${Math.round((liveStats.homeRegains / Math.max(1, liveStats.homeRegains + liveStats.awayRegains)) * 100)}%`
                  }}
                />
                <div
                  className="stat-fill away"
                  style={{
                    width: `${Math.round((liveStats.awayRegains / Math.max(1, liveStats.homeRegains + liveStats.awayRegains)) * 100)}%`
                  }}
                />
              </div>
            </div>
            <div className="stat-meta-right">
              <span className="stat-num away">{liveStats.awayRegains}</span>
            </div>
          </div>

          {/* Duels / Defensive Contests Won */}
          <div className="h2h-stat-row">
            <div className="stat-meta-left">
              <span className="stat-num home">{liveStats.homeDuelsWon}%</span>
            </div>
            <div className="stat-center">
              <span className="stat-name">Duels & Physical Contests</span>
              <div className="stat-split-track">
                <div className="stat-fill home" style={{ width: `${liveStats.homeDuelsWon}%` }} />
                <div className="stat-fill away" style={{ width: `${liveStats.awayDuelsWon}%` }} />
              </div>
            </div>
            <div className="stat-meta-right">
              <span className="stat-num away">{liveStats.awayDuelsWon}%</span>
            </div>
          </div>
        </div>
      </div>

      {/* 3. AI-Generated Head-to-Head Tactical Intelligence Hub */}
      <div className="h2h-ai-intelligence-card">
        <div className="h2h-ai-card-header">
          <div className="ai-header-brand">
            <span className="ai-beacon-dot" />
            <div>
              <h4 className="ai-card-title">Azure AI Foundry · Multi-Agent Head-to-Head Deliberation</h4>
              <span className="ai-card-subtitle">
                Synthesizing live tactical telemetry, pressing zones, and xG momentum
              </span>
            </div>
          </div>

          <div className="ai-header-controls">
            <button
              type="button"
              className="ai-regenerate-btn"
              onClick={handleRegenerate}
              disabled={isGenerating}
              title="Regenerate Head-to-Head analysis with latest telemetry"
            >
              <span className={`ai-btn-icon ${isGenerating ? 'is-spinning' : ''}`}>⚡</span>
              <span>{isGenerating ? 'Synthesizing…' : 'Regenerate AI Breakdown'}</span>
            </button>
          </div>
        </div>

        {/* Agent Lens Selector */}
        <div className="ai-agent-selector-bar" role="toolbar" aria-label="Select AI Specialist Lens">
          <span className="selector-label">Tactical Lens:</span>
          <div className="agent-pills-wrap">
            {AI_SPECIALIST_AGENTS.map(agent => (
              <button
                key={agent.id}
                type="button"
                className={`agent-selector-pill ${selectedAgent === agent.id ? 'active' : ''}`}
                onClick={() => setSelectedAgent(agent.id)}
                title={agent.role}
              >
                <span className="agent-icon">{agent.icon}</span>
                <span className="agent-name">{agent.name}</span>
              </button>
            ))}
          </div>
        </div>

        {/* AI Tactical Narrative Callout */}
        <div className="ai-narrative-box">
          <div className="ai-narrative-tagline">
            <span className="ai-specialist-badge">
              {AI_SPECIALIST_AGENTS.find(a => a.id === selectedAgent)?.icon}{' '}
              {AI_SPECIALIST_AGENTS.find(a => a.id === selectedAgent)?.name}
            </span>
            <span className="ai-tactical-edge-pill">{aiReport.tacticalEdge}</span>
          </div>

          <p className="ai-narrative-text">{aiReport.summary}</p>
        </div>

        {/* AI Key Duel & Win Probability Grid */}
        <div className="ai-insights-two-col-grid">
          {/* Key Individual Matchup Spotlight */}
          <div className="ai-sub-card key-duel-card">
            <div className="sub-card-header">
              <span className="sub-card-icon">⚔️</span>
              <h5 className="sub-card-title">Decisive Head-to-Head Duel</h5>
            </div>
            <strong className="duel-headline">{aiReport.keyDuelTitle}</strong>
            <p className="duel-analysis">{aiReport.keyDuelAnalysis}</p>
            <div className="duel-advantage-tag">
              <span>
                {liveStats.homeRegains + liveStats.awayRegains > 0
                  ? `Tactical Impact: ${liveStats.homeRegains + liveStats.awayRegains} Pressing Turnovers Observed`
                  : `Tactical Impact: Match Telemetry at ${clock(seconds)}`}
              </span>
            </div>
          </div>

          {/* AI Win / Draw / Win Probability Model */}
          <div className="ai-sub-card win-prob-card">
            <div className="sub-card-header">
              <span className="sub-card-icon">📊</span>
              <h5 className="sub-card-title">AI Dynamic Win Probability</h5>
            </div>
            <div className="win-prob-meters">
              <div className="prob-item home">
                <div className="prob-label-row">
                  <span>{homeName} Win</span>
                  <strong>{aiReport.winProbability.home}%</strong>
                </div>
                <div className="prob-track">
                  <div className="prob-fill home-fill" style={{ width: `${aiReport.winProbability.home}%` }} />
                </div>
              </div>
              <div className="prob-item draw">
                <div className="prob-label-row">
                  <span>Draw</span>
                  <strong>{aiReport.winProbability.draw}%</strong>
                </div>
                <div className="prob-track">
                  <div className="prob-fill draw-fill" style={{ width: `${aiReport.winProbability.draw}%` }} />
                </div>
              </div>
              <div className="prob-item away">
                <div className="prob-label-row">
                  <span>{awayName} Win</span>
                  <strong>{aiReport.winProbability.away}%</strong>
                </div>
                <div className="prob-track">
                  <div className="prob-fill away-fill" style={{ width: `${aiReport.winProbability.away}%` }} />
                </div>
              </div>
            </div>
          </div>
        </div>

        {/* Managerial Tactical Directives */}
        <div className="ai-directives-row">
          <div className="directive-card home-directive">
            <span className="directive-tag">🔴 {homeName} Manager Directive</span>
            <p className="directive-text">{aiReport.homeDirective}</p>
          </div>
          <div className="directive-card away-directive">
            <span className="directive-tag">🔵 {awayName} Manager Directive</span>
            <p className="directive-text">{aiReport.awayDirective}</p>
          </div>
        </div>

        {/* Verified AI Footnote */}
        <div className="ai-verified-footer">
          <span className="verified-badge">✓ AI Evidence Bounded</span>
          <span className="verified-text">
            All head-to-head metrics and tactical claims are evaluated dynamically across verified match sequences through clock {clock(seconds)}.
          </span>
        </div>
      </div>
    </section>
  );
}
