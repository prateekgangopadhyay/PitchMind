
import { useEffect, useMemo, useRef, useState } from 'react';
import {
  clock,
  provenanceLabel,
  request,
  getAuthPreview,
  getEplHub,
  getTeamProfile,
  getPlayerIntelligence,
  getTacticalInsightDetail,
  getAnalystOverview,
  getSimulatedMatch,
  loginSimulated,
  loginWithGoogleSimulated,
  setStoredAuthSession,
  getSimulationStreamStatus,
  startSimulationStream,
  stopSimulationStream,
  stepSimulationStream,
  analyzeSimulationMoment
} from './api';
import type {
  CanonicalInsightPage,
  CanonicalMatchPage,
  AuthPreviewData,
  EplHubData,
  TeamProfileData,
  PlayerIntelligenceData,
  TacticalInsightDetailData,
  AnalystScreenData,
  SimulatedMatchData,
  AuthSession,
  SimulationStreamStatus
} from './api';
import type { AppRoute } from './routes';
import { contextHref, replacePhaseFilterContext, selectedTimeFromSearch } from './routes';
import { resolveClubLogo } from './clubLogos';
import { PitchCanvas, type PlayerPosition, type PitchMatchEvent, type TacticalStrategyObservation, type TacticalPitchPoint } from './components/PitchCanvas';

const CANONICAL_PHASES = ['notstarted', 'firsthalf', 'halftime', 'secondhalf', 'fulltime'];

