# C4 L3 — Application components

```mermaid
flowchart TB
  subgraph Domain["PitchMind.Domain (zero external dependencies)"]
    E[MatchEvent + TrackingFrame]
    M[MatchState / Possession / Phase]
    T[Tactical Mechanism Rules]
    V[Evidence Graph / Claim Validation Rules]
  end
  subgraph Application["PitchMind.Application"]
    I[IngestMatchEvent]
    P[ProjectMatchState]
    D[DetectTacticalMechanism]
    R[ReplayTimeline]
    X[GenerateGroundedExplanation]
    O[Outbox Dispatcher]
    Ports[Repository / Model / Publisher Ports]
  end
  subgraph Infrastructure["Adapters"]
    Pg[PostgresMatchEventStore / explicit schema migrator]
    Hubs[Event Hubs]
    Bus[Service Bus]
    Ai[Foundry]
    Redis[Managed Redis]
  end
  I --> E
  P --> M
  D --> T
  D --> V
  X --> V
  I --> P --> D
  R --> Ports
  X --> Ports
  O --> Ports
  Pg -. implements .-> Ports
  Hubs -. implements .-> Ports
  Bus -. implements .-> Ports
  Ai -. implements .-> Ports
  Redis -. implements .-> Ports
  Api[HTTP API] --> ReadModel[SyntheticMatchService]
  ReadModel --> Fixture[Seeded immutable fixture]
  ReadModel --> D
  PostgresLedger[PostgresMatchEventStore] -. implements IMatchEventStore .-> Ports
```

## Verified versus target paths (2026-10-08)

`PitchMind.Api` currently composes `SyntheticMatchService`, which reads the generated immutable fixture directly and provides selected-time state, events, insight, evidence and deterministic explanation. `SyntheticFixtureGenerator` uses seed/version-derived IDs and stable timestamps. `PressingSequenceDetector` is a bounded demonstration rule with metre coordinates and a same-period ordered event chain. This endpoint path does not call the event store.

Separately, `PostgresMatchEventStore` implements `IMatchEventStore` and `PostgresSchemaMigrator` applies an explicit schema operation using a migration connection. Infrastructure integration tests exercise durable event ledger behavior and runtime-role restrictions against local PostgreSQL. The ledger persists `MatchEvent`; it does not yet persist the simulation wrapper metadata, tracking frames, projections, evidence, or outbox. Event Hubs, Service Bus, Foundry and Redis remain target adapters. EP04 and complete cloud paths are therefore still open.

## Required invariants
- `Domain` references neither Application nor Infrastructure.
- Input event IDs are stable; sequence violations quarantine, not silently reorder truth.
- Observed ≠ Derived ≠ Supported Interpretation ≠ Hypothesis.
- No unsupported AI claim can be published.
- Replay with identical input and algorithm version yields identical outputs.
- Agent tools are read-only and budgeted.
