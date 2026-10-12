# PitchMind — Consolidated API Contract and Implementation Registry
**Current baseline:** EP01-S04 contract work in progress on 2026-10-08. **Design target:** proposed `v1` extension, **NOT implemented** unless explicitly marked `CURRENT`. Source files: `src/PitchMind.Api/Program.cs`, `src/PitchMind.Contracts/V1/MatchContracts.cs`, `frontend/src/api.ts`. No missing API is assumed working because it appears here.

## Principles
Public route prefix `/api/v1`; JSON camelCase; timestamps match selected-time contract, period/added-time explicit when supported; football data exclusively from versioned synthetic JSON/authoritative projections; `X-Correlation-ID` returned in every response. All read responses include `provenance`, `sourceVersion/hash`, `canonicalRevision`, `schemaVersion` and `asOf` **once implemented**. Never leak events later than `atSeconds`. JWT with scoped roles for nonpublic and mutation routes. Bounded queries, stable cursor pagination, ProblemDetails on errors. For prototype current endpoints `atSeconds` integer 0..5400, default 0; do not extrapolate to added-time games. Revisions with `ETag` / `If-None-Match` proposed.

### Current executable API routes
| Method | Route | State | Reality and notes |
|---|---|---|---|
| GET | `/api/health` | **CURRENT** | Process identification, not readiness of external dependencies |
| GET | `/health` | **CURRENT** | ASP.NET health, no Azure dependency probe |
| GET | `/api/v1/matches` | **MAPPED / UNAVAILABLE** | Returns `503 MATCH_DATA_UNAVAILABLE` until API route composition is replaced with a canonical, revisioned PostgreSQL-backed read port |
| GET | `/api/v1/matches/{matchId}/state?atSeconds=N` | **MAPPED / UNAVAILABLE** | Returns `503 MATCH_DATA_UNAVAILABLE` until API route composition is replaced with a canonical, revisioned PostgreSQL-backed read port |
| GET | `/api/v1/matches/{matchId}/events?atSeconds=N` | **MAPPED / UNAVAILABLE** | Returns `503 MATCH_DATA_UNAVAILABLE` until API route composition is replaced with a canonical, revisioned PostgreSQL-backed read port |
| GET | `/api/v1/matches/{matchId}/insights?atSeconds=N` | **MAPPED / UNAVAILABLE** | Returns `503 MATCH_DATA_UNAVAILABLE` until API route composition is replaced with a canonical, revisioned PostgreSQL-backed read port |
| GET | `/api/v1/matches/{matchId}/evidence/{insightId}?atSeconds=N` | **MAPPED / UNAVAILABLE** | Returns `503 MATCH_DATA_UNAVAILABLE` until API route composition is replaced with a canonical, revisioned PostgreSQL-backed read port |
| POST | `/api/v1/matches/{matchId}/analyst` | **MAPPED / UNAVAILABLE** | Accepts `AnalystRequest` below; valid requests return `503 MATCH_DATA_UNAVAILABLE` until API composition uses a canonical, revisioned PostgreSQL-backed read port; rate limiting may return `429` before validation |

Only `/api/health` and `/health` currently return successful process health. Match routes are mapped but fail closed because the synthetic scenario generator was moved out of production assemblies and no canonical feed adapter is registered. Ordinary match reads and valid Analyst requests return typed `503 MATCH_DATA_UNAVAILABLE`; the analyst endpoint returns typed `429 RATE_LIMITED` after its configured quota. The Analyst handler validates JSON media type, malformed JSON, required fields, `atSeconds` bounds and the audience enum. Requests above the 4096-byte Kestrel limit return typed `413 REQUEST_TOO_LARGE`, including chunked requests; unsupported content type returns typed `415 UNSUPPORTED_MEDIA_TYPE`. API problem responses include the v1 error code and correlation ID and do not expose framework exception messages. Successful `/api/v1` responses include `X-Contract-Version: 1`; all responses include `X-Correlation-ID`. The previous test-only fixture is not exposed as canonical data. C# DTOs remain in `PitchMind.Contracts.V1`; the React consumer still loads only through `request()` and has no production fallback facts. Startup accepts `Runtime:Profile=local` only with the `Development` environment and accepts the `azure` profile in supported host environments; the in-memory event store is registered only with the local profile. These profile checks do not replace authentication, identity, or Azure adapter configuration.

The current `POST /api/v1/matches/{matchId}/analyst` body is `{ "insightId": "…", "atSeconds": 0, "audience": "fan" }`. `insightId` must be a nonempty string, `atSeconds` an integer from 0 through 5400 inclusive, and `audience` exactly `fan` or `analyst`. A valid request still returns unavailable until a canonical read port exists; the API does not fabricate an explanation.

