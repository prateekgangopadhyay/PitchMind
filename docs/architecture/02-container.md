# C4 Level 2 — PitchMind containers and data-plane boundaries

**Legend:** Solid arrows under **CURRENT** are backed by existing implementation or recorded demo behavior. **TARGET** describes the desired Azure-only deployment and is **not verified as an integrated cloud path**. **OPTIONAL** is feature/cost gated. [Target HLD](06-target-azure-hld.md) | [Observed limitations](../engineering/ARCHITECTURE-REVIEW-2026-10-08.md) | [Azure deployment evidence](../engineering/evidence/2026-10-08-DEV-DEPLOYMENT.md).

## CURRENT — local canonical reads, no candidate-feed canonical ingest

```mermaid
flowchart LR
  U["Browser / React UI"] --> A["ASP.NET Core read-only BFF"]
  A --> H["Health endpoints"]
  A --> MR["Fixture/demo routes: fail-closed 503"]
  A --> C["Canonical BFF routes (role-protected)"]
  ID["Entra issuer / audience"] -. "JWT bearer in Azure; local header adapter only when explicitly enabled" .-> A
  C --> Q["IMatchProjectionReader / IMatchInsightReader"]
  Q --> PG["Local PostgreSQL projections, events, insights, evidence"]
  T["Deterministic test golden generator"] -. test assemblies only .-> Tests["Automated tests"]
  L["Separate PostgreSQL event ledger"] --> P[("Local PostgreSQL (standalone tests)")]
  J["Candidate synthetic JSON"] -. "bounded raw schema parsing" .-> R["ManuLivCandidateFeedReader"]
  R -. "LOCAL paced raw replay; caller-managed resume cursor" .-> RP["LocalCandidateFeedReplayProducer"]
  RP -. "no canonical admission or persistence" .-> X["Clock / units / identity semantics unresolved"]
```

The local canonical BFF reads persisted projections, ledger events, insights and evidence through `PostgresMatchProjectionReader` and `PostgresMatchInsightReader`; the registration is conditional on `Runtime:Profile=local` and a PostgreSQL connection string. Without the adapter, canonical reads return sanitized `503 DEPENDENCY_UNAVAILABLE`. Canonical routes require the EP06-S04 role policies. Azure profiles require explicit Entra authority/audience configuration and map the protected routes with unavailable adapters until an Azure PostgreSQL read adapter is configured; no fixture fallback exists. Legacy match/demo routes remain fail-closed and protected; no synthetic demo is served by the runtime. No candidate-feed canonical admission path is implemented. `ManuLivCandidateFeedReader` parses raw candidate records and their source hash; `LocalCandidateFeedReplayProducer` emits those records in order at a bounded local rate and returns a source-bound resume cursor for the caller to persist after handling. Neither component maps or persists canonical football facts, and this local producer is not the target Event Hubs adapter. The candidate JSON has owner-confirmed synthetic provenance and redistribution permission, but its time policy, coordinate units/orientation, player metrics, canonical event mappings and football realism remain unverified; it is not yet imported.

## TARGET — canonical Azure platform

```mermaid
flowchart LR
  Web["React client"] --> Ingress["TLS ingress to ACA (optional Front Door/WAF)"]
  Ingress --> BFF["ASP.NET Core API / BFF, Azure Container Apps"]
  Web <-. "live cursor" .-> Signal["Azure SignalR"]
  BFF --> Signal
  BFF --> PG[("Azure PostgreSQL Flexible Server — canonical")]
  JSON["Approved synthetic JSON + versioned manifest"] --> Sim["ACA simulated feed producer"]
  Sim --> Candidate["Bounded raw candidate replay; noncanonical"]
  Candidate --> Mapper["Reviewed source mapper + football validation"]
  Mapper --> Admission["Canonical admission transaction"]
  Admission --> PG
  PG --> Outbox["Transactional outbox intent + assigned revision"]
  Outbox --> Dispatcher["Outbox dispatcher"]
  Dispatcher --> EH["Azure Event Hubs (admitted canonical telemetry)"]
  EH --> Worker["ACA EventWorker: projections / tactical"]
  Worker --> PG
  Dispatcher --> Signal
  Dispatcher --> Bus["Azure Service Bus commands / DLQ"]
  Bus --> Agent["ACA AgentWorker: bounded evidence tools"]
  Agent --> PG
  Agent --> Foundry["Microsoft Foundry (candidate explanation)"]
  Agent --> Guard["Independent claim validator"]
  Guard --> PG
  BFF -. "optional rebuildable cache" .-> Redis["Azure Managed Redis (P1)"]
  Bus -. "optional recap" .-> Functions["Azure Functions (P1)"]
  Functions --> Blob["Azure Blob Storage"]
  PG -. "optional indexed copy" .-> Search["Azure AI Search (P2)"]
  EH -. "optional archival" .-> Lake["Data Lake / Data Explorer (P2)"]
  BFF -. "OTel" .-> Monitor["Azure Monitor / Application Insights"]
```

**Non-negotiable contract:** raw source candidates are never published directly to the canonical telemetry channel. PostgreSQL assigns each admitted per-match `canonicalRevision` atomically with the record and outbox intent. Event Hubs is **at-least-once** telemetry; service-side `MatchId + EventId + Sequence + payload hash` and PostgreSQL transaction establish correctness. Service Bus is asynchronous command transport, not an event-ledger replacement. SignalR carries revision/cursor notifications, not canonical truth. Redis, Search and Data Explorer are rebuildable projections. Foundry may suggest words but cannot change score, event IDs or detector evidence. The BFF enforces selected-time and authorization on every read.

**Environment mapping:** LOCAL Docker PostgreSQL + local event/AI adapters through ports; Azure DEV/STAGE/PROD managed services only. Source JSON units, rights and sparse frame quality require review before pressure/xT/xG or player performance claims.

## C# project responsibilities and dependency policy

`tests/PitchMind.Architecture.Tests` enforces this source-project reference map using evaluated MSBuild items, including imported properties and targets. It also captures the final compiler references after `CoreCompile` target hooks have run. A source project added without a documented policy, an unexpected reference, or a missing declared dependency fails the architecture test. Internal `PitchMind.*` assemblies must use mapped project references; aliases with `HintPath`, imported references, package compile/runtime assets, and late compiler inputs are checked as well. `PitchMind.Domain` must have no project, package, explicit framework, raw assembly, COM, or native references; the SDK's implicit `Microsoft.NETCore.App` reference is allowed. Production projects must not embed files from test support or simulation/fixture/scenario/golden data; those resources belong in `tests/PitchMind.TestFixtures` and are checked from evaluated `EmbeddedResource` items.

| Project | Responsibility | Allowed project references |
|---|---|---|
| `PitchMind.Domain` | Football concepts and rules; no scenario data or fixture generation | None; SDK implicit framework baseline only |
| `PitchMind.Contracts` | Transport contracts | None |
| `PitchMind.Application` | Use cases and ports | `PitchMind.Domain` |
| `PitchMind.Infrastructure` | Persistence and external adapters | `PitchMind.Application` |
| `PitchMind.Api` | HTTP composition and endpoints | `PitchMind.Application`, `PitchMind.Infrastructure`, `PitchMind.Contracts` |
| `PitchMind.EventWorker` | Event-processing host composition | `PitchMind.Application` |
| `PitchMind.AgentWorker` | Explanation-worker host composition | `PitchMind.Application` |

**Release state:** The Azure integration arrows above are **proposed**. An existing hosted offline demo does not prove the connectivity represented here, and no new DEV deployment is authorized by this diagram.
