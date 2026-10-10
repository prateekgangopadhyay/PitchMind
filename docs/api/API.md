# PitchMind — Consolidated API Contract and Implementation Registry
**Current baseline:** EP01-S04 contract work in progress on 2026-10-08. **Design target:** proposed `v1` extension, **NOT implemented** unless explicitly marked `CURRENT`. Source files: `src/PitchMind.Api/Program.cs`, `src/PitchMind.Contracts/V1/MatchContracts.cs`, `frontend/src/api.ts`. No missing API is assumed working because it appears here.

## Principles
Public route prefix `/api/v1`; JSON camelCase; timestamps match selected-time contract, period/added-time explicit when supported; football data exclusively from versioned synthetic JSON/authoritative projections; `X-Correlation-ID` returned in every response. All read responses include `provenance`, `sourceVersion/hash`, `canonicalRevision`, `schemaVersion` and `asOf` **once implemented**. Never leak events later than `atSeconds`. JWT with scoped roles for nonpublic and mutation routes. Bounded queries, stable cursor pagination, ProblemDetails on errors. For prototype current endpoints `atSeconds` integer 0..5400, default 0; do not extrapolate to added-time games. Revisions with `ETag` / `If-None-Match` proposed.

### Current executable API routes
| Method | Route | State | Reality and notes |
|---|---|---|---|
| GET | `/api/health` | **CURRENT** | Process identification, not readiness of external dependencies |
| GET | `/health` | **CURRENT** | ASP.NET health, no Azure dependency probe |
| GET | `/api/v1/matches` | **CURRENT** | One entire offline `FixtureResponse` (`schemaVersion` is not claimed); unpaginated; returns events/frames as part of fixture |
| GET | `/api/v1/matches/{matchId}/state?atSeconds=N` | **CURRENT** | Deterministic event-prefix projection: score, phase/period, observed possession, active roster, yellow-card counts and selected sampled frame |
| GET | `/api/v1/matches/{matchId}/events?atSeconds=N` | **CURRENT** | Time-bounded offline synthetic event wrapper array |
| GET | `/api/v1/matches/{matchId}/insights?atSeconds=N` | **CURRENT** | At most one simulated pressure-sequence candidate after its full outcome; `press-sequence-v2` is explicitly unapproved and non-publishable |
| GET | `/api/v1/matches/{matchId}/key-moments?atSeconds=N` | **CURRENT** | Deterministic offline relevance ranking of recorded goals/cards/shots and the simulated tactical pattern. Response carries source/provenance, selected time/sequence, policy version/status and event/frame/claim references. `key-moment-v1-demo` is unapproved and every item is non-publishable; routine passes are omitted and nearby shots are clustered in a policy window. |
| GET | `/api/v1/matches/{matchId}/evidence/{insightId}?atSeconds=N` | **CURRENT** | Selected-time evidence conditions, 7 event IDs, frame IDs and limitations |
| POST | `/api/v1/matches/{matchId}/analyst` | **CURRENT** | `{insightId,atSeconds,audience:'fan'|'analyst'}`; deterministic-fallback ONLY, 30/IP/min and 4KiB body cap |

These are from the offline fixture, **not** PostgreSQL, Azure Event Hubs, Microsoft Foundry or authenticated SignalR. Successful `/api/v1` responses include `X-Contract-Version: 1` and all responses include `X-Correlation-ID`. Error bodies use `application/problem+json` with `type,title,status,code,correlationId` (plus optional `detail,instance`); the analyst limiter/framework body-size rejection retain framework-generated failure bodies until their dedicated API stories. Current `atSeconds` 0..5400; period changes come from the fixture's kickoff, halftime and full-time events. C# DTOs are in `PitchMind.Contracts.V1`; React consumer interfaces are in `frontend/src/api.ts`. Football DTOs expose `canonicalRevision` as null for this demo; durable PostgreSQL revisions and correction history are not implemented.

`key-moments` scores events with configurable, versioned demo weights and exponential time decay. The default policy clusters shots from the same team when consecutive shots are within 45 seconds; goals and cards remain individual moments. A tactical item retains event/frame evidence references and its evidence-graph claim node. The source does not define an approved “major chance” signal, so a recorded shot is not upgraded to one. This is a deterministic prototype contract, not a Product Owner-approved notification policy.

### Canonical BFF routes (EP06-S01 implementation; acceptance pending)