The current v1 DTO property names and representative JSON types are pinned by `PitchMind.Contracts.Tests`. `scripts/api-contract-integration.py` runs the live HTTP route, typed error-envelope, Analyst validation/body-size, and rate-limit checks against a fresh loopback API process; local validation and the Azure Pipelines .NET job invoke it after the Release build. This verifies the current fail-closed prototype contract and does not establish a canonical read path or a hosted pipeline run. `MatchContractMappings` maps application read models to public response DTOs when canonical read-port routes are implemented. The React test fetch stubs and C# golden fixture generator are test-only; production UI data is loaded through `request()` and never falls back to those examples.

### Target API routes — do not advertise as executable before acceptance
| Method | Route | Story owner | Request | Response / authorization |
|---|---|---|---|---|
| GET | `/api/v1/competitions` | EP07-S02 | `season?,cursor?,limit?` | synthetic competition summaries, anonymous demo or fan |
| GET | `/api/v1/fixtures` | EP06-S01/EP07-S02 | `competitionId?,status?,cursor?,limit?` | bounded fixture cards, version/hash, team/source rights |
| GET | `/api/v1/matches/{id}/summary` | EP06-S01 | `atSeconds?,revision?` | state + leading factual story + Key Moment summary, same selected time |
| GET | `/api/v1/matches/{id}/frames` | EP07-S04 | `fromMs,toMs,limit,cursor?` | observed tracking samples and quality; never invented positions |
| GET | `/api/v1/matches/{id}/events` | EP06-S01 | `fromMs?,toMs?,cursor?,limit?,revision?` | versioned pagination, ordered match events with provenance |
| GET | `/api/v1/matches/{id}/insights` | EP05-S04 | `atSeconds,category?,truthClass?,cursor?,limit?` | evidence-classified supported tactical insights |
| GET | `/api/v1/matches/{id}/key-moments` | EP05-S05 | `atSeconds?,limit?` | ranked validated moments with evidence IDs and score impact |
| GET | `/api/v1/matches/{id}/evidence/{insightId}` | EP07-S05 | `atSeconds,revision?,cursor?` | immutable event/frame/metric refs + conditions/missingness |
| GET | `/api/v1/matches/{id}/timeline/backfill` | EP06-S03 | `afterSequence,revision?,limit?` | canonical ordered catch-up, nextCursor/hasMore |
| POST | `/api/v1/realtime/negotiate` | EP06-S02 | `{matchId,clientRevision?}` | authorized short-lived Azure SignalR connection details |
| GET | `/api/v1/teams` | EP07-S01/S02 | `competitionId?,cursor?,limit?` | actual synthetic fixture team catalogue, never source-code clubs |
| GET | `/api/v1/teams/{id}/profile` | EP07-S06 | `window?,asOf?` | evidence-backed tactics, formations as nominal metadata only |
| GET | `/api/v1/players` | EP07-S06 | `teamId?,cursor?,limit?` | synthetic/rights-cleared participant list |
| GET | `/api/v1/players/{id}/impact` | EP07-S06 | `matchId,atSeconds?` | computed touches/carries/observed event contributions or unavailable |
| POST | `/api/v1/analyst/questions` | EP08-S06 | `{matchId,atSeconds,revision,audience,question,selectedInsightId?}` | structured validated answer, citations, limitations, model/fallback source; fan/analyst |
| GET | `/api/v1/analyst/questions/{requestId}` | EP08-S06 | `requestId` | bounded async status if model processing exceeds HTTP budget |
| POST | `/api/v1/replays` | EP04-S04 | `{matchId,revision,fromMs,toMs,speed}` | ephemeral session ID and source hash; no canonical writes |
| GET | `/api/v1/replays/{id}/snapshots` | EP04-S04 | `atMs,limit?` | deterministic versioned snapshots, auth by session owner |
| POST | `/api/v1/exports/evidence` | EP09-S04 | `{matchId,insightIds,format}` | authorized background job, retention/expiry, no raw secrets |
| GET | `/api/v1/exports/{jobId}` | EP09-S04 | `jobId` | status, signed short-lived link once ready; role and owner scoped |
| POST | `/internal/v1/ingestion/events` | EP03-S03 | authenticated envelope batch | **internal only**; idempotency and quarantine; not public browser route |
| GET | `/internal/v1/ingestion/quarantine` | EP03-S04 | bounded match/filter | admin restricted; reason codes and source pointers |
| GET | `/api/v1/search/evidence` | EP12-S01 | filters,cursor | P2 indexed authorized evidence, canonical links |

New required routes are **proposals pending exact JSON schema and actual implementation**. User interface cards must render unavailable states until underlying data and algorithms exist.

