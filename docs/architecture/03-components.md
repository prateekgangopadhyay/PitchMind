# C4 Level 3 — Components, domain purity and realization map

**CURRENT** sections document the source-verified local implementation; **TARGET** sections describe proposed ports and adapters, not an integrated runtime.

**Authority:** [Football LLD](07-football-analytics-detailed-lld.md), [API](../api/API.md), [Architecture review](../engineering/ARCHITECTURE-REVIEW-2026-10-08.md). Current runtime and target ports are intentionally separated to avoid asserting unavailable integrations.

## Realized application components (source verified, not new test evidence)

```mermaid
flowchart TB
  API["PitchMind.Api Program.cs / HTTP routes"] --> Match["SyntheticMatchService"]
  Match --> Aggregate["MatchAggregate deterministic ordered-event reducer"]
  Aggregate --> State["score, phase, possession, active roster, cautions"]
  Match --> Fixture["SyntheticFixtureGenerator, embedded synthetic-v2 JSON"]
  Match --> Press["PressingSequenceDetector"]
  Match --> Replay["Selected-time projection/evidence/fallback"]
  Ledger["PostgresMatchEventStore"] --> DB[("Local PostgreSQL canonical ledger")]
  ProjectionRead["PostgresMatchProjectionStore: metadata + projection/provenance read"] --> DB
  CanonicalWrite["PostgresCanonicalUpdateStore (event + projection + outbox transaction)"] --> DB
  PointInTime["Application: GetMatchAtQueryService: selected state + detector replay"] --> ReplayReader["Infrastructure: PostgresMatchProjectionReader: event envelopes + versioned context + bounded frames"] --> DB
  Dispatcher["PostgresOutboxDispatcher (leased at-least-once delivery)"] --> Publisher["IOutboxPublisher port (transport adapter not registered)"]
  Inbox["PostgresConsumerInbox (same-database idempotent effects)"] --> DB
  Migrator["PostgresSchemaMigrator"] --> DB
  EventWorker["EventWorker scaffold (idle)"]
  AgentWorker["AgentWorker scaffold (idle)"]
```

The fixture generator reads the fictional `data/simulations/synthetic-fixture-v2.json` definition as an embedded versioned resource; team/player/scenario facts are not literals in production C#. `SyntheticMatchService` folds the selected-time event prefix through the pure `MatchAggregate`, which enforces ordered sequence, 11-per-team initial roster, participant, period, score, card, substitution and terminal-state rules. The PostgreSQL v4 migration adds the projection-to-source provenance foreign key while preserving v3 canonical admission revisions. The v5 migration adds outbox lease/retry/dead-letter state and a consumer inbox. Schema v6 adds nullable replay seed and source-event envelope columns; v7 adds nullable versioned replay context without inventing values for legacy rows. `PostgresMatchProjectionReader.ReadReplaySnapshotAtAsync` returns the selected projection, event envelopes, replay seed/context and tracking frames from one SQL statement, so a replay sees one PostgreSQL MVCC snapshot. The selected frame set is bounded by canonical revision and elapsed clock; each frame retains its independent sequence (same-clock samples are valid), period, metric coordinate version and source hash, and the payload ID must match the indexed frame row ID. The replay context's fixture version, provenance and pitch dimensions must match the registered match metadata. `GetMatchAtQueryService` replays the pure aggregate and verifies the persisted state digest, then executes the exact requested supported detector version (`press-sequence-v1`) against those same-time inputs and returns input/output digests with ordered event/frame evidence IDs. It checks roster activity at each frame time, including substitutions and dismissals, and uses period two at the halftime clock. Additional same-clock frames are allowed when they do not make the press-arrival evidence ambiguous. Missing context, frames, metric coordinate semantics, active-player mappings or source lineage fails closed. This is an internal Application query, is not persisted as a derived-output canonical fact, and is not registered in API/EventWorker runtime. `PostgresMatchProjectionStore` records immutable source metadata and reads persisted projection state and insight provenance, checking hashes and selected-sequence bounds. `PostgresCanonicalUpdateStore` verifies aggregate predecessor state hash/clock/sequence and immutable source identity against persisted history, then commits event, canonical/projection revisioned state and outbox intent atomically; `PostgresOutboxDispatcher` provides leased at-least-once publication with bounded retries; `PostgresConsumerInbox` couples same-database effects to dedupe receipts. These infrastructure primitives are not registered in the API/EventWorker runtime; there is no Azure broker publisher, metrics exporter/alert, or projection API read path. Competition linkage, source import and persisted correction workflows remain unimplemented. The candidate `data/simulations/manu-liv-match-dataset.json` still does not drive the C# service.

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
  Feed --> Validate
  EventHubs --> Validate
  Validate --> Facts
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
- `EventEnvelope` and `TrackingFrame` carry source/fixture hash, 1-based original sequence and explicit canonical index, period/elapsed time, pitch units/orientation, observed-vs-interpolated coverage.
- PostgreSQL commits validated event, tracking reference, projection, evidence and outbox atomically; checkpoint follows durable commit; repeated delivery is safe.
- `MatchTimeContext` fixes match, selected timestamp, revision and period across API, pitch, scoreboard, events, claims and AI.
- Foundry receives read-only evidence scoped to selected time; separate deterministic validator checks every published claim; malformed model output falls back.
- React gets data via BFF; no clubs, players, scores, tactical observations or event-specific narrative hardcoded in `*.cs` or `*.tsx` after feed integration.
- **Every actual change** to an aggregate, port, trust boundary or deployment needs an updated diagram/ADR/API and contract tests in the same PR. Docs do not imply feature acceptance or authorize Azure DEV mutation.
