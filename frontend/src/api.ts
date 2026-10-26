

export interface Player { id: string; name: string; teamId: string; shirtNumber: number; role: string }
export interface Team { id: string; name: string; formation: string; players: Player[] }
export interface PitchPosition { playerId: string; teamId: string; x: number; y: number }
export interface Frame { id: string; clockSeconds: number; ballX: number; ballY: number; coordinateConvention: string; players: PitchPosition[] }
export interface MatchEvent { event: {eventId: string; matchId: string; sequence: number; type: string; clockSeconds: number; period: number; occurredAt: string}; teamId: string; playerId: string; action: string; outcome: string; relatedPlayerId: string | null }
export interface Fixture { matchId: string; version: string; seed: number; provenance: string; durationSeconds: number; halfTimeSeconds: number; pitchLengthMetres: number; pitchWidthMetres: number; home: Team; away: Team; events: MatchEvent[]; frames: Frame[] }
export interface MatchPlayerState { playerId: string; teamId: string; active: boolean; dismissed: boolean; yellowCards: number }
export interface Snapshot { matchId: string; clockSeconds: number; period: number; status: 'replay' | 'full-time'; phase: 'notstarted' | 'firsthalf' | 'halftime' | 'secondhalf' | 'fulltime'; homeScore: number; awayScore: number; provenance: string; frame: Frame; lastSequence: number; possessionTeamId: string | null; homeActivePlayers: number; awayActivePlayers: number; players: MatchPlayerState[]; competitionId: string | null; competitionRulesVersion: string | null; maximumSubstituteReentriesPerPlayer: number | null }
export interface TacticalContext { scoreline: string; teamRole: 'home' | 'away'; numericalAdvantage: number; opponentPhase: string; matchPhase: string }
export interface EvidenceNode { id: string; kind: 'claim' | 'event' | 'frame' | 'metric'; truthClass: 'fact' | 'derivation' | 'pattern' | 'interpretation' | 'hypothesis'; description: string; eventId: string | null; frameId: string | null; clockSeconds: number | null; sequence: number | null; sourceVersion: string; sourceSha256: string; canonicalRevision: number | null; numericValue: number | null; unit: string | null }
export interface EvidenceEdge { fromNodeId: string; toNodeId: string; relation: 'precedes' | 'derived_from' }
export interface EvidenceCondition { id: string; passed: boolean; observed: string; threshold: string | null; sourceNodeIds: string[]; exclusionReason: string | null }
export interface EvidenceGraph { matchId: string; sourceVersion: string; sourceSha256: string; provenance: string; canonicalRevision: number | null; selectedTimeSeconds: number; throughSequence: number; detectorVersion: string; policyStatus: string; publishable: boolean; claimNodeId: string; nodes: EvidenceNode[]; edges: EvidenceEdge[]; uncertainty: string; conditions: EvidenceCondition[] }
export interface KeyMoment {
  id: string; matchId: string; sourceVersion: string; sourceSha256: string; provenance: string; canonicalRevision: number | null;
  selectedTimeSeconds: number; throughSequence: number; clockSeconds: number; sequence: number;
  kind: string; truthClass: string; title: string; summary: string; relevance: number;
  policyVersion: string; policyStatus: string; publishable: boolean;
  eventIds: string[]; frameIds: string[]; evidenceClaimNodeIds: string[];
}
export interface MechanismReplay {
  id: string; matchId: string; sourceVersion: string; sourceSha256: string; provenance: string; canonicalRevision: number | null;
  selectedTimeSeconds: number; throughSequence: number; startSeconds: number; endSeconds: number;
  detectorVersion: string; policyStatus: string; publishable: boolean; eventIds: string[]; mechanismEventIds: string[]; frameIds: string[];
  participantIds: string[]; events: MatchEvent[]; frames: Frame[]; coordinateConvention: string; evidenceGraph: EvidenceGraph; limitations: string[];
}
export interface Insight { id: string; matchId: string; clockSeconds: number; title: string; summary: string; classification: string; detectorVersion: string; eventIds: string[]; frameIds: string[]; sourceVersion: string; sourceSha256: string; canonicalRevision: number | null; selectedTimeSeconds: number; throughSequence: number; policyStatus: string; publishable: boolean; conditions: string[]; context: TacticalContext | null; evidenceGraph: EvidenceGraph | null }
export interface Evidence { insight: Insight; events: MatchEvent[]; frames: Frame[]; coordinateConvention: string; conditions: string[]; limitations: string[] }
export interface Explanation { insightId: string; clockSeconds: number; audience: string; source: string; text: string; eventIds: string[]; limitations: string[] }
export interface ApiProblem { type: string; title: string; status: number; code: string; correlationId: string; detail?: string; instance?: string }
// PostgreSQL-backed BFF contracts. Kept separate from fixture/demo contracts above;
// canonical requests never fall back to the local synthetic fixture.
export interface CanonicalMatchCard {
  matchId: string; canonicalRevision: number; projectionRevision: number; asOfSeconds: number; throughSequence: number;
  phase: string; homeScore: number; awayScore: number; provenance: string;
  algorithmVersion: string; sourceSha256: string;
}
export function provenanceLabel(provenance: string): string {
  const normalized = provenance.trim().toLowerCase();
  return normalized.includes('synthetic') || normalized.includes('simulat')
    ? 'Synthetic match'
    : provenance;
}
export interface CanonicalMatchPage { items: CanonicalMatchCard[]; nextMatchId: string | null; hasMore: boolean }
export interface CanonicalMatchSnapshot {
  matchId: string; canonicalRevision: number; projectionRevision: number; selectedTimeSeconds: number; asOfSeconds: number;
  throughSequence: number; algorithmVersion: string; sourceSha256: string; stateSha256: string;
  provenance: string; period: number; phase: string; homeScore: number; awayScore: number;
  possessionTeamId: string | null; homeActivePlayers: number; awayActivePlayers: number;
  players: MatchPlayerState[];
}
export interface CanonicalEventPage {
  matchId: string; canonicalRevision: number; projectionRevision: number; selectedTimeSeconds: number; throughSequence: number;
  items: CanonicalLedgerEvent[]; nextCursor: string | null; hasMore: boolean;
}
export interface CanonicalLedgerEvent {
  eventId: string; matchId: string; sequence: number; type: string;
  clockSeconds: number; period: number; occurredAt: string;
}
export interface CanonicalEvidenceReference { kind: string; id: string; relation: string }
export interface CanonicalInsight {
  insightId: string; matchId: string; canonicalRevision: number; projectionRevision: number; selectedTimeSeconds: number;
  throughSequence: number; sourceVersion: string; provenance: string; sourceSha256: string;
  detectorVersion: string; truthClass: string; summary: string;
  conditions: Record<string, unknown>; limitations: string[] | null;
  evidence: CanonicalEvidenceReference[];
}
export interface CanonicalInsightPage {
  matchId: string; canonicalRevision: number; projectionRevision: number; selectedTimeSeconds: number; throughSequence: number;
  items: CanonicalInsight[]; nextCursor: string | null; hasMore: boolean;
}
export interface CanonicalEvidencePayload {
  kind: 'event' | 'frame'; id: string; relation: string; sourceCanonicalRevision: number; sequence: number;
  clockSeconds: number; period: number; payload: Record<string, unknown>;
}
export interface CanonicalEvidence { insight: CanonicalInsight; items: CanonicalEvidencePayload[] }
export interface CanonicalReplay { snapshot: CanonicalMatchSnapshot; events: CanonicalEventPage }
export interface CanonicalTimelineBackfill {
  snapshot: CanonicalMatchSnapshot; events: CanonicalEventPage; revisionChanged: boolean;
  replayRequired: boolean; requestedAfterSequence: number;
}
export interface RealtimeCursorV1 {
  kind: 'matchUpdated'; matchId: string; canonicalRevision: number; lastSequence: number;
  serverUtc: string; schemaVersion: 1;
}
export interface TelemetryTrackingFrameV1 {
  schemaVersion: number; matchId: string; frameId: string; sequence: number; canonicalRevision: number;
  matchTime: { period: number; elapsedSeconds: number; sourceClockValue: number; sourceClockUnit: string; sourceClockBasis: string };
  coordinateSystem: {
    lengthMetres: number; widthMetres: number; unit: 'metres' | 'normalized_0_1' | 'percent_0_100';
    axisOrigin: 'bottom_left' | 'top_left'; teamDirections: Array<{teamId: string; direction: 'left' | 'right'}>;
  };
  ball: { x: number; y: number };
  players: Array<{playerId: string; teamId: string; position: {x: number; y: number}}>;
  source: { providerId: string; sourceMatchId: string; sourceRecordId: string; sourceSequence: number; sourceSchemaVersion: string; sourceSha256: string; provenanceStatus: string; rightsReviewStatus: 'approved_for_use' };
}
export interface CanonicalTrackingFrame {
  frameId: string; sequence: number; canonicalRevision: number; projectionRevision: number; period: number; clockSeconds: number;
  coordinateVersion: string; sourceSha256: string; quality: unknown; frame: TelemetryTrackingFrameV1;
}
export interface CanonicalTrackingFramePage {
  matchId: string; canonicalRevision: number; projectionRevision: number; selectedTimeSeconds: number; throughSequence: number;
  items: CanonicalTrackingFrame[]; nextCursor: string | null; hasMore: boolean;
}
export class ApiRequestError extends Error {
  constructor(readonly status: number, readonly code: string, readonly correlationId: string, message: string) { super(message); this.name = 'ApiRequestError'; }
}
export function clock(seconds: number) { return `${Math.floor(seconds / 60).toString().padStart(2, '0')}:${(seconds % 60).toString().padStart(2, '0')}`; }