## Envelope and DTO contracts — proposed additions
`MatchTimeContext`: `{matchId,mode:'liveHead'|'selectedTime',selectedElapsedMs,period,contextRevision,canonicalRevision}`.
`Snapshot`: `{matchId,asOfElapsedMs,period,score:{home,away},status,activePlayers,frameRef?,canonicalRevision,provenance,sourceHash,coverage}`.
`InsightSummary`: `{insightId,matchId,asOfElapsedMs,truthClass,algorithmVersion,summary,conditionResults,evidenceRefs,limitations,revision}`.
`EvidenceRef`: `{kind:'event'|'frame'|'metric'|'condition',id,matchId,elapsedMs,sourceVersion,algorithmVersion,canonicalRevision}`.
`ValidatedExplanation`: `{answerId,selectedTime,audience,source:'foundry-validated'|'deterministic-fallback',text,claims:[{text,evidenceIds}],limitations,revision,modelVersion?,traceId}`.
`RealtimeCursor`: `{kind:'matchUpdated',matchId,canonicalRevision,lastSequence,serverUtc,schemaVersion}`; **payload does not carry canonical truth**.
`EventEnvelope` and `TrackingFrame`: [simulation feed contract](../data/SIMULATION-FEED-CONTRACT.md); hash, coordinates, provenance, quality and period mandatory where applicable.

## Synchronization and pagination example
1. UI chooses selected 55:08; all page queries add `atSeconds=3308` and match/contract revision. A 55:09 goal, its tactical insight, evidence and AI explanation cannot appear in this view.
2. Live cursor `lastSequence=N+1` arrives while user scrubs; show "New match update — return to live" but keep `SelectedTime`; fetch on explicit click only.
3. Historical snapshot `revision=R` and evidence `revision=R` must match. On 409 revision conflict, refetch atomic snapshot, invalidate stale queries by clientContextRevision and rerender after new evidence resolves.
4. Pagination: stable `(matchId,revision,sequence)` cursor; max proposed 100 events per page; endpoint rejects excessive unbounded requests with 400 or 413; complete data not duplicated/omitted across pages.

## Errors, security and evolution
`application/problem+json` with `type,title,status,detail,instance,correlationId` and typed `code`: `INVALID_TIME`, `SCHEMA_UNSUPPORTED`, `EVIDENCE_MISSING`, `REVISION_CONFLICT`, `INSUFFICIENT_TRACKING`, `UNAUTHORIZED`, `FORBIDDEN`, `RATE_LIMITED`, `REQUEST_TOO_LARGE`, `UNSUPPORTED_MEDIA_TYPE`, `DEPENDENCY_UNAVAILABLE`. Current verified validation, evidence, size and rate-limit failures use `INVALID_REQUEST`, `EVIDENCE_MISSING`, `REQUEST_TOO_LARGE`, `UNSUPPORTED_MEDIA_TYPE` and `RATE_LIMITED`. Use 400 for validation, 401 unauthenticated, 403 denied, 404 inaccessible/unknown, 409 revision/conflict, 413 oversized, 415 unsupported media type, 422 semantically invalid envelope, 429 exhausted rate, 503 unavailable. No sensitive provider payload in error detail.
Authentication target: Entra/OIDC auth code+PKCE; strong JWT issuer/audience/expiration signing key validation; scoped roles fan/analyst/admin; anonymous access only an owner-approved synthetic demo profile. Mutations require appropriate role, anti-CSRF where using cookies, replay/export quotas, strict CORS/CSP, no client-side Foundry credentials.
API version `v1` is additive compatible. Breaking schema/meaning change = `v2` and migration plan. Generated OpenAPI document and typed React/.NET contract tests should be published in CI. Every newly implemented endpoint must update this file and receive positive, negative, historical, authorization, rate-limit, failure and cursor tests **in the same PR**.

## Supplied source feed compatibility gap (2026-10-08)
The user-supplied [Manchester United–Liverpool source profile](../data/MANU-LIV-SOURCE-PROFILE.md) shows source `sequence` **1..2199**, `clockSeconds` **0..5761** with added time, **19 event types** versus the current 8-value `MatchEventType` enum, and **0–100-looking coordinates** with unknown units. The owner has confirmed synthetic provenance and redistribution permission, but clock policy, coordinate units/orientation, player metric meaning, canonical event mappings and football realism remain unverified. The old generator is test-only; ordinary match reads return `503 MATCH_DATA_UNAVAILABLE` (the analyst endpoint may return `429` when rate-limited before its handler) until route composition is replaced with a canonical, revisioned PostgreSQL-backed read port and the input gates pass. Adapter and API changes need versioned contracts and regression tests; missing coverage remains unavailable.
