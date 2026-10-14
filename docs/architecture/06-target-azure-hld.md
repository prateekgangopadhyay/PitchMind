# PitchMind target Azure High-Level Design (C4 extension)
**Status:** target only, not proof of integrated Azure deployment. **Change governance:** same-PR C4, LLD, API and ADR for material architecture changes.

## Principles
Hexagonal/Clean Architecture (Domain zero external dependencies); DDD match/fixture/possession/evidence bounded contexts; domain clocks deterministic; schema/versioned source provenance; append-only PostgreSQL as correctness boundary, at-least-once Event Hubs/Service Bus transport; Redis/search/ADX projections only; zero facts sourced from frontend or LLM; selected-time replay isolated from live head and canonical writes. LOCAL adapters via Docker; deployed environments Azure-only.

## Target architecture
```mermaid
flowchart LR
 U[Browser React] --> FD[Public edge; required when ACA is internal/private]
 FD --> API[ASP.NET Core BFF on ACA]
 U -. PKCE .-> AUTH[Entra External ID]
 U <-. cursor .-> SR[Azure SignalR]
 API --> SR
 API --> PG[(Azure PostgreSQL Flexible Server)]
FEED[Approved synthetic JSON Blob] --> GEN[ACA feeder Job / source mapper]
GEN --> PG
PG --> OUT[Transactional outbox; admission assigns revision]
 OUT --> PUB[Dispatcher]
PUB --> EH[Event Hubs canonical envelope partition=matchId]
EH --> EW[.NET Event Worker projection consumers]
EW --> RULES[Domain Tactical Engine]
RULES --> PG
 PUB --> SR
 PUB --> SB[Service Bus commands + DLQ]
 SB --> AW[Agent Worker ACA]
 AW --> TOOLS[Read-only evidence tools]
 TOOLS --> PG
 AW --> FO[Microsoft Foundry]
 AW --> CV[Claim validator]
 CV --> PG
 API -. Optional cache .-> REDIS[Azure Managed Redis P1]
 SB -. Recap P1 .-> FN[Azure Functions]
 FN --> BLOB[Storage export P1]
 PG -. Indexed read P2 .-> SEARCH[Azure AI Search]
 EH -. History P2 .-> ADX[Data Lake + Data Explorer]
 API -. OTEL .-> MON[Azure Monitor App Insights]
 EW -. OTEL .-> MON
 AW -. OTEL .-> MON
 ```

For the current `canonical-dev-topology.bicep` candidate, ACA is internal with public network access disabled, so direct public browser ingress does not apply. Front Door Premium with a Private Link origin is one possible public path, but no edge module or configuration is included; the public path remains blocked pending architecture review. The separate offline demo's public ingress is not evidence for this canonical topology.

## Platform service and local-equivalent matrix
| Boundary | LOCAL | DEV/STAGE/PROD | Verification |
|---|---|---|---|
| Web/API | .NET + Vite or same-origin Docker | ACA workload, TLS, managed identity | BFF smoke, auth, health, CSP |
| Stream | file replay / in-process adapter | Event Hubs with partition by matchId | 500eps defined burst, lag, idempotency |
| Canonical truth | PostgreSQL Docker | Flexible Server private DNS/VNet, independent migration ID | restart/PITR/transaction/roles |
| Cache | Redis Docker (optional) | Azure Managed Redis | outage bypass and revision-keyed invalidation |
| Commands | local queue adapter | Service Bus DLQ, idempotent inbox | retry poison and recovery |
| Live | local SignalR server | Azure SignalR | negotiated group access, reconnect + backfill |
| AI | deterministic fallback/mock | Foundry model deployment via managed identity | grounding/timeout/token budget |
| Jobs | local worker | Azure Functions / ACA Job | idempotent status, export permissions |
| Identity | explicit dev-only test auth | Entra auth code+PKCE, roles `fan/analyst/admin` | unauthenticated/denied cases |
| Infra | Compose | Bicep, VNet integrated ACA, private endpoints, managed identity, Key Vault | preflight what-if, DNS and TLS proofs |
| CI/CD | local validation | Azure DevOps OIDC + ACR immutable digest promotion | green pipeline, release manifest, gates |