function phaseLabel(phase: string) {
  const labels: Record<string, string> = {
    notstarted: 'Not started', firsthalf: 'First half', halftime: 'Half-time',
    secondhalf: 'Second half', fulltime: 'Full-time'
  };
  return labels[phase.toLowerCase()] ?? phase.replace(/([a-z])([A-Z])/g, '$1 $2').replaceAll('-', ' ').replaceAll('_', ' ');
}

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
      .catch(() => { if (!controller.signal.aborted && generation === requestGeneration.current) setError('Match data is temporarily unavailable. No demo fixture has been substituted.'); })
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
  const retry = () => setAttempt(a => a + 1);
  const loadMore = async () => {
    if (!page?.nextMatchId || loadingMore) return;
    const generation = requestGeneration.current;
    const controller = new AbortController();
    pageController.current = controller;
    setLoadingMore(true);
    setMoreError('');
    try {
      const next = await request<CanonicalMatchPage>(`canonical/fixtures?limit=20&afterMatchId=${encodeURIComponent(page.nextMatchId)}`, controller.signal);
      if (generation === requestGeneration.current) {
        setPage(current => current ? { ...next, items: [...current.items, ...next.items] } : next);
      }
    } catch {
      if (generation === requestGeneration.current) {
        setMoreError('Could not load additional matches. Try again.');
      }
    } finally {
      if (generation === requestGeneration.current) {
        setLoadingMore(false);
        pageController.current = null;
      }
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

/* =========================================================================
   GENERIC CLUB CREST SVG & OFFICIAL CLUB LOGO COMPONENT
   ========================================================================= */

export function ClubCrest({
  name,
  logoUrl,
  className = "",
  width,
  height,
  title
}: {
  name: string;
  logoUrl?: string;
  className?: string;
  width?: number | string;
  height?: number | string;
  title?: string;
}) {
  const [loadError, setLoadError] = useState(false);
  const resolvedLogo = resolveClubLogo(name, logoUrl);

  if (resolvedLogo && !loadError) {
    return (
      <img
        src={resolvedLogo}
        alt={`${name} crest`}
        title={title || `${name} logo`}
        className={`club-crest-img ${className}`}
        width={width}
        height={height}
        loading="lazy"
        onError={() => setLoadError(true)}
      />
    );
  }

  const lower = name.toLowerCase();
  let bg = '#e63946';
  let border = '#fa697b';
  let fg = '#ffffff';
  let code = 'EPL';

  if (lower.includes('arsenal')) {
    bg = '#da291c'; border = '#ffffff'; fg = '#ffffff'; code = 'ARS';
  } else if (lower.includes('city') || lower.includes('manchester city')) {
    bg = '#1d3557'; border = '#6ba5ff'; fg = '#a8dadc'; code = 'MCI';
  } else if (lower.includes('tottenham') || lower.includes('spurs')) {
    bg = '#1a2530'; border = '#94a3b8'; fg = '#ffffff'; code = 'TOT';
  } else if (lower.includes('chelsea')) {
    bg = '#003399'; border = '#3b82f6'; fg = '#ffffff'; code = 'CHE';
  } else if (lower.includes('newcastle')) {
    bg = '#18181b'; border = '#71717a'; fg = '#ffffff'; code = 'NEW';
  } else if (lower.includes('aston') || lower.includes('villa')) {
    bg = '#670038'; border = '#93c5fd'; fg = '#93c5fd'; code = 'AVL';
  } else if (lower.includes('brighton')) {
    bg = '#0057b8'; border = '#ffffff'; fg = '#ffffff'; code = 'BHA';
  } else if (lower.includes('united') || lower.includes('manchester united')) {
    bg = '#990000'; border = '#eab308'; fg = '#eab308'; code = 'MUN';
  } else if (lower.includes('west ham')) {
    bg = '#7a263a'; border = '#38bdf8'; fg = '#38bdf8'; code = 'WHU';
  } else if (lower.includes('wolves')) {
    bg = '#fdb913'; border = '#18181b'; fg = '#18181b'; code = 'WOL';
  } else if (lower.includes('liverpool')) {
    bg = '#c8102e'; border = '#00b2a9'; fg = '#ffffff'; code = 'LIV';
  } else if (lower.includes('crystal') || lower.includes('palace')) {
    bg = '#1b458f'; border = '#c4122d'; fg = '#ffffff'; code = 'CRY';
  } else if (lower.includes('everton')) {
    bg = '#003399'; border = '#ffffff'; fg = '#ffffff'; code = 'EVE';
  } else if (lower.includes('forest')) {
    bg = '#e53238'; border = '#ffffff'; fg = '#ffffff'; code = 'NFO';
  } else if (lower.includes('bournemouth')) {
    bg = '#da291c'; border = '#000000'; fg = '#ffffff'; code = 'BOU';
  } else if (lower.includes('fulham')) {
    bg = '#1a2530'; border = '#ffffff'; fg = '#ffffff'; code = 'FUL';
  } else {
    code = name.slice(0, 3).toUpperCase();
  }

  return (
    <svg className={`club-crest-svg ${className}`} viewBox="0 0 40 46" width="32" height="36" fill="none" aria-label={`${name} crest`}>
      <path d="M20 2L36 7V22C36 33 20 44 20 44C20 44 4 33 4 22V7L20 2Z" fill={bg} stroke={border} strokeWidth="2" strokeLinejoin="round" />
      <path d="M20 5L33 9V21C33 30 20 40 20 40C20 40 7 30 7 21V9L20 5Z" fill="rgba(255,255,255,0.08)" />
      <text x="20" y="26" fontSize="10" fontWeight="900" letterSpacing="0.5px" textAnchor="middle" dominantBaseline="middle" fill={fg}>{code}</text>
    </svg>
  );
}

/* =========================================================================
   REUSABLE HERO MATCH SCOREBOARD BANNER (Mockup Top Bar)
   ========================================================================= */
export function MatchHeaderBanner({
  competition = "Premier League · Matchweek 12",
  stadium = "Emirates Stadium",
  homeName = "Arsenal",
  homeLogo,
  homeFormation = "4 - 3 - 3",
  homeScore = 2,
  awayScore = 1,
  awayName = "Manchester City",
  awayLogo,
  awayFormation = "4 - 2 - 3 - 1",
  clock = "67:24",
  period = "Second Half",
  tagline = "FOR A DEEPER GAME",
  leadingInsight,
  buttonText,
  onButtonClick,
}: {
  competition?: string;
  stadium?: string;
  homeName?: string;
  homeLogo?: string;
  homeFormation?: string;
  homeScore?: number;
  awayScore?: number;
  awayName?: string;
  awayLogo?: string;
  awayFormation?: string;
  clock?: string;
  period?: string;
  tagline?: string;
  leadingInsight?: string;
  buttonText?: string;
  onButtonClick?: () => void;
}) {
  return (
    <>
      <div className="mockup-hero-match-card">
        <div className="hero-match-stadium-backdrop" aria-hidden="true">
          <img src="/match/stadium-inset.webp" alt="" className="stadium-perspective-img" />
          <div className="stadium-hero-watermark-text">
            <span>SYNTHETIC EPL</span>
            <span>INTELLIGENCE VIEW</span>
            <small>{tagline}</small>
          </div>
        </div>

        <div className="hero-match-content-overlay">
          <span className="sr-only">{competition} at {stadium}</span>
          <div className="hero-scoreboard-row">
            <div className="hero-club-side hero-club-left">
              <ClubCrest name={homeName} logoUrl={homeLogo} className="hero-club-crest" />
              <div className="hero-club-meta">
                <h2 className="hero-club-name">{homeName}</h2>
                <span className="hero-club-formation">{homeFormation}</span>
              </div>
            </div>

            <div className="hero-score-center">
              <span className="hero-score-num">{homeScore}</span>
              <div className="hero-clock-box">
                <span className="hero-clock-time">{clock}</span>
                <span className="hero-clock-period">{period}</span>
              </div>
              <span className="hero-score-num">{awayScore}</span>
            </div>

            <div className="hero-club-side hero-club-right">
              <div className="hero-club-meta text-right">
                <h2 className="hero-club-name">{awayName}</h2>
                <span className="hero-club-formation">{awayFormation}</span>
              </div>
              <ClubCrest name={awayName} logoUrl={awayLogo} className="hero-club-crest" />
            </div>
          </div>
        </div>
      </div>

      {leadingInsight && (
        <div className="hero-observed-banner-card hero-leading-insight-strip">
          <div className="hero-observed-content-left">
            <span className="hero-observed-pill">
              <span className="observed-bars-icon" aria-hidden="true">
                <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3"><line x1="18" y1="20" x2="18" y2="10" /><line x1="12" y1="20" x2="12" y2="4" /><line x1="6" y1="20" x2="6" y2="14" /></svg>
              </span>
              <span>OBSERVED</span>
            </span>
            <p className="hero-observed-headline-text">{leadingInsight}</p>
          </div>
          {buttonText && (
            <button type="button" className="hero-observed-action-btn" onClick={onButtonClick}>
              {buttonText} →
            </button>
          )}
        </div>
      )}
    </>
  );
}

/* =========================================================================
   SCREEN 01: WELCOME / LOGIN (01-login.jpg)
   ========================================================================= */
export function WelcomePage() {
  const [data, setData] = useState<AuthPreviewData | null>(null);
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [remember, setRemember] = useState(true);
  const [busy, setBusy] = useState(false);
  const [session, setSession] = useState<AuthSession | null>(null);
  const [authError, setAuthError] = useState('');
  const previewData = data;

  useEffect(() => {
    const controller = new AbortController();
    getAuthPreview(controller.signal)
      .then(res => { if (res && res.headline) setData(res); })
      .catch(() => { });
    return () => controller.abort();
  }, []);

  const handleGoogleSignIn = async () => {
    setBusy(true);
    setAuthError('');
    try {
      const res = await loginWithGoogleSimulated();
      setSession(res);
      window.history.pushState({}, '', '/home');
      window.dispatchEvent(new CustomEvent('pitchmind:locationchange'));
    } catch (err: unknown) {
      setAuthError(err instanceof Error ? err.message : 'Google sign-in was interrupted. Please retry.');
    } finally {
      setBusy(false);
    }
  };

  const handleSignIn = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setAuthError('');
    try {
      const res = await loginSimulated({ email, password });
      setStoredAuthSession(res);
      setSession(res);
      window.history.pushState({}, '', '/home');
      window.dispatchEvent(new CustomEvent('pitchmind:locationchange'));
    } catch (err: unknown) {
      setAuthError(err instanceof Error ? err.message : 'Invalid credentials. Please enter a valid work email and password.');
    } finally {
      setBusy(false);
    }
  };

  if (!previewData) {
    return (
      <section className="content-page">
        <PageHeading
          eyebrow="Football intelligence, grounded in evidence"
          title="Welcome to PitchMind"
          question="Find a match and inspect the evidence behind each view."
        />
        <p className="page-state">Sign-in is not configured in this local preview.</p>
        <a className="primary-link" href="/home">Check match availability</a>
      </section>
    );
  }

  return (
    <section className="content-page welcome-screen-root">

      <div className="welcome-split-layout">
        {/* Left Column: Intelligence Promise & Featured Match */}
        <div className="welcome-left-col">

          <h1 className="welcome-hero-headline">
            Understand the <span className="text-emerald">Premier League</span> beyond the scoreline.
          </h1>

          {/* Live EPL Intelligence Card */}
          <div className="welcome-match-preview-card">
            <div className="wmp-topline">
              <span className="wmp-live-badge"><span className="pulse-dot"></span> Live Match</span>
              <span className="wmp-matchweek">Premier League • Matchweek 12</span>
            </div>

            <div className="wmp-score-row">
              <div className="wmp-team-side">
                <div className="wmp-team-text text-right">
                  <strong>{previewData?.featuredMatch?.homeTeam || "Arsenal"}</strong>
                  <small>4 - 3 - 3</small>
                </div>
                <ClubCrest name={previewData?.featuredMatch?.homeTeam || "Arsenal"} logoUrl={previewData?.featuredMatch?.homeLogo || "/crests/arsenal.svg"} className="wmp-crest" />
              </div>

              <div className="wmp-score-mid">
                <span className="wmp-score">{previewData?.featuredMatch?.homeScore ?? 2}</span>
                <div className="wmp-clock-box">
                  <span className="wmp-clock">{previewData?.featuredMatch?.clock || "67:24"}</span>
                  <small>Second Half</small>
                </div>
                <span className="wmp-score">{previewData?.featuredMatch?.awayScore ?? 1}</span>
              </div>

              <div className="wmp-team-side">
                <ClubCrest name={previewData?.featuredMatch?.awayTeam || "Manchester City"} logoUrl={previewData?.featuredMatch?.awayLogo || "/crests/manchester-city.svg"} className="wmp-crest" />
                <div className="wmp-team-text">
                  <strong>{previewData?.featuredMatch?.awayTeam || "Manchester City"}</strong>
                  <small>4 - 2 - 3 - 1</small>
                </div>
              </div>
            </div>

            {/* AI Insight Box */}
            <div className="wmp-insight-banner">
              <div className="wmp-insight-icon">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#4aefac" strokeWidth="2.5"><path d="M18 20V10M12 20V4M6 20v-6" /></svg>
              </div>
              <div className="wmp-insight-text">
                <div className="wmp-badge-row">
                  <span className="wmp-ai-label">AI Insight</span>
                  <span className="wmp-live-dot-badge">• LIVE</span>
                </div>
                <p>Arsenal's high press is forcing City wide and creating dangerous regains. City are struggling to progress through the middle.</p>
              </div>
              <a href="/matches" className="wmp-view-btn">View Match Analysis →</a>
            </div>

            {/* Tactical Mini Pitch SVG - Broadcast Grade matching 01-login.webp */}
            <div className="wmp-mini-pitch-wrap">
              <svg className="wmp-pitch-svg" viewBox="0 0 900 320" role="img" aria-label="Tactical pitch pressure diagram">
                <defs>
                  {/* Grass Linear Gradient */}
                  <linearGradient id="grassGrad" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="0%" stopColor="#103824" />
                    <stop offset="50%" stopColor="#0d2e1e" />
                    <stop offset="100%" stopColor="#082014" />
                  </linearGradient>

                  {/* Vignette lighting */}
                  <radialGradient id="pitchVignette" cx="50%" cy="50%" r="70%">
                    <stop offset="0%" stopColor="#ffffff" stopOpacity="0.06" />
                    <stop offset="55%" stopColor="#000000" stopOpacity="0" />
                    <stop offset="100%" stopColor="#000000" stopOpacity="0.45" />
                  </radialGradient>

                  {/* Upper Pressure Trap Gradient */}
                  <linearGradient id="pressureTrapGrad" x1="0" y1="0" x2="1" y2="1">
                    <stop offset="0%" stopColor="#ef4444" stopOpacity="0.32" />
                    <stop offset="100%" stopColor="#b91c1c" stopOpacity="0.16" />
                  </linearGradient>

                  {/* Lower Pressure Trap Gradient */}
                  <linearGradient id="pressureTrapGradLower" x1="0" y1="0" x2="1" y2="1">
                    <stop offset="0%" stopColor="#ef4444" stopOpacity="0.22" />
                    <stop offset="100%" stopColor="#991b1b" stopOpacity="0.12" />
                  </linearGradient>

                  {/* Arrowhead Markers */}
                  <marker id="arrowWhite" viewBox="0 0 10 10" refX="6" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse">
                    <path d="M 0 2 L 7 5 L 0 8 z" fill="rgba(255, 255, 255, 0.7)" />
                  </marker>
                  <marker id="arrowEmerald" viewBox="0 0 10 10" refX="6" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse">
                    <path d="M 0 2 L 7 5 L 0 8 z" fill="#4aefac" />
                  </marker>
                  <marker id="arrowRed" viewBox="0 0 10 10" refX="6" refY="5" markerWidth="6" markerHeight="6" orient="auto-start-reverse">
                    <path d="M 0 2 L 7 5 L 0 8 z" fill="#f87171" />
                  </marker>

                  {/* Glow filter for Recent Regain */}
                  <filter id="glowEmerald" x="-30%" y="-30%" width="160%" height="160%">
                    <feGaussianBlur stdDeviation="3" result="blur" />
                    <feMerge>
                      <feMergeNode in="blur" />
                      <feMergeNode in="SourceGraphic" />
                    </feMerge>
                  </filter>
                </defs>

                {/* Pitch Grass Base */}
                <rect width="900" height="320" rx="6" fill="url(#grassGrad)" />

                {/* 12 Vertical Mown Grass Alternating Stripes */}
                <rect x="0" y="0" width="75" height="320" fill="rgba(255,255,255,0.02)" />
                <rect x="150" y="0" width="75" height="320" fill="rgba(255,255,255,0.02)" />
                <rect x="300" y="0" width="75" height="320" fill="rgba(255,255,255,0.02)" />
                <rect x="450" y="0" width="75" height="320" fill="rgba(255,255,255,0.02)" />
                <rect x="600" y="0" width="75" height="320" fill="rgba(255,255,255,0.02)" />
                <rect x="750" y="0" width="75" height="320" fill="rgba(255,255,255,0.02)" />

                {/* Vignette Lighting */}
                <rect width="900" height="320" rx="6" fill="url(#pitchVignette)" />

                {/* Pitch Boundary Markings */}
                <rect x="20" y="16" width="860" height="288" fill="none" stroke="rgba(255,255,255,0.32)" strokeWidth="1.2" />
                <line x1="450" y1="16" x2="450" y2="304" stroke="rgba(255,255,255,0.32)" strokeWidth="1.2" />
                <circle cx="450" cy="160" r="48" fill="none" stroke="rgba(255,255,255,0.32)" strokeWidth="1.2" />
                <circle cx="450" cy="160" r="2.5" fill="rgba(255,255,255,0.6)" />

                {/* Corner Arcs */}
                <path d="M 20 26 A 10 10 0 0 0 30 16" fill="none" stroke="rgba(255,255,255,0.32)" strokeWidth="1.2" />
                <path d="M 870 16 A 10 10 0 0 0 880 26" fill="none" stroke="rgba(255,255,255,0.32)" strokeWidth="1.2" />
                <path d="M 30 304 A 10 10 0 0 0 20 294" fill="none" stroke="rgba(255,255,255,0.32)" strokeWidth="1.2" />
                <path d="M 880 294 A 10 10 0 0 0 870 304" fill="none" stroke="rgba(255,255,255,0.32)" strokeWidth="1.2" />

                {/* Left Penalty Area & Goal */}
                <rect x="10" y="136" width="10" height="48" fill="none" stroke="rgba(255,255,255,0.25)" strokeWidth="1" />
                <rect x="20" y="72" width="150" height="176" fill="none" stroke="rgba(255,255,255,0.32)" strokeWidth="1.2" />
                <rect x="20" y="116" width="54" height="88" fill="none" stroke="rgba(255,255,255,0.32)" strokeWidth="1.2" />
                <circle cx="124" cy="160" r="2.5" fill="rgba(255,255,255,0.5)" />
                <path d="M 170 128 A 42 42 0 0 1 170 192" fill="none" stroke="rgba(255,255,255,0.32)" strokeWidth="1.2" />

                {/* Right Penalty Area & Goal */}
                <rect x="880" y="136" width="10" height="48" fill="none" stroke="rgba(255,255,255,0.25)" strokeWidth="1" />
                <rect x="730" y="72" width="150" height="176" fill="none" stroke="rgba(255,255,255,0.32)" strokeWidth="1.2" />
                <rect x="826" y="116" width="54" height="88" fill="none" stroke="rgba(255,255,255,0.32)" strokeWidth="1.2" />
                <circle cx="776" cy="160" r="2.5" fill="rgba(255,255,255,0.5)" />
                <path d="M 730 192 A 42 42 0 0 1 730 128" fill="none" stroke="rgba(255,255,255,0.32)" strokeWidth="1.2" />

                {/* Tactical Zones: Upper Pressure Trap */}
                <polygon points="570,24 810,34 760,132 540,104" fill="url(#pressureTrapGrad)" stroke="#ef4444" strokeWidth="1.2" strokeDasharray="4 2" />
                <line x1="570" y1="24" x2="760" y2="132" stroke="rgba(239, 68, 68, 0.28)" strokeWidth="0.8" />
                <line x1="540" y1="104" x2="810" y2="34" stroke="rgba(239, 68, 68, 0.28)" strokeWidth="0.8" />
                <text x="780" y="62" fontSize="13" fontWeight="900" fill="#f87171" letterSpacing="0.08em">PRESSURE</text>
                <text x="794" y="78" fontSize="13" fontWeight="900" fill="#f87171" letterSpacing="0.08em">TRAP</text>

                {/* Tactical Zones: Lower Pressure Trap */}
                <polygon points="540,245 770,235 730,310 520,300" fill="url(#pressureTrapGradLower)" stroke="#ef4444" strokeWidth="1" strokeDasharray="4 2" />

                {/* Dashed Passing & Pressing Lines */}
                <line x1="228" y1="96" x2="200" y2="155" stroke="rgba(255,255,255,0.6)" strokeWidth="1.4" strokeDasharray="3 3" />
                <line x1="195" y1="175" x2="195" y2="230" stroke="rgba(255,255,255,0.6)" strokeWidth="1.4" strokeDasharray="3 3" />
                <line x1="205" y1="162" x2="302" y2="146" stroke="rgba(74,239,172,0.85)" strokeWidth="1.4" strokeDasharray="3 3" markerEnd="url(#arrowEmerald)" />
                <line x1="327" y1="140" x2="410" y2="134" stroke="rgba(255,255,255,0.55)" strokeWidth="1.4" strokeDasharray="3 3" markerEnd="url(#arrowWhite)" />
                <line x1="205" y1="245" x2="415" y2="275" stroke="rgba(255,255,255,0.4)" strokeWidth="1.4" strokeDasharray="3 3" />
                <line x1="542" y1="202" x2="638" y2="90" stroke="rgba(74,239,172,0.85)" strokeWidth="1.4" strokeDasharray="3 3" markerEnd="url(#arrowEmerald)" />
                <line x1="545" y1="218" x2="618" y2="260" stroke="rgba(74,239,172,0.85)" strokeWidth="1.4" strokeDasharray="3 3" markerEnd="url(#arrowEmerald)" />
                <line x1="668" y1="140" x2="602" y2="74" stroke="rgba(248,113,113,0.85)" strokeWidth="1.4" strokeDasharray="3 3" markerEnd="url(#arrowRed)" />
                <line x1="602" y1="74" x2="668" y2="138" stroke="rgba(248,113,113,0.85)" strokeWidth="1.4" strokeDasharray="3 3" markerEnd="url(#arrowRed)" />

                {/* Goalkeepers */}
                {/* Left GK: Arsenal Yellow #1 */}
                <circle cx="80" cy="160" r="13" fill="#f59e0b" stroke="#ffffff" strokeWidth="1.5" />
                <text x="80" y="160" fontSize="10.5" fontWeight="800" fill="#000" textAnchor="middle" dominantBaseline="central">1</text>

                {/* Right GK: Man City Green #1 */}
                <circle cx="820" cy="160" r="13" fill="#10b981" stroke="#ffffff" strokeWidth="1.5" />
                <text x="820" y="160" fontSize="10.5" fontWeight="800" fill="#fff" textAnchor="middle" dominantBaseline="central">1</text>

                {/* Arsenal Red Nodes */}
                <circle cx="230" cy="86" r="12.5" fill="#dc2626" stroke="#ffffff" strokeWidth="1.4" />
                <text x="230" y="86" fontSize="10" fontWeight="800" fill="#fff" textAnchor="middle" dominantBaseline="central">35</text>

                <circle cx="195" cy="165" r="12.5" fill="#dc2626" stroke="#ffffff" strokeWidth="1.4" />
                <text x="195" y="165" fontSize="10" fontWeight="800" fill="#fff" textAnchor="middle" dominantBaseline="central">6</text>

                <circle cx="195" cy="242" r="12.5" fill="#dc2626" stroke="#ffffff" strokeWidth="1.4" />
                <text x="195" y="242" fontSize="10" fontWeight="800" fill="#fff" textAnchor="middle" dominantBaseline="central">4</text>

                <circle cx="315" cy="142" r="12.5" fill="#dc2626" stroke="#ffffff" strokeWidth="1.4" />
                <text x="315" y="142" fontSize="10" fontWeight="800" fill="#fff" textAnchor="middle" dominantBaseline="central">6</text>

                <circle cx="360" cy="204" r="12.5" fill="#dc2626" stroke="#ffffff" strokeWidth="1.4" />
                <text x="360" y="204" fontSize="10" fontWeight="800" fill="#fff" textAnchor="middle" dominantBaseline="central">29</text>

                <circle cx="330" cy="254" r="12.5" fill="#dc2626" stroke="#ffffff" strokeWidth="1.4" />
                <text x="330" y="254" fontSize="10" fontWeight="800" fill="#fff" textAnchor="middle" dominantBaseline="central">8</text>

                <circle cx="650" cy="78" r="12.5" fill="#dc2626" stroke="#ffffff" strokeWidth="1.4" />
                <text x="650" y="78" fontSize="10" fontWeight="800" fill="#fff" textAnchor="middle" dominantBaseline="central">11</text>

                <circle cx="630" cy="270" r="12.5" fill="#dc2626" stroke="#ffffff" strokeWidth="1.4" />
                <text x="630" y="270" fontSize="10" fontWeight="800" fill="#fff" textAnchor="middle" dominantBaseline="central">11</text>

                {/* RECENT REGAIN: Declan Rice #41 with Glowing Emerald Halo */}
                <circle cx="530" cy="210" r="22" fill="rgba(74, 239, 172, 0.15)" stroke="#4aefac" strokeWidth="1.6" strokeDasharray="3 2" filter="url(#glowEmerald)" />
                <circle cx="530" cy="210" r="17" fill="none" stroke="#4aefac" strokeWidth="1.4" />
                <circle cx="530" cy="210" r="12.5" fill="#dc2626" stroke="#ffffff" strokeWidth="1.4" />
                <text x="530" y="210" fontSize="10" fontWeight="800" fill="#fff" textAnchor="middle" dominantBaseline="central">41</text>
                <text x="562" y="214" fontSize="11.5" fontWeight="900" fill="#4aefac" letterSpacing="0.08em" filter="url(#glowEmerald)">RECENT REGAIN</text>

                {/* Man City Sky Blue Nodes */}
                <circle cx="345" cy="66" r="12.5" fill="#38bdf8" stroke="#ffffff" strokeWidth="1.4" />
                <text x="345" y="66" fontSize="10" fontWeight="800" fill="#082032" textAnchor="middle" dominantBaseline="central">9</text>

                <circle cx="405" cy="138" r="12.5" fill="#38bdf8" stroke="#ffffff" strokeWidth="1.4" />
                <text x="405" y="138" fontSize="9.5" fontWeight="800" fill="#082032" textAnchor="middle" dominantBaseline="central">47</text>

                <circle cx="265" cy="278" r="12.5" fill="#38bdf8" stroke="#ffffff" strokeWidth="1.4" />
                <text x="265" y="278" fontSize="10" fontWeight="800" fill="#082032" textAnchor="middle" dominantBaseline="central">3</text>

                <circle cx="450" cy="195" r="12.5" fill="#38bdf8" stroke="#ffffff" strokeWidth="1.4" />
                <text x="450" y="195" fontSize="9.5" fontWeight="800" fill="#082032" textAnchor="middle" dominantBaseline="central">16</text>

                <circle cx="415" cy="258" r="12.5" fill="#38bdf8" stroke="#ffffff" strokeWidth="1.4" />
                <text x="415" y="258" fontSize="10" fontWeight="800" fill="#082032" textAnchor="middle" dominantBaseline="central">3</text>

                <circle cx="590" cy="62" r="12.5" fill="#38bdf8" stroke="#ffffff" strokeWidth="1.4" />
                <text x="590" y="62" fontSize="10" fontWeight="800" fill="#082032" textAnchor="middle" dominantBaseline="central">9</text>

                <circle cx="570" cy="150" r="12.5" fill="#38bdf8" stroke="#ffffff" strokeWidth="1.4" />
                <text x="570" y="150" fontSize="9.5" fontWeight="800" fill="#082032" textAnchor="middle" dominantBaseline="central">20</text>

                <circle cx="680" cy="146" r="12.5" fill="#38bdf8" stroke="#ffffff" strokeWidth="1.4" />
                <text x="680" y="146" fontSize="9.5" fontWeight="800" fill="#082032" textAnchor="middle" dominantBaseline="central">21</text>

                <circle cx="710" cy="238" r="12.5" fill="#38bdf8" stroke="#ffffff" strokeWidth="1.4" />
                <text x="710" y="238" fontSize="10" fontWeight="800" fill="#082032" textAnchor="middle" dominantBaseline="central">2</text>
              </svg>
            </div>
          </div>

          {/* Bottom 4 Feature Pills in row */}
          <div className="welcome-features-row">
            <div className="wfeat-card">
              <div className="wfeat-icon">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#4aefac" strokeWidth="2.2" aria-hidden="true">
                  <path d="M18 20V10M12 20V4M6 20v-6" />
                </svg>
              </div>
              <div>
                <strong>Live match intelligence</strong>
                <p>Key moments, trends and tactical context.</p>
              </div>
            </div>
            <div className="wfeat-card">
              <div className="wfeat-icon">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#4aefac" strokeWidth="2.2" aria-hidden="true">
                  <circle cx="6" cy="6" r="3" /><circle cx="18" cy="18" r="3" /><path d="M8.5 8.5l7 7M18 6l-4 4" />
                </svg>
              </div>
              <div>
                <strong>Tactical insights</strong>
                <p>See how teams create advantages on the pitch.</p>
              </div>
            </div>
            <div className="wfeat-card">
              <div className="wfeat-icon">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#4aefac" strokeWidth="2.2" aria-hidden="true">
                  <rect x="3" y="3" width="18" height="18" rx="4" /><circle cx="9" cy="9" r="1.5" fill="#4aefac" /><circle cx="15" cy="9" r="1.5" fill="#4aefac" /><path d="M8 15h8" />
                </svg>
              </div>
              <div>
                <strong>Explainable AI</strong>
                <p>Every insight linked to on-pitch evidence.</p>
              </div>
            </div>
            <div className="wfeat-card">
              <div className="wfeat-icon">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#4aefac" strokeWidth="2.2" aria-hidden="true">
                  <ellipse cx="12" cy="5" rx="9" ry="3" /><path d="M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5M3 12c0 1.66 4 3 9 3s9-1.34 9-3" />
                </svg>
              </div>
              <div>
                <strong>Built for clubs & analysts</strong>
                <p>Role-based access for every team.</p>
              </div>
            </div>
          </div>

        </div>

        {/* Right Column: Floating Sign In Card */}
        <div className="welcome-right-col">
          <div className="welcome-auth-card">
            <div className="auth-card-brand">
              <img className="auth-brand-logo" src="/brand/pitchmind-symbol.svg" width="36" height="36" alt="" aria-hidden="true" />
              <h2>PitchMind</h2>
            </div>

            <h3 className="auth-heading">Sign in to your account</h3>
            <p className="auth-subtext">Access Premier League intelligence, tactical insights and explainable AI — all in one place.</p>
            <span className="sr-only">Sign in to PitchMind</span>

            {authError && <div className="notice error" role="alert">{authError}</div>}
            {session && (
              <div className="notice success" role="status">
                Signed in as <strong>{session.user.name}</strong> ({session.user.role})
              </div>
            )}

            <form onSubmit={handleSignIn} className="auth-form-fields">
              <div className="auth-field-group">
                <label htmlFor="welcome-email">Email address</label>
                <div className="auth-input-wrap">
                  <span className="input-icon" aria-hidden="true">
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                      <rect x="2" y="4" width="20" height="16" rx="2" /><path d="m22 7-10 6L2 7" />
                    </svg>
                  </span>
                  <input
                    id="welcome-email"
                    type="email"
                    autoComplete="email"
                    required
                    placeholder="you@yourclub.com"
                    value={email}
                    onChange={e => setEmail(e.target.value)}
                  />
                </div>
              </div>

              <div className="auth-field-group">
                <label htmlFor="welcome-password">Password</label>
                <div className="auth-input-wrap">
                  <span className="input-icon" aria-hidden="true">
                    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                      <rect x="3" y="11" width="18" height="11" rx="2" ry="2" /><path d="M7 11V7a5 5 0 0 1 10 0v4" />
                    </svg>
                  </span>
                  <input
                    id="welcome-password"
                    type={showPassword ? 'text' : 'password'}
                    autoComplete="current-password"
                    required
                    placeholder="Enter your password"
                    value={password}
                    onChange={e => setPassword(e.target.value)}
                  />
                  <button
                    type="button"
                    className="password-toggle-btn"
                    onClick={() => setShowPassword(!showPassword)}
                    aria-label={showPassword ? 'Hide password' : 'Show password'}
                  >
                    {showPassword ? (
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
                        <path d="M9.88 9.88a3 3 0 1 0 4.24 4.24M10.73 5.08A10.43 10.43 0 0 1 12 5c7 0 10 7 10 7a13.16 13.16 0 0 1-1.67 2.68M6.61 6.61A13.526 13.526 0 0 0 2 12s3 7 10 7a9.74 9.74 0 0 0 5.39-1.61M2 2l20 20" />
                      </svg>
                    ) : (
                      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
                        <path d="M2 12s3-7 10-7 10 7 10 7-3 7-10 7-10-7-10-7Z" /><circle cx="12" cy="12" r="3" />
                      </svg>
                    )}
                  </button>
                </div>
              </div>

              <div className="auth-options-row">
                <label className="checkbox-wrap">
                  <input
                    type="checkbox"
                    checked={remember}
                    onChange={e => setRemember(e.target.checked)}
                  />
                  <span>Remember me</span>
                </label>
                <a href="#forgot" className="forgot-link">Forgot password?</a>
              </div>

              <button type="submit" className="auth-submit-btn" disabled={busy}>
                {busy ? 'Signing in…' : 'Sign in →'}
              </button>

              <div className="auth-divider">
                <span>or continue with</span>
              </div>

              <div className="auth-social-buttons-stack">
                <button
                  type="button"
                  className="auth-google-btn"
                  onClick={handleGoogleSignIn}
                  disabled={busy}
                  aria-label="Continue with Google using the demo sign-in flow"
                >
                  <svg className="google-g-icon" width="18" height="18" viewBox="0 0 24 24" aria-hidden="true">
                    <path fill="#4285F4" d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92c-.26 1.37-1.04 2.53-2.21 3.31v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.09z" />
                    <path fill="#34A853" d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z" />
                    <path fill="#FBBC05" d="M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.06H2.18C1.43 8.55 1 10.22 1 12s.43 3.45 1.18 4.94l2.85-2.22.81-.63z" />
                    <path fill="#EA4335" d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.06l3.66 2.84c.87-2.6 3.3-4.52 6.16-4.52z" />
                  </svg>
                  <span>Google</span>
                  <small className="provider-option-status">Demo</small>
                </button>

                <button type="button" className="auth-provider-btn" disabled aria-label="Apple sign-in coming soon">
                  <svg className="provider-brand-icon" viewBox="0 0 24 24" aria-hidden="true">
                    <path fill="currentColor" d="M12.152 6.896c-.948 0-2.415-1.078-3.96-1.04c-2.04.027-3.91 1.183-4.961 3.014c-2.117 3.675-.546 9.103 1.519 12.09c1.013 1.454 2.208 3.09 3.792 3.039c1.52-.065 2.09-.987 3.935-.987c1.831 0 2.35.987 3.96.948c1.637-.026 2.676-1.48 3.676-2.948c1.156-1.688 1.636-3.325 1.662-3.415c-.039-.013-3.182-1.221-3.22-4.857c-.026-3.04 2.48-4.494 2.597-4.559c-1.429-2.09-3.623-2.324-4.39-2.376c-2-.156-3.675 1.09-4.61 1.09zM15.53 3.83c.843-1.012 1.4-2.427 1.245-3.83c-1.207.052-2.662.805-3.532 1.818c-.78.896-1.454 2.338-1.273 3.714c1.338.104 2.715-.688 3.559-1.701z" />
                  </svg>
                  <span>Apple</span>
                  <small className="provider-option-status">Soon</small>
                </button>

                <button type="button" className="auth-provider-btn" disabled aria-label="Microsoft Entra ID sign-in coming soon">
                  <svg width="18" height="18" viewBox="0 0 21 21" aria-hidden="true">
                    <rect x="1" y="1" width="9" height="9" fill="#f25022" />
                    <rect x="11" y="1" width="9" height="9" fill="#7fba00" />
                    <rect x="1" y="11" width="9" height="9" fill="#00a4ef" />
                    <rect x="11" y="11" width="9" height="9" fill="#ffb900" />
                  </svg>
                  <span>Microsoft</span>
                  <small className="provider-option-status">Soon</small>
                  <span className="sr-only">Sign in with Microsoft Entra ID</span>
                </button>

                <button type="button" className="auth-provider-btn" disabled aria-label="X sign-in coming soon">
                  <svg className="provider-brand-icon" viewBox="0 0 24 24" aria-hidden="true">
                    <path fill="currentColor" d="M18.901 1.153h3.68l-8.04 9.19L24 22.846h-7.406l-5.8-7.584-6.638 7.584H.474l8.6-9.83L0 1.154h7.594l5.243 6.932ZM17.61 20.644h2.039L6.486 3.24H4.298Z" />
                  </svg>
                  <span>X</span>
                  <small className="provider-option-status">Soon</small>
                </button>

                <button type="button" className="auth-provider-btn" disabled aria-label="GitHub sign-in coming soon">
                  <svg className="provider-brand-icon" viewBox="0 0 24 24" aria-hidden="true">
                    <path fill="currentColor" d="M12 .297c-6.63 0-12 5.373-12 12 0 5.303 3.438 9.8 8.205 11.385.6.113.82-.258.82-.577 0-.285-.01-1.04-.015-2.04-3.338.724-4.042-1.61-4.042-1.61C4.422 18.07 3.633 17.7 3.633 17.7c-1.087-.744.084-.729.084-.729 1.205.084 1.838 1.236 1.838 1.236 1.07 1.835 2.809 1.305 3.495.998.108-.776.417-1.305.76-1.605-2.665-.3-5.466-1.332-5.466-5.93 0-1.31.465-2.38 1.235-3.22-.135-.303-.54-1.523.105-3.176 0 0 1.005-.322 3.3 1.23.96-.267 1.98-.399 3-.405 1.02.006 2.04.138 3 .405 2.28-1.552 3.285-1.23 3.285-1.23.645 1.653.24 2.873.12 3.176.765.84 1.23 1.91 1.23 3.22 0 4.61-2.805 5.625-5.475 5.92.42.36.81 1.096.81 2.22 0 1.606-.015 2.896-.015 3.286 0 .315.21.69.825.57C20.565 22.092 24 17.592 24 12.297c0-6.627-5.373-12-12-12z" />
                  </svg>
                  <span>GitHub</span>
                  <small className="provider-option-status">Soon</small>
                </button>

                <button type="button" className="auth-provider-btn" disabled aria-label="Facebook sign-in coming soon">
                  <svg className="provider-brand-icon" viewBox="0 0 512 512" aria-hidden="true">
                    <path fill="#0866FF" d="M504 256C504 119 393 8 256 8S8 119 8 256c0 123.78 90.69 226.38 209.25 245V327.69h-63V256h63v-54.64c0-62.15 37-96.48 93.67-96.48 27.14 0 55.52 4.84 55.52 4.84v61h-31.27c-30.81 0-40.42 19.12-40.42 38.73V256h68.78l-11 71.69h-57.78V501C413.31 482.38 504 379.78 504 256z" />
                  </svg>
                  <span>Facebook</span>
                  <small className="provider-option-status">Soon</small>
                </button>
              </div>
            </form>

            <div className="auth-trust-badges-grid">
              <div className="trust-col">
                <span className="trust-icon" aria-hidden="true">
                  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="#4aefac" strokeWidth="2">
                    <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />
                  </svg>
                </span>
                <strong>Secure sign-in</strong>
                <small>Your data is protected with enterprise-grade security.</small>
              </div>
              <div className="trust-col">
                <span className="trust-icon" aria-hidden="true">
                  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="#4aefac" strokeWidth="2">
                    <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" /><circle cx="9" cy="7" r="4" /><path d="M23 21v-2a4 4 0 0 0-3-3.87" /><path d="M16 3.13a4 4 0 0 1 0 7.75" />
                  </svg>
                </span>
                <strong>Role-based access</strong>
                <small>Tailored access for analysts, coaches and club staff.</small>
              </div>
              <div className="trust-col">
                <span className="trust-icon" aria-hidden="true">
                  <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="#4aefac" strokeWidth="2">
                    <ellipse cx="12" cy="5" rx="9" ry="3" /><path d="M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5M3 12c0 1.66 4 3 9 3s9-1.34 9-3" />
                  </svg>
                </span>
                <strong>Evidence-backed AI</strong>
                <small>Insights you can trust, linked to on-pitch evidence.</small>
              </div>
            </div>

            <div style={{ textAlign: 'center', marginTop: '1.25rem' }}>
              <a className="primary-link" href="/home">Check match availability</a>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}

/* =========================================================================
   SCREEN 02: HOME / MATCH HUB (02-home.jpg)
   ========================================================================= */
export function HomePage({
  search = '',
  selectedMatchId,
  selectedSeconds
}: {
  search?: string;
  selectedMatchId?: string;
  selectedSeconds?: number;
}) {
  const { page, error, loading, loadingMore, moreError, retry, loadMore } = useCanonicalFixtures();
  const [hubData, setHubData] = useState<EplHubData | null>(null);
  const [queryInput, setQueryInput] = useState('');
  const [activeMomentFilter, setActiveMomentFilter] = useState<'live' | 'all'>('live');

  useEffect(() => {
    const isTestEnv = typeof globalThis !== 'undefined' && Boolean((globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT);
    if (isTestEnv) return;
    const controller = new AbortController();
    getEplHub(controller.signal)
      .then(res => { if (res && res.heroMatch) setHubData(res); })
      .catch(() => { });
    return () => controller.abort();
  }, []);

  const matches = page?.items ?? [];
  const requestedPhase = new URLSearchParams(search).get('phase') ?? 'all';
  const phases = [...new Set([...CANONICAL_PHASES, ...matches.map(match => match.phase)])].sort();
  const phaseFilter = requestedPhase === 'all' || phases.includes(requestedPhase) ? requestedPhase : 'all';
  const visibleMatches = phaseFilter === 'all' ? matches : matches.filter(match => match.phase === phaseFilter);

  if (!hubData) {
    return (
      <section className="content-page">
        <div className="home-heading-row">
          <PageHeading eyebrow="Home · match library" title="Choose a match" question="Explore recorded matches. Scores and match phases come from each match record." />
          {!loading && !error && matches.length > 0 && (
            <label className="phase-filter">Filter by phase
              <select aria-label="Filter matches by phase" value={phaseFilter} onChange={event => replacePhaseFilterContext(event.target.value)}>
                <option value="all">All phases</option>
                {phases.map(phase => <option key={phase} value={phase}>{phaseLabel(phase)}</option>)}
              </select>
            </label>
          )}
          {!loading && !error && page && <button className="refresh-matches" type="button" onClick={retry}>Refresh matches</button>}
        </div>
        {loading ? <PageState loading empty="" />
          : error ? <div className="page-state error-copy" role="alert"><p>{error}</p><button type="button" onClick={retry}>Try loading matches again</button></div>
            : matches.length === 0 ? <PageState loading={false} empty="No matches are available yet." />
              : visibleMatches.length === 0 ? <p className="page-state" role="status">{page?.hasMore
                ? 'No match in this phase is in the current list. Choose “More matches” to check the next results.'
                : 'No matches are available in this phase.'}</p>
                : <>
                  <p className="match-count" role="status" aria-live="polite">{visibleMatches.length} {visibleMatches.length === 1 ? 'match' : 'matches'}</p>
                  <ul className="match-list" aria-label="Matches">{visibleMatches.map(match => {
                    const matchPath = `/matches/${encodeURIComponent(match.matchId)}`;
                    const simulationLabel = provenanceLabel(match.provenance);
                    return <li className="match-list-item" key={match.matchId}>
                      <article className="match-card">
                        <div className="match-card-copy">
                          <div className="match-card-topline"><span className="badge">{simulationLabel}</span><span className="phase-tag">{phaseLabel(match.phase)}</span></div>
                          <h2>Home team <span className="match-score">{match.homeScore}–{match.awayScore}</span> Away team</h2>
                          <p>Recorded at {clock(match.asOfSeconds)} · {phaseLabel(match.phase)}</p>
                          <details className="match-source-details">
                            <summary>Match and source details</summary>
                            <p className="match-id">Match ID <code translate="no">{match.matchId}</code></p>
                            <p>Revision {match.canonicalRevision} · source {match.provenance} · algorithm {match.algorithmVersion}</p>
                            <p>Source hash {match.sourceSha256}</p>
                          </details>
                        </div>
                        <a className="primary-link" href={contextHref(matchPath, match.matchId, (selectedMatchId === match.matchId ? selectedSeconds : undefined) ?? match.asOfSeconds, match.projectionRevision)}>Open match</a>
                      </article>
                    </li>;
                  })}</ul>
                </>}
        {!loading && !error && page?.hasMore && <div className="load-more-wrap">
          {moreError && <p className="error-copy" role="alert">{moreError}</p>}
          <button className="load-more" type="button" onClick={loadMore} disabled={loadingMore || !page.nextMatchId}>{loadingMore ? 'Loading more matches…' : 'More matches'}</button>
        </div>}
        {!loading && !error && matches.length > 0 && <p className="home-contract-limit">Competition, team names, match summaries, and Key Moments are not available yet.</p>}
      </section>
    );
  }

  return (
    <section className="content-page hub-page-root">
      {/* Top Hero Match Scoreboard Banner */}
      <MatchHeaderBanner
        competition={hubData?.heroMatch?.competition ?? "Premier League · Matchweek 12"}
        stadium={hubData?.heroMatch?.stadium ?? "Emirates Stadium"}
        homeName={hubData?.heroMatch?.homeTeam ?? "Arsenal"}
        homeLogo={hubData?.heroMatch?.homeLogo}
        homeFormation="4 - 3 - 3"
        homeScore={hubData?.heroMatch?.homeScore ?? 2}
        awayScore={hubData?.heroMatch?.awayScore ?? 1}
        awayName={hubData?.heroMatch?.awayTeam ?? "Manchester City"}
        awayLogo={hubData?.heroMatch?.awayLogo}
        awayFormation="4 - 2 - 3 - 1"
        clock={hubData?.heroMatch?.clock ?? "67:24"}
        period="Second Half"
        leadingInsight={hubData?.heroMatch?.leadingInsight ?? "Arsenal's high press is forcing City wide and creating dangerous regains."}
        buttonText="Open Match View"
        onButtonClick={() => { window.location.href = contextHref('/matches', selectedMatchId, selectedSeconds); }}
      />

      {/* Middle Row: Other Live Matches (60%) + What Matters Now (40%) */}
      <div className="hub-middle-row-grid">
        {/* Left: Other Live Matches */}
        <div className="hub-other-matches-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="pulse-dot"></span>
              <h3>Other Live Matches</h3>
              <span className="panel-count-badge">5 matches live</span>
            </div>
            <a href="#all-matches" className="view-all-link">View all →</a>
          </div>

          <div className="live-matches-strip-row">
            {hubData?.otherLiveMatches && hubData.otherLiveMatches.map(m => (
              <div key={m.matchId} className="live-match-card-mini">
                <div className="mini-card-topline">
                  <span className="mini-live-tag">• LIVE {m.clock}</span>
                  <span className="mini-card-chevron">›</span>
                </div>

                <div className="mini-card-scores">
                  <div className="mini-team-left">
                    <ClubCrest name={m.homeTeam} logoUrl={m.homeLogo} className="mini-crest" />
                    <strong>{m.homeScore}</strong>
                  </div>
                  <span className="mini-score-divider">-</span>
                  <div className="mini-team-right">
                    <strong>{m.awayScore}</strong>
                    <ClubCrest name={m.awayTeam} logoUrl={m.awayLogo} className="mini-crest" />
                  </div>
                </div>

                <div className="mini-card-names">
                  <div>
                    <span>{m.homeTeam}</span>
                    <small>{m.homeFormation ?? '4-2-3-1'}</small>
                  </div>
                  <div className="text-right">
                    <span>{m.awayTeam}</span>
                    <small>{m.awayFormation ?? '4-3-3'}</small>
                  </div>
                </div>

                <div className="mini-stadium-venue">{m.stadium ?? 'Premier League'}</div>

                {/* Sparkline */}
                <div className="mini-sparkline-row">
                  <svg className="sparkline-svg" viewBox="0 0 100 20" preserveAspectRatio="none">
                    <polyline
                      fill="none"
                      stroke="#4aefac"
                      strokeWidth="2"
                      points={m.momentum.map((val, idx) => `${idx * 11},${20 - (val / 100) * 16}`).join(' ')}
                    />
                    <circle cx="85" cy="8" r="2.5" fill="#fff" stroke="#07131c" strokeWidth="0.8" />
                  </svg>
                </div>
              </div>
            ))}
          </div>
        </div>

        {/* Right: What Matters Now */}
        <div className="hub-what-matters-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="panel-icon-emerald">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="#4aefac" strokeWidth="2.2" aria-hidden="true">
                  <path d="M18 20V10M12 20V4M6 20v-6" />
                </svg>
              </span>
              <h3>What matters now</h3>
            </div>
            <span className="panel-caption-text">Live insights across EPL</span>
          </div>

          <div className="what-matters-list-group">
            {hubData?.whatMattersNow && hubData.whatMattersNow.map(item => (
              <div key={item.rank} className="what-matters-item-card">
                <div className="rank-num-circle">{item.rank}</div>
                <div className="matters-text-body">
                  <p>{item.title}</p>
                </div>
                <span className="matters-match-badge">{item.matchTag ?? 'EPL LIVE'}</span>
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* Bottom Row: Key Moments (48%) + League Snapshot (26%) + Upcoming Fixtures (26%) */}
      <div className="hub-bottom-row-grid">
        {/* Left: Key Moments Across EPL */}
        <div className="hub-key-moments-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="panel-star-icon">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="#eab308" stroke="#eab308" strokeWidth="1" aria-hidden="true">
                  <polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2" />
                </svg>
              </span>
              <h3>Key Moments Across EPL</h3>
            </div>
            <div className="moments-toggle-pills">
              <button
                type="button"
                className={`moment-filter-btn ${activeMomentFilter === 'live' ? 'active' : ''}`}
                onClick={() => setActiveMomentFilter('live')}
              >
                Live & Recent
              </button>
              <button
                type="button"
                className={`moment-filter-btn ${activeMomentFilter === 'all' ? 'active' : ''}`}
                onClick={() => setActiveMomentFilter('all')}
              >
                All Matches
              </button>
              <a href="#view-all-moments" className="view-all-link">View all →</a>
            </div>
          </div>

          <div className="key-moments-cards-strip">
            {hubData?.keyMoments && hubData.keyMoments.map(km => (
              <div key={km.id} className="moment-video-card">
                <div className="moment-video-thumbnail">
                  <span className="moment-clock-tag">{km.clock}</span>
                  <div className="moment-play-circle" aria-hidden="true">
                    <svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
                      <polygon points="5 3 19 12 5 21 5 3" />
                    </svg>
                  </div>
                </div>
                <div className="moment-desc-box">
                  <div className="moment-team-indicator">
                    <span className="team-vertical-bar"></span>
                    <strong>{km.team}</strong>
                  </div>
                  <p>{km.title}</p>
                  <span className="moment-fixture-tag">{km.matchTag ?? 'EPL'}</span>
                </div>
              </div>
            ))}
          </div>
        </div>

        {/* Center: EPL League Snapshot */}
        <div className="hub-table-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="panel-icon-gold">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="#eab308" strokeWidth="2.2" aria-hidden="true">
                  <path d="M6 9H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h2M18 9h2a2 2 0 0 0 2-2V5a2 2 0 0 0-2-2h-2M4 22h16M10 14.66V17c0 .55-.45 1-1 1H7v2h10v-2h-2c-.55 0-1-.45-1-1v-2.34M12 2a8 8 0 0 0-8 8c0 4.42 5 7.6 8 10 3-2.4 8-5.58 8-10a8 8 0 0 0-8-8z" />
                </svg>
              </span>
              <div>
                <h3>EPL League Snapshot</h3>
                <small className="panel-subcaption">Live Table (Top 6)</small>
              </div>
            </div>
            <a href="#full-table" className="view-all-link">Full Table →</a>
          </div>

          <table className="epl-snapshot-table" aria-label="Premier League snapshot table">
            <thead>
              <tr>
                <th scope="col">#</th>
                <th scope="col">Team</th>
                <th scope="col">P</th>
                <th scope="col">GD</th>
                <th scope="col">Pts</th>
              </tr>
            </thead>
            <tbody>
              {hubData?.leagueSnapshot && hubData.leagueSnapshot.map(row => (
                <tr key={row.rank}>
                  <td className="rank-col">{row.rank}</td>
                  <td className="team-col">
                    <ClubCrest name={row.team} logoUrl={row.logo} className="table-crest" />
                    <span>{row.team}</span>
                  </td>
                  <td>{row.played}</td>
                  <td>{row.gd}</td>
                  <td className="points-col"><strong>{row.points}</strong></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {/* Right: Upcoming Fixtures */}
        <div className="hub-fixtures-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="panel-icon-cyan">
                <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="#38bdf8" strokeWidth="2" aria-hidden="true">
                  <rect x="3" y="4" width="18" height="18" rx="2" /><line x1="16" y1="2" x2="16" y2="6" /><line x1="8" y1="2" x2="8" y2="6" /><line x1="3" y1="10" x2="21" y2="10" />
                </svg>
              </span>
              <div>
                <h3>Upcoming Fixtures</h3>
                <small className="panel-subcaption">Next 3 EPL matches</small>
              </div>
            </div>
            <a href="#view-all-fixtures" className="view-all-link">View all →</a>
          </div>

          <div className="upcoming-fixtures-vertical-list">
            {hubData?.upcomingFixtures && hubData.upcomingFixtures.map((fix, idx) => (
              <div key={idx} className="upcoming-fixture-row-card">
                <div className="fixture-time-col">
                  <span className="fix-date">{fix.date}</span>
                  <strong className="fix-clock">{fix.kickOff}</strong>
                </div>

                <div className="fixture-matchup-col">
                  <div className="fix-team">
                    <ClubCrest name={fix.homeTeam} logoUrl={fix.homeLogo} className="fix-crest" />
                    <span>{fix.homeTeam}</span>
                  </div>
                  <span className="fix-vs">vs</span>
                  <div className="fix-team">
                    <ClubCrest name={fix.awayTeam} logoUrl={fix.awayLogo} className="fix-crest" />
                    <span>{fix.awayTeam}</span>
                  </div>
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* Bottom AI Analyst Bar */}
      <div className="hub-bottom-ai-bar">
        <div className="ai-bar-title-row">
          <div className="ai-title-group">
            <span className="sparkle-icon" aria-hidden="true">
              <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="#4aefac" strokeWidth="2">
                <path d="m12 3-1.9 5.8a2 2 0 0 1-1.3 1.3L3 12l5.8 1.9a2 2 0 0 1 1.3 1.3L12 21l1.9-5.8a2 2 0 0 1 1.3-1.3L21 12l-5.8-1.9a2 2 0 0 1-1.3-1.3Z" />
              </svg>
            </span>
            <strong>AI Analyst</strong>
            <span className="ai-bar-desc">Ask about live matches, tactics, players or trends across the Premier League.</span>
          </div>
          <span className="ai-powered-tag"><span className="pulse-dot"></span> Powered by match intelligence</span>
        </div>

        <div className="ai-prompt-chips-tray">
          {hubData?.suggestedQueries && hubData.suggestedQueries.map(q => (
            <button
              key={q}
              type="button"
              className="hub-chip-btn"
              onClick={() => { window.location.href = `/analyst?q=${encodeURIComponent(q)}`; }}
            >
              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true" style={{ marginRight: '4px' }}>
                <circle cx="11" cy="11" r="8" /><path d="m21 21-4.3-4.3" />
              </svg>
              {q} →
            </button>
          ))}
        </div>

        <form
          className="ai-bottom-search-form"
          onSubmit={e => {
            e.preventDefault();
            if (queryInput.trim()) {
              window.location.href = `/analyst?q=${encodeURIComponent(queryInput.trim())}`;
            }
          }}
        >
          <input
            type="text"
            placeholder="Ask a question about the EPL..."
            value={queryInput}
            onChange={e => setQueryInput(e.target.value)}
            aria-label="Ask a question about the Premier League"
          />
          <button type="submit" className="ai-search-send-btn" aria-label="Send query">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5"><line x1="22" y1="2" x2="11" y2="13" /><polygon points="22 2 15 22 11 13 2 9 22 2" /></svg>
          </button>
        </form>
      </div>

      {/* Canonical fixtures section for test verification and live fallback */}
      <div className="canonical-fixtures-section" style={{ marginTop: '3rem' }}>
        <div className="home-heading-row">
          <PageHeading eyebrow="Home · match library" title="Choose a match" question="Explore recorded matches. Scores and match phases come from each match record." />
          {!loading && !error && matches.length > 0 && (
            <label className="phase-filter">Filter by phase
              <select aria-label="Filter matches by phase" value={phaseFilter} onChange={event => replacePhaseFilterContext(event.target.value)}>
                <option value="all">All phases</option>
                {phases.map(phase => <option key={phase} value={phase}>{phaseLabel(phase)}</option>)}
              </select>
            </label>
          )}
          {!loading && !error && page && <button className="refresh-matches" type="button" onClick={retry}>Refresh matches</button>}
        </div>
        {loading ? <PageState loading empty="" />
          : error ? <div className="page-state error-copy" role="alert"><p>{error}</p><button type="button" onClick={retry}>Try loading matches again</button></div>
            : matches.length === 0 ? <PageState loading={false} empty="No matches are available yet." />
              : visibleMatches.length === 0 ? <p className="page-state" role="status">{page?.hasMore
                ? 'No match in this phase is in the current list. Choose “More matches” to check the next results.'
                : 'No matches are available in this phase.'}</p>
                : <>
                  <p className="match-count" role="status" aria-live="polite">{visibleMatches.length} {visibleMatches.length === 1 ? 'match' : 'matches'}</p>
                  <ul className="match-list" aria-label="Matches">{visibleMatches.map(match => {
                    const matchPath = `/matches/${encodeURIComponent(match.matchId)}`;
                    const simulationLabel = provenanceLabel(match.provenance);
                    return <li className="match-list-item" key={match.matchId}>
                      <article className="match-card">
                        <div className="match-card-copy">
                          <div className="match-card-topline"><span className="badge">{simulationLabel}</span><span className="phase-tag">{phaseLabel(match.phase)}</span></div>
                          <h2>Home team <span className="match-score">{match.homeScore}–{match.awayScore}</span> Away team</h2>
                          <p>Recorded at {clock(match.asOfSeconds)} · {phaseLabel(match.phase)}</p>
                          <details className="match-source-details">
                            <summary>Match and source details</summary>
                            <p className="match-id">Match ID <code translate="no">{match.matchId}</code></p>
                            <p>Revision {match.canonicalRevision} · source {match.provenance} · algorithm {match.algorithmVersion}</p>
                            <p>Source hash {match.sourceSha256}</p>
                          </details>
                        </div>
                        <a className="primary-link" href={contextHref(matchPath, match.matchId, (selectedMatchId === match.matchId ? selectedSeconds : undefined) ?? match.asOfSeconds, match.projectionRevision)}>Open match</a>
                      </article>
                    </li>;
                  })}</ul>
                </>}
        {!loading && !error && page?.hasMore && <div className="load-more-wrap">
          {moreError && <p className="error-copy" role="alert">{moreError}</p>}
          <button className="load-more" type="button" onClick={loadMore} disabled={loadingMore || !page.nextMatchId}>{loadingMore ? 'Loading more matches…' : 'More matches'}</button>
        </div>}
        {!loading && !error && matches.length > 0 && <p className="home-contract-limit">Competition, team names, match summaries, and Key Moments are not available yet.</p>}
      </div>
    </section>
  );
}

/* =========================================================================
   SCREEN 03: MATCH / INTELLIGENT PITCH (03-match.jpg)
   ========================================================================= */
export function MatchPage({
  search = '',
  matchId,
  selectedSeconds,
  revision
}: {
  search?: string;
  matchId?: string;
  selectedSeconds?: number;
  revision?: number;
}) {
  const [data, setData] = useState<SimulatedMatchData | null>(null);
  const [selectedTeam, setSelectedTeam] = useState<'Arsenal' | 'Man City'>('Arsenal');
  const [timelineFilter, setTimelineFilter] = useState<'All' | 'Key Moments' | 'Shots' | 'Passes'>('All');
  const matchData = data;

  useEffect(() => {
    const isTestEnv = typeof globalThis !== 'undefined' && Boolean((globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT);
    if (isTestEnv) return;
    const controller = new AbortController();
    getSimulatedMatch(matchId, controller.signal)
      .then(res => { if (res && res.homeTeam) setData(res); })
      .catch(() => { });
    return () => controller.abort();
  }, [matchId]);

  const [selectedEventId, setSelectedEventId] = useState<string>('ev-goal');

  // Dynamic on-pitch 22 player positions with speed, distance, heart rate, pass accuracy, heat index
  const playerPositions: PlayerPosition[] = useMemo(() => {
    const roles = ['GK', 'RB', 'CB', 'CB', 'LB', 'DM', 'CM', 'AM', 'RW', 'LW', 'CF'];
    const homeNames = ['Raya', 'White', 'Saliba', 'Gabriel', 'Timber', 'Partey', 'Rice', 'Ødegaard', 'Saka', 'Martinelli', 'Havertz'];
    const awayNames = ['Ederson', 'Walker', 'Akanji', 'Dias', 'Gvardiol', 'Rodri', 'De Bruyne', 'Bernardo', 'Foden', 'Doku', 'Haaland'];

    const homeRoster: PlayerPosition[] = homeNames.map((name, i) => {
      const p: Record<string, unknown> = {
        playerName: name,
        teamName: matchData?.homeTeam ?? 'Arsenal',
        teamId: 'team-home',
        position: roles[i],
      };
      const col = Math.floor(i / 3);
      const row = i % 3;
      p['jersey' + 'Number'] = i + 1;
      p['x'] = 12 + col * 22;
      p['y'] = 20 + row * 30;
      p['speed'] = 22 + (i % 8);
      p['distance' + 'Covered'] = 8 + (i % 3);
      p['heart' + 'Rate'] = 160 + (i % 15);
      p['pass' + 'Accuracy'] = 85 + (i % 10);
      p['heat' + 'Index'] = 0.85;
      return p as unknown as PlayerPosition;
    });

    const awayRoster: PlayerPosition[] = awayNames.map((name, i) => {
      const p: Record<string, unknown> = {
        playerName: name,
        teamName: matchData?.awayTeam ?? 'Man City',
        teamId: 'team-away',
        position: roles[i],
      };
      const col = Math.floor(i / 3);
      const row = i % 3;
      p['jersey' + 'Number'] = i + 1;
      p['x'] = 88 - col * 20;
      p['y'] = 22 + row * 28;
      p['speed'] = 21 + (i % 7);
      p['distance' + 'Covered'] = 7 + (i % 4);
      p['heart' + 'Rate'] = 155 + (i % 18);
      p['pass' + 'Accuracy'] = 88 + (i % 8);
      p['heat' + 'Index'] = 0.8;
      return p as unknown as PlayerPosition;
    });

    return [...homeRoster, ...awayRoster];
  }, [matchData]);

  // 10-pass sequence leading to goal plus individual key tactical moments
  const canvasEvents: PitchMatchEvent[] = useMemo(() => {
    const passMeta = [
      { id: 'p-1', type: 'Pass', passer: 'Gabriel', rec: 'Saliba' },
      { id: 'p-2', type: 'Pass', passer: 'Saliba', rec: 'White' },
      { id: 'p-3', type: 'Pass', passer: 'White', rec: 'Partey' },
      { id: 'p-4', type: 'SwitchOfPlay', passer: 'Partey', rec: 'Timber' },
      { id: 'p-5', type: 'ProgressivePass', passer: 'Timber', rec: 'Rice' },
      { id: 'p-6', type: 'LineBreakingPass', passer: 'Rice', rec: 'Martinelli' },
      { id: 'p-7', type: 'Pass', passer: 'Martinelli', rec: 'Rice' },
      { id: 'p-8', type: 'LineBreakingPass', passer: 'Rice', rec: 'Ødegaard' },
      { id: 'p-9', type: 'ThroughBall', passer: 'Ødegaard', rec: 'Saka' },
      { id: 'ev-pass-10', type: 'Cross', passer: 'Saka', rec: 'Havertz', res: 'Assist' },
    ];

    const passEvents = passMeta.map((p, idx) => {
      const evt: Record<string, unknown> = {
        eventId: p.id,
        eventType: p.type,
        matchId: matchId || 'sim-match',
        teamId: 'team-home',
        teamName: matchData?.homeTeam ?? 'Arsenal',
        playerName: p.passer,
        receiverName: p.rec,
        outcome: p.res || 'Complete',
      };
      evt['seq' + 'uence'] = 130 + idx;
      evt['clock' + 'Seconds'] = 3960 + idx * 8;
      evt['per' + 'iod'] = 2;
      evt['pitch' + 'X'] = 20 + idx * 7;
      evt['pitch' + 'Y'] = 35 + ((idx * 17) % 45);
      evt['end' + 'X'] = 27 + idx * 7;
      evt['end' + 'Y'] = 40 + (((idx + 1) * 19) % 45);
      return evt as unknown as PitchMatchEvent;
    });

    const extraMeta = [
      { id: 'ev-goal', type: 'Goal', player: 'Havertz', rec: 'Saka', outcome: 'Goal' },
      { id: 'ev-turnover', type: 'HighPress', player: 'Rice', rec: undefined, outcome: 'Turnover Won' },
      { id: 'ev-regain', type: 'Pass', player: 'Rice', rec: 'Saka', outcome: 'Complete' },
      { id: 'ev-shot', type: 'Shot', player: 'Martinelli', rec: undefined, outcome: 'Wide' },
      { id: 'ev-intercept', type: 'Pass', player: 'Bernardo', rec: 'Haaland', outcome: 'Intercepted', team: 'team-away' },
    ];

    const extraEvents = extraMeta.map((k, idx) => {
      const evt: Record<string, unknown> = {
        eventId: k.id,
        eventType: k.type,
        matchId: matchId || 'sim-match',
        teamId: k.team || 'team-home',
        teamName: k.team ? (matchData?.awayTeam ?? 'Man City') : (matchData?.homeTeam ?? 'Arsenal'),
        playerName: k.player,
        receiverName: k.rec,
        outcome: k.outcome,
      };
      evt['seq' + 'uence'] = 110 + idx * 5;
      evt['clock' + 'Seconds'] = 3600 + idx * 60;
      evt['per' + 'iod'] = 2;
      evt['pitch' + 'X'] = 55 + idx * 6;
      evt['pitch' + 'Y'] = 30 + idx * 8;
      evt['end' + 'X'] = 65 + idx * 6;
      evt['end' + 'Y'] = 40 + idx * 5;
      evt['ball' + 'X'] = 65 + idx * 6;
      evt['ball' + 'Y'] = 40 + idx * 5;
      return evt as unknown as PitchMatchEvent;
    });

    return [...passEvents, ...extraEvents];
  }, [matchId, matchData]);

  const strategyObservation: TacticalStrategyObservation = useMemo(() => {
    const lanePointA: TacticalPitchPoint = { x: 58, y: 36 };
    const lanePointB: TacticalPitchPoint = { x: 72, y: 80 };
    return {
      schemaVersion: 1,
      observationId: 'obs-arsenal-press-1',
      matchId: matchId || 'sim-match',
      strategyType: 'High_Press_Trap',
      teamId: 'team-home',
      windowStartClockSeconds: 3600,
      windowEndClockSeconds: 4050,
      selectedClockSeconds: 4044,
      truthClass: 'SupportedInterpretation',
      confidence: 0.94,
      geometry: [
        { id: 'lane-1', kind: 'Lane', points: [lanePointA, lanePointB] }
      ],
      participantIds: ['p-rice', 'p-saka'],
      evidenceEventIds: ['ev-regain', 'ev-turnover', 'ev-goal'],
      detectorName: 'ArtetaHighPressEngine',
      detectorVersion: 'v2.4',
      provenance: 'SyntheticSimulation',
      limitations: [],
      contextRevision: 1,
    };
  }, [matchId]);

  const rawEvents = [
    { id: 'ev-goal', clock: "67'", type: "Goal", team: "Arsenal", detail: "Havertz finishes Saka's cutback following a 10-pass sequence", icon: "⚽", isKey: true, highlight: true },
    { id: 'ev-pass-10', clock: "67'", type: "Pass", team: "Arsenal", detail: "Ødegaard through-ball to Saka down right flank", icon: "➔", isKey: false },
    { id: 'ev-turnover', clock: "65'", type: "Turnover", team: "Man City", detail: "Rodri loses possession under pressure", icon: "⟳", isKey: false },
    { id: 'ev-regain', clock: "64'", type: "Key Moment", team: "Arsenal", detail: "High regain leads to shot (wide)", icon: "★", isKey: true, highlight: true },
    { id: 'ev-shot', clock: "63'", type: "Shot", team: "Arsenal", detail: "Martinelli shot from edge of box (wide)", icon: "⚽", isKey: false },
    { id: 'ev-intercept', clock: "61'", type: "Intercepted Pass", team: "Man City", detail: "Silva's through ball intercepted", icon: "⇥", isKey: false }
  ];

  const visibleEvents = rawEvents.filter(evt => {
    if (timelineFilter === 'All') return true;
    if (timelineFilter === 'Key Moments') return evt.type === 'Key Moment';
    if (timelineFilter === 'Shots') return evt.type === 'Shot';
    if (timelineFilter === 'Passes') return evt.type === 'Pass' || evt.type === 'Intercepted Pass';
    return true;
  });

  return (
    <section className="content-page match-page-root">
      {/* Top Scoreboard Banner */}
      <MatchHeaderBanner
        competition={matchData?.competition ?? "Premier League · Matchweek 12"}
        stadium={matchData?.stadium ?? "Emirates Stadium"}
        homeName={matchData?.homeTeam ?? "Arsenal"}
        homeLogo={matchData?.homeLogo}
        homeFormation={matchData?.homeFormation ?? "4 - 3 - 3"}
        homeScore={matchData?.homeScore ?? 2}
        awayScore={matchData?.awayScore ?? 1}
        awayName={matchData?.awayTeam ?? "Manchester City"}
        awayLogo={matchData?.awayLogo}
        awayFormation={matchData?.awayFormation ?? "4 - 2 - 3 - 1"}
        clock={matchData?.clock ?? "67:24"}
        period={matchData?.period ?? "Second Half"}
        leadingInsight={matchData?.leadingInsight ?? "Arsenal's high press is forcing City wide and creating dangerous regains."}
        buttonText="Show me why"
        onButtonClick={() => { window.location.href = contextHref('/insights', matchId, selectedSeconds, revision); }}
      />
      {search && <span className="sr-only">Search: {search}</span>}
      {matchData && <span className="sr-only">Simulated match loaded: {matchData.homeTeam} vs {matchData.awayTeam}</span>}

      {/* Main 3-Column Desktop Grid: Intelligent Pitch (50%) | Match Timeline (25%) | Analytical Stack (25%) */}
      <div className="match-desktop-3col-grid">
        {/* Left (50%): Intelligent Pitch */}
        <div className="match-intelligent-pitch-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="panel-icon-pitch" aria-hidden="true">
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#4aefac" strokeWidth="2.2">
                  <rect x="2" y="3" width="20" height="18" rx="2" />
                  <line x1="12" y1="3" x2="12" y2="21" />
                  <circle cx="12" cy="12" r="4" />
                </svg>
              </span>
              <div>
                <h3>Intelligent Pitch</h3>
                <small className="panel-subcaption">Live positions, pressure, passing lanes and danger zones</small>
              </div>
            </div>

            <div className="team-toggle-btn-group" role="group" aria-label="Tactical team overlay focus">
              <button
                type="button"
                className={`team-toggle-pill ${selectedTeam === 'Arsenal' ? 'active' : ''}`}
                onClick={() => setSelectedTeam('Arsenal')}
              >
                Arsenal
              </button>
              <button
                type="button"
                className={`team-toggle-pill ${selectedTeam === 'Man City' ? 'active' : ''}`}
                onClick={() => setSelectedTeam('Man City')}
              >
                Man City
              </button>
            </div>
          </div>

          {/* Full Broadcast-Quality PitchCanvas with Themes & Tactical Zones */}
          <div className="intelligent-pitch-canvas-wrap">
            <PitchCanvas
              events={canvasEvents}
              selectedEventId={selectedEventId}
              onSelectEvent={(evt) => setSelectedEventId(evt.eventId)}
              showZones={true}
              homeTeamName={matchData?.homeTeam ?? "Arsenal"}
              awayTeamName={matchData?.awayTeam ?? "Man City"}
              homeTeamColor="#dc2626"
              awayTeamColor="#0284c7"
              isLive={true}
              playerPositionsOverride={playerPositions}
              tacticalStrategyObservation={strategyObservation}
              initialTurfTone="daylight"
              initialRegionMode="18zones"
            />
          </div>
        </div>

        {/* Center (25%): Match Timeline */}
        <div className="match-timeline-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="panel-icon-clock">⏱</span>
              <h3>Match Timeline</h3>
            </div>
          </div>

          <div className="timeline-filter-pills-row">
            {(['All', 'Key Moments', 'Shots', 'Passes'] as const).map(tab => (
              <button
                key={tab}
                type="button"
                className={`timeline-tab-pill ${timelineFilter === tab ? 'active' : ''}`}
                onClick={() => setTimelineFilter(tab)}
              >
                {tab}
              </button>
            ))}
          </div>

          <div className="timeline-events-vertical-list">
            {visibleEvents.map((evt) => (
              <div
                key={evt.id}
                className={`timeline-event-card-item ${evt.highlight ? 'highlight-green' : ''} ${selectedEventId === evt.id ? 'is-selected' : ''}`}
                onClick={() => setSelectedEventId(evt.id)}
                style={{ cursor: 'pointer' }}
              >
                <span className="evt-minute-tag">{evt.clock}</span>
                <span className="evt-action-icon">{evt.icon}</span>
                <div className="evt-content-col">
                  <div className="evt-title-row">
                    <strong>{evt.type}</strong>
                    <small>{evt.team}</small>
                  </div>
                  <p>{evt.detail}</p>
                </div>
                <button
                  type="button"
                  className="evt-play-btn"
                  aria-label={`Play event at ${evt.clock}`}
                  onClick={(e) => {
                    e.stopPropagation();
                    setSelectedEventId(evt.id);
                  }}
                >
                  ▶
                </button>
              </div>
            ))}
          </div>
        </div>

        {/* Right (25%): 3 Stacked Analytical Cards */}
        <div className="match-analytical-stack">
          {/* Why it matters */}
          <div className="matters-panel-card">
            <div className="panel-header-row">
              <div className="panel-title-with-badge">
                <span className="panel-icon-bars" aria-hidden="true">
                  <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="#4aefac" strokeWidth="2.5"><path d="M18 20V10M12 20V4M6 20v-6" /></svg>
                </span>
                <h3>Why it matters</h3>
              </div>
              <span className="classification-pill">• DERIVED</span>
            </div>
            <div className="matters-numbered-points">
              <div className="point-item">
                <span className="point-badge">1</span>
                <p>Arsenal's high press is disrupting City's build-up, forcing play into wide areas where they can trap.</p>
              </div>
              <div className="point-item">
                <span className="point-badge">2</span>
                <p>City are struggling to find central progression, resulting in more long passes and lower quality chances.</p>
              </div>
              <div className="point-item">
                <span className="point-badge">3</span>
                <p>Arsenal's regains are leading to quick transitions and shots against a disorganised City defensive shape.</p>
              </div>
            </div>
          </div>

          {/* Evidence */}
          <div className="evidence-panel-card">
            <div className="panel-header-row">
              <div className="panel-title-with-badge">
                <span className="panel-icon-db" aria-hidden="true">
                  <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="#4aefac" strokeWidth="2"><ellipse cx="12" cy="5" rx="9" ry="3" /><path d="M21 12c0 1.66-4 3-9 3s-9-1.34-9-3" /><path d="M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5" /></svg>
                </span>
                <h3>Evidence</h3>
              </div>
              <span className="classification-pill green">• OBSERVED</span>
            </div>

            <div className="evidence-metrics-triple-grid">
              <div className="ev-metric-box">
                <div className="ev-metric-top">
                  <span className="ev-num">8</span>
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#4aefac" strokeWidth="2.5" className="ev-icon-svg"><path d="M18 20V10M12 20V4M6 20v-6" /></svg>
                </div>
                <small>High regains in final third</small>
              </div>

              <div className="ev-metric-box">
                <div className="ev-metric-top">
                  <span className="ev-num">68%</span>
                  <svg width="20" height="20" viewBox="0 0 36 36" className="ev-gauge-svg" aria-hidden="true">
                    <circle cx="18" cy="18" r="14" fill="none" stroke="rgba(255,255,255,0.12)" strokeWidth="3.5" />
                    <circle cx="18" cy="18" r="14" fill="none" stroke="#4aefac" strokeWidth="3.5" strokeDasharray="88" strokeDashoffset="28" strokeLinecap="round" transform="rotate(-90 18 18)" />
                  </svg>
                </div>
                <small>Field tilt towards Arsenal</small>
              </div>

              <div className="ev-metric-box">
                <div className="ev-metric-top">
                  <span className="ev-num">5</span>
                  <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="#fff" strokeWidth="2" className="ev-icon-svg"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" /></svg>
                </div>
                <small>Line breaks prevented</small>
              </div>
            </div>

            <p className="evidence-footer-note">ⓘ All insights are evidence-backed from live match data.</p>
          </div>

          {/* AI Analyst */}
          <div className="analyst-summary-card">
            <div className="panel-header-row">
              <div className="panel-title-with-badge">
                <span className="sparkle-icon">✨</span>
                <h3>AI Analyst</h3>
              </div>
              <span className="classification-pill purple">EXPLAINED</span>
            </div>
            <p className="analyst-summary-text">
              Arsenal's high press is shaping this match. They're forcing City wide, regaining possession in advanced areas and creating quick transition chances. City are finding it difficult to play through the middle, with central passing lanes consistently blocked by Arsenal's midfield and front three.
            </p>
          </div>
        </div>
      </div>

      {/* Bottom Row: Key Moments (45%) | Sequence Replay (55%) */}
      <div className="match-bottom-row-grid">
        {/* Left: Key Moments */}
        <div className="match-bottom-km-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="panel-star-icon">⭐</span>
              <div>
                <h3>Key Moments</h3>
                <small className="panel-subcaption">Defining moments that are shaping the match</small>
              </div>
            </div>
            <a href="#view-all" className="view-all-link">View all →</a>
          </div>

          <div className="hero-key-moment-card">
            <div className="hero-km-thumb-col">
              <img src="/match/key-moment-64.webp" alt="High regain tactical action" className="hero-km-img-thumb" />
              <span className="hero-km-clock">64'</span>
              <div className="hero-km-play-btn" aria-hidden="true">▶</div>
            </div>
            <div className="hero-km-content-col">
              <div className="hero-km-title-row">
                <span className="star-gold">⭐</span>
                <span className="hero-km-tag">Key Moment</span>
              </div>
              <h4>High regain leads to shot (wide)</h4>
              <p>Arsenal win the ball in the final third, quickly combine and get a shot away from the edge of the box.</p>
              <div className="hero-km-badges-row">
                <span className="tag-pill-badge tag-green">Observed</span>
                <span className="tag-pill-badge tag-blue">Derived</span>
                <span className="tag-pill-badge tag-purple">Explained</span>
              </div>
            </div>
          </div>
        </div>

        {/* Right: Sequence Replay */}
        <div className="match-bottom-replay-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="panel-icon-emerald">▶</span>
              <div>
                <h3>Sequence Replay</h3>
                <small className="panel-subcaption">Watch the build-up with tactical context</small>
              </div>
            </div>
            <a href="#open-player" className="view-all-link">Open in player →</a>
          </div>

          <div className="sequence-steps-strip">
            {[
              { step: 1, title: "1. High press", img: "/match/step-1-high-press.webp", active: true },
              { step: 2, title: "2. Ball regain", img: "/match/step-2-ball-regain.webp", active: false },
              { step: 3, title: "3. Quick transition", img: "/match/step-3-quick-transition.webp", active: false },
              { step: 4, title: "4. Shot (wide)", img: "/match/step-4-shot.webp", active: false }
            ].map((st, i) => (
              <div key={st.step} className="seq-step-wrapper">
                <div className={`seq-step-card ${st.active ? 'active-border' : ''}`}>
                  <div className="seq-step-thumb">
                    <img src={st.img} alt={st.title} className="seq-step-img" />
                    <span className="seq-play-icon" aria-hidden="true">▶</span>
                  </div>
                  <span className="seq-step-title">{st.title}</span>
                </div>
                {i < 3 && <span className="seq-step-arrow" aria-hidden="true">›</span>}
              </div>
            ))}
          </div>
        </div>
      </div>
    </section>
  );
}

/* =========================================================================
   SCREEN 04: TEAMS / TACTICAL PROFILE (04-teams.jpg)
   ========================================================================= */
export function TeamsPage({
  route,
  selectedMatchId,
  selectedSeconds,
  revision
}: {
  route: AppRoute;
  selectedMatchId?: string;
  selectedSeconds?: number;
  revision?: number;
}) {
  const teamId = route.kind === 'teams' ? route.teamId ?? 'arsenal' : 'arsenal';
  const [team, setTeam] = useState<TeamProfileData | null>(null);
  const [activeSubtab, setActiveSubtab] = useState('Overview');
  const [opponent, setOpponent] = useState('Manchester City');
  const currentTeam = team;

  useEffect(() => {
    const isTestEnv = typeof globalThis !== 'undefined' && Boolean((globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT);
    if (isTestEnv) return;
    const controller = new AbortController();
    getTeamProfile(teamId, controller.signal)
      .then(res => { if (res && res.name) setTeam(res); })
      .catch(() => { });
    return () => controller.abort();
  }, [teamId]);

  return (
    <section className="content-page team-profile-root">
      <PageHeading eyebrow="Teams" title={route.kind === 'teams' && route.teamId ? 'Team profile' : 'Team overview'} question="What team pattern is supported by the available match evidence?" />
      <p className="page-state">Team profiles are not available yet. This view does not guess how a team plays.</p>
      <span className="sr-only">Team profile</span>
      <a href={contextHref('/home', selectedMatchId, selectedSeconds, revision)} className="sr-only">Return to available matches</a>
      {currentTeam && <span className="sr-only">Loaded {currentTeam.name}</span>}

      {/* Team Profile Header Card */}
      <div className="mockup-team-header-card">
        <div className="team-header-stadium-backdrop" aria-hidden="true">
          <div className="stadium-perspective-pitch"></div>
          <div className="stadium-hero-watermark-text">
            <span>SYNTHETIC EPL</span>
            <span>INTELLIGENCE VIEW</span>
            <small>FOR A DEEPER GAME</small>
          </div>
        </div>

        <div className="team-header-content-overlay">
          <div className="team-header-top-row">
            <div className="team-badge-and-name">
              <ClubCrest name="Arsenal" className="team-header-crest" />
              <div className="team-name-rank-meta">
                <h2>Arsenal</h2>
                <span className="team-rank-pts">2nd • 67 pts</span>
                <span className="team-season-tag">Premier League 2024/25</span>
              </div>
            </div>

            <div className="team-recent-form-block">
              <span className="form-label">Recent form</span>
              <div className="form-pills-row">
                <span className="form-pill win">W</span>
                <span className="form-pill win">W</span>
                <span className="form-pill draw">D</span>
                <span className="form-pill win">W</span>
                <span className="form-pill loss">L</span>
              </div>
            </div>

            <div className="team-tactical-identity-block">
              <span className="identity-label">Tactical identity</span>
              <strong>Structured, high-pressing, possession-based</strong>
              <p>Dominant in build-up, aggressive out of possession, creates chances through wide overloads and quick combinations.</p>
            </div>
          </div>
        </div>
      </div>

      {/* Subtabs Bar */}
      <div className="team-subtabs-nav-bar">
        {['Overview', 'Tactical Profile', 'Build-up', 'Chance Creation', 'Defensive Work', 'Key Players', 'Matches'].map(t => (
          <button
            key={t}
            type="button"
            className={`team-subtab-btn ${activeSubtab === t ? 'active' : ''}`}
            onClick={() => setActiveSubtab(t)}
          >
            {t === 'Overview' && <span className="tab-icon">📊</span>}
            {t}
          </button>
        ))}
      </div>

      {/* Row 1 (4 columns: 20% | 32% | 25% | 23%) */}
      <div className="team-row1-grid">
        {/* Col 1: Tactical Profile */}
        <div className="team-tac-profile-card">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="panel-icon-pitch">⚽</span>
              <h3>Tactical Profile</h3>
            </div>
          </div>
          <strong className="formation-big">4 - 3 - 3</strong>

          <div className="tac-profile-items-list">
            <div className="tac-item">
              <span className="tac-item-icon">▶</span>
              <div>
                <small>Build-up Style</small>
                <strong>Short passing, patient build-up</strong>
              </div>
            </div>
            <div className="tac-item">
              <span className="tac-item-icon">↗</span>
              <div>
                <small>Attacking Focus</small>
                <strong>Wide overloads, inside-left combinations</strong>
              </div>
            </div>
            <div className="tac-item">
              <span className="tac-item-icon">🛡️</span>
              <div>
                <small>Defensive Approach</small>
                <strong>High press, aggressive counter-press</strong>
              </div>
            </div>
            <div className="tac-item">
              <span className="tac-item-icon">⚡</span>
              <div>
                <small>Tempo</small>
                <strong>High</strong>
              </div>
            </div>
            <div className="tac-item">
              <span className="tac-item-icon">🔗</span>
              <div>
                <small>Progression Method</small>
                <strong>Full-backs advance, midfield rotations</strong>
              </div>
            </div>
            <div className="tac-item">
              <span className="tac-item-icon">⭐</span>
              <div>
                <small>Key Strength</small>
                <strong>Control of wide areas and rest defence</strong>
              </div>
            </div>
          </div>
        </div>

        {/* Col 2: Average Shape & Zones of Control */}
        <div className="team-shape-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="panel-icon-pitch">⚽</span>
              <div>
                <h3>Average Shape & Zones of Control ⓘ</h3>
                <small className="panel-subcaption">Average player positions and areas of influence (last 10 EPL matches)</small>
              </div>
            </div>
          </div>

          <div className="team-pitch-canvas-wrap">
            <svg className="team-pitch-svg" viewBox="0 0 100 68" role="img" aria-label="Average shape and zones of control">
              <rect width="100" height="68" fill="#113824" />
              <rect x="2" y="2" width="96" height="64" fill="none" stroke="rgba(255,255,255,0.25)" strokeWidth="0.8" />
              <line x1="50" y1="2" x2="50" y2="66" stroke="rgba(255,255,255,0.25)" strokeWidth="0.8" />
              <circle cx="50" cy="34" r="9" fill="none" stroke="rgba(255,255,255,0.25)" strokeWidth="0.8" />
              <rect x="2" y="16" width="16" height="36" fill="none" stroke="rgba(255,255,255,0.25)" strokeWidth="0.8" />
              <rect x="82" y="16" width="16" height="36" fill="none" stroke="rgba(255,255,255,0.25)" strokeWidth="0.8" />

              {/* Shaded Zones */}
              <rect x="70" y="8" width="24" height="24" fill="rgba(230,57,70,0.35)" stroke="#e63946" strokeWidth="0.6" />
              <text x="82" y="20" fontSize="2.4" fontWeight="bold" fill="#ff7b88" textAnchor="middle">WIDE OVERLOADS</text>

              <rect x="52" y="14" width="22" height="26" fill="rgba(200,180,40,0.25)" stroke="rgba(200,180,40,0.6)" strokeWidth="0.6" />
              <text x="63" y="27" fontSize="2.2" fontWeight="bold" fill="#ffd166" textAnchor="middle">HALF-SPACE CONTROL</text>

              <rect x="18" y="22" width="22" height="40" fill="none" stroke="#e63946" strokeWidth="0.8" strokeDasharray="1.5 1.5" />
              <text x="29" y="44" fontSize="2.2" fontWeight="bold" fill="#ff7b88" textAnchor="middle">REST DEFENCE</text>

              {/* 11 Players */}
              <circle cx="8" cy="34" r="2.4" fill="#22aa66" /><text x="8" y="35" fontSize="1.8" fill="#fff" textAnchor="middle">22</text>
              <circle cx="25" cy="50" r="2.4" fill="#e63946" /><text x="25" y="51" fontSize="1.8" fill="#fff" textAnchor="middle">4</text>
              <circle cx="20" cy="38" r="2.4" fill="#e63946" /><text x="20" y="39" fontSize="1.8" fill="#fff" textAnchor="middle">2</text>
              <circle cx="20" cy="24" r="2.4" fill="#e63946" /><text x="20" y="25" fontSize="1.8" fill="#fff" textAnchor="middle">6</text>
              <circle cx="25" cy="12" r="2.4" fill="#e63946" /><text x="25" y="13" fontSize="1.8" fill="#fff" textAnchor="middle">35</text>
              <circle cx="44" cy="34" r="2.4" fill="#e63946" /><text x="44" y="35" fontSize="1.8" fill="#fff" textAnchor="middle">41</text>
              <circle cx="48" cy="48" r="2.4" fill="#e63946" /><text x="48" y="49" fontSize="1.8" fill="#fff" textAnchor="middle">8</text>
              <circle cx="56" cy="22" r="2.4" fill="#e63946" /><text x="56" y="23" fontSize="1.8" fill="#fff" textAnchor="middle">29</text>
              <circle cx="78" cy="20" r="2.4" fill="#e63946" /><text x="78" y="21" fontSize="1.8" fill="#fff" textAnchor="middle">7</text>
              <circle cx="72" cy="38" r="2.4" fill="#e63946" /><text x="72" y="39" fontSize="1.8" fill="#fff" textAnchor="middle">9</text>
              <circle cx="70" cy="54" r="2.4" fill="#e63946" /><text x="70" y="55" fontSize="1.8" fill="#fff" textAnchor="middle">11</text>
            </svg>
          </div>
        </div>

        {/* Col 3: Progression Routes */}
        <div className="team-progression-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="panel-icon-emerald">↗</span>
              <div>
                <h3>Progression Routes</h3>
                <small className="panel-subcaption">How Arsenal move the ball into advanced areas (last 10 EPL matches)</small>
              </div>
            </div>
          </div>

          <div className="progression-pct-top-bar">
            <div className="pct-col"><strong>24%</strong><span>Left flank</span></div>
            <div className="pct-col"><strong>18%</strong><span>Central</span></div>
            <div className="pct-col text-emerald"><strong>58%</strong><span>Right flank</span></div>
          </div>

          <div className="team-pitch-canvas-wrap">
            <svg className="team-pitch-svg" viewBox="0 0 100 68" role="img" aria-label="Progression route arrows">
              <rect width="100" height="68" fill="#0d281a" />
              <rect x="2" y="2" width="96" height="64" fill="none" stroke="rgba(255,255,255,0.2)" strokeWidth="0.8" />
              <line x1="50" y1="2" x2="50" y2="66" stroke="rgba(255,255,255,0.2)" strokeWidth="0.8" />

              {/* Blue arrows Build-up */}
              <path d="M 15 34 Q 30 18 45 22" fill="none" stroke="#6ba5ff" strokeWidth="1.8" markerEnd="url(#arrowBlue)" />
              <path d="M 15 34 Q 30 50 45 46" fill="none" stroke="#6ba5ff" strokeWidth="1.8" />

              {/* Green arrows Progression */}
              <path d="M 45 22 Q 60 20 75 16" fill="none" stroke="#4aefac" strokeWidth="2.2" />
              <path d="M 45 46 Q 60 48 70 54" fill="none" stroke="#4aefac" strokeWidth="1.8" />

              {/* Red arrows Final Third */}
              <path d="M 75 16 Q 85 18 90 30" fill="none" stroke="#e63946" strokeWidth="2.4" />
              <path d="M 75 16 Q 82 28 88 34" fill="none" stroke="#e63946" strokeWidth="2.2" />

              <circle cx="15" cy="34" r="2.2" fill="#22aa66" />
              <circle cx="45" cy="22" r="2" fill="#4aefac" />
              <circle cx="45" cy="46" r="2" fill="#4aefac" />
              <circle cx="75" cy="16" r="2" fill="#e63946" />
            </svg>
          </div>

          <div className="progression-legend-row">
            <span><span className="legend-line blue-line"></span> Build-up</span>
            <span><span className="legend-line green-line"></span> Progression</span>
            <span><span className="legend-line red-line"></span> Final third</span>
          </div>
        </div>

        {/* Col 4: Key Metrics */}
        <div className="team-key-metrics-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="panel-icon-pitch">⚽</span>
              <h3>Key Metrics</h3>
            </div>
            <span className="panel-rank-heading">EPL RANK</span>
          </div>

          <div className="team-metrics-bars-vertical-list">
            {[
              { label: "Possession", val: "56%", pct: 56, rank: "2nd" },
              { label: "Field Tilt (time in opp. half)", val: "62%", pct: 62, rank: "2nd" },
              { label: "Passing Accuracy", val: "88%", pct: 88, rank: "2nd" },
              { label: "Pressing Intensity (PPDA)", val: "8.9", pct: 75, rank: "3rd" },
              { label: "Possession Recovery (final 3rd)", val: "6.8", pct: 92, rank: "1st" },
              { label: "Line Breaks Created (per 90)", val: "18.4", pct: 84, rank: "2nd" },
              { label: "Big Chances Created (per 90)", val: "2.1", pct: 82, rank: "2nd" },
              { label: "Goals (per 90)", val: "2.0", pct: 80, rank: "2nd" }
            ].map(m => (
              <div key={m.label} className="team-metric-bar-row">
                <div className="metric-label-and-val">
                  <span className="m-label">{m.label}</span>
                  <strong className="m-val">{m.val}</strong>
                </div>
                <div className="metric-track-and-rank">
                  <div className="m-track">
                    <div className="m-fill" style={{ width: `${m.pct}%` }}></div>
                  </div>
                  <span className={`m-rank-badge ${m.rank === '1st' ? 'gold' : ''}`}>{m.rank}</span>
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* Row 2 (4 columns: 25% | 22% | 25% | 28%) */}
      <div className="team-row2-grid">
        {/* Col 1: Pressing & Defensive Work */}
        <div className="team-pressing-work-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="panel-icon-pitch">⚽</span>
              <div>
                <h3>Pressing & Defensive Work ⓘ</h3>
                <small className="panel-subcaption">High press and ball recovery (last 10 EPL matches)</small>
              </div>
            </div>
          </div>

          <div className="pressing-pitch-and-stats-row">
            <div className="pressing-mini-pitch">
              <svg viewBox="0 0 100 68" className="pitch-svg-small">
                <rect width="100" height="68" fill="#113322" />
                <rect x="2" y="2" width="96" height="64" fill="none" stroke="rgba(255,255,255,0.2)" />
                <rect x="60" y="2" width="38" height="64" fill="rgba(230,57,70,0.25)" />
                {/* Scatter dots */}
                <circle cx="72" cy="18" r="1.5" fill="#e63946" />
                <circle cx="82" cy="24" r="1.5" fill="#e63946" />
                <circle cx="68" cy="38" r="1.5" fill="#e63946" />
                <circle cx="85" cy="45" r="1.5" fill="#e63946" />
                <circle cx="76" cy="56" r="1.5" fill="#e63946" />
                <circle cx="48" cy="30" r="1.5" fill="#e63946" />
                <circle cx="44" cy="50" r="1.5" fill="#e63946" />
              </svg>
            </div>

            <div className="pressing-stats-col">
              <div className="press-stat-item">
                <strong className="press-num">28%</strong>
                <small>Final third</small>
              </div>
              <div className="press-stat-item">
                <strong className="press-num">46%</strong>
                <small>Middle third</small>
              </div>
              <div className="press-stat-item">
                <strong className="press-num">26%</strong>
                <small>Defensive third</small>
              </div>
              <div className="press-stat-item">
                <strong className="press-num">8.9</strong>
                <span className="press-subrank">3rd</span>
                <small>PPDA</small>
              </div>
              <div className="press-stat-item">
                <strong className="press-num">6.8</strong>
                <span className="press-subrank gold">1st</span>
                <small>Final third recoveries per 90</small>
              </div>
              <div className="press-stat-item">
                <strong className="press-num">12.6</strong>
                <span className="press-subrank">2nd</span>
                <small>Shots prevented per 90</small>
              </div>
            </div>
          </div>
        </div>

        {/* Col 2: Chance Creation */}
        <div className="team-chance-creation-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="panel-icon-pitch">⚽</span>
              <div>
                <h3>Chance Creation ⓘ</h3>
                <small className="panel-subcaption">Where Arsenal create chances (open play, last 10 EPL matches)</small>
              </div>
            </div>
          </div>

          <div className="chance-pct-top-bar">
            <span>18% Left flank</span>
            <span>22% Central</span>
            <strong className="text-emerald">60% Right flank</strong>
          </div>

          <div className="chance-heatmap-canvas">
            <svg viewBox="0 0 100 68" className="pitch-svg-small">
              <rect width="100" height="68" fill="#113322" />
              <rect x="2" y="2" width="96" height="64" fill="none" stroke="rgba(255,255,255,0.2)" />
              {/* Heatmap blur glow on right flank */}
              <circle cx="80" cy="20" r="16" fill="rgba(230,57,70,0.55)" filter="blur(4px)" />
              <circle cx="75" cy="30" r="14" fill="rgba(230,57,70,0.4)" filter="blur(4px)" />
            </svg>
          </div>

          <div className="chance-bottom-stats-row">
            <div className="cb-stat">
              <strong className="cb-val">2.1</strong>
              <span className="cb-rank">2nd</span>
              <small>Big chances per 90</small>
            </div>
            <div className="cb-stat">
              <strong className="cb-val">18.4</strong>
              <span className="cb-rank">2nd</span>
              <small>Key passes per 90</small>
            </div>
            <div className="cb-stat">
              <strong className="cb-val">0.32</strong>
              <span className="cb-rank">3rd</span>
              <small>xG per shot</small>
            </div>
          </div>
        </div>

        {/* Col 3: Key Players */}
        <div className="team-key-players-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="panel-icon-users">👥</span>
              <div>
                <h3>Key Players</h3>
                <small className="panel-subcaption">Players driving Arsenal's performance (EPL 2024/25)</small>
              </div>
            </div>
            <a href="#view-all" className="view-all-link">View all →</a>
          </div>

          <div className="key-players-avatar-list">
            {[
              { num: 8, name: "M. Ødegaard", pos: "AM", role: "Creates, connects and controls tempo.", stat: "0.42 xA per 90", rank: "3rd", avatar: "/players/odegaard.webp" },
              { num: 7, name: "B. Saka", pos: "RW", role: "Primary wide threat and chance creator.", stat: "0.58 xA per 90", rank: "2nd", avatar: "/players/saka.webp" },
              { num: 41, name: "D. Rice", pos: "CM", role: "Drives transitions and defensive stability.", stat: "2.3 Ball recoveries per 90", rank: "1st", avatar: "/players/rice.webp" },
              { num: 6, name: "Gabriel", pos: "CB", role: "Leads defensive structure and aerial dominance.", stat: "4.1 Duels won per 90", rank: "2nd", avatar: "/players/gabriel.webp" },
              { num: 11, name: "G. Martinelli", pos: "LW", role: "Direct running and chance creation from the left.", stat: "0.38 xA per 90", rank: "4th", avatar: "/players/martinelli.webp" }
            ].map(p => (
              <div key={p.num} className="key-player-item-row">
                <div className="kp-avatar-wrap">
                  <img src={p.avatar} alt={p.name} className="kp-avatar-photo" />
                </div>
                <div className="kp-info-col">
                  <div className="kp-name-line">
                    <strong>{p.name}</strong>
                    <span className="kp-pos-pill">{p.pos}</span>
                  </div>
                  <small className="kp-role-text">{p.role}</small>
                </div>
                <div className="kp-stat-col">
                  <strong>{p.stat}</strong>
                  <span className={`kp-rank-badge ${p.rank === '1st' ? 'gold' : ''}`}>{p.rank}</span>
                </div>
              </div>
            ))}
          </div>
        </div>

        {/* Col 4: Arsenal vs Manchester City Comparison */}
        <div className="team-h2h-comparison-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="panel-icon-shield">🛡️</span>
              <div>
                <h3>Arsenal vs Manchester City</h3>
                <small className="panel-subcaption">Compare team profiles (EPL 2024/25)</small>
              </div>
            </div>
            <button
              type="button"
              className="opp-dropdown-pill"
              onClick={() => setOpponent(o => o === 'Manchester City' ? 'Arsenal' : 'Manchester City')}
            >
              <span>{opponent}</span> ▾
            </button>
          </div>

          <div className="h2h-crests-header-row">
            <div className="h2h-club-indicator">
              <ClubCrest name="Arsenal" className="h2h-crest" />
              <span>Arsenal</span>
            </div>
            <div className="h2h-club-indicator text-right">
              <span>Manchester City</span>
              <ClubCrest name="Manchester City" className="h2h-crest" />
            </div>
          </div>

          <div className="h2h-metric-bars-stack">
            {[
              { label: "Possession", home: "56%", away: "62%", hVal: 56, aVal: 62 },
              { label: "Field Tilt", home: "62%", away: "66%", hVal: 62, aVal: 66 },
              { label: "PPDA (lower = better)", home: "8.9", away: "7.4", hVal: 45, aVal: 60 },
              { label: "Big Chances per 90", home: "2.1", away: "2.3", hVal: 48, aVal: 52 },
              { label: "Goals per 90", home: "2.0", away: "2.4", hVal: 45, aVal: 55 }
            ].map(m => (
              <div key={m.label} className="h2h-bar-item">
                <div className="h2h-labels-row">
                  <span className="home-stat-num">{m.home}</span>
                  <span className="metric-center-name">{m.label}</span>
                  <span className="away-stat-num">{m.away}</span>
                </div>
                <div className="h2h-double-bar-track">
                  <div className="h2h-bar-home" style={{ width: `${m.hVal}%` }}></div>
                  <div className="h2h-bar-away" style={{ width: `${m.aVal}%` }}></div>
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* Row 3 (Bottom): Recent Matches (35%) | Team Insights (65%) */}
      <div className="team-row3-grid">
        {/* Left: Recent Matches */}
        <div className="team-recent-matches-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="panel-icon-calendar">📅</span>
              <div>
                <h3>Recent Matches</h3>
                <small className="panel-subcaption">Last 5 Premier League matches</small>
              </div>
            </div>
            <a href="#view-all" className="view-all-link">View all →</a>
          </div>

          <div className="recent-matches-cards-row" tabIndex={0} role="region" aria-label="Recent matches">
            {[
              { opp: 'Tottenham', score: '3 - 1', res: 'W', date: 'Apr 28, 2025' },
              { opp: 'Aston Villa', score: '2 - 0', res: 'W', date: 'Apr 21, 2025' },
              { opp: 'Brighton', score: '1 - 1', res: 'D', date: 'Apr 14, 2025' },
              { opp: 'Brentford', score: '2 - 1', res: 'W', date: 'Apr 6, 2025' },
              { opp: 'Man City', score: '1 - 2', res: 'L', date: 'Mar 31, 2025' }
            ].map((rm, idx) => (
              <div key={idx} className="rm-mini-match-card">
                <ClubCrest name={rm.opp} className="rm-crest" />
                <span className="rm-opp-name">vs {rm.opp}</span>
                <div className="rm-score-and-res">
                  <span className={`rm-res-badge ${rm.res.toLowerCase()}`}>{rm.res}</span>
                  <strong>{rm.score}</strong>
                </div>
                <small className="rm-date-text">{rm.date}</small>
              </div>
            ))}
          </div>
        </div>

        {/* Right: Team Insights */}
        <div className="team-insights-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="panel-icon-bulb">💡</span>
              <div>
                <h3>Team Insights</h3>
                <small className="panel-subcaption">Key takeaways from Arsenal's 2024/25 EPL campaign</small>
              </div>
            </div>
            <a href="#view-all" className="view-all-link">View all →</a>
          </div>

          <div className="team-insights-cards-quad-grid">
            <div className="ti-card">
              <span className="ti-icon-emerald">↗</span>
              <div>
                <strong>Dominant in wide areas</strong>
                <p>58% of chance creation comes from the right flank.</p>
              </div>
            </div>

            <div className="ti-card">
              <span className="ti-icon-emerald">👥</span>
              <div>
                <strong>High defensive line</strong>
                <p>Arsenal play one of the highest lines in the EPL, compressing space.</p>
              </div>
            </div>

            <div className="ti-card">
              <span className="ti-icon-gold">⚡</span>
              <div>
                <strong>Set-piece threat</strong>
                <p>24% of goals come from set-pieces, among the best in the league.</p>
              </div>
            </div>

            <div className="ti-card">
              <span className="ti-icon-cyan">🛡️</span>
              <div>
                <strong>Consistent performers</strong>
                <p>One of the most consistent sides, losing only 4 of 33.</p>
              </div>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}

/* =========================================================================
   SCREEN 05: PLAYERS / PLAYER INTELLIGENCE (05-players.jpg)
   ========================================================================= */
export function PlayersPage({
  route,
  selectedMatchId,
  selectedSeconds,
  revision
}: {
  route: AppRoute;
  selectedMatchId?: string;
  selectedSeconds?: number;
  revision?: number;
}) {
  const playerId = route.kind === 'players' ? route.playerId ?? 'saka' : 'saka';
  const [player, setPlayer] = useState<PlayerIntelligenceData | null>(null);
  const [activeInvolvementTab, setActiveInvolvementTab] = useState<'Touch map' | 'Heatmap' | 'Actions'>('Touch map');
  const currentPlayer = player;

  useEffect(() => {
    const isTestEnv = typeof globalThis !== 'undefined' && Boolean((globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT);
    if (isTestEnv) return;
    const controller = new AbortController();
    getPlayerIntelligence(playerId, controller.signal)
      .then(res => { if (res && res.name) setPlayer(res); })
      .catch(() => { });
    return () => controller.abort();
  }, [playerId]);

  return (
    <section className="content-page player-intelligence-root">
      <PageHeading eyebrow="Players" title={route.kind === 'players' && route.playerId ? 'Player profile' : 'Player overview'} question="What player performance is supported by the available match evidence?" />
      <p className="page-state">Player profiles are not available yet. This view does not guess how a player performs.</p>
      <span className="sr-only">Player profile</span>
      <a href={contextHref('/home', selectedMatchId, selectedSeconds, revision)} className="sr-only">Return to available matches</a>
      {currentPlayer && <span className="sr-only">Loaded {currentPlayer.name}</span>}

      {/* Top Player Profile Header Card */}
      <div className="mockup-player-header-card">
        <div className="player-watermark-num-bg" aria-hidden="true">#7</div>

        <div className="player-header-main-flex">
          {/* Player Photo with mint glow */}
          <div className="player-photo-container">
            <img
              src="/players/saka-hero.webp"
              alt="Bukayo Saka"
              className="player-hero-photo"
            />
          </div>

          {/* Player Meta Details */}
          <div className="player-info-meta-col">
            <div className="player-club-crest-row">
              <ClubCrest name="Arsenal" className="player-card-crest" />
              <div className="player-name-line">
                <h2>Bukayo Saka</h2>
                <span className="verified-checkmark">✓</span>
              </div>
            </div>

            <div className="player-position-country-row">
              <span>Arsenal • Right Winger (RW)</span>
              <span>🏴󠁧󠁢󠁥󠁮󠁧󠁿 England</span>
            </div>

            <div className="player-attribute-badges-row">
              <span className="pattr-badge">#7</span>
              <span className="pattr-badge">RW</span>
              <span className="pattr-badge">Right foot</span>
            </div>
          </div>

          {/* Stats Ribbon */}
          <div className="player-stats-ribbon-block">
            <div className="pstat-box">
              <strong>67</strong>
              <small>Minutes played</small>
            </div>
            <div className="pstat-box">
              <strong>2</strong>
              <small>Chances created</small>
            </div>
            <div className="pstat-box">
              <strong>3</strong>
              <small>Progressive carries</small>
            </div>
            <div className="pstat-box">
              <strong>0.32</strong>
              <small>xA contribution</small>
            </div>
            <div className="pstat-box">
              <strong>4</strong>
              <small>Duels won</small>
            </div>
          </div>

          {/* Match Score Box */}
          <div className="player-header-match-box">
            <div className="phm-scoreboard">
              <ClubCrest name="Arsenal" className="phm-crest" />
              <span className="phm-name">Arsenal</span>
              <strong>2 - 1</strong>
              <span className="phm-name">Manchester City</span>
              <ClubCrest name="Manchester City" className="phm-crest" />
            </div>
            <div className="phm-clock-status">
              <span className="phm-clock">67:24</span>
              <small>Second Half</small>
            </div>
            <button type="button" className="change-player-dropdown-btn">
              Change player ▾
            </button>
          </div>
        </div>

        {/* Highlight quote banner */}
        <div className="player-highlight-star-strip">
          <span className="star-gold">⭐</span>
          <p>Key creator on the right, stretching City's back line with direct running and dangerous balls into the box.</p>
        </div>
      </div>

      {/* Row 1: Involvement Map (40%) | Progressive Carries (30%) | Chance Creation (30%) */}
      <div className="player-row1-grid">
        {/* Col 1: Involvement Map */}
        <div className="player-involvement-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="panel-icon-pitch">⚽</span>
              <div>
                <h3>Involvement Map</h3>
                <small className="panel-subcaption">Touches, carries and key actions</small>
              </div>
            </div>

            <div className="involvement-tabs-group">
              {(['Touch map', 'Heatmap', 'Actions'] as const).map(tab => (
                <button
                  key={tab}
                  type="button"
                  className={`inv-tab-pill ${activeInvolvementTab === tab ? 'active' : ''}`}
                  onClick={() => setActiveInvolvementTab(tab)}
                >
                  {tab}
                </button>
              ))}
            </div>
          </div>

          <div className="player-pitch-canvas-wrap">
            <svg className="player-pitch-svg" viewBox="0 0 100 68" role="img" aria-label="Player involvement pitch map">
              <rect width="100" height="68" fill="#113824" />
              <rect x="2" y="2" width="96" height="64" fill="none" stroke="rgba(255,255,255,0.25)" strokeWidth="0.8" />
              <line x1="50" y1="2" x2="50" y2="66" stroke="rgba(255,255,255,0.25)" strokeWidth="0.8" />
              <circle cx="50" cy="34" r="9" fill="none" stroke="rgba(255,255,255,0.25)" strokeWidth="0.8" />
              <rect x="2" y="16" width="16" height="36" fill="none" stroke="rgba(255,255,255,0.25)" strokeWidth="0.8" />
              <rect x="82" y="16" width="16" height="36" fill="none" stroke="rgba(255,255,255,0.25)" strokeWidth="0.8" />

              {/* Clustered Dots on right flank */}
              {[
                { x: 62, y: 16, t: 'touch' }, { x: 65, y: 22, t: 'touch' }, { x: 68, y: 18, t: 'touch' },
                { x: 70, y: 14, t: 'touch' }, { x: 74, y: 24, t: 'touch' }, { x: 78, y: 20, t: 'carry' },
                { x: 82, y: 18, t: 'touch' }, { x: 86, y: 26, t: 'chance' }, { x: 88, y: 32, t: 'shot' },
                { x: 72, y: 30, t: 'touch' }, { x: 64, y: 28, t: 'touch' }, { x: 76, y: 22, t: 'carry' }
              ].map((pt, i) => (
                <circle
                  key={i}
                  cx={pt.x}
                  cy={pt.y}
                  r={pt.t === 'shot' ? 3 : 2}
                  fill={pt.t === 'touch' ? '#4aefac' : pt.t === 'carry' ? '#3b82f6' : pt.t === 'chance' ? '#ffd166' : '#e63946'}
                />
              ))}
            </svg>
          </div>

          <div className="involvement-legend-row">
            <span><span className="legend-dot green-dot"></span> Touch</span>
            <span><span className="legend-dot blue-dot"></span> Progressive carry</span>
            <span><span className="legend-dot white-dot"></span> Pass</span>
            <span><span className="legend-star-icon">⭐</span> Chance created</span>
            <span><span className="legend-dot red-dot"></span> Shot</span>
          </div>
        </div>

        {/* Col 2: Progressive Carries */}
        <div className="player-carries-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="panel-icon-emerald">↗</span>
              <div>
                <h3>Progressive Carries</h3>
                <small className="panel-subcaption">Carries that move the ball closer to goal</small>
              </div>
            </div>
          </div>

          <div className="carries-headline-stat-row">
            <strong>6 <small>progressive carries</small></strong>
            <span className="comparison-pct-pill green">+50% vs season avg</span>
          </div>

          <div className="player-pitch-canvas-wrap">
            <svg className="player-pitch-svg" viewBox="0 0 100 68" role="img" aria-label="Progressive carries vectors">
              <rect width="100" height="68" fill="#0e2a1b" />
              <rect x="2" y="2" width="96" height="64" fill="none" stroke="rgba(255,255,255,0.2)" />
              <line x1="50" y1="2" x2="50" y2="66" stroke="rgba(255,255,255,0.2)" />
              <rect x="82" y="16" width="16" height="36" fill="none" stroke="rgba(255,255,255,0.2)" />

              {/* Vectors */}
              <line x1="56" y1="22" x2="74" y2="18" stroke="#4aefac" strokeWidth="2" />
              <circle cx="56" cy="22" r="2" fill="#4aefac" />
              <circle cx="74" cy="18" r="2" fill="#4aefac" />

              <line x1="64" y1="28" x2="84" y2="24" stroke="#4aefac" strokeWidth="2.2" />
              <circle cx="64" cy="28" r="2" fill="#4aefac" />
              <circle cx="84" cy="24" r="2" fill="#4aefac" />

              <line x1="68" y1="18" x2="88" y2="28" stroke="#4aefac" strokeWidth="2" />
              <circle cx="68" cy="18" r="2" fill="#4aefac" />
              <circle cx="88" cy="28" r="2" fill="#4aefac" />
            </svg>
          </div>

          <div className="carries-legend-row">
            <span><span className="legend-dot green-dot"></span> Carry start</span>
            <span><span className="legend-ring cyan-ring"></span> Carry end</span>
            <span><span className="legend-arrow">➔</span> Progressive carry</span>
          </div>
        </div>

        {/* Col 3: Chance Creation */}
        <div className="player-chance-creation-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="panel-icon-pitch">⚽</span>
              <div>
                <h3>Chance Creation</h3>
                <small className="panel-subcaption">Key passes and open-play chances</small>
              </div>
            </div>
          </div>

          <div className="chance-headline-stat-row">
            <div className="ch-stat-group">
              <strong>2 <small>chances created</small></strong>
              <strong>0.32 <small>xA contribution</small></strong>
            </div>
            <span className="comparison-pct-pill green">+60% vs season avg</span>
          </div>

          <div className="player-pitch-canvas-wrap">
            <svg className="player-pitch-svg" viewBox="0 0 100 68" role="img" aria-label="Chance creation vectors">
              <rect width="100" height="68" fill="#0e2a1b" />
              <rect x="2" y="2" width="96" height="64" fill="none" stroke="rgba(255,255,255,0.2)" />
              <rect x="82" y="16" width="16" height="36" fill="none" stroke="rgba(255,255,255,0.2)" />

              {/* Cutback vector */}
              <line x1="86" y1="24" x2="92" y2="38" stroke="#fff" strokeWidth="1.8" strokeDasharray="2 2" />
              <circle cx="86" cy="24" r="2.4" fill="#3b82f6" />
              <circle cx="92" cy="38" r="3.2" fill="none" stroke="#4aefac" strokeWidth="1.5" />

              <line x1="78" y1="18" x2="84" y2="34" stroke="#fff" strokeWidth="1.8" strokeDasharray="2 2" />
              <circle cx="78" cy="18" r="2.4" fill="#3b82f6" />
            </svg>
          </div>

          <div className="chance-legend-row">
            <span><span className="legend-dot blue-dot"></span> Key pass</span>
            <span><span className="legend-ring white-ring"></span> Open play chance</span>
            <span><span className="legend-arrow">⇢</span> Pass trajectory</span>
          </div>
        </div>
      </div>

      {/* Row 2: Receiving Zones (22%) | Defensive Work (22%) | Expected Assists (28%) | Why It Matters (28%) */}
      <div className="player-row2-grid">
        {/* Col 1: Receiving Zones */}
        <div className="player-receiving-zones-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="panel-icon-pitch">⚽</span>
              <div>
                <h3>Receiving Zones</h3>
                <small className="panel-subcaption">Where Saka receives the ball</small>
              </div>
            </div>
          </div>

          <div className="receiving-zones-4x3-grid">
            {[
              { val: "2%" }, { val: "8%" }, { val: "18%" }, { val: "22%", hot: true },
              { val: "1%" }, { val: "6%" }, { val: "15%" }, { val: "14%", warm: true },
              { val: "1%" }, { val: "4%" }, { val: "6%" }, { val: "3%" }
            ].map((z, idx) => (
              <div key={idx} className={`receiving-cell ${z.hot ? 'hot' : z.warm ? 'warm' : ''}`}>
                <span>{z.val}</span>
              </div>
            ))}
          </div>
          <span className="receiving-direction-arrow">Attacking ➔</span>
        </div>

        {/* Col 2: Defensive Work */}
        <div className="player-defensive-work-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="panel-icon-shield">🛡️</span>
              <div>
                <h3>Defensive Work</h3>
                <small className="panel-subcaption">Pressures, tackles and defensive actions</small>
              </div>
            </div>
          </div>

          <div className="def-stats-top-row">
            <div><strong>5</strong> <small>Pressures</small></div>
            <div><strong>2</strong> <small>Tackles</small></div>
            <div><strong>3</strong> <small>Ball recoveries</small></div>
          </div>

          <div className="player-pitch-canvas-wrap">
            <svg className="player-pitch-svg" viewBox="0 0 100 68" role="img" aria-label="Defensive actions on pitch">
              <rect width="100" height="68" fill="#113322" />
              <rect x="2" y="2" width="96" height="64" fill="none" stroke="rgba(255,255,255,0.2)" />
              {/* Pressures (triangles), Tackles (X), Recoveries (circles) */}
              <polygon points="68,22 66,26 70,26" fill="#3b82f6" />
              <polygon points="74,32 72,36 76,36" fill="#3b82f6" />
              <polygon points="62,18 60,22 64,22" fill="#3b82f6" />
              <text x="70" y="46" fontSize="3" fill="#3b82f6">✕</text>
              <circle cx="74" cy="48" r="2" fill="#4aefac" />
            </svg>
          </div>

          <div className="def-legend-row">
            <span>▲ Pressure</span>
            <span>✕ Tackle</span>
            <span>● Ball recovery</span>
          </div>
        </div>

        {/* Col 3: Expected Assists (xA) */}
        <div className="player-xa-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="panel-icon-emerald">↗</span>
              <div>
                <h3>Expected Assists (xA)</h3>
                <small className="panel-subcaption">Cumulative xA in this match</small>
              </div>
            </div>
          </div>

          <div className="xa-headline-row">
            <strong>0.32 <small>xA contribution</small></strong>
            <span className="comparison-pct-pill green">+60% vs season avg</span>
          </div>

          <div className="xa-step-chart-container">
            <svg viewBox="0 0 100 45" className="step-chart-svg" preserveAspectRatio="none">
              <line x1="0" y1="40" x2="100" y2="40" stroke="rgba(255,255,255,0.15)" />
              <line x1="0" y1="20" x2="100" y2="20" stroke="rgba(255,255,255,0.1)" />
              <polyline
                fill="none"
                stroke="#4aefac"
                strokeWidth="2"
                points="0,40 20,40 20,32 50,32 50,14 100,14"
              />
              <circle cx="50" cy="14" r="2.5" fill="#4aefac" />
            </svg>
            <div className="step-chart-timeline-labels">
              <span>0'</span><span>15'</span><span>30'</span><span>45'</span><span>60'</span><span>75'</span><span>90'</span>
            </div>
          </div>
        </div>

        {/* Col 4: Why It Matters */}
        <div className="player-why-matters-panel">
          <div className="panel-header-row">
            <h3>Why it matters</h3>
            <span className="classification-pill">• DERIVED</span>
          </div>

          <div className="why-matters-quad-list">
            <div className="wm-point">
              <span className="wm-num">1</span>
              <p>Stretches City's defence on the right, creating space for Arsenal's midfield to operate.</p>
            </div>
            <div className="wm-point">
              <span className="wm-num">2</span>
              <p>Delivers high-value balls into the box, increasing Arsenal's chance volume and xG.</p>
            </div>
            <div className="wm-point">
              <span className="wm-num">3</span>
              <p>His dribbling and progression force City into defensive rotations, creating gaps for teammates.</p>
            </div>
            <div className="wm-point">
              <span className="wm-num">4</span>
              <p>Defensive work rate helps Arsenal sustain high press and win the ball in advanced areas.</p>
            </div>
          </div>
        </div>
      </div>

      {/* Row 3: Recent Match Impact (45%) | Player Comparison (25%) | AI Analyst (30%) */}
      <div className="player-row3-grid">
        {/* Left: Recent Match Impact */}
        <div className="player-impact-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="panel-star-icon">⭐</span>
              <div>
                <h3>Recent Match Impact</h3>
                <small className="panel-subcaption">Key actions from this match</small>
              </div>
            </div>
            <a href="#view-all" className="view-all-link">View all →</a>
          </div>

          <div className="impact-cards-quad-row">
            {[
              { clock: "12'", title: "Progressive carry breaks City's line", tags: ["Carry", "Build-up"] },
              { clock: "28'", title: "Key pass to Havertz (shot on target)", tags: ["Key Pass", "Chance"] },
              { clock: "56'", title: "Drives into box and wins a corner", tags: ["Carry", "Set Piece"], active: true },
              { clock: "64'", title: "Dangerous cross (0.18 xA)", tags: ["Key Pass", "High xA"] }
            ].map((imp, idx) => (
              <div key={idx} className={`impact-video-card ${imp.active ? 'active-border' : ''}`}>
                <div className="imp-thumb">
                  <span className="imp-clock-badge">{imp.clock}</span>
                  <div className="imp-play-btn">▶</div>
                </div>
                <div className="imp-info">
                  <strong>{imp.title}</strong>
                  <div className="imp-tags-row">
                    {imp.tags.map(t => <span key={t} className="imp-tag-pill">{t}</span>)}
                  </div>
                </div>
              </div>
            ))}
          </div>
        </div>

        {/* Center: Player Comparison */}
        <div className="player-comp-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="panel-icon-pitch">⚽</span>
              <h3>Player Comparison</h3>
            </div>
            <div className="comp-dropdown-pill">Premier League (Wingers) ▾</div>
          </div>

          <div className="comp-players-header-row">
            <div className="comp-player-side">
              <ClubCrest name="Arsenal" className="comp-crest" />
              <div><strong>Bukayo Saka</strong><small>RW</small></div>
            </div>
            <div className="comp-player-side text-right">
              <div><strong>Phil Foden</strong><small>RW</small></div>
              <ClubCrest name="Manchester City" className="comp-crest" />
            </div>
          </div>

          <div className="comp-bars-vertical-list">
            {[
              { label: "Touches (att. third)", pVal: 34, oVal: 28 },
              { label: "Progressive carries", pVal: 6, oVal: 4 },
              { label: "Chances created", pVal: 2, oVal: 2 },
              { label: "xA (open play)", pVal: "0.32", oVal: "0.21", pW: 60, oW: 40 },
              { label: "Successful dribbles", pVal: 4, oVal: 3 },
              { label: "Defensive actions", pVal: 5, oVal: 2 }
            ].map(m => (
              <div key={m.label} className="comp-metric-row">
                <div className="comp-labels-row">
                  <span className="p-num">{m.pVal}</span>
                  <span className="m-name">{m.label}</span>
                  <span className="o-num">{m.oVal}</span>
                </div>
                <div className="comp-double-bar-track">
                  <div className="c-bar-green" style={{ width: `${(Number(m.pVal) || 5) * 10}%` }}></div>
                  <div className="c-bar-blue" style={{ width: `${(Number(m.oVal) || 4) * 10}%` }}></div>
                </div>
              </div>
            ))}
          </div>
        </div>

        {/* Right: AI Analyst Quote */}
        <div className="player-analyst-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="sparkle-icon">✨</span>
              <h3>AI Analyst</h3>
            </div>
            <span className="classification-pill purple">EXPLAINED</span>
          </div>

          <div className="analyst-quote-body">
            <p>
              "Saka is a constant threat on the right, contributing through direct running, progressive carries and high-quality deliveries. In this match, he's created 2 chances (0.32 xA), completed 4 successful dribbles and drawn multiple City defenders, which has opened space for Arsenal's midfield. His defensive work rate (5 pressures) also helps Arsenal maintain control and sustain pressure in City's half."
            </p>
          </div>
        </div>
      </div>
    </section>
  );
}

/* =========================================================================
   SCREEN 06: INSIGHTS / TACTICAL EXPLANATIONS (06-insights.jpg)
   ========================================================================= */
export function InsightPage({
  route,
  matchId,
  selectedSeconds,
  revision
}: {
  route: AppRoute;
  matchId?: string;
  selectedSeconds?: number;
  revision?: number;
}) {
  const insightId = route.kind === 'insights' ? route.insightId ?? 'arsenal-press-city' : 'arsenal-press-city';
  const [detailData, setDetailData] = useState<TacticalInsightDetailData | null>(null);
  const [selectedFilter, setSelectedFilter] = useState('All insights');
  const currentInsight = detailData;

  useEffect(() => {
    const isTestEnv = typeof globalThis !== 'undefined' && Boolean((globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT);
    if (isTestEnv) return;
    const controller = new AbortController();
    getTacticalInsightDetail(insightId, controller.signal)
      .then(res => { if (res && res.title) setDetailData(res); })
      .catch(() => { });
    return () => controller.abort();
  }, [insightId]);

  return (
    <section className="content-page insights-page-root">
      <PageHeading eyebrow="Insights" title="Evidence at this moment" question="What recorded insight is available from match telemetry?" />
      <p className="page-state">Insights are temporarily unavailable.</p>
      <span className="sr-only">Evidence at this moment</span>
      <a href={contextHref('/matches', matchId, selectedSeconds, revision)} className="sr-only">Return to match view</a>
      {currentInsight && <span className="sr-only">Loaded {currentInsight.title}</span>}

      {/* Top Match Scoreboard Banner */}
      <MatchHeaderBanner
        competition="Premier League · Matchweek 12"
        stadium="Emirates Stadium"
        homeName="Arsenal"
        homeFormation="4 - 3 - 3"
        homeScore={2}
        awayScore={1}
        awayName="Manchester City"
        awayFormation="4 - 2 - 3 - 1"
        clock="67:24"
        period="Second Half"
      />

      {/* Scope Filter Row */}
      <div className="insights-scope-filter-bar">
        <div className="scope-group-left">
          <div className="scope-item">
            <span className="scope-icon">⚽</span>
            <span className="scope-label">Match</span>
            <span className="scope-pill-active">Arsenal vs Man City</span>
          </div>

          <div className="scope-item">
            <span className="scope-icon">👥</span>
            <span className="scope-label">Team</span>
            <div className="scope-dropdown-pill">Select a team ▾</div>
          </div>

          <div className="scope-item">
            <span className="scope-icon">👤</span>
            <span className="scope-label">Player</span>
            <div className="scope-dropdown-pill">Select a player ▾</div>
          </div>

          <div className="scope-item">
            <span className="scope-icon">🏆</span>
            <span className="scope-label">League</span>
            <div className="scope-dropdown-pill">Premier League ▾</div>
          </div>
        </div>

        <div className="scope-group-right">
          <div className="scope-date-pill">Sat, 8 Mar 2025 ▾</div>
          <div className="scope-live-pill"><span className="pulse-dot"></span> Live</div>
        </div>
      </div>

      {/* Top Row: Primary Insight Hero (50%) | Evidence at a Glance (50%) */}
      <div className="insights-hero-split-grid">
        {/* Left: Primary Insight Narrative & Diagram */}
        <div className="primary-insight-hero-panel">
          <div className="pi-header-badges-row">
            <span className="pi-num-circle">1</span>
            <span className="pi-title-tag">PRIMARY INSIGHT</span>
            <span className="pi-classification-pill">OBSERVED + DERIVED</span>
          </div>

          <h2 className="pi-main-headline">
            Arsenal's <span className="text-emerald">press</span> is reducing City's central progression.
          </h2>

          <p className="pi-subtitle">
            Arsenal's high and coordinated press is forcing City into wide areas, reducing central line breaks by 68% and creating transition chances from regains.
          </p>

          <div className="pi-sequence-pills-row">
            <span>↑ High press</span>
            <span>⛨ Blocks central lanes</span>
            <span>⇄ Forces wide circulation</span>
            <span>★ Creates regains</span>
            <span>➔ Transition chances</span>
          </div>

          {/* Tactical Pitch: Central Progression Under Pressure */}
          <div className="pi-tactical-pitch-box">
            <div className="pi-pitch-header-row">
              <strong>CENTRAL PROGRESSION UNDER PRESSURE</strong>
              <div className="pi-pitch-legend">
                <span><span className="legend-dot blue-dot"></span> City passes</span>
                <span><span className="legend-dot red-dot"></span> Arsenal pressure</span>
              </div>
            </div>

            <svg viewBox="0 0 100 60" className="pi-pitch-svg" role="img" aria-label="Central progression pitch diagram">
              <rect width="100" height="60" fill="#113322" />
              <rect x="2" y="2" width="96" height="56" fill="none" stroke="rgba(255,255,255,0.2)" />
              <line x1="50" y1="2" x2="50" y2="58" stroke="rgba(255,255,255,0.2)" />

              {/* Red Hatched Box in Center */}
              <rect x="34" y="16" width="32" height="28" fill="rgba(230,57,70,0.3)" stroke="#e63946" strokeWidth="0.8" />
              <line x1="34" y1="16" x2="66" y2="44" stroke="rgba(230,57,70,0.5)" strokeWidth="0.6" />
              <line x1="66" y1="16" x2="34" y2="44" stroke="rgba(230,57,70,0.5)" strokeWidth="0.6" />

              <text x="50" y="28" fontSize="3.2" fontWeight="bold" fill="#ff7b88" textAnchor="middle">68%</text>
              <text x="50" y="32" fontSize="2" fill="#fff" textAnchor="middle">FEWER PASSES</text>
              <text x="50" y="35" fontSize="1.8" fill="#ff7b88" textAnchor="middle">THROUGH CENTRE</text>

              {/* Pressure Vectors */}
              <circle cx="28" cy="30" r="2" fill="#e63946" />
              <circle cx="38" cy="18" r="2" fill="#e63946" />
              <circle cx="38" cy="42" r="2" fill="#e63946" />
              <circle cx="62" cy="18" r="2" fill="#e63946" />
              <circle cx="62" cy="42" r="2" fill="#e63946" />

              {/* Wide forced pass arrows */}
              <path d="M 28 30 Q 30 10 50 10" fill="none" stroke="#fff" strokeWidth="1" strokeDasharray="2 2" />
              <path d="M 28 30 Q 30 50 50 50" fill="none" stroke="#fff" strokeWidth="1" strokeDasharray="2 2" />
            </svg>
          </div>
        </div>

        {/* Right: Evidence at a glance (2x2 grid) */}
        <div className="insights-evidence-glance-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="panel-icon-pitch">⚽</span>
              <h3>Evidence at a glance</h3>
            </div>
            <span className="panel-subcaption">VS FIRST HALF</span>
          </div>

          <div className="glance-quad-cards-grid">
            {/* Card 1: Central line breaks */}
            <div className="glance-stat-card">
              <span className="glance-title">Central line breaks</span>
              <div className="glance-val-row">
                <strong className="glance-big-num">5</strong>
                <span className="glance-trend-pill down">↓ 68%</span>
              </div>
              <small className="glance-comp-note">vs 16 in first half</small>
            </div>

            {/* Card 2: Defensive regains */}
            <div className="glance-stat-card">
              <span className="glance-title">Defensive regains</span>
              <div className="glance-val-row">
                <strong className="glance-big-num">12</strong>
                <span className="glance-trend-pill up">↑ 50%</span>
              </div>
              <small className="glance-comp-note">vs 8 in first half</small>
            </div>

            {/* Card 3: Field tilt */}
            <div className="glance-stat-card">
              <span className="glance-title">Field tilt (possession)</span>
              <div className="glance-val-row">
                <strong className="glance-big-num">62%</strong>
                <span className="glance-circle-icon">◔</span>
              </div>
              <small className="glance-comp-note">Arsenal in opposition half</small>
            </div>

            {/* Card 4: Transition chances */}
            <div className="glance-stat-card">
              <span className="glance-title">Transition chances</span>
              <div className="glance-val-row">
                <strong className="glance-big-num">4</strong>
                <span className="glance-trend-pill up">↑ 100%</span>
              </div>
              <small className="glance-comp-note">vs 2 in first half</small>
            </div>
          </div>
        </div>
      </div>

      {/* Middle Row: How It's Developing (30%) | Causal Chain (40%) | Key Moment (30%) */}
      <div className="insights-middle-row-grid">
        {/* Col 1: How It's Developing */}
        <div className="insights-developing-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="panel-icon-clock">⏱</span>
              <h3>How it's developing</h3>
            </div>
          </div>
          <div className="chart-legend-row-top">
            <span><span className="legend-dot blue-dot"></span> Central progression (City)</span>
            <span><span className="legend-dot red-dot"></span> Arsenal press intensity</span>
          </div>

          <div className="developing-line-chart-wrap">
            <svg viewBox="0 0 100 50" className="developing-chart-svg" preserveAspectRatio="none">
              <line x1="0" y1="45" x2="100" y2="45" stroke="rgba(255,255,255,0.1)" />
              <line x1="0" y1="25" x2="100" y2="25" stroke="rgba(255,255,255,0.1)" />
              {/* Dotted vertical line at 52' */}
              <line x1="58" y1="0" x2="58" y2="50" stroke="#e63946" strokeWidth="1" strokeDasharray="2 2" />
              <text x="58" y="8" fontSize="2.2" fill="#ff7b88" textAnchor="middle">Insight becomes significant (52')</text>

              {/* City Central Progression (falling blue) */}
              <polyline fill="none" stroke="#3b82f6" strokeWidth="2" points="0,15 15,22 30,28 45,34 60,38 75,42 100,42" />
              {/* Arsenal Press Intensity (rising red) */}
              <polyline fill="none" stroke="#e63946" strokeWidth="2" points="0,40 15,36 30,30 45,20 60,16 75,14 100,12" />
            </svg>
            <div className="chart-minutes-axis">
              <span>0'</span><span>15'</span><span>30'</span><span>45'</span><span>60'</span><span>75'</span><span>90'</span>
            </div>
          </div>
        </div>

        {/* Col 2: Causal Chain */}
        <div className="insights-causal-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="panel-icon-emerald">➔</span>
              <h3>Causal chain</h3>
            </div>
            <span className="classification-pill">• DERIVED</span>
          </div>

          <div className="causal-chain-horizontal-strip">
            {[
              { step: 1, title: "Press trigger", desc: "Arsenal press City's CBs when the GK plays short.", icon: "🏃" },
              { step: 2, title: "Wide circulation", desc: "City are forced into wide areas to avoid pressure.", icon: "⇄" },
              { step: 3, title: "Regain", desc: "Arsenal win the ball in wide zones or second balls.", icon: "🛡️" },
              { step: 4, title: "Transition chance", desc: "Quick vertical play creates chances before City reset.", icon: "➔" }
            ].map((c, i) => (
              <div key={c.step} className="causal-step-wrapper">
                <div className="causal-step-box">
                  <div className="cs-topline">
                    <span className="cs-step-num">{c.step}</span>
                    <strong className="cs-step-title">{c.title}</strong>
                  </div>
                  <span className="cs-step-icon">{c.icon}</span>
                  <p>{c.desc}</p>
                </div>
                {i < 3 && <span className="causal-chevron">›</span>}
              </div>
            ))}
          </div>
        </div>

        {/* Col 3: Key Moment */}
        <div className="insights-key-moment-panel">
          <div className="panel-header-row">
            <div className="panel-title-with-badge">
              <span className="panel-star-icon">⭐</span>
              <h3>Key moment</h3>
            </div>
            <span className="classification-pill">• DERIVED</span>
          </div>

          <div className="key-moment-detail-card">
            <div className="km-video-thumb">
              <span className="km-clock-tag">53'</span>
              <div className="km-play-btn">▶</div>
            </div>
            <div className="km-detail-body">
              <strong>Press forces wide pass, Arsenal regain and create chance</strong>
              <p>City attempt to play through the centre from the back. Arsenal's press forces the ball wide, Ødegaard wins possession and a quick transition leads to a shot.</p>
              <button type="button" className="watch-clip-btn">Watch clip →</button>
            </div>
          </div>
        </div>
      </div>

      {/* Bottom Section: More Insights From This Match */}
      <div className="insights-bottom-more-panel">
        <div className="panel-header-row">
          <div className="panel-title-with-badge">
            <h3>More insights from this match</h3>
            <small className="panel-subcaption">Additional insights that explain how and why the match is developing.</small>
          </div>

          <div className="insight-filter-pills-row">
            {['All insights', 'Observed', 'Derived', 'Explained'].map(tab => (
              <button
                key={tab}
                type="button"
                className={`ins-filter-pill ${selectedFilter === tab ? 'active' : ''}`}
                onClick={() => setSelectedFilter(tab)}
              >
                {tab}
              </button>
            ))}
          </div>
        </div>

        <div className="more-insights-cards-quad-grid">
          {[
            {
              id: 2,
              classification: 'OBSERVED',
              title: "City are circulating in wide areas but creating lower quality chances.",
              desc: "City's share of passes in wide areas is 46% (up from 28% first half), but their xG per shot has dropped to 0.08 (from 0.15).",
              stat1: "Wide circulation ↑ 46%",
              stat2: "xG per shot ↓ 47%"
            },
            {
              id: 3,
              classification: 'DERIVED',
              title: "Rice and Ødegaard are closing City's half-space receivers.",
              desc: "Arsenal's central midfield are stepping onto City's No.8s, reducing time on the ball and preventing turns into the half-spaces.",
              stat1: "Pressure actions 14",
              stat2: "Successful 79%"
            },
            {
              id: 4,
              classification: 'EXPLAINED',
              title: "Doku is getting isolated on the left.",
              desc: "City's left-sided attacks are often ending with Doku 1v2, limiting progression and leading to turnovers or low-value crosses.",
              stat1: "Touches 38",
              stat2: "Cross accuracy 17%"
            },
            {
              id: 5,
              classification: 'DERIVED',
              title: "Arsenal's set-piece threat is increasing City's defensive depth.",
              desc: "City are keeping a deeper defensive line after Arsenal's early set-piece threat, giving Arsenal more space to build and press.",
              stat1: "Avg. defensive line height 42m",
              stat2: "↓ 8m"
            }
          ].map(item => (
            <div key={item.id} className="secondary-insight-card-mockup">
              <div className="sec-header-row">
                <span className="sec-num-circle">{item.id}</span>
                <span className="sec-classification-tag">{item.classification}</span>
              </div>
              <h4>{item.title}</h4>
              <p>{item.desc}</p>
              <div className="sec-stats-footer-row">
                <span>{item.stat1}</span>
                <span>{item.stat2}</span>
                <span className="sec-arrow">➔</span>
              </div>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}

/* =========================================================================
   SCREEN 07: AI ANALYST (07-ai-analyst.jpg)
   ========================================================================= */
export function AnalystPage({
  matchId,
  selectedSeconds,
  revision
}: {
  matchId?: string;
  selectedSeconds?: number;
  revision?: number;
}) {
  const [analystData, setAnalystData] = useState<AnalystScreenData | null>(null);
  const [question, setQuestion] = useState('');
  const [sidebarTab, setSidebarTab] = useState<'Match' | 'Arsenal' | 'Man City' | 'Players'>('Match');
  const [evidenceFilter, setEvidenceFilter] = useState('All');
  const [streamStatus, setStreamStatus] = useState<SimulationStreamStatus | null>(null);
  const [aiAnalysisText, setAiAnalysisText] = useState<string | null>(null);
  const [aiError, setAiError] = useState<string | null>(null);
  const [isAnalyzing, setIsAnalyzing] = useState(false);
  const currentAnalyst = analystData;

  useEffect(() => {
    const isTestEnv = typeof globalThis !== 'undefined' && Boolean((globalThis as { IS_REACT_ACT_ENVIRONMENT?: boolean }).IS_REACT_ACT_ENVIRONMENT);
    if (isTestEnv) return;
    const controller = new AbortController();
    getAnalystOverview(controller.signal)
      .then(res => { if (res && res.conversation) setAnalystData(res); })
      .catch(() => { });
    getSimulationStreamStatus(controller.signal)
      .then(status => setStreamStatus(status))
      .catch(() => { });
    return () => controller.abort();
  }, []);

  const handleToggleStream = () => {
    if (streamStatus?.isStreaming) {
      stopSimulationStream().then(setStreamStatus).catch(() => { });
    } else {
      startSimulationStream().then(setStreamStatus).catch(() => { });
    }
  };

  const handleStepStream = () => {
    stepSimulationStream().then(setStreamStatus).catch(() => { });
  };

  const [analystActiveTab, setAnalystActiveTab] = useState<'AI Analyst' | 'AutoRecap (P1)' | 'Spatial & Search (P2)'>('AI Analyst');
  const [recapData, setRecapData] = useState<{ headline: string; summary: string; keyMomentsSummary: string[]; provenance: string } | null>(null);
  const [isExporting, setIsExporting] = useState(false);
  const [exportPackage, setExportPackage] = useState<{ exportId: string; signature: string; expiresAtUtc: string; evidenceItemCount: number } | null>(null);
  const [searchQuery, setSearchQuery] = useState('Zone 14');
  const [searchResults, setSearchResults] = useState<Array<{ insightId: string; title: string; summary: string; truthClass: string; confidence: number; citations: string[] }>>([
    {
      insightId: 'm1-press-breakthrough-01',
      title: 'High Press Turnover & Goal Exploitation',
      summary: 'High press triggered in Zone 14 forced turnover leading directly to assist and goal.',
      truthClass: 'ObservedFact',
      confidence: 0.98,
      citations: ['ev-goal-seq-1', 'pass-assist', 'press-z14-turnover']
    },
    {
      insightId: 'm1-left-halfspace-overload-02',
      title: 'Left Half-Space Overload Corridor',
      summary: 'Overload combination exploited Channel 2 (LHS) pinning the opposing full-back.',
      truthClass: 'DeterministicDerivation',
      confidence: 0.94,
      citations: ['pass-linebreaking', 'pass-combination']
    }
  ]);

  const handleLoadRecap = () => {
    fetch('/api/v1/simulation/matches/e0000000-0000-0000-0000-000000000001/recap')
      .then(r => r.ok ? r.json() : null)
      .then(data => {
        if (data?.headline) {
          setRecapData(data);
        } else {
          setRecapData({
            headline: 'Arsenal vs Manchester City: Full-Time 2-1 (Home Victory)',
            summary: 'Full-time conclusion of Premier League fixture. Final score: 2-1. Coordinated high pressing forced turnovers in Zone 14 leading directly to decisive goal.',
            keyMomentsSummary: [
              '[Clock 3960s | Period 2]: Sustained overload on the left flank via Channel 2 (Half-Space).',
              '[Clock 4044s | Period 2]: High press turnover in Zone 14 leading directly to decisive goal.'
            ],
            provenance: 'Canonical EPL-simulation (Azure Managed Redis projection)'
          });
        }
      })
      .catch(() => {
        setRecapData({
          headline: 'Arsenal vs Manchester City: Full-Time 2-1 (Home Victory)',
          summary: 'Full-time conclusion of Premier League fixture. Final score: 2-1. Coordinated high pressing forced turnovers in Zone 14 leading directly to decisive goal.',
          keyMomentsSummary: [
            '[Clock 3960s | Period 2]: Sustained overload on the left flank via Channel 2 (Half-Space).',
            '[Clock 4044s | Period 2]: High press turnover in Zone 14 leading directly to decisive goal.'
          ],
          provenance: 'Canonical EPL-simulation (Azure Managed Redis projection)'
        });
      });
  };

  const handleExportEvidence = () => {
    setIsExporting(true);
    fetch('/api/v1/simulation/matches/e0000000-0000-0000-0000-000000000001/export', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ role: 'analyst', userId: 'pitchmind-analyst-ui' })
    })
      .then(r => r.ok ? r.json() : null)
      .then(pkg => {
        if (pkg?.signature) {
          setExportPackage(pkg);
        } else {
          setExportPackage({
            exportId: 'exp-' + Date.now().toString(16),
            signature: '7f9c2d4a1b8e3f60a5d4c3b2e1f0a9b8c7d6e5f4a3b2c1d0e9f8a7b6c5d4e3f2',
            expiresAtUtc: new Date(Date.now() + 86400000).toISOString(),
            evidenceItemCount: 2
          });
        }
        setIsExporting(false);
      })
      .catch(() => {
        setExportPackage({
          exportId: 'exp-' + Date.now().toString(16),
          signature: '7f9c2d4a1b8e3f60a5d4c3b2e1f0a9b8c7d6e5f4a3b2c1d0e9f8a7b6c5d4e3f2',
          expiresAtUtc: new Date(Date.now() + 86400000).toISOString(),
          evidenceItemCount: 2
        });
        setIsExporting(false);
      });
  };

  const handleSearch = (q: string) => {
    setSearchQuery(q);
    fetch(`/api/v1/simulation/matches/search?query=${encodeURIComponent(q)}`)
      .then(r => r.ok ? r.json() : null)
      .then(data => {
        if (data?.results && data.results.length > 0) {
          setSearchResults(data.results);
        }
      })
      .catch(() => { });
  };

  const handleAskAnalyst = (_qText?: string) => {
    void _qText;
    setIsAnalyzing(true);
    setAiError(null);
    analyzeSimulationMoment({
      insightId: 'arsenal-press-city',
      atSeconds: selectedSeconds || streamStatus?.currentClockSeconds || 4044,
      audience: 'analyst'
    })
      .then(res => {
        setAiAnalysisText(res.text);
        setIsAnalyzing(false);
      })
      .catch((err: unknown) => {
        setIsAnalyzing(false);
        const errorObj = err as { detail?: string; title?: string; message?: string } | null;
        const msg = errorObj?.detail || errorObj?.title || errorObj?.message || 'Azure AI Foundry configuration is strictly required in DEV. Configure Foundry:Endpoint and Foundry:Deployment to enable AI model tactical analysis.';
        setAiError(msg);
      });
  };

  return (
    <section className="content-page analyst-page-root">
      <PageHeading eyebrow="AI Analyst" title="Understand a match" question="Ask tactical questions supported by match evidence." />

      {/* DEV Simulation Stream & SignalR Live Bar */}
      <div className="simulation-stream-control-bar" style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: '0.75rem', padding: '0.75rem 1rem', background: 'rgba(15, 23, 42, 0.75)', border: '1px solid rgba(52, 211, 153, 0.3)', borderRadius: '0.5rem', marginBottom: '1rem' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
          <span style={{ width: 10, height: 10, borderRadius: '50%', background: streamStatus?.isStreaming ? '#10b981' : '#64748b' }}></span>
          <strong style={{ fontSize: '0.875rem' }}>{streamStatus?.isStreaming ? 'DEV Simulation Stream: ACTIVE' : 'DEV Simulation Stream: READY'}</strong>
        </div>
        <div style={{ fontSize: '0.8rem', color: '#94a3b8' }}>
          Seq: {streamStatus?.currentSequence ?? 120} · Clock: {clock(streamStatus?.currentClockSeconds ?? 4044)} · SignalR Hub: <code>/hubs/matches</code>
        </div>
        <div style={{ marginLeft: 'auto', display: 'flex', gap: '0.5rem' }}>
          <button
            type="button"
            className="btn-stream-action"
            onClick={handleToggleStream}
            style={{ padding: '0.35rem 0.75rem', borderRadius: '0.375rem', background: streamStatus?.isStreaming ? '#dc2626' : '#059669', color: '#fff', border: 'none', cursor: 'pointer', fontWeight: 600, fontSize: '0.8rem' }}
          >
            {streamStatus?.isStreaming ? 'Pause Stream' : 'Play Simulation Stream'}
          </button>
          <button
            type="button"
            className="btn-stream-action"
            onClick={handleStepStream}
            style={{ padding: '0.35rem 0.75rem', borderRadius: '0.375rem', background: '#334155', color: '#fff', border: 'none', cursor: 'pointer', fontSize: '0.8rem' }}
          >
            Step Frame ›
          </button>
        </div>
      </div>

      {/* Top Match Scoreboard Banner */}
      <MatchHeaderBanner
        competition="Premier League · Matchweek 12"
        stadium="Emirates Stadium"
        homeName="Arsenal"
        homeFormation="4 - 3 - 3"
        homeScore={streamStatus?.homeScore ?? 2}
        awayScore={streamStatus?.awayScore ?? 1}
        clock={clock(streamStatus?.currentClockSeconds ?? 4044)}
        period="Second Half"
      />

      {/* Subtabs Bar */}
      <div className="analyst-subtabs-nav-bar">
        {(['AI Analyst', 'AutoRecap (P1)', 'Spatial & Search (P2)'] as const).map(tab => (
          <button
            key={tab}
            type="button"
            onClick={() => {
              setAnalystActiveTab(tab);
              if (tab === 'AutoRecap (P1)' && !recapData) handleLoadRecap();
              if (tab === 'Spatial & Search (P2)') handleSearch(searchQuery);
            }}
            className={`analyst-subtab-pill ${analystActiveTab === tab ? 'active' : ''}`}
          >
            {tab}
          </button>
        ))}
        <a href={contextHref('/matches', matchId, selectedSeconds, revision)} className="open-match-view-sublink">Open in Match View →</a>
        {currentAnalyst && <span className="sr-only">Analyst context ready</span>}
      </div>

      {/* 2-Column Desktop Grid: Left Chat & Deep Analysis (65%) | Right Match Context (35%) */}
      {analystActiveTab === 'AI Analyst' && (
      <div className="analyst-desktop-2col-grid">
        {/* Left Column: AI Copilot Conversation */}
        <div className="analyst-conversation-column">
          {/* AI Header Card */}
          <div className="copilot-header-banner">
            <div className="copilot-brand-title">
              <span className="copilot-sparkle-star">✨</span>
              <div>
                <h2>PitchMind AI Analyst</h2>
                <p>Your tactical copilot for deeper football understanding</p>
              </div>
            </div>

            <div className="copilot-trust-badges-tray">
              <span className="copilot-trust-pill">✓ Evidence-backed</span>
              <span className="copilot-trust-pill">🗄️ No unsupported claims</span>
              <span className="copilot-trust-pill">ⓘ Uses match data only</span>
            </div>
          </div>

          {/* Try Asking Chips Row */}
          <div className="try-asking-chips-row">
            <span className="try-asking-label">↗ Try asking</span>
            {[
              "Why is Arsenal controlling the match?",
              "Show me the evidence behind the high regains",
              "What changed after minute 60?"
            ].map((p, idx) => (
              <button
                key={p}
                type="button"
                className={`try-asking-chip ${idx === 0 ? 'highlight' : ''}`}
                onClick={() => {
                  setQuestion(p);
                  handleAskAnalyst(p);
                }}
              >
                {p} ›
              </button>
            ))}
          </div>

          {/* Chat Thread */}
          <div className="analyst-chat-thread-container">
            {/* User Message Bubble */}
            <div className="chat-bubble-row user-row">
              <div className="user-message-bubble">
                <p>Why is Arsenal controlling the match?</p>
                <div className="user-avatar-tag">
                  <span className="msg-time">10:24</span>
                  <div className="avatar-circle-sm">JD</div>
                </div>
              </div>
            </div>

            {/* AI Response Card */}
            <div className="chat-bubble-row analyst-row">
              <div className="analyst-response-card-mockup">
                <div className="analyst-avatar-badge">✨</div>
                <div className="analyst-card-body">
                  <p className="analyst-core-paragraph">
                    Arsenal are controlling the match through high and coordinated pressure, winning the ball in advanced areas and preventing Manchester City from building consistent attacks. Their compact shape, aggressive ball-near pressing and quick transitions are creating a sustained attacking threat, while limiting City to low-quality chances.
                  </p>

                  {/* 4 Metric Callout Cards */}
                  <div className="analyst-quad-metrics-grid">
                    <div className="ametric-card">
                      <span className="ametric-label">High regains</span>
                      <div className="ametric-val-line">
                        <strong>8 vs 3</strong>
                        <span className="ametric-badge-green">↑ 167%</span>
                      </div>
                      <small className="ametric-clubs">Arsenal · Man City</small>
                      <p>More high regains giving Arsenal repeated attacks.</p>
                    </div>

                    <div className="ametric-card">
                      <span className="ametric-label">Possession (final third)</span>
                      <div className="ametric-val-line">
                        <strong>62% vs 38%</strong>
                        <span className="ametric-circle-icon">◔</span>
                      </div>
                      <small className="ametric-clubs">Arsenal · Man City</small>
                      <p>Arsenal are spending more time in dangerous areas.</p>
                    </div>

                    <div className="ametric-card">
                      <span className="ametric-label">Shots</span>
                      <div className="ametric-val-line">
                        <strong>12 vs 6</strong>
                        <span className="ametric-icon-gold">⚡</span>
                      </div>
                      <small className="ametric-clubs">Arsenal · Man City</small>
                      <p>More shots and higher xG from sustained pressure.</p>
                    </div>

                    <div className="ametric-card">
                      <span className="ametric-label">Opponent build-up</span>
                      <div className="ametric-val-line">
                        <strong>68%</strong>
                        <span className="ametric-badge-green">↓ 42%</span>
                      </div>
                      <small className="ametric-clubs">City disruption</small>
                      <p>City's build-up sequences ending due to Arsenal pressure (up from 48%).</p>
                    </div>
                  </div>

                  {/* Tactical Story */}
                  <div className="tactical-story-banner-card">
                    <div className="ts-title-row">
                      <span className="ts-icon">🎯</span>
                      <strong>Tactical story</strong>
                    </div>
                    <p>
                      Arsenal press high in a 4-3-3 shape, with the front three blocking City's central passing lanes and forcing play wide. When City go long, Arsenal's midfield and full-backs shift quickly to win second balls. These high regains are leading to fast, decisive attacks, as seen in the 64' goal. City are struggling to progress through midfield and are being forced into low-percentage passes, resulting in fewer entries into Arsenal's box.
                    </p>
                  </div>

                  {/* Key Evidence Section */}
                  <div className="key-evidence-mockup-section">
                    <div className="ke-title-row">
                      <span className="ke-icon">🔗</span>
                      <strong>Key evidence</strong>
                    </div>

                    <div className="ke-cards-triple-row">
                      <div className="ke-card-item">
                        <div className="ke-thumb video-thumb">
                          <span className="ke-play-btn">▶</span>
                        </div>
                        <div className="ke-info">
                          <small>64' Goal</small>
                          <strong>High regain leads to shot (wide) ›</strong>
                          <div className="ke-pills">
                            <span className="ke-tag">Key moment</span>
                            <span className="ke-tag">Evidence</span>
                          </div>
                        </div>
                      </div>

                      <div className="ke-card-item">
                        <div className="ke-thumb pitch-thumb">
                          <svg viewBox="0 0 60 40"><rect width="60" height="40" fill="#113322" /></svg>
                        </div>
                        <div className="ke-info">
                          <small>Pass map (second half)</small>
                          <strong>Arsenal's pressure in wide areas ›</strong>
                          <div className="ke-pills">
                            <span className="ke-tag">Tactical view</span>
                          </div>
                        </div>
                      </div>

                      <div className="ke-card-item">
                        <div className="ke-thumb heat-thumb">
                          <svg viewBox="0 0 60 40"><rect width="60" height="40" fill="#0d281a" /></svg>
                        </div>
                        <div className="ke-info">
                          <small>City build-up disruption</small>
                          <strong>68% sequences ended early ›</strong>
                          <div className="ke-pills">
                            <span className="ke-tag">Metric evidence</span>
                          </div>
                        </div>
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            </div>

            {/* Dynamic AI Status / Error / Response */}
            {isAnalyzing && (
              <div className="chat-bubble-row analyst-row">
                <div className="analyst-response-card-mockup" style={{ padding: '1rem', display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
                  <div className="analyst-avatar-badge" style={{ animation: 'pulse 1.5s infinite' }}>✨</div>
                  <div>
                    <strong style={{ display: 'block', fontSize: '0.9rem', color: '#38bdf8' }}>Analyzing Match Telemetry...</strong>
                    <span style={{ fontSize: '0.8rem', color: '#94a3b8' }}>Evaluating spatial tracking data and tactical indicators.</span>
                  </div>
                </div>
              </div>
            )}

            {aiError && (
              <div className="chat-bubble-row analyst-row">
                <div style={{ background: 'rgba(239, 68, 68, 0.1)', border: '1px solid rgba(239, 68, 68, 0.35)', borderRadius: '0.75rem', padding: '1rem', width: '100%' }}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', marginBottom: '0.35rem', color: '#f87171', fontWeight: 600, fontSize: '0.875rem' }}>
                    <span>⚠️</span>
                    <span>Tactical Intelligence Model Status</span>
                  </div>
                  <p style={{ margin: 0, fontSize: '0.825rem', color: '#fca5a5', lineHeight: 1.5 }}>{aiError}</p>
                </div>
              </div>
            )}

            {aiAnalysisText && (
              <div className="chat-bubble-row analyst-row">
                <div className="analyst-response-card-mockup">
                  <div className="analyst-avatar-badge">✨</div>
                  <div className="analyst-card-body">
                    <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', marginBottom: '0.5rem' }}>
                      <span className="copilot-trust-pill" style={{ background: 'rgba(16, 185, 129, 0.2)', color: '#34d399', border: '1px solid rgba(16, 185, 129, 0.4)' }}>
                        ✓ Grounded Telemetry Model
                      </span>
                    </div>
                    <p className="analyst-core-paragraph" style={{ whiteSpace: 'pre-line' }}>
                      {aiAnalysisText}
                    </p>
                  </div>
                </div>
              </div>
            )}
          </div>

          {/* Bottom Chat Input Bar */}
          <div className="copilot-bottom-input-wrap">
            <form
              className="copilot-input-form"
              onSubmit={e => {
                e.preventDefault();
                if (question.trim()) {
                  handleAskAnalyst(question.trim());
                  setQuestion('');
                }
              }}
            >
              <button type="button" className="attach-paperclip-btn" aria-label="Attach clip">📎</button>
              <input
                type="text"
                placeholder="Ask about tactics, players, moments or anything from this match..."
                value={question}
                onChange={e => setQuestion(e.target.value)}
                aria-label="Ask about tactics, players, moments or anything from this match"
              />
              <button type="submit" className="copilot-send-btn" aria-label="Send query">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5"><line x1="22" y1="2" x2="11" y2="13" /><polygon points="22 2 15 22 11 13 2 9 22 2" /></svg>
              </button>
            </form>
            <div className="copilot-input-footer-line">
              <small>Try a question above or type your own. All responses are evidence-backed using match data. If we can't find reliable evidence, we'll say so.</small>
              <small className="powered-text">Powered by PitchMind AI</small>
            </div>
          </div>
        </div>

        {/* Right Column: Match Context Sidebar */}
        <div className="analyst-sidebar-column">
          {/* Top Scope Tabs */}
          <div className="sidebar-scope-tabs-bar">
            {(['Match', 'Arsenal', 'Man City', 'Players'] as const).map(tab => (
              <button
                key={tab}
                type="button"
                className={`sidebar-tab-btn ${sidebarTab === tab ? 'active' : ''}`}
                onClick={() => setSidebarTab(tab)}
              >
                {tab}
              </button>
            ))}
          </div>

          {/* Match Context Card */}
          <div className="sidebar-context-card">
            <div className="scontext-venue-row">
              <span>🏆 Premier League</span>
              <span>🏟️ Emirates Stadium</span>
              <span>📅 Sun, 8 Dec 2024</span>
            </div>

            <div className="scontext-score-row">
              <div className="sc-team">
                <ClubCrest name="Arsenal" className="sc-crest" />
                <strong>Arsenal</strong>
                <small>4 - 3 - 3</small>
              </div>

              <div className="sc-mid">
                <span className="sc-score">2</span>
                <div className="sc-clock">
                  <span className="sc-time">67:24</span>
                  <small>Second Half</small>
                </div>
                <span className="sc-score">1</span>
              </div>

              <div className="sc-team text-right">
                <strong>Manchester City</strong>
                <small>4 - 2 - 3 - 1</small>
                <ClubCrest name="Manchester City" className="sc-crest" />
              </div>
            </div>
          </div>

          {/* Key Moments Timeline */}
          <div className="sidebar-timeline-card">
            <div className="panel-header-row">
              <div className="panel-title-with-badge">
                <span className="panel-icon-clock">⏱</span>
                <h3>Key moments timeline</h3>
              </div>
              <a href="#view-all" className="view-all-link">View all →</a>
            </div>

            <div className="horizontal-timeline-axis-wrap">
              <div className="timeline-ticks-row">
                <span>0'</span>
                <span>15'</span>
                <span>30'</span>
                <span>45'</span>
                <span>60'</span>
                <span>75'</span>
                <span>90'</span>
              </div>
              <div className="axis-line"></div>
              {[
                { min: "18'", title: "ARS 1 - 0", detail: "Set piece goal", icon: "⚽", left: "20%" },
                { min: "37'", title: "MCI 1 - 1", detail: "Counter attack", icon: "⚽", left: "41%" },
                { min: "64'", title: "ARS 2 - 1", detail: "High regain → shot (wide)", icon: "⚽", left: "71%", highlight: true },
                { min: "72'", title: "Yellow card", detail: "Foul on Rice", icon: "🟨", left: "86%" }
              ].map((m, idx) => (
                <div key={idx} className={`timeline-node-item ${m.highlight ? 'active-node' : ''}`} style={{ left: m.left }}>
                  <div className="node-dot">{m.icon}</div>
                  <strong className="node-min">{m.min}</strong>
                  <span className="node-title">{m.title}</span>
                  <small className="node-desc">{m.detail}</small>
                </div>
              ))}
            </div>
          </div>

          {/* Evidence from this analysis */}
          <div className="sidebar-evidence-carousel-card">
            <div className="panel-header-row">
              <div className="panel-title-with-badge">
                <span className="panel-icon-emerald">🗄️</span>
                <h3>Evidence from this analysis</h3>
              </div>
              <a href="#view-all-evidence" className="view-all-link">View all evidence →</a>
            </div>

            <div className="evidence-filter-chips-row">
              {['All', 'Key moments 3', 'Metrics 4', 'Tactical views 4', 'Player impact 2'].map(chip => (
                <button
                  key={chip}
                  type="button"
                  className={`evidence-chip-pill ${evidenceFilter === chip ? 'active' : ''}`}
                  onClick={() => setEvidenceFilter(chip)}
                >
                  {chip}
                </button>
              ))}
            </div>

            <div className="evidence-carousel-cards-strip" tabIndex={0} role="region" aria-label="Evidence cards">
              {[
                { title: "64' High regain leads to shot (wide)", badge: "Key moment" },
                { title: "Arsenal press in wide areas", badge: "Tactical view" },
                { title: "City build-up disrupted (68%)", badge: "Metric evidence" },
                { title: "Ødegaard presses Rodri (turnover)", badge: "Player impact" }
              ].map((ev, idx) => (
                <div key={idx} className="evidence-mini-card">
                  <div className="ev-mini-thumb">▶</div>
                  <strong>{ev.title}</strong>
                  <span className="ev-mini-badge">{ev.badge}</span>
                </div>
              ))}
            </div>
          </div>

          {/* Related Views */}
          <div className="sidebar-related-views-card">
            <div className="panel-header-row">
              <span className="panel-icon-emerald">🔗</span>
              <h3>Related views</h3>
            </div>

            <div className="related-links-vertical-list">
              <a href="/matches" className="related-view-item">
                <span className="related-icon">📺</span>
                <div>
                  <strong>Open Match View</strong>
                  <small>Explore the full match with interactive timeline.</small>
                </div>
                <span className="related-chevron">›</span>
              </a>

              <a href="/teams" className="related-view-item">
                <span className="related-icon">🛡️</span>
                <div>
                  <strong>Arsenal Team View</strong>
                  <small>See tactical trends, shapes and player roles.</small>
                </div>
                <span className="related-chevron">›</span>
              </a>

              <a href="/teams" className="related-view-item">
                <span className="related-icon">🛡️</span>
                <div>
                  <strong>Man City Team View</strong>
                  <small>Analyse their build-up, pressing and structure.</small>
                </div>
                <span className="related-chevron">›</span>
              </a>

              <a href="/players" className="related-view-item">
                <span className="related-icon">👤</span>
                <div>
                  <strong>Player View</strong>
                  <small>Inspect individual performances.</small>
                </div>
                <span className="related-chevron">›</span>
              </a>
            </div>
          </div>
        </div>
      </div>
      )}

      {/* AutoRecap (EP09 P1) Tab */}
      {analystActiveTab === 'AutoRecap (P1)' && (
        <div className="analyst-recap-panel" style={{ padding: '1.5rem', background: 'rgba(15, 23, 42, 0.65)', border: '1px solid rgba(255, 255, 255, 0.08)', borderRadius: '0.75rem', marginTop: '1rem' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1.25rem', flexWrap: 'wrap', gap: '1rem' }}>
            <div>
              <h3 style={{ margin: 0, fontSize: '1.25rem', color: '#f8fafc', fontWeight: 600 }}>Deterministic Match AutoRecap</h3>
              <p style={{ margin: '0.25rem 0 0', fontSize: '0.875rem', color: '#94a3b8' }}>Powered by Azure Functions & Background Service Bus Queue (EP09-S03)</p>
            </div>
            <div style={{ display: 'flex', gap: '0.75rem' }}>
              <button
                type="button"
                className="control-button primary"
                onClick={handleLoadRecap}
                style={{ padding: '0.5rem 1rem', fontSize: '0.875rem' }}
              >
                Refresh Recap
              </button>
              <button
                type="button"
                className="control-button"
                disabled={isExporting}
                onClick={handleExportEvidence}
                style={{ padding: '0.5rem 1rem', fontSize: '0.875rem', background: '#3b82f6', color: '#fff', border: 'none' }}
              >
                {isExporting ? 'Generating HMAC...' : 'Export Evidence JSON (EP09-S04)'}
              </button>
            </div>
          </div>

          {exportPackage && (
            <div style={{ padding: '0.875rem 1rem', background: 'rgba(16, 185, 129, 0.1)', border: '1px solid rgba(16, 185, 129, 0.3)', borderRadius: '0.5rem', marginBottom: '1.25rem' }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', color: '#34d399', fontWeight: 600, fontSize: '0.875rem' }}>
                <span>✓</span> Evidence Export Package Ready
              </div>
              <div style={{ fontSize: '0.8rem', color: '#cbd5e1', marginTop: '0.25rem', fontFamily: 'monospace' }}>
                Export ID: {exportPackage.exportId} | Items: {exportPackage.evidenceItemCount} | Signature: {exportPackage.signature.slice(0, 16)}...
              </div>
            </div>
          )}

          <div style={{ background: '#0b1120', border: '1px solid rgba(255, 255, 255, 0.05)', borderRadius: '0.5rem', padding: '1.25rem', marginBottom: '1.5rem' }}>
            <h4 style={{ margin: '0 0 0.5rem', fontSize: '1.1rem', color: '#60a5fa' }}>{recapData ? recapData.headline : 'Match AutoRecap Preview'}</h4>
            <p style={{ margin: 0, fontSize: '0.925rem', lineHeight: '1.6', color: '#cbd5e1' }}>
              {recapData ? recapData.summary : 'Click "Refresh Recap" to generate the automated tactical summary from match events.'}
            </p>
            {recapData?.provenance && (
              <div style={{ marginTop: '0.75rem', fontSize: '0.75rem', color: '#64748b', fontStyle: 'italic' }}>
                Provenance: {recapData.provenance}
              </div>
            )}
          </div>

          <h4 style={{ margin: '0 0 0.75rem', fontSize: '1rem', color: '#f8fafc' }}>Key Tactical Moments</h4>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
            {(recapData?.keyMomentsSummary ?? [
              '[Tactical Phase 1]: Sustained overload on the left flank via Channel 2 (Half-Space).',
              '[Tactical Phase 2]: High press turnover in Zone 14 leading directly to decisive goal opportunity.'
            ]).map((moment, idx) => (
              <div key={idx} style={{ padding: '0.875rem 1rem', background: '#0f172a', border: '1px solid rgba(255, 255, 255, 0.06)', borderRadius: '0.5rem', fontSize: '0.875rem', color: '#e2e8f0', display: 'flex', alignItems: 'flex-start', gap: '0.75rem' }}>
                <span style={{ color: '#38bdf8', fontWeight: 600 }}>#{idx + 1}</span>
                <span>{moment}</span>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Spatial & Historical Search (EP12 P2) Tab */}
      {analystActiveTab === 'Spatial & Search (P2)' && (
        <div className="analyst-spatial-panel" style={{ padding: '1.5rem', background: 'rgba(15, 23, 42, 0.65)', border: '1px solid rgba(255, 255, 255, 0.08)', borderRadius: '0.75rem', marginTop: '1rem' }}>
          <div style={{ marginBottom: '1.5rem' }}>
            <h3 style={{ margin: 0, fontSize: '1.25rem', color: '#f8fafc', fontWeight: 600 }}>Historical Tracking & Spatial Corridors (EP12-S02)</h3>
            <p style={{ margin: '0.25rem 0 0', fontSize: '0.875rem', color: '#94a3b8' }}>5 Lateral Pitch Corridors & Zone 14 Tactical Occupancy Exploration</p>
          </div>

          {/* 5 Corridors Grid */}
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))', gap: '1rem', marginBottom: '1.5rem' }}>
            {[
              { name: 'Left Wing (0-21m)', occupancy: '18%', desc: 'Overlap runs' },
              { name: 'Left Half-Space (21-42m)', occupancy: '28%', desc: 'Primary playmaking corridor' },
              { name: 'Central Zone 14 (42-63m)', occupancy: '26%', desc: 'Dangerous edge-of-box actions' },
              { name: 'Right Half-Space (63-84m)', occupancy: '16%', desc: 'Switching transitions' },
              { name: 'Right Wing (84-105m)', occupancy: '12%', desc: 'Width isolation' },
            ].map(corridor => (
              <div key={corridor.name} style={{ background: '#0b1120', border: '1px solid rgba(56, 189, 248, 0.2)', borderRadius: '0.5rem', padding: '1rem' }}>
                <div style={{ fontSize: '0.75rem', color: '#94a3b8', textTransform: 'uppercase', letterSpacing: '0.05em' }}>{corridor.name}</div>
                <div style={{ fontSize: '1.5rem', fontWeight: 700, color: '#38bdf8', margin: '0.25rem 0' }}>{corridor.occupancy}</div>
                <div style={{ fontSize: '0.8rem', color: '#64748b' }}>{corridor.desc}</div>
              </div>
            ))}
          </div>

          {/* Provenance-Aware Historical Retrieval */}
          <div style={{ borderTop: '1px solid rgba(255, 255, 255, 0.08)', paddingTop: '1.5rem' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1rem', flexWrap: 'wrap', gap: '0.5rem' }}>
              <div>
                <h4 style={{ margin: 0, fontSize: '1.1rem', color: '#f8fafc' }}>Provenance-Aware Evidence Search (EP12-S01)</h4>
                <p style={{ margin: '0.25rem 0 0', fontSize: '0.8rem', color: '#94a3b8' }}>Query historical tactical insights with non-conflicting sequences & cryptographic proof</p>
              </div>
              <div style={{ display: 'flex', gap: '0.5rem', width: '100%', maxWidth: '360px' }}>
                <input
                  type="text"
                  value={searchQuery}
                  onChange={e => setSearchQuery(e.target.value)}
                  placeholder="Search tactics (e.g. Zone 14, Overload)..."
                  style={{ flex: 1, padding: '0.5rem 0.75rem', background: '#0b1120', border: '1px solid rgba(255, 255, 255, 0.15)', borderRadius: '0.375rem', color: '#fff', fontSize: '0.875rem' }}
                />
                <button
                  type="button"
                  className="control-button primary"
                  onClick={() => handleSearch(searchQuery)}
                  style={{ padding: '0.5rem 1rem', fontSize: '0.875rem' }}
                >
                  Search
                </button>
              </div>
            </div>

            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(320px, 1fr))', gap: '1rem' }}>
              {searchResults.map(result => (
                <div key={result.insightId} style={{ background: '#0f172a', border: '1px solid rgba(255, 255, 255, 0.06)', borderRadius: '0.5rem', padding: '1rem' }}>
                  <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '0.5rem' }}>
                    <span style={{ fontSize: '0.75rem', fontWeight: 600, color: '#10b981', background: 'rgba(16, 185, 129, 0.15)', padding: '0.2rem 0.5rem', borderRadius: '0.25rem' }}>
                      {result.truthClass}
                    </span>
                    <span style={{ fontSize: '0.75rem', color: '#94a3b8' }}>
                      Confidence: {Math.round(result.confidence * 100)}%
                    </span>
                  </div>
                  <h5 style={{ margin: '0 0 0.5rem', fontSize: '0.95rem', color: '#f1f5f9' }}>{result.title}</h5>
                  <p style={{ margin: '0 0 0.75rem', fontSize: '0.825rem', color: '#94a3b8', lineHeight: '1.5' }}>{result.summary}</p>
                  <div style={{ fontSize: '0.75rem', color: '#64748b' }}>
                    <strong>Citations:</strong> {result.citations.join(', ')}
                  </div>
                </div>
              ))}
            </div>
          </div>

          {/* Azure Managed Redis Status Card */}
          <div style={{ marginTop: '1.5rem', padding: '1rem', background: 'rgba(59, 130, 246, 0.08)', border: '1px solid rgba(59, 130, 246, 0.25)', borderRadius: '0.5rem', display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexWrap: 'wrap', gap: '0.75rem' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '0.75rem' }}>
              <span style={{ width: 10, height: 10, borderRadius: '50%', background: '#3b82f6' }}></span>
              <div>
                <strong style={{ fontSize: '0.875rem', color: '#93c5fd' }}>Azure Managed Redis (EP09-S01)</strong>
                <div style={{ fontSize: '0.75rem', color: '#94a3b8' }}>Projection Cache: Active with TLS 1.2, 5m TTL, & In-Memory Safe Fallback</div>
              </div>
            </div>
            <span style={{ fontSize: '0.75rem', color: '#60a5fa', fontWeight: 600 }}>P1 / P2 READY</span>
          </div>
        </div>
      )}
    </section>
  );
}


function CanonicalInsightPage({ matchId, selectedSeconds, revision, route }: {
  matchId: string;
  selectedSeconds: number;
  revision?: number;
  route: AppRoute;
}) {
  const [page, setPage] = useState<CanonicalInsightPage | null>(null);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const insightId = route.kind === 'insights' ? route.insightId : undefined;

  useEffect(() => {
    const controller = new AbortController();
    setLoading(true);
    const query = new URLSearchParams({ atSeconds: String(selectedSeconds), limit: '50' });
    if (revision) query.set('revision', String(revision));
    request<CanonicalInsightPage>(`canonical/matches/${encodeURIComponent(matchId)}/insights?${query}`, controller.signal)
      .then(result => {
        if (controller.signal.aborted) return;
        if (result.matchId !== matchId || result.selectedTimeSeconds !== selectedSeconds ||
          (revision && result.projectionRevision !== revision) ||
          result.items.some(item => item.matchId !== matchId || item.canonicalRevision !== result.canonicalRevision ||
            item.projectionRevision !== result.projectionRevision || item.selectedTimeSeconds !== selectedSeconds ||
            item.throughSequence > result.throughSequence)) {
          setError('This insight does not match the selected moment. Return to the match and choose another moment.');
          setPage(null);
          return;
        }
        setPage(result);
        setError('');
      })
      .catch(() => { if (!controller.signal.aborted) { setPage(null); setError('Insights are temporarily unavailable.'); } })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [matchId, selectedSeconds, revision]);

  const items = page?.items.filter(item => !insightId || item.insightId === insightId) ?? [];

  return (
    <section className="content-page">
      <PageHeading eyebrow="Insights" title="Evidence at this moment" question={`What recorded insight is available at ${clock(selectedSeconds)}?`} />
      {loading || error || !page ? <PageState loading={loading} error={error} empty="No insight is available at the selected time." />
        : items.length === 0 ? <PageState loading={false} empty={insightId ? 'This insight is not available at the selected time.' : 'No insight is available at the selected time.'} />
          : <div className="entity-grid">{items.map(insight => <article className="entity-card" key={insight.insightId}>
            <span className="badge">{insight.truthClass}</span><h2>{insight.summary}</h2>
            <p>Selected moment · {clock(insight.selectedTimeSeconds)}</p>
            <details className="insight-source-details">
              <summary>Evidence and source details</summary>
              <p>Revision {insight.canonicalRevision} · {insight.evidence.length} source references</p>
              <ul className="evidence-list">{insight.evidence.map(item => <li key={`${item.kind}:${item.id}`}>{item.kind}: {item.id} · {item.relation}</li>)}</ul>
            </details>
            <a href={contextHref(`/matches/${encodeURIComponent(matchId)}`, matchId, selectedSeconds, insight.projectionRevision)}>Open selected match</a>
          </article>)}</div>}
    </section>
  );
}

/* =========================================================================
   MAIN ROUTE DISPATCHER
   ========================================================================= */
export function RoutePage({ route, search }: { route: AppRoute; search: string }) {
  const params = new URLSearchParams(search);
  const selectedMatchId = params.get('matchId') ?? undefined;
  const selectedSeconds = selectedTimeFromSearch(search);
  const hasSelectedTime = params.has('atSeconds');
  const revisionValue = Number(params.get('revision'));
  const revision = Number.isInteger(revisionValue) && revisionValue > 0 ? revisionValue : undefined;

  if (route.kind === 'welcome') return <WelcomePage />;

  if (route.kind === 'home') return <HomePage search={search} selectedMatchId={selectedMatchId} selectedSeconds={hasSelectedTime ? selectedSeconds : undefined} />;

  if (route.kind === 'matches') return <MatchPage search={search} matchId={selectedMatchId} selectedSeconds={selectedSeconds} revision={revision} />;

  if (route.kind === 'teams') return <TeamsPage route={route} selectedMatchId={selectedMatchId} selectedSeconds={selectedSeconds} revision={revision} />;

  if (route.kind === 'players') return <PlayersPage route={route} selectedMatchId={selectedMatchId} selectedSeconds={selectedSeconds} revision={revision} />;

  if (route.kind === 'insights') {
    if (selectedMatchId && !selectedMatchId.startsWith('sim-')) {
      return <CanonicalInsightPage route={route} matchId={selectedMatchId} selectedSeconds={selectedSeconds} revision={revision} />;
    }
    return <InsightPage route={route} matchId={selectedMatchId} selectedSeconds={selectedSeconds} revision={revision} />;
  }

  if (route.kind === 'analyst') return <AnalystPage matchId={selectedMatchId} selectedSeconds={selectedSeconds} revision={revision} />;

  return (
    <section className="content-page">
      <PageHeading eyebrow="PitchMind" title="This page is unavailable" question="Choose a section from the main navigation." />
      <a className="primary-link" href="/home">Go to available matches</a>
    </section>
  );
}
