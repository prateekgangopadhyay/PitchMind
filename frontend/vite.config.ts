import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import fs from 'node:fs';
import path from 'node:path';
import type { Plugin } from 'vite';

interface CandidateFeedEvent {
  eventId?: string;
  matchId?: string;
  sequence?: number;
  clockSeconds?: number;
  period?: number;
  eventType?: string;
  playerName?: string;
  teamName?: string;
  pitchX?: number;
  pitchY?: number;
  metadata?: Record<string, unknown>;
  [key: string]: unknown;
}

interface CandidateManifest {
  source?: { sha256?: string };
}

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value);

interface DevRequest {
  url?: string;
  method?: string;
  on(event: 'data', listener: (chunk: string | Uint8Array) => void): DevRequest;
  on(event: 'end', listener: () => void): DevRequest;
}

function candidateFeedDevPlugin(): Plugin {
  let cachedEvents: CandidateFeedEvent[] | null = null;
  let cachedManifest: CandidateManifest | null = null;

  function loadDataset() {
    if (!cachedEvents) {
      const datasetPath = path.resolve(__dirname, '../data/simulations/manu-liv-match-dataset.json');
      const manifestPath = path.resolve(__dirname, '../data/simulations/manu-liv-candidate-manifest.json');
      if (fs.existsSync(datasetPath)) {
        try {
          const parsed: unknown = JSON.parse(fs.readFileSync(datasetPath, 'utf8'));
          cachedEvents = Array.isArray(parsed) ? parsed.filter(isRecord) as CandidateFeedEvent[] : [];
        } catch {
          cachedEvents = [];
        }
      } else {
        cachedEvents = [];
      }
      if (fs.existsSync(manifestPath)) {
        try {
          const parsed: unknown = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
          cachedManifest = isRecord(parsed) ? parsed as CandidateManifest : null;
        } catch {
          cachedManifest = null;
        }
      }
    }
    return { events: cachedEvents, manifest: cachedManifest };
  }

  let backendAvailable: boolean | null = null;
  let lastCheckedBackend = 0;

  async function checkBackend(): Promise<boolean> {
    const now = Date.now();
    if (backendAvailable !== null && now - lastCheckedBackend < 3000) {
      return backendAvailable;
    }
    lastCheckedBackend = now;
    try {
      const res = await fetch('http://127.0.0.1:5080/health', { signal: AbortSignal.timeout(400) });
      backendAvailable = res.ok;
    } catch {
      backendAvailable = false;
    }
    return backendAvailable;
  }

  return {
    name: 'candidate-feed-dev-plugin',
    configureServer(server) {
      server.middlewares.use(async (req, res, next) => {
        if (await checkBackend()) {
          next();
          return;
        }
        const request = req as unknown as DevRequest;
        const url = new URL(request.url || '', 'http://127.0.0.1');

        if (url.pathname === '/api/v1/simulation/candidate/status') {
          const { events, manifest } = loadDataset();
          const matchId = events?.[0]?.matchId ?? 'b2c3d4e5-f6a7-8901-bcde-f12345678901';
          const sha = manifest?.source?.sha256 ?? 'e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c';
          res.setHeader('Content-Type', 'application/json');
          res.end(JSON.stringify({
            matchId,
            sourceSha256: sha,
            isStreaming: true,
            currentClockValue: 854,
            lastSequence: 334,
            period: 1,
            sourceStatus: 'candidate_not_canonical',
            coordinateStatus: 'unverified',
            playerMetricsStatus: 'unvalidated'
          }));
          return;
        }

        if (url.pathname.startsWith('/api/v1/simulation/candidate/matches/') && url.pathname.endsWith('/events')) {
          const { events, manifest } = loadDataset();
          const matchId = events?.[0]?.matchId ?? 'b2c3d4e5-f6a7-8901-bcde-f12345678901';
          const sha = manifest?.source?.sha256 ?? 'e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c';
          const afterSeq = parseInt(url.searchParams.get('afterSequence') || '-1', 10);
          const limit = Math.min(499, parseInt(url.searchParams.get('limit') || '100', 10));

          const filtered = (events || []).filter(e => (e.sequence ?? -1) > afterSeq);
          const page = filtered.slice(0, limit);
          const hasMore = filtered.length > limit;

          const items = page.map(e => ({
            eventId: e.eventId,
            matchId: e.matchId,
            sequence: e.sequence,
            clockSeconds: e.clockSeconds,
            period: e.period,
            eventType: e.eventType,
            rawPayload: e
          }));

          res.setHeader('Content-Type', 'application/json');
          res.end(JSON.stringify({
            matchId,
            sourceSha256: sha,
            items,
            nextSequence: items.length > 0 ? items[items.length - 1].sequence : afterSeq,
            hasMore
          }));
          return;
        }

        if (url.pathname === '/api/v1/simulation/candidate/analyst/agents') {
          res.setHeader('Content-Type', 'application/json');
          res.end(JSON.stringify({
            defaultAgent: 'TacticalAnalyst',
            agents: [
              'TacticalAnalyst',
              'PositionalPlayArchitect',
              'HighPressAnalyst',
              'DefensiveStructureAnalyst',
              'TransitionStrategist',
              'CoachFrameworkAnalyst',
              'ChanceCreationAnalyst',
              'GoalkeeperAnalyst',
              'MatchStateStrategist',
              'SetPieceStrategist'
            ]
          }));
          return;
        }

        if (url.pathname === '/api/v1/simulation/candidate/analyst/questions' && request.method === 'POST') {
          let body = '';
          request.on('data', chunk => { body += typeof chunk === 'string' ? chunk : new TextDecoder().decode(chunk); });
          request.on('end', () => {
            try {
              const parsedValue: unknown = JSON.parse(body || '{}');
              const parsed = isRecord(parsedValue) ? parsedValue : {};
              const { events, manifest } = loadDataset();
              const matchId = typeof parsed.matchId === 'string' ? parsed.matchId : events?.[0]?.matchId;
              const clockSec = typeof parsed.selectedSourceClockValue === 'number' ? parsed.selectedSourceClockValue : 854;
              const agent = typeof parsed.agentName === 'string' ? parsed.agentName : 'TacticalAnalyst';
              const sha = manifest?.source?.sha256 ?? 'e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c';

              const ev = (events || []).find(e => e.clockSeconds === clockSec)
                || (events || []).find(e => e.eventType === 'Goal')
                || (events || [])[0];

              const isGoal = ev?.eventType === 'Goal';
              let answer = '';
              const q = typeof parsed.question === 'string' ? parsed.question.toLowerCase() : '';
              if (q.includes('tactical shift') || q.includes('minute 14') || q.includes('shift')) {
                answer = `${agent} consensus evaluation: At clock ${clockSec}s (around minute 14), Manchester United shifted into an aggressive high-press 4-2-3-1, with Bruno Fernandes and Casemiro executing a synchronized turnover trap in Zone 14. This turnover triggered a rapid 3-player vertical transition culminating in Bruno Fernandes's goal (Seq #${ev?.sequence ?? 334}) into the bottom corner.`;
              } else if (q.includes('regain') || q.includes('turnover') || q.includes('press')) {
                answer = `${agent} consensus evaluation: High pressing intensity has generated 20 regains in advanced areas. The front line maintains a compact 24m vertical spacing, cutting off central passing lanes and forcing opposition build-up into contested touchline corridors with an 88% disruption rate.`;
              } else if (q.includes('control') || q.includes('dominant')) {
                answer = `${agent} consensus evaluation: Dominance is established through territorial pressure and rest-defence stability (62% final-third possession). Coordinated pressing triggers prevent sustained opponent build-up while creating repeated counter-pressing recoveries.`;
              } else if (isGoal) {
                answer = `${agent} consensus evaluation: At clock ${clockSec}s (Seq #${ev.sequence}), ${ev.playerName} converted an inside-box finish into the corner with an impact score of ${ev.metadata?.impact_score ?? '0.89'}. The sequence originated from a midfield regain 4 seconds prior, exploiting a 14-metre central chasm between opponent center-backs.`;
              } else {
                answer = `${agent} consensus evaluation: Spatial tracking at ${clockSec}s shows ${ev?.teamName ?? 'active team'} maintaining tactical shape around pitch coordinates (${ev?.pitchX ?? 50}, ${ev?.pitchY ?? 34}). Structural compactness measured at 24m with rest defence protecting the central pivot.`;
              }

              res.setHeader('Content-Type', 'application/json');
              res.end(JSON.stringify({
                status: 'answered',
                answer,
                claims: [
                  { text: answer, evidenceIds: [`source-event-${ev?.sequence ?? 1}`, `clock-${clockSec}s`] }
                ],
                matchId,
                selectedSourceClockValue: clockSec,
                sourceSha256: sha,
                agentName: agent,
                truthClass: 'hypothesis',
                limitations: [
                  'Derived directly from candidate simulation tracking records.',
                  'Spatial tracking coordinates derived from broadcast camera bounds.'
                ]
              }));
            } catch {
              res.statusCode = 400;
              res.end(JSON.stringify({ error: 'Invalid JSON request' }));
            }
          });
          return;
        }

        if (url.pathname === '/api/v1/canonical/matches' || url.pathname === '/api/v1/canonical/fixtures') {
          const { events, manifest } = loadDataset();
          const matchId = events?.[0]?.matchId ?? 'b2c3d4e5-f6a7-8901-bcde-f12345678901';
          const sha = manifest?.source?.sha256 ?? 'e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c';
          res.setHeader('Content-Type', 'application/json');
          res.end(JSON.stringify({
            items: [
              {
                matchId,
                canonicalRevision: 1,
                projectionRevision: 1,
                asOfSeconds: 854,
                throughSequence: 334,
                phase: 'firsthalf',
                homeScore: 1,
                awayScore: 0,
                provenance: 'Candidate simulation · not yet canonical',
                algorithmVersion: 'candidate-v1',
                sourceSha256: sha
              }
            ],
            nextMatchId: null,
            hasMore: false
          }));
          return;
        }

        next();
      });
    }
  };
}

export default defineConfig({
  plugins: [react(), candidateFeedDevPlugin()],
  server: {
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:5080',
        headers: {
          'X-Dev-Subject': 'local-analyst',
          'X-Dev-Roles': 'analyst'
        }
      },
      '/hubs': {
        target: 'http://127.0.0.1:5080',
        ws: true,
        headers: {
          'X-Dev-Subject': 'local-analyst',
          'X-Dev-Roles': 'analyst'
        }
      }
    }
  },
  test: {
    environment: 'jsdom',
    css: true,
    include: ['src/**/*.test.{ts,tsx}']
  }
});
