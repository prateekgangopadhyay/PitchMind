# PitchMind API contract

Version 1, 2026-10-08. This consolidates current executable HTTP routes; Azure adapter/live/admin APIs remain planned. Current fixture is synthetic and read-only, with deterministic fallback rather than an active Foundry integration. API and static frontend share an origin in the container build.

| Method | Route | Contract |
|---|---|---|
| GET | `/api/health` | API identity and `ok`; no dependency readiness claim |
| GET | `/health` | Process health; current implementation has no external dependency checks |
| GET | `/api/v1/matches` | Array containing seeded fictional fixture, teams, duration, events/frames and synthetic provenance |
| GET | `/api/v1/matches/{matchId}/state?atSeconds=3309` | Match ID, elapsed clock, period, replay/full-time status, score, provenance, latest frame and sequence |
| GET | `/api/v1/matches/{matchId}/events?atSeconds=3309` | Ordered synthetic event wrappers visible through selected time |
| GET | `/api/v1/matches/{matchId}/insights?atSeconds=3309` | Evidence-supported candidate patterns whose complete outcome is visible through selected time |
| GET | `/api/v1/matches/{matchId}/evidence/{insightId}?atSeconds=3309` | Insight, exact source events/frames, coordinate convention, rule conditions and limitations |
| POST | `/api/v1/matches/{matchId}/analyst` | Bounded selected-time explanation request below; deterministic fallback response |

`atSeconds` is integer elapsed match time in [0,5400], default 0. Historical inspection never grants access to events after selected time. Fixture halftime is at 2700; full-time at 5400. Added-time/competition clock policies are deferred. Identifiers are immutable; missing GUID matches return 404. Response JSON uses camelCase and enum string values. `X-Correlation-ID` is the server trace ID. There is no production authentication in this offline slice; anonymous synthetic reads are not a validated production identity policy.

```json
{"insightId":"<id returned by insights>","atSeconds":3309,"audience":"fan"}
```

Audience is `fan` or `analyst`; default fan. Response contains `insightId`, `clockSeconds`, `audience`, `source: deterministic-fallback`, text, evidence event IDs and limitations. Audience changes text only. Unsupported or unavailable evidence is not invented. No match mutation or model configuration endpoint exists.

Errors use problem JSON: 400 for invalid selected time/audience/body; 404 for unknown match or insight not yet visible. Malformed GUID route behavior follows ASP.NET routing and may return a framework 404. Analyst requests are limited to 30 per IP per minute (429 on exhaustion) and request bodies to 4096 bytes (413 when exceeded). No retryable stream cursor or pagination contract is implemented. `/matches` currently contains the full fixture; replace with summaries and paginated canonical events before scaling beyond the bounded demonstration dataset.

Coordinates are metres in a fixed stadium frame (105 × 68), not percentages. Attack direction switches at halftime. A player frame is an observation at its timestamp; routine frames are sampled every 30 seconds, with exact mechanism frames. Never animate interpolation as if it were measured tracking.

## Planned contracts and release gates

Azure live stream requires authenticated SignalR negotiation, authorized match groups and versioned messages with `matchId`, sequence, revision, schema version and UTC publication time. A reconnect cursor triggers canonical backfill; historical selected time remains independent of live cursor. Ingestion/admin endpoints require separate permissions, stable payload hashes and explicit quarantine/reconciliation. PostgreSQL-backed retrieval must preserve selected-time behavior demonstrated by local contract tests. Publish precise schemas and authorization tests before marking EP06-S02/S03/S04 done.