These separately namespaced routes read PostgreSQL canonical projections and persisted evidence. They never fall back to the offline fixture. If a PostgreSQL read adapter is not configured or the database is unavailable, the route returns a sanitized `503 DEPENDENCY_UNAVAILABLE`. The routes are not authenticated yet; cloud exposure is blocked pending EP06-S04 or an explicitly approved synthetic public-read boundary.

| Method | Route | State | Reality and notes |
|---|---|---|---|
| GET | `/api/v1/canonical/fixtures?afterMatchId&limit` | **IMPLEMENTED / UNVERIFIED** | Latest persisted projection cards; `limit` 1..100. |
| GET | `/api/v1/canonical/matches/{matchId}/summary?atSeconds&revision` | **IMPLEMENTED / UNVERIFIED** | Selected-time persisted state, source/state hashes and revision. `atSeconds` 0..5400. |
| GET | `/api/v1/canonical/matches/{matchId}/events?atSeconds&revision&cursor&limit` | **IMPLEMENTED / UNVERIFIED** | Ledger events ordered by original sequence, bounded by selected time and projection sequence; cursor bound to match, time and revision. |
| GET | `/api/v1/canonical/matches/{matchId}/insights?atSeconds&revision&cursor&limit` | **IMPLEMENTED / UNVERIFIED** | Persisted insight records only when every evidence reference resolves to a selected-time event/frame. Other evidence kinds are suppressed until their source is resolvable. |
| GET | `/api/v1/canonical/matches/{matchId}/insights/{insightId}?atSeconds&revision` | **IMPLEMENTED / UNVERIFIED** | Same evidence and selected-time constraints as the insight list. |
| GET | `/api/v1/canonical/matches/{matchId}/evidence/{insightId}?atSeconds&revision` | **IMPLEMENTED / UNVERIFIED** | Persisted insight plus complete event/frame payloads, each bounded by selected time and projection sequence; incomplete evidence returns 404. |
| GET | `/api/v1/canonical/matches/{matchId}/replay?atSeconds&revision&cursor&limit` | **IMPLEMENTED / UNVERIFIED** | One selected-time snapshot plus a bounded page of ordered ledger events. |
| GET | `/api/v1/canonical/matches/{matchId}/frames?atSeconds&revision&limit` | **IMPLEMENTED / UNVERIFIED** | Exact-second observed tracking frames only, bounded by the selected projection's revision sequence; validates indexed identity/time/hash against `TrackingFrameV1`, including positive pitch dimensions no larger than 120×90 m; empty page means no sample at that second, and invalid/oversized stored data returns `503 TRACKING_DATA_INVALID`. No interpolation or future frame lookahead. Limit 1..25. |
| GET | `/api/v1/canonical/matches/{matchId}/key-moments?atSeconds` | **IMPLEMENTED / UNVERIFIED** | Deliberately returns `503 CANONICAL_KEY_MOMENTS_UNAVAILABLE`; the canonical database has no persisted key-moment projection. The fixture demo endpoint is not used as fallback. |

The machine-readable contract is [openapi-canonical-bff-v1.yaml](openapi-canonical-bff-v1.yaml). The canonical route DTOs are distinct from the fixture demo DTOs. The tracking route reads only rows already admitted to `pitchmind.tracking_frames`; it does not import `data/simulations/manu-liv-match-dataset.json`. Owner-declared synthetic provenance and redistribution permission are confirmed, while coordinate semantics, player metrics, canonical mappings, football realism and source admission remain under review. No source positions are displayed until an explicit validated `TrackingFrameV1` is persisted. This implementation is not accepted until PostgreSQL contract/integration tests, negative HTTP checks, independent review and Product Owner acceptance are recorded.

The canonical evidence response currently carries the persisted insight's typed event/frame references and resolved source records. It does not return insight `conditions`, stored `limitations`, or metric evidence payloads. Consumers must state those gaps and may not infer the absent data. Extending those response fields requires an explicit API and persistence contract change.

EP04-S04 provides the internal PostgreSQL projection reader for selected-time/revision-bounded snapshots and event windows. EP06-S01 maps separate canonical-prefixed routes to those read ports; the existing `/api/v1/matches` routes continue to serve the offline fixture only.