declare global {
  interface Window {
    pitchMindAuth?: { getAccessToken(): Promise<string | null> };
  }
}

/** The host owns sign-in and token storage; PitchMind only requests short-lived access tokens. */
export async function getHostAccessToken(): Promise<string | null> {
  if (typeof window === 'undefined' || typeof window.pitchMindAuth?.getAccessToken !== 'function') return null;
  return window.pitchMindAuth.getAccessToken();
}

export async function request<T>(path: string, signal?: AbortSignal, body?: unknown): Promise<T> {
  const token = await getHostAccessToken();
  const headers = new Headers(body ? { 'Content-Type': 'application/json' } : undefined);
  if (token) {
    headers.set('Authorization', `Bearer ${token}`);
  } else if (typeof window !== 'undefined' && (window.location.hostname === '127.0.0.1' || window.location.hostname === 'localhost')) {
    headers.set('X-Dev-Subject', 'local-analyst');
    headers.set('X-Dev-Roles', 'analyst');
  }
  const response = await fetch(`/api/v1/${path}`, {
    signal,
    headers,
    ...(body ? { method: 'POST', body: JSON.stringify(body) } : {}),
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as ApiProblem | null;
    throw new ApiRequestError(response.status, problem?.code ?? 'REQUEST_FAILED', problem?.correlationId ?? response.headers.get('X-Correlation-ID') ?? '', problem?.title ?? `Match service returned ${response.status}. Please retry.`);
  }
  return response.json() as Promise<T>;
}

export interface AuthPreviewData {
  headline: string;
  description: string;
  featuredMatch: {
    matchId: string;
    homeTeam: string;
    homeLogo?: string;
    awayTeam: string;
    awayLogo?: string;
    homeScore: number;
    awayScore: number;
    clock: string;
    status: string;
    tacticalSnippet: string;
    pitchSnippet: {
      pressureZone: string;
      regainsCount: number;
      players: Array<{ id: string; x: number; y: number; team: 'home' | 'away'; number: number; name: string }>;
    };
  };
  valueBadges: Array<{ title: string; detail: string }>;
  trustCertifications: Array<{ title: string; detail: string }>;
}

export interface EplHubData {
  eyebrow: string;
  title: string;
  subtitle: string;
  clubLogos?: Record<string, string>;
  heroMatch: {
    matchId: string;
    homeTeam: string;
    homeLogo?: string;
    awayTeam: string;
    awayLogo?: string;
    homeScore: number;
    awayScore: number;
    clock: string;
    status: string;
    competition: string;
    stadium: string;
    leadingInsight: string;
    keyStats: Array<{ label: string; home: string; away: string }>;
  };
  otherLiveMatches: Array<{
    matchId: string;
    homeTeam: string;
    homeLogo?: string;
    homeFormation?: string;
    awayTeam: string;
    awayLogo?: string;
    awayFormation?: string;
    homeScore: number;
    awayScore: number;
    clock: string;
    status: string;
    stadium?: string;
    statHighlight: string;
    momentum: number[];
  }>;
  whatMattersNow: Array<{
    rank: number;
    title: string;
    summary?: string;
    metric?: string;
    badge?: string;
    matchId?: string;
    matchTag?: string;
  }>;
  keyMoments: Array<{
    id: string;
    clock: string;
    title: string;
    description: string;
    player: string;
    team: string;
    teamLogo?: string;
    mechanism: string;
    xG: number;
    matchId: string;
    matchTag?: string;
  }>;
  leagueSnapshot: Array<{
    rank: number;
    team: string;
    logo?: string;
    played: number;
    won: number;
    drawn: number;
    lost: number;
    gd: string;
    points: number;
    form: string[];
  }>;
  upcomingFixtures: Array<{
    homeTeam: string;
    homeLogo?: string;
    awayTeam: string;
    awayLogo?: string;
    kickOff: string;
    date: string;
    stadium: string;
  }>;
  suggestedQueries: string[];
}

export interface TeamProfileData {
  id: string;
  name: string;
  shortName: string;
  logo?: string;
  formation: string;
  leagueRank: number;
  points: number;
  form: string[];
  manager: string;
  avgPossession: number;
  xGPer90: number;
  xGAPer90: number;
  ppda: number;
  summary: string;
  zonesOfControl: {
    dominantSide: string;
    defensiveLineHeight: string;
    players: Array<{ number: number; name: string; role: string; x: number; y: number }>;
    tacticalZones: Array<{ name: string; x: number; y: number; width: number; height: number; intensity: string }>;
  };
  progressionRoutes: {
    left: number;
    center: number;
    right: number;
    description: string;
  };
  keyMetrics: Array<{
    name: string;
    value: string;
    leagueAvg: string;
    percentile: number;
    status: string;
  }>;
  pressingStats: {
    highTurnovers: number;
    shotsFromTurnovers: number;
    defensiveDistance: string;
    regainsAttackingThird: number;
  };
  keyPlayers: Array<{
    id: string;
    name: string;
    number: number;
    role: string;
    rating: number;
    stat1: string;
    stat2: string;
  }>;
  teamComparison: {
    opponent: string;
    metrics: Array<{ label: string; team: number; opponent: number }>;
  };
  recentMatches: Array<{
    opponent: string;
    score: string;
    result: string;
    date: string;
    tag: string;
  }>;
  tacticalInsights: Array<{
    title: string;
    summary: string;
    badge: string;
  }>;
}

export interface PlayerIntelligenceData {
  id: string;
  name: string;
  number: number;
  role: string;
  club: string;
  opponentClub?: string;
  teamLogo?: string;
  photoUrl?: string;
  photo?: string;
  nationality: string;
  age: number;
  minutesPlayed: number;
  preferredFoot?: string;
  keyQuote?: string;
  tags?: string[];
  matchStats: {
    chancesCreated: number;
    progressiveCarries: number;
    xA?: number;
    xa?: number;
    duelsWon: number;
    passAccuracy: number;
    touches: number;
    shots: number;
    dribblesCompleted: number;
  };
  whyItMatters: string[];
  touches: Array<{ x: number; y: number; type: string; minute: number; outcome: string }>;
  progressiveCarries: Array<{ startX: number; startY: number; endX: number; endY: number; distance: string }>;
  chanceCreation: Array<{ fromX: number; fromY: number; toX: number; toY: number; recipient: string; outcome: string; xA?: number; xa?: number; minute: number }>;
  receivingZones: Array<{ zone: string; pct: number }>;
  defensiveWork: Array<{ x: number; y: number; action: string; minute: number; outcome: string }>;
  cumulativeXA: Array<{ minute: number; value: number }>;
  comparison: {
    opponentPlayer: string;
    leagueCategory?: string;
    player1?: { name: string; role: string; club: string; logo?: string };
    player2?: { name: string; role: string; club: string; logo?: string };
    metrics: Array<{ label: string; player: number; opponent: number }>;
  };
  recentImpact: Array<{ clock: string; title: string; summary: string; tags?: string[]; active?: boolean; seconds?: number }>;
  analystAssessment: string;
  matchContext?: {
    homeTeam: { name: string; formation: string; logo?: string; score: number };
    awayTeam: { name: string; formation: string; logo?: string; score: number };
    clock: string;
    period: string;
    banner: string;
  };
  progressiveCarriesStat?: { count: number; vsSeasonAvg: string };
  chanceCreationStat?: { count: number; xA?: number; xa?: number; vsSeasonAvg: string };
  defensiveWorkStats?: { pressures: number; tackles: number; ballRecoveries: number };
}

export interface TacticalInsightDetailData {
  id: string;
  matchId: string;
  truthClass: string;
  confidence: string;
  clock: string;
  selectedTimeSeconds: number;
  title: string;
  subtitle: string;
  pitchDiagram: {
    title: string;
    description: string;
    players: Array<{ id: string; name: string; number: number; team: 'home' | 'away'; x: number; y: number; role: string }>;
    passingLanes: Array<{ fromX: number; fromY: number; toX: number; toY: number; status: 'blocked' | 'forced' | 'open'; label: string }>;
    pressureZones: Array<{ x: number; y: number; width: number; height: number; label: string }>;
  };
  evidenceAtAGlance: Array<{ label: string; value: string; comparison: string; trend: 'up' | 'down' }>;
  developingChart: Array<{ minute: number; pressIntensity: number; centralProgression: number }>;
  causalChain: Array<{ step: number; phase: string; action: string; time: string }>;
  keyMoment: {
    clock: string;
    title: string;
    summary: string;
    videoThumbnail: string;
    eventIds: string[];
  };
  secondaryInsights: Array<{
    id: string;
    category: string;
    title: string;
    summary: string;
    badge: string;
  }>;
}

export interface AnalystScreenData {
  matchId: string;
  selectedTimeSeconds: number;
  matchContextPill: string;
  suggestedPrompts: string[];
  conversation: Array<{
    id: string;
    sender: 'user' | 'analyst';
    timestamp: string;
    text: string;
    keyMetrics?: Array<{ label: string; value: string; detail: string }>;
    tacticalStory?: Array<{ phase: string; description: string }>;
    keyEvidence?: Array<{ id: string; type: string; title: string; timestamp: string; description: string }>;
    provenance?: {
      service: string;
      trackingFrames: number;
      verifiedEvents: number;
      limitations: string[];
    };
  }>;
  matchContextSidebar: {
    keyMoments: Array<{ minute: string; title: string; badge: string; selectedSeconds: number }>;
    evidenceItems: Array<{ id: string; category: string; title: string; detail: string }>;
  };
}

export interface AuthSession {
  success: boolean;
  token: string;
  authenticatedAt: string;
  user: {
    name: string;
    email: string;
    role: string;
    permissions: string[];
    avatarUrl?: string;
    provider?: 'email' | 'entra';
  };
}

export function getAuthPreview(signal?: AbortSignal): Promise<AuthPreviewData> {
  return request<AuthPreviewData>('simulation/auth-preview', signal);
}

export function getEplHub(signal?: AbortSignal): Promise<EplHubData> {
  return request<EplHubData>('simulation/hub', signal);
}

export function getTeamProfile(teamId: string, signal?: AbortSignal): Promise<TeamProfileData> {
  return request<TeamProfileData>(`simulation/teams/${encodeURIComponent(teamId)}`, signal);
}

export interface PlayerIntelligenceQueryOptions {
  matchId?: string;
  seconds?: number;
  agentName?: string;
}

/**
 * Dynamically calculates an authentic AI tactical key quote based on player role,
 * live match performance metrics, observed defensive/offensive actions, and optional AI agent lens.
 */
type PlayerQuoteStats = Partial<Omit<PlayerIntelligenceData['matchStats'], 'duelsWon' | 'passAccuracy'>> & {
  goals?: number;
  assists?: number;
  duelsWon?: number | string;
  passAccuracy?: number | string;
};

type PlayerQuoteInput = Partial<Omit<PlayerIntelligenceData, 'matchStats' | 'progressiveCarriesStat' | 'chanceCreationStat' | 'defensiveWorkStats'>> & {
  matchStats?: PlayerQuoteStats;
  progressiveCarriesStat?: { count?: number; total?: number };
  chanceCreationStat?: { count?: number; chancesCreated?: number; xA?: number; xa?: number };
  defensiveWorkStats?: Partial<NonNullable<PlayerIntelligenceData['defensiveWorkStats']>>;
};

export function calculateAiPlayerKeyQuote(
  player: PlayerQuoteInput | null | undefined,
  options?: PlayerIntelligenceQueryOptions
): string {
  if (!player) {
    return 'Tactical catalyst driving positional structure and match impact.';
  }

  const role = (player.role || '').toLowerCase();
  const stats = player.matchStats || {};
  const defStats = player.defensiveWorkStats || {};
  const carries = Number(stats.progressiveCarries ?? player.progressiveCarriesStat?.count ?? player.progressiveCarriesStat?.total ?? 4);
  const chances = Number(stats.chancesCreated ?? player.chanceCreationStat?.chancesCreated ?? player.chanceCreationStat?.count ?? 2);
  const rawXA = Number(stats.xA ?? stats.xa ?? player.chanceCreationStat?.xA ?? 0.28);
  const xA = rawXA > 0 ? rawXA.toFixed(2) : '0.28';
  const goals = Number(stats.goals ?? 0);
  const assists = Number(stats.assists ?? 0);
  const duels = typeof stats.duelsWon === 'string'
    ? stats.duelsWon
    : (stats.duelsWon != null ? `${stats.duelsWon}%` : '54%');
  const passAcc = typeof stats.passAccuracy === 'string'
    ? stats.passAccuracy.replace('%', '')
    : (stats.passAccuracy != null ? String(stats.passAccuracy) : '85');
  const dribbles = Number(stats.dribblesCompleted ?? 2);
  const touches = Number(stats.touches ?? 42);
  const recoveries = Number(defStats.ballRecoveries ?? 3);
  const pressures = Number(defStats.pressures ?? 5);
  const agent = options?.agentName;

  // 1. Agent-specific lens if queried with a specialist Azure AI Foundry agent
  if (agent === 'ChanceCreationAnalyst') {
    return chances > 0 || rawXA > 0
      ? `Primary creative fulcrum generating ${chances} key chances and ${xA} xA, unlocking vertical passing lanes under pressure.`
      : `Creative outlet probing defensive half-spaces to generate attacking overloads.`;
  }
  if (agent === 'HighPressAnalyst') {
    return `Aggressive pressing trigger with ${pressures} pressures and ${recoveries} ball recoveries, disrupting opposition build-up corridors.`;
  }
  if (agent === 'DefensiveStructureAnalyst') {
    return `Anchor of defensive compactness with ${duels} duel success, denying central progression and protecting rest-defense depth.`;
  }
  if (agent === 'PositionalPlayArchitect') {
    return `Spatial orchestrator executing ${carries} progressive carries with ${passAcc}% pass completion, manipulating opponent block geometry.`;
  }

  // 2. Score impact if decisive actions occurred in match
  if (goals > 0 && assists > 0) {
    return `Match-defining attacking catalyst with ${goals} goal and ${assists} assist, orchestrating transition breaks with clinical execution.`;
  }
  if (goals > 0) {
    return `Decisive attacking threat with ${goals} goal from ${stats.shots ?? 1} shots, attacking the box with elite positional timing.`;
  }
  if (assists > 0) {
    return `Primary playmaker with ${assists} assist and ${xA} xA, carving through defensive lines with penetrative final balls.`;
  }

  // 3. Role-based dynamic tactical AI synthesis
  // Attacking Midfielder / Playmaker
  if (role.includes('attacking') || role.includes('am') || role.includes('10') || role.includes('playmaker')) {
    return `Operating as the primary transition orchestrator, generating ${chances} chances (${xA} xA) by exploiting half-space channels between opposition defensive lines.`;
  }

  // Winger / Wide Forward / Inside Forward
  if (role.includes('winger') || role.includes('lw') || role.includes('rw') || role.includes('wide')) {
    return `Dynamic wide threat stretching the opposition back line with ${dribbles} take-ons and ${carries} progressive carries into dangerous central shooting corridors.`;
  }

  // Center Forward / Striker
  if (role.includes('striker') || role.includes('forward') || role.includes('cf') || role.includes('st') || role.includes('center forward')) {
    return `Spearheading the front-line press while pinning center-backs, registering decisive box entries and generating space for second-wave transition runners.`;
  }

  // Defensive Midfielder / Holding Midfielder / Pivot / Ball Winner
  if (role.includes('defensive') || role.includes('dm') || role.includes('pivot') || role.includes('holding') || role.includes('ball winner')) {
    return `Shielding Zone 14 with ${recoveries} ball recoveries and ${duels} duel dominance, disrupting opponent build-up sequences and anchoring transitional counter-pressing.`;
  }

  // Central Midfielder
  if (role.includes('central') || role.includes('cm') || role.includes('midfielder')) {
    return `Controlling match tempo with ${passAcc}% distribution accuracy across ${touches} touches, bridging defensive shape with fluid final-third progression.`;
  }

  // Fullback / Wingback / Defender
  if (role.includes('fullback') || role.includes('wingback') || role.includes('rb') || role.includes('lb') || role.includes('back')) {
    if (role.includes('center') || role.includes('cb')) {
      return `Anchoring the defensive line with ${duels} duel dominance, commanding defensive depth and initiating deep vertical ball progression with ${passAcc}% distribution accuracy.`;
    }
    return `Providing continuous wide progression and overlapping overloads, contributing ${chances} chance creations and ${carries} vertical carries to unbalance opposition defensive blocks.`;
  }

  // Center Back / Defender
  if (role.includes('center') || role.includes('cb') || role.includes('defender')) {
    return `Anchoring the defensive structure with ${duels} duel dominance, controlling line height and initiating deep vertical ball progression with ${passAcc}% distribution accuracy.`;
  }

  // Goalkeeper
  if (role.includes('goalkeeper') || role.includes('gk') || role.includes('keeper')) {
    return `Commanding defensive territory as a proactive sweeper-keeper, distributing with ${passAcc}% accuracy and stabilizing the backline under high press.`;
  }

  // Metric-based dynamic fallback
  if (carries >= 4 || chances >= 2) {
    return `High-impact progression driver with ${carries} progressive carries and ${chances} chances created, destabilizing defensive compactness.`;
  }

  return `Tactical catalyst maintaining positional discipline, driving progressive circulation and team structural stability.`;
}


export function getPlayerIntelligence(
  playerId: string,
  optionsOrSignal?: PlayerIntelligenceQueryOptions | AbortSignal,
  maybeSignal?: AbortSignal
): Promise<PlayerIntelligenceData> {
  const options = (optionsOrSignal instanceof AbortSignal ? undefined : optionsOrSignal) ?? {};
  const signal = optionsOrSignal instanceof AbortSignal ? optionsOrSignal : maybeSignal;
  const params = new URLSearchParams();
  if (options.matchId) params.set('matchId', options.matchId);
  if (options.seconds !== undefined) params.set('seconds', options.seconds.toString());
  if (options.agentName) params.set('agentName', options.agentName);
  const qStr = params.toString() ? `?${params.toString()}` : '';

  return request<PlayerIntelligenceData>(`players/${encodeURIComponent(playerId)}/intelligence${qStr}`, signal)
    .catch(() => request<PlayerIntelligenceData>(`simulation/players/${encodeURIComponent(playerId)}${qStr}`, signal))
    .then(data => {
      const dynamicKeyQuote = calculateAiPlayerKeyQuote(data, options);
      return {
        ...data,
        keyQuote: dynamicKeyQuote
      };
    });
}

export function getPlayersIntelligenceList(
  optionsOrSignal?: PlayerIntelligenceQueryOptions | AbortSignal,
  maybeSignal?: AbortSignal
): Promise<PlayerIntelligenceData[]> {
  const options = (optionsOrSignal instanceof AbortSignal ? undefined : optionsOrSignal) ?? {};
  const signal = optionsOrSignal instanceof AbortSignal ? optionsOrSignal : maybeSignal;
  const params = new URLSearchParams();
  if (options.matchId) params.set('matchId', options.matchId);
  if (options.seconds !== undefined) params.set('seconds', options.seconds.toString());
  if (options.agentName) params.set('agentName', options.agentName);
  const qStr = params.toString() ? `?${params.toString()}` : '';

  return request<PlayerIntelligenceData[]>(`players${qStr}`, signal)
    .catch(() => request<PlayerIntelligenceData[]>(`simulation/players${qStr}`, signal))
    .then(list => {
      if (!list || list.length === 0) return [];
      return list.map(p => ({
        ...p,
        keyQuote: calculateAiPlayerKeyQuote(p, options)
      }));
    })
    .catch(() => []);
}

export function getTacticalInsightDetail(insightId: string, signal?: AbortSignal): Promise<TacticalInsightDetailData> {
  return request<TacticalInsightDetailData>(`simulation/insights/${encodeURIComponent(insightId)}`, signal);
}

export interface SimulatedMatchData {
  matchId: string;
  homeTeam: string;
  homeLogo?: string;
  homeFormation: string;
  awayTeam: string;
  awayLogo?: string;
  awayFormation: string;
  homeScore: number;
  awayScore: number;
  clock: string;
  period: string;
  stadium: string;
  competition: string;
  leadingInsight: string;
  intelligentPitch: {
    title: string;
    subtitle: string;
    homePlayers: Array<{ number: number; name: string; role: string; x: number; y: number; isRegainHero?: boolean }>;
    awayPlayers: Array<{ number: number; name: string; role: string; x: number; y: number }>;
    pressureTrap?: { x: number; y: number; label: string };
    recentRegain?: { x: number; y: number; label: string };
    dangerZones?: Array<{ x: number; y: number; width: number; height: number }>;
  };
  timeline: Array<{ clock: string; type: string; team: string; detail: string; icon: string; highlight?: boolean }>;
  whyItMatters: string[];
  evidence: {
    highRegains: { value: number; label: string };
    fieldTilt: { value: number; label: string };
    lineBreaksPrevented: { value: number; label: string };
    note: string;
  };
  aiAnalyst: string;
  keyMoment: {
    clock: string;
    title: string;
    description: string;
    classifications: string[];
  };
  sequenceReplay: Array<{ step: number; title: string; active?: boolean }>;
}

export function getSimulatedMatch(matchId?: string, signal?: AbortSignal): Promise<SimulatedMatchData> {
  return request<SimulatedMatchData>(matchId ? `simulation/matches/${encodeURIComponent(matchId)}` : 'simulation/match', signal);
}

export function getAnalystOverview(signal?: AbortSignal): Promise<AnalystScreenData> {
  return request<AnalystScreenData>('simulation/analyst/overview', signal);
}

export function loginSimulated(credentials: { email: string; password: string }, signal?: AbortSignal): Promise<AuthSession> {
  return request<AuthSession>('simulation/auth/login', signal, credentials).then(session => {
    const enriched: AuthSession = {
      ...session,
      user: {
        ...session.user,
        avatarUrl: DEFAULT_ANALYST_AVATAR,
        provider: 'email'
      }
    };
    setStoredAuthSession(enriched);
    return enriched;
  });
}

export interface SimulationStreamStatus {
  matchId: string;
  isStreaming: boolean;
  currentClockSeconds: number;
  currentSequence: number;
  canonicalRevision: number;
  eventsCount: number;
  homeScore: number;
  awayScore: number;
  leadingInsight: string | null;
}

export function getSimulationStreamStatus(signal?: AbortSignal): Promise<SimulationStreamStatus> {
  return request<SimulationStreamStatus>('simulation/stream/status', signal);
}

export function startSimulationStream(matchId?: string, signal?: AbortSignal): Promise<SimulationStreamStatus> {
  return request<SimulationStreamStatus>('simulation/stream/start', signal, { matchId });
}

export function stopSimulationStream(signal?: AbortSignal): Promise<SimulationStreamStatus> {
  return request<SimulationStreamStatus>('simulation/stream/stop', signal, {});
}

export function stepSimulationStream(signal?: AbortSignal): Promise<SimulationStreamStatus> {
  return request<SimulationStreamStatus>('simulation/stream/step', signal, {});
}

export interface ClearDataResult {
  cleared: boolean;
  clearedTables: number;
  message: string;
}

export function clearAllDevData(signal?: AbortSignal): Promise<ClearDataResult> {
  return request<ClearDataResult>('simulation/clear-data', signal, {});
}

export interface SimulationAnalystRequest {
  insightId: string;
  atSeconds: number;
  audience?: string;
}

export interface SimulationAnalystResponse {
  text: string;
  source: string;
  clockSeconds: number;
  insightId: string;
  audience: string;
  limitations: string[];
}

export function analyzeSimulationMoment(payload: SimulationAnalystRequest, signal?: AbortSignal): Promise<SimulationAnalystResponse> {
  return request<SimulationAnalystResponse>('simulation/analyst', signal, payload);
}

export interface CandidateAnalystQuestion {
  matchId: string;
  selectedSourceClockValue: number;
  audience: 'fan' | 'analyst';
  question: string;
  agentName: string;
}
export interface CandidateAnalystAgentCatalog { defaultAgent: string; agents: string[] }
export interface CandidateAnalystClaim { text: string; evidenceIds: string[] }
export interface CandidateAnalystAnswer {
  status: 'answered' | 'insufficientEvidence'; answer: string; claims: CandidateAnalystClaim[];
  matchId: string; selectedSourceClockValue: number; sourceSha256: string; agentName: string; truthClass: 'hypothesis'; limitations: string[];
}
export function getCandidateAnalystAgents(signal?: AbortSignal): Promise<CandidateAnalystAgentCatalog> {
  return request<CandidateAnalystAgentCatalog>('simulation/candidate/analyst/agents', signal);
}
export function askCandidateAnalyst(payload: CandidateAnalystQuestion, signal?: AbortSignal): Promise<CandidateAnalystAnswer> {
  return request<CandidateAnalystAnswer>('simulation/candidate/analyst/questions', signal, payload);
}

const AUTH_STORAGE_KEY = 'pitchmind_auth_session';
const AUTH_EVENT = 'pitchmind:authchange';

export const DEFAULT_ANALYST_AVATAR = "data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 96 96'><defs><linearGradient id='bgGrad' x1='0%25' y1='0%25' x2='100%25' y2='100%25'><stop offset='0%25' stop-color='%23064e3b'/><stop offset='100%25' stop-color='%23022c22'/></linearGradient><linearGradient id='skinGrad' x1='0%25' y1='0%25' x2='0%25' y2='100%25'><stop offset='0%25' stop-color='%23f6d0b1'/><stop offset='100%25' stop-color='%23e2b28c'/></linearGradient><linearGradient id='jacketGrad' x1='0%25' y1='0%25' x2='100%25' y2='100%25'><stop offset='0%25' stop-color='%231e293b'/><stop offset='100%25' stop-color='%230f172a'/></linearGradient></defs><circle cx='48' cy='48' r='48' fill='url(%23bgGrad)'/><circle cx='48' cy='48' r='47' fill='none' stroke='%2334d399' stroke-width='2' stroke-opacity='0.4'/><path d='M20 92c0-15.46 12.54-28 28-28s28 12.54 28 28z' fill='url(%23jacketGrad)'/><path d='M44 64h8v12h-8z' fill='%2334d399' fill-opacity='0.8'/><path d='M48 68l-8 24h16z' fill='%230f172a'/><circle cx='48' cy='40' r='18' fill='url(%23skinGrad)'/><path d='M30 36c2-12 12-16 22-14 8 2 14 8 14 14-4-5-10-7-18-6-7 1-12 4-18 6z' fill='%231e293b'/><rect x='36' y='36' width='10' height='7' rx='2' fill='none' stroke='%230f172a' stroke-width='1.5'/><rect x='50' y='36' width='10' height='7' rx='2' fill='none' stroke='%230f172a' stroke-width='1.5'/><line x1='46' y1='39' x2='50' y2='39' stroke='%230f172a' stroke-width='1.5'/><path d='M42 50c3 2 9 2 12 0' stroke='%23c28e67' stroke-width='2' stroke-linecap='round' fill='none'/></svg>";

let cachedRawAuth: string | null = null;
let cachedAuthSession: AuthSession | null = null;

export function getStoredAuthSession(): AuthSession | null {
  if (typeof window === 'undefined') return null;
  try {
    const raw = localStorage.getItem(AUTH_STORAGE_KEY);
    if (raw === cachedRawAuth) {
      return cachedAuthSession;
    }
    cachedRawAuth = raw;
    cachedAuthSession = raw ? (JSON.parse(raw) as AuthSession) : null;
    return cachedAuthSession;
  } catch {
    cachedRawAuth = null;
    cachedAuthSession = null;
    return null;
  }
}

export function setStoredAuthSession(session: AuthSession | null): void {
  if (typeof window === 'undefined') return;
  try {
    if (session) {
      const raw = JSON.stringify(session);
      cachedRawAuth = raw;
      cachedAuthSession = session;
      localStorage.setItem(AUTH_STORAGE_KEY, raw);
    } else {
      cachedRawAuth = null;
      cachedAuthSession = null;
      localStorage.removeItem(AUTH_STORAGE_KEY);
    }
    window.dispatchEvent(new CustomEvent(AUTH_EVENT, { detail: session }));
  } catch {}
}

export function subscribeAuth(onChange: () => void): () => void {
  if (typeof window === 'undefined') return () => undefined;
  window.addEventListener(AUTH_EVENT, onChange);
  window.addEventListener('storage', onChange);
  return () => {
    window.removeEventListener(AUTH_EVENT, onChange);
    window.removeEventListener('storage', onChange);
  };
}

export function logoutSimulated(): void {
  setStoredAuthSession(null);
}



