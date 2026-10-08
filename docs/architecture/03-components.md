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
    Pg[PostgreSQL]
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
```

## Required invariants
- `Domain` references neither Application nor Infrastructure.
- Input event IDs are stable; sequence violations quarantine, not silently reorder truth.
- Observed ≠ Derived ≠ Supported Interpretation ≠ Hypothesis.
- No unsupported AI claim can be published.
- Replay with identical input and algorithm version yields identical outputs.
- Agent tools are read-only and budgeted.
