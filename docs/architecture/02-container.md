# C4 Level 2 — PitchMind containers and data-plane boundaries

**Legend:** Solid arrows under **CURRENT** are backed by existing implementation or recorded demo behavior. **TARGET** describes the desired Azure-only deployment and is **not verified as an integrated cloud path**. **OPTIONAL** is feature/cost gated. [Target HLD](06-target-azure-hld.md) | [Observed limitations](../engineering/ARCHITECTURE-REVIEW-2026-10-08.md) | [Azure deployment evidence](../engineering/evidence/2026-10-08-DEV-DEPLOYMENT.md).

## CURRENT — offline synthetic demonstration (not canonical Azure ingestion)

```mermaid
flowchart LR
  U["Browser / React UI"] --> A["ASP.NET Core read-only BFF"]
  A --> S["SyntheticMatchService"]
  S --> G["C# generated fixture (transitional)"]
  S --> T["Deterministic pressing rule"]
  S --> E["Selected-time evidence and fallback"]
  L["PostgreSQL event ledger and canonical update store"] --> P[("Local PostgreSQL (standalone tests)")]
  P --> O["Pending transactional outbox"] --> D["Leased dispatcher / publisher port"]
  J["JSON file in repository (not wired)"] -. "not imported" .-> S
```

The current PostgreSQL adapters persist events and projection provenance, and the EP04-S03 local slice atomically writes aggregate-derived event/projection/outbox records. A PostgreSQL dispatcher and same-database consumer inbox are implemented as unregistered primitives; there is no API/EventWorker integration or cloud broker adapter, so this does not provide a live ingestion path. The earlier Central India ACA prototype serves the offline fixture-backed API only. The C# fixture contains football facts and must be removed after the approved feed adapter is integrated.

The EP09-S01 local slice adds an exact canonical-revision read contract and a fail-open cache-aside wrapper for immutable snapshots. Its cache port has no concrete Redis adapter or DI/configuration yet because the current Redis package could not be restored in the restricted local environment. This is not evidence of a Redis-backed current path; event-window reads remain PostgreSQL reads.

## TARGET — canonical Azure platform

```mermaid
flowchart LR
  Web["React client"] --> Ingress["TLS ingress to ACA (optional Front Door/WAF)"]
  Ingress --> BFF["ASP.NET Core API / BFF, Azure Container Apps"]
  Web <-. "live cursor" .-> Signal["Azure SignalR"]
  BFF --> Signal
  BFF --> PG[("Azure PostgreSQL Flexible Server — canonical")]
  JSON["Approved synthetic JSON + versioned manifest"] --> Sim["ACA simulated feed producer"]
  Sim --> EH["Azure Event Hubs (matchId partition)"]
  EH --> Worker["ACA EventWorker: validation / projections / tactical"]
  Worker --> PG
  PG --> Outbox["Transactional outbox / inbox"]
  Outbox --> Dispatcher["Background dispatcher"]
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

**Non-negotiable contract:** Event Hubs is **at-least-once** telemetry; service-side `MatchId + EventId + Sequence + payload hash` and PostgreSQL transaction establish correctness. Service Bus is asynchronous command transport, not an event-ledger replacement. SignalR carries revision/cursor notifications, not canonical truth. Redis, Search and Data Explorer are rebuildable projections. Foundry may suggest words but cannot change score, event IDs or detector evidence. The BFF enforces selected-time and authorization on every read.

**Environment mapping:** LOCAL Docker PostgreSQL + local event/AI adapters through ports; Azure DEV/STAGE/PROD managed services only. Source JSON units, rights and sparse frame quality require review before pressure/xT/xG or player performance claims.

## C# project responsibilities and dependency policy

`tests/PitchMind.Architecture.Tests` enforces this source-project reference map. A source project added without a documented policy, an unexpected reference, or a missing declared dependency fails the architecture test. `PitchMind.Domain` must have no project, package, or framework references.

| Project | Responsibility | Allowed project references |
|---|---|---|
| `PitchMind.Domain` | Pure football concepts and rules | None |
| `PitchMind.Contracts` | Transport contracts | None |
| `PitchMind.Application` | Use cases and ports | `PitchMind.Domain` |
| `PitchMind.Infrastructure` | Persistence and external adapters | `PitchMind.Application` |
| `PitchMind.Api` | HTTP composition and endpoints | `PitchMind.Application`, `PitchMind.Infrastructure`, `PitchMind.Contracts` |
| `PitchMind.EventWorker` | Event-processing host composition | `PitchMind.Application` |
| `PitchMind.AgentWorker` | Explanation-worker host composition | `PitchMind.Application` |

**Release state:** The Azure integration arrows above are **proposed**. An existing hosted offline demo does not prove the connectivity represented here, and no new DEV deployment is authorized by this diagram.