The current v1 DTO property names and representative JSON types are pinned by `PitchMind.Contracts.Tests`. `MatchContractMappings` is the only API mapping from the current application read models to public football response DTOs. `FixtureResponse.version` retains the source fixture version; API contract version is `X-Contract-Version`. `MatchStateResponse` adds phase, possession, active-player counts, and player active/dismissed/yellow-card state. It preserves the existing `status` values (`replay` and `full-time`) and adds `phase` values `notstarted`, `firsthalf`, `halftime`, `secondhalf`, and `fulltime`; same-clock events are applied in source sequence order and later events are excluded. Current offline frame DTOs explicitly identify their generated 105 × 68 metre coordinate convention; this does not certify the supplied candidate feed's units or mappings. The React test fetch stubs are test-only fixtures; production UI data is loaded through `request()` and never falls back to those examples.

Current insight DTOs include `sourceVersion`, nullable `canonicalRevision`, `selectedTimeSeconds`, `throughSequence`, `policyStatus`, `publishable`, and the detector's `conditions`. The offline demo returns `classification: "simulated"`, `policyStatus: "unapproved-demo"`, `publishable: false`, and `canonicalRevision: null`; this is not a canonical football fact, football approval, or an implementation of the candidate Manu–Liverpool feed. The detector only evaluates the selected-time prefix and reports the highest included event sequence. Do not relabel this response as evidence-supported, approved, or production-ready.

The current candidate insight also includes `context`: selected-time scoreline, pressing team's `home`/`away` role, signed active-player difference for that team, reducer match phase, and the observed opponent transition from in-possession at the trigger to defending after the regain. This uses the same selected-time event prefix as the score projection. It does not infer a low block, a decision to concede possession, intent, control, or comparative tactical quality. Context is null if absent; consumers must not substitute inferred values.

Current insights also carry a typed `evidenceGraph` with claim, event, frame, and metric nodes; truth classes include `fact`, `derivation`, `pattern`, `interpretation`, and `hypothesis`; edges use `precedes` and `derived_from`. The offline graph marks source event/frame nodes as facts only within the synthetic fixture; graph provenance remains `synthetic`, canonical revision is null, and the pattern claim is not publishable. A metric derivation node records selected-time context and links to the selected-prefix score, lineup, phase and detector events. Its `numericValue` is the signed active-player difference for the pressing team and its `unit` is `active-players`; it is reducer-derived context, not a provider performance metric. No player-performance metric nodes are emitted because none are validated. The builder rejects unresolved IDs and evidence outside the selected-time prefix. The validator refuses publishable status unless policy is approved and evidence is from a revisioned canonical source.

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
`application/problem+json` with `type,title,status,detail,instance,correlationId` and typed `code`: `INVALID_TIME`, `SCHEMA_UNSUPPORTED`, `EVIDENCE_MISSING`, `REVISION_CONFLICT`, `INSUFFICIENT_TRACKING`, `UNAUTHORIZED`, `FORBIDDEN`, `RATE_LIMITED`, `DEPENDENCY_UNAVAILABLE`. The currently mapped validation and evidence failures use `INVALID_REQUEST` and `EVIDENCE_MISSING`; middleware/framework-generated 413 and 429 responses are not yet normalized. Use 400 for validation, 401 unauthenticated, 403 denied, 404 inaccessible/unknown, 409 revision/conflict, 413 oversized, 422 semantically invalid envelope, 429 exhausted rate, 503 unavailable. No sensitive provider payload in error detail.
Authentication target: Entra/OIDC auth code+PKCE; strong JWT issuer/audience/expiration signing key validation; scoped roles fan/analyst/admin; anonymous access only an owner-approved synthetic demo profile. Mutations require appropriate role, anti-CSRF where using cookies, replay/export quotas, strict CORS/CSP, no client-side Foundry credentials.
API version `v1` is additive compatible. Breaking schema/meaning change = `v2` and migration plan. Generated OpenAPI document and typed React/.NET contract tests should be published in CI. Every newly implemented endpoint must update this file and receive positive, negative, historical, authorization, rate-limit, failure and cursor tests **in the same PR**.

## Supplied source feed compatibility gap (2026-10-08)
The user-supplied [Manchester United–Liverpool source profile](../data/MANU-LIV-SOURCE-PROFILE.md) shows source `sequence` **1..2199** (synthetic-v2 test fixture starts 0), `clockSeconds` **0..5761** with added time (current offline API clamps at 5400), **19 source event types** versus the current **11-value synthetic `MatchEventType` enum**, and **0–100-looking coordinates** rather than metres. The existing `SyntheticFixtureGenerator` and `SyntheticMatchService` MUST NOT be advertised as serving this feed. Adapter and API changes need versioned clock/coordinate/event type contracts and regression tests; missing coverage is expressed as unavailable.
