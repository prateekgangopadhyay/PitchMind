# C4 Level 3 — Components, domain purity and realization map

**CURRENT** sections document the source-verified local implementation; **TARGET** sections describe proposed ports and adapters, not an integrated runtime.

**Authority:** [Football LLD](07-football-analytics-detailed-lld.md), [API](../api/API.md), [Architecture review](../engineering/ARCHITECTURE-REVIEW-2026-10-08.md). Current runtime and target ports are intentionally separated to avoid asserting unavailable integrations.

## Realized application components (source verified, not new test evidence)

```mermaid
flowchart TB
  API["PitchMind.Api Program.cs / HTTP routes"] --> Profile["RuntimeProfileGuard: local in Development; azure in supported hosts"]
  API --> Guard["Ordinary match request → 503; malformed requests → typed 4xx; body capped at 4 KiB including chunked input"]
  API --> Contracts["PitchMind.Contracts — versioned transport DTOs"]
  Test["PitchMind.TestFixtures — fictional golden fixtures"] -. "test-only reference" .-> AppTests["Application tests"]
  Ledger["PostgresMatchEventStore"] --> DB[("Local PostgreSQL event ledger")]
  ProjectionRead["PostgresMatchProjectionStore: source metadata + projection/provenance read"] --> DB
  CanonicalWrite["PostgresCanonicalUpdateStore: event + projection + outbox transaction"] --> DB
  PointInTime["Internal Application: GetMatchAtQueryService"] --> ReplayReader["PostgresMatchProjectionReader: projection + replay envelope + bounded frames"] --> DB
  Migrator["PostgresSchemaMigrator"] --> DB
  Candidate["Candidate JSON"] -. "raw schema only" .-> RawReader["ManuLivCandidateFeedReader"]
  RawReader -. "not connected to canonical admission" .-> Boundary["Mapping blocked: source semantics unverified"]
  EventWorker["EventWorker scaffold (idle)"]
  AgentWorker["AgentWorker scaffold (idle)"]
```

The profile guard rejects selecting local-only adapters outside Development while allowing an explicit Azure profile in Development, Staging, or Production. The in-memory event store is registered only for the local profile; Azure startup does not fall back to it. This does not configure Azure authentication, managed identity, or canonical service adapters. The event store is a separately exercised adapter; current API paths do **not** consume it. The PostgreSQL projection and canonical-write stores and the point-in-time query/reader are implementation components but are not registered in API/EventWorker composition; the diagram does not imply a live request path. `EventWorker`, `AgentWorker` and Azure adapters must not be described as production-enabled. `ManuLivCandidateFeedReader` computes the exact source SHA-256 and parses source identity/order/time labels while retaining each original JSON record. It does not map event types, convert coordinates or clocks, persist data, or enable API reads; it is not wired into runtime composition. The candidate therefore still does not drive canonical match service behavior.

## Target Ports and Adapters

```mermaid
flowchart TB
  subgraph Domain["PitchMind.Domain — zero external dependencies"]
    MatchId["Match / Period / Team / Player / Clock"]
    Facts["Canonical event / tracking / possession"]
    Rules["Phase-aware tactical detectors"]
    Evidence["Typed evidence graph / truth classification"]
  end
  subgraph App["PitchMind.Application — use cases and ports"]
    Validate["Normalize / Admit / Quarantine"]
    Project["Project match / score / active roster"]
    Detect["Detect mechanism / key moment"]
    Query["SelectedTime / ReplayAt"]
    Explain["Grounded explanation policy"]
    Ports["IEventLedger / IMatchProjectionStore / IOutbox / IModelClient / IRealtimeNotifier"]
    CanonicalPorts["ICanonicalUpdateStore / IOutboxPublisher / consumer inbox"]
  end
  subgraph Infra["PitchMind.Infrastructure — Azure/local adapters"]
    Feed["JSON source adapter / manifest"]
    Pg["PostgreSQL schema / checkpoint / outbox"]
    EventHubs["Event Hubs publisher/consumer"]
    Bus["Service Bus + DLQ"]
    Hub["Azure SignalR"]
    Model["Microsoft Foundry client"]
    Cache["Redis (optional)"]
  end
  BFF["Thin API / role + selected-time guards"] --> Query
  EventWorker2["EventWorker host"] --> Validate
  AgentWorker2["AgentWorker host"] --> Explain
  Feed -. "raw candidate only; never canonical publication" .-> MapSource["Reviewed source mapper"]
  MapSource --> Validate
  Validate --> Admission["PostgreSQL canonical admission + revision + outbox in one transaction"]
  Admission --> EventHubs
  EventHubs --> ValidateProjection["Consume admitted telemetry for projections"]
  ValidateProjection --> Facts
  Facts --> Project --> MatchId
  Project --> Detect --> Rules
  Detect --> Evidence
  Query --> Ports
  Explain --> Ports
  Pg -. "implements" .-> Ports
  Pg -. "implements canonical update ports" .-> CanonicalPorts
  Pg -. "leases outbox rows" .-> OutboxDispatcher["PostgresOutboxDispatcher"]
  OutboxDispatcher -. "publishes through" .-> PublisherPort["IOutboxPublisher"]
  Pg -. "implements consumer inbox" .-> CanonicalPorts
  Bus -. "implements" .-> Ports
  Hub -. "implements" .-> Ports
  Model -. "implements" .-> Ports
  Cache -. "optional" .-> Ports
```

## Runtime, data and code invariants
- Pure Domain may not depend on Application, Infrastructure, Azure SDK, Npgsql or React. Infrastructure handles JSON source vocabulary, clock/units conversion, source record pointers, nullable coordinates and bad input quarantine.
- Raw candidates carry source hash, original bytes/IDs/order and raw clock labels; they have no `canonicalRevision` and are not canonical envelopes. Only PostgreSQL assigns a positive per-match `canonicalRevision` atomically with an admitted record and its outbox intent. `EventEnvelope` and `TrackingFrame` then carry the source/fixture hash, original sequence and explicit canonical index, period/elapsed time, pitch units/orientation, and observed-vs-interpolated coverage.
- PostgreSQL commits validated event, tracking reference, projection, evidence and outbox atomically; checkpoint follows durable commit; repeated delivery is safe.
- `MatchTimeContext` fixes match, selected timestamp, revision and period across API, pitch, scoreboard, events, claims and AI.
- Foundry receives read-only evidence scoped to selected time; separate deterministic validator checks every published claim; malformed model output falls back.
- React gets data via BFF; no clubs, players, scores, tactical observations or event-specific narrative hardcoded in `*.cs` or `*.tsx` after feed integration.
- **Every actual change** to an aggregate, port, trust boundary or deployment needs an updated diagram/ADR/API and contract tests in the same PR. Docs do not imply feature acceptance or authorize Azure DEV mutation.