## Component boundaries
- Domain owns `MatchId, EventId, MatchClock, MatchState, Team, Player, Possession, Phase, PitchCoordinate, Mechanism, Evidence`. Forbidden: Azure SDK, Npgsql, HTTP, UI concepts, LLM prompts.
- Application owns `IngestEvent, NormalizeFrame, ProjectMatch, DetectMechanism, BuildEvidence, QuerySnapshot, ReplayAt, ExplainInsight, DispatchOutbox` and `IEventLedger, ITrackingStore, IOutbox, IModelClient, IRealtimeNotifier, ISimulationFeedReader` ports.
- Infrastructure implements adapters: feed parser, Blob, Event Hubs, PostgreSQL, Service Bus, Foundry, SignalR, Redis, OTEL. Never embed policy in adapters.
- API enforces version, identity/authorization, pagination, query bounds, problem details, correlation, selectedTime/revision consistency.
- EventWorker processes match-partition streams, validates monotonic sequence/idempotency, commits event+projection+evidence+outbox atomically; checkpoints only after DB success.
- AgentWorker accesses **read-only** typed tools, invokes Foundry with bounded evidence, deterministically validates claims, writes validated output via authorized application port; returns fallback on timeout/reject.
- React shell renders factual DTOs only; no hardcoded football results or derived metric business logic.

## Critical flows
```mermaid
sequenceDiagram
 participant J as JSON
 participant F as Feed adapter
 participant H as Event Hubs
 participant W as EventWorker
 participant P as PostgreSQL
 participant O as Outbox/SignalR
 participant B as BFF
 participant R as React
 participant A as AgentWorker/Foundry
 J->>F: file/hash/schema/rights
 F->>P: validate/mapping candidate; admit event/frame and assign canonicalRevision
 P-->>F: commit / duplicate / quarantine + assigned revision
 P-->>O: outbox canonical envelope with revision
 O->>H: at-least-once publish keyed by matchId
 H-->>W: canonical event/frame with canonicalRevision
 W->>P: idempotent projection/evidence transaction
 P-->>W: commit / duplicate / quarantine
 W-->>H: checkpoint only if committed
 O-->>R: match revision/lastSequence only
 R->>B: snapshot?atSeconds=X&revision=N
 B->>P: canonical snapshot, evidence
 P-->>R: same selected-time view through BFF
 O->>A: durable explanation command
 A->>P: read-only evidence
 A->>A: validate source claims
 A->>P: publish validated text or deterministic fallback
 ```

## Security, operations and cost
Private endpoint DNS/route and ACA VNet integration REQUIRED: inaccessible resources are not fixed by disabling network policies. Runtime least privilege, separate schema migration principal, Entra JWT audience/issuer/role verification, managed identity in Azure, no secrets in browser or logs. Public anonymous demo only with explicit PO approval and scoped read-only rate limit.
Failure: Event Hubs lag delays downstream projections/publication but cannot advance canonical state; PostgreSQL loss -> stale/503 not invented live state; Redis down -> canonical read; SignalR down -> polling/backfill; Foundry down -> labeled deterministic fallback; Service Bus down -> transactional outbox recovery; invalid/late input -> append correction/revision/quarantine, do not alter canonical history.
Monitor: correlation IDs, lag/offset, error/quarantine, transaction retry, replay mismatch, p50/p95/p99 API latency, AI costs/tokens, user-facing staleness, uptime and monthly burn. FinOps approval required for model and ingestion quotas. Restore drills, backup/PITR and immutable rollback are release gates.

## Explicit differences from current prototype
`SyntheticMatchService` remains a fixture-backed application component for test scenarios; it is not registered or resolved by the API routes. Match routes currently return `503 MATCH_DATA_UNAVAILABLE` until API route composition is replaced with a canonical, revisioned read port backed by admitted PostgreSQL data. API service registration binds `InMemoryMatchEventStore`; independent `PostgresMatchEventStore` stores only `MatchEvent`. No live Azure adapters/auth/Foundry workers yet. `docs/engineering/evidence/2026-10-08-DEV-DEPLOYMENT.md` proves a **bounded offline** DEV prototype only. No new DEV deployment authorized by this HLD.
