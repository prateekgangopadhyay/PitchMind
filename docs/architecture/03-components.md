# C4 Level 3 — Components, domain purity and realization map

**CURRENT** sections document the source-verified local implementation; **TARGET** sections describe proposed ports and adapters, not an integrated runtime.

**Authority:** [Football LLD](07-football-analytics-detailed-lld.md), [API](../api/API.md), [Architecture review](../engineering/ARCHITECTURE-REVIEW-2026-10-08.md). Current runtime and target ports are intentionally separated to avoid asserting unavailable integrations.

## Realized application components (source verified, not new test evidence)

```mermaid
flowchart TB
  API["PitchMind.Api Program.cs / HTTP routes"] --> Match["SyntheticMatchService"]
  API --> CanonicalRoutes["/api/v1/canonical BFF route group"]
  CanonicalRoutes --> ProjectionPort["Application IMatchProjectionReader"]
  CanonicalRoutes --> InsightPort["Application IMatchInsightReader"]
  ProjectionPort --> ProjectionReader["PostgresMatchProjectionReader"]
  InsightPort --> InsightReader["PostgresMatchInsightReader"]
  ProjectionReader --> DB[("PostgreSQL canonical ledger, projections, insights and outbox")]
  InsightReader --> DB
  Match --> Aggregate["MatchAggregate deterministic ordered-event reducer"]
  Aggregate --> State["score, phase, possession, active roster, cautions"]
  Match --> Fixture["SyntheticFixtureGenerator, embedded synthetic-v2 JSON"]
  Match --> Press["Domain PressSequenceDetector (versioned unapproved demo policy)"]
  Press --> Graph["TacticalEvidenceGraphBuilder + validator"]
  Graph --> Insight["Typed claim/event/frame nodes + evidence edges"]
  Match --> Replay["Selected-time projection/evidence/fallback"]
  Ledger["PostgresMatchEventStore"] --> DB
  CanonicalWrite["PostgresCanonicalUpdateStore (event + projection + outbox transaction)"] --> DB
  Dispatcher["PostgresOutboxDispatcher (leased at-least-once delivery)"] --> Publisher["IOutboxPublisher port (transport adapter not registered)"]
  Inbox["PostgresConsumerInbox (same-database idempotent effects)"] --> DB
  Migrator["PostgresSchemaMigrator"] --> DB
  EventWorker["EventWorker scaffold (idle)"]
  AgentWorker["AgentWorker scaffold (idle)"]
```

The fixture generator reads the fictional `data/simulations/synthetic-fixture-v2.json` definition as an embedded versioned resource; team/player/scenario facts are not literals in production C#. `SyntheticMatchService` folds the selected-time event prefix through the pure `MatchAggregate`, which enforces ordered sequence, 11-per-team initial roster, participant, period, score, card, substitution and terminal-state rules. Its bounded insight path calls the Domain `PressSequenceDetector` with that same event prefix, selected time/through-sequence, fixture version, pitch dimensions, projected selected-time score/roster/phase context, and available frames. The response context states scoreline, home/away role, active-player difference, match phase and the observed opponent possession transition; it does not infer a playing style, intent or superiority. The detector builds a typed evidence graph with ordered event nodes, frame nodes, a pattern claim, a reducer-derived context metric, `precedes` and `derived_from` edges, and a validator that blocks unsupported publishable status. It emits no provider player-performance metric nodes. The only executable policy is `press-sequence-v2` with status `unapproved-demo`; its output is `simulated` and `publishable=false`. This fixture path remains separate from the canonical BFF routes. The PostgreSQL v3 schema adds lease/retry/dead-letter state, a projection FK for outbox records, and a consumer inbox. `PostgresCanonicalUpdateStore` commits event, revisioned state and outbox intent atomically; `PostgresOutboxDispatcher` uses stable IDs, leases and bounded retries, and emits local .NET metrics; `PostgresConsumerInbox` couples a same-database effect to its dedupe receipt. The canonical BFF uses `IMatchProjectionReader` and `IMatchInsightReader`: `PostgresMatchProjectionReader` verifies projection hash/metadata and bounds cursor pages by selected time and sequence; `PostgresMatchInsightReader` returns stored insights only when every event/frame reference resolves by both selected clock and projection sequence, and serves complete persisted event/frame payloads from `/evidence/{insightId}`. With no configured canonical adapter, canonical routes return 503 and do not use the fixture. Canonical key moments remain unavailable because there is no persisted key-moment read model. Authentication/authorization is a separate EP06-S04 gate, so these routes must not be exposed through a cloud ingress until that control is implemented or an approved public-synthetic boundary exists. `EventWorker`, `AgentWorker` and Azure adapters must not be described as production-enabled. The candidate `data/simulations/manu-liv-match-dataset.json` still does not drive the C# service.

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
    Ports["IMatchEventStore / ICanonicalUpdateStore / IOutboxPublisher / inbox transaction / model + realtime ports"]
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
  Feed --> Validate
  EventHubs --> Validate
  Validate --> Facts
  Facts --> Project --> MatchId
  Project --> Detect --> Rules
  Detect --> Evidence
  Query --> Ports
  Explain --> Ports
  Pg -. "implements event and canonical update ports" .-> Ports
  Pg -. "leases outbox rows" .-> OutboxDispatcher["PostgresOutboxDispatcher"]
  OutboxDispatcher -. "publishes through" .-> PublisherPort["IOutboxPublisher"]
  Pg -. "implements consumer inbox" .-> Ports
  Bus -. "implements" .-> Ports
  Hub -. "implements" .-> Ports
  Model -. "implements" .-> Ports
  Cache -. "optional" .-> Ports
```

## Runtime, data and code invariants
- Pure Domain may not depend on Application, Infrastructure, Azure SDK, Npgsql or React. Infrastructure handles JSON source vocabulary, clock/units conversion, source record pointers, nullable coordinates and bad input quarantine.
- `EventEnvelope` and `TrackingFrame` carry source/fixture hash, 1-based original sequence and explicit canonical index, period/elapsed time, pitch units/orientation, observed-vs-interpolated coverage.
- PostgreSQL commits validated event, tracking reference, projection, evidence and outbox atomically; checkpoint follows durable commit; repeated delivery is safe.
- `MatchTimeContext` fixes match, selected timestamp, revision and period across API, pitch, scoreboard, events, claims and AI.
- Foundry receives read-only evidence scoped to selected time; separate deterministic validator checks every published claim; malformed model output falls back.
- React gets data via BFF; no clubs, players, scores, tactical observations or event-specific narrative hardcoded in `*.cs` or `*.tsx` after feed integration.
- **Every actual change** to an aggregate, port, trust boundary or deployment needs an updated diagram/ADR/API and contract tests in the same PR. Docs do not imply feature acceptance or authorize Azure DEV mutation.
