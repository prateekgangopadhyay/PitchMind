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
    ? 'Simulated EPL-context match'
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
  teamLogo?: string;
  nationality: string;
  age: number;
  minutesPlayed: number;
  matchStats: {
    chancesCreated: number;
    progressiveCarries: number;
    xA: number;
    duelsWon: number;
    passAccuracy: number;
    touches: number;
    shots: number;
    dribblesCompleted: number;
  };
  whyItMatters: string[];
  touches: Array<{ x: number; y: number; type: string; minute: number; outcome: string }>;
  progressiveCarries: Array<{ startX: number; startY: number; endX: number; endY: number; distance: string }>;
  chanceCreation: Array<{ fromX: number; fromY: number; toX: number; toY: number; recipient: string; outcome: string; xA: number; minute: number }>;
  receivingZones: Array<{ zone: string; pct: number }>;
  defensiveWork: Array<{ x: number; y: number; action: string; minute: number; outcome: string }>;
  cumulativeXA: Array<{ minute: number; value: number }>;
  comparison: {
    opponentPlayer: string;
    metrics: Array<{ label: string; player: number; opponent: number }>;
  };
  recentImpact: Array<{ clock: string; title: string; summary: string }>;
  analystAssessment: string;
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
    provider?: 'google' | 'email';
  };
}

export function getAuthPreview(signal?: AbortSignal): Promise<AuthPreviewData> {
  return request<AuthPreviewData>('simulation/auth-preview', signal);
}

export function getEplHub(signal?: AbortSignal): Promise<EplHubData> {
  return request<EplHubData>('simulation/hub', signal);
}

export function getTeamProfile(teamId?: string, signal?: AbortSignal): Promise<TeamProfileData> {
  return request<TeamProfileData>(`simulation/teams/${teamId ? encodeURIComponent(teamId) : 'arsenal'}`, signal);
}

export function getPlayerIntelligence(playerId?: string, signal?: AbortSignal): Promise<PlayerIntelligenceData> {
  return request<PlayerIntelligenceData>(`simulation/players/${playerId ? encodeURIComponent(playerId) : 'saka'}`, signal);
}

export function getTacticalInsightDetail(insightId?: string, signal?: AbortSignal): Promise<TacticalInsightDetailData> {
  return request<TacticalInsightDetailData>(`simulation/insights/${insightId ? encodeURIComponent(insightId) : 'arsenal-press-city'}`, signal);
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

export function loginWithGoogleSimulated(): Promise<AuthSession> {
  const session: AuthSession = {
    success: true,
    token: `pm-google-${Date.now()}`,
    authenticatedAt: new Date().toISOString(),
    user: {
      name: 'Prateek Gangopadhyay',
      email: 'prateek.gangopadhyay@gmail.com',
      role: 'Lead Tactical Analyst',
      permissions: ['MatchRead', 'MatchReplay', 'AnalystAction', 'TeamProfileRead', 'FullIntelligence'],
      avatarUrl: DEFAULT_ANALYST_AVATAR,
      provider: 'google'
    }
  };
  setStoredAuthSession(session);
  return Promise.resolve(session);
}

export function logoutSimulated(): void {
  setStoredAuthSession(null);
}



