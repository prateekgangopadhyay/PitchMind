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
 SR -->|authorized cursor notification| U
 API --> PG[(Azure PostgreSQL Flexible Server)]
FEED[Approved synthetic JSON Blob] --> GEN[ACA feeder Job / source mapper]
GEN --> EH[Event Hubs ingress partition=matchId]
EH --> EW[.NET Event Worker ingress consumer]
EW --> TX[Validate, map, idempotently admit]
TX --> PG
PG --> OUT[Transactional outbox committed atomically with canonical event/revision]
EW --> BLOB[Azure Blob checkpoint after PostgreSQL commit or durable quarantine]
OUT --> PUB[Outbox dispatcher]
PUB --> CACHE[Cache projection writer]
PUB --> SR
PUB -. future adapter .-> SB[Service Bus commands + DLQ]
 SB --> AW[Agent Worker ACA]
 AW --> TOOLS[Read-only evidence tools]
 TOOLS --> PG
 AW --> FO[Microsoft Foundry]
 AW -. "profile-filtered hybrid retrieval; read-only" .-> KBSEARCH[Azure AI Search tactical knowledge index]
 AW --> CV[Claim validator]
 CV --> PG
 CACHE --> REDIS[Azure Managed Redis P1]
 SB -. Recap P1 .-> FN[Azure Functions]
 FN --> BLOB[Storage export P1]
 PG -. Indexed canonical-evidence read P2 .-> SEARCH[Azure AI Search]
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
| Tactical knowledge RAG | embedded, bounded lexical retrieval | Azure AI Search vector + semantic hybrid query using managed identity | corpus provenance, profile allowlist, grounding/refusal and outage tests; target only |
| Jobs | local worker | Azure Functions / ACA Job | idempotent status, export permissions |
| Identity | explicit dev-only test auth | Entra auth code+PKCE, roles `fan/analyst/admin` | unauthenticated/denied cases |
| Infra | Compose | Bicep, VNet integrated ACA, private endpoints, managed identity, Key Vault | preflight what-if, DNS and TLS proofs |
| CI/CD | local validation | Azure DevOps OIDC + ACR immutable digest promotion | green pipeline, release manifest, gates |

## Component boundaries
- Domain owns `MatchId, EventId, MatchClock, MatchState, Team, Player, Possession, Phase, PitchCoordinate, Mechanism, Evidence`. Forbidden: Azure SDK, Npgsql, HTTP, UI concepts, LLM prompts.
- Application owns `IngestEvent, NormalizeFrame, ProjectMatch, DetectMechanism, BuildEvidence, QuerySnapshot, ReplayAt, ExplainInsight, DispatchOutbox` and `IEventLedger, ITrackingStore, IOutbox, IModelClient, IRealtimeNotifier, ISimulationFeedReader` ports.
- Infrastructure implements adapters: feed parser, Blob, Event Hubs, PostgreSQL, Service Bus, Foundry, SignalR, Redis, OTEL. Never embed policy in adapters.
- API enforces version, identity/authorization, pagination, query bounds, problem details, correlation, selectedTime/revision consistency.
- **Target ingress flow:** the feeder streams source events to Event Hubs first. EventWorker validates schema, identity, ordering and source semantics, then commits the canonical event/revision and transactional outbox record together in PostgreSQL. The Event Hubs checkpoint advances only after that commit or a durable quarantine decision. Independent outbox handlers fan out after commit: one updates the revision-fenced, rebuildable Redis cache; another sends cursor-only SignalR notifications. Those handlers may run in parallel and retry independently. SignalR never carries canonical football facts; clients fetch the revisioned snapshot/evidence through the BFF. If a pre-database durable staging step is required, model it as an inbox/staging ledger and do not call it the transactional outbox.
- AgentWorker accesses **read-only** typed tools, invokes Foundry with bounded evidence, deterministically validates claims, writes validated output via authorized application port; returns fallback on timeout/reject.
- Tactical knowledge search is a separate read-only corpus of reviewed educational summaries and source links. It is profile-topic-filtered, hybrid-ranked and limited to three bounded passages. It may explain a general concept but cannot establish that an event, player action, intent or outcome occurred. The canonical-evidence search projection is distinct from this knowledge index.
- React shell renders factual DTOs only; no hardcoded football results or derived metric business logic.

**Observed code slice (different from target):** EP04-S03 currently composes an Azure-only EventWorker publisher over the PostgreSQL leased outbox and an opt-in EventProcessorClient projection consumer. Its publisher sends persisted `ProjectionPublishedV1` records to Event Hubs; its consumer reads those messages and writes a payload-bound inbox receipt and rebuildable latest snapshot to PostgreSQL before updating the Azure Blob checkpoint. This is the reverse of the corrected ingress-first target above. The current code does not implement Event Hubs source ingestion into the canonical event ledger, post-commit Redis fan-out, or Azure SignalR notifications. Existing local automated-test evidence is code-level only; no Azure resource, credential, network connection, cloud checkpoint, or deployment has been exercised.

## Critical flows
```mermaid
sequenceDiagram
 participant J as JSON feed
 participant F as Feeder/mapper
 participant H as Event Hubs ingress
 participant W as EventWorker
 participant P as PostgreSQL
 participant O as Outbox dispatcher
 participant C as Redis cache projection
 participant S as Azure SignalR
 participant B as BFF
 participant R as React
 participant A as AgentWorker/Foundry
 J->>F: read source file/hash/schema/provenance
 F->>H: stream source event in source order, keyed by matchId
 H-->>W: at-least-once event delivery
 W->>W: validate/map; preserve source ID, sequence, provenance and clock semantics
 W->>P: canonical event/revision + outbox in one transaction
 P-->>W: committed / duplicate / durable quarantine
 W-->>H: advance Blob checkpoint only after durable outcome
 P-->>O: committed outbox record becomes dispatchable
 par post-commit parallel fan-out
 O->>C: idempotently update revision-fenced cache
 O->>S: publish cursor/revision only
 end
 S-->>R: match revision/lastSequence only
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
Failure: Event Hubs lag delays canonical admission; PostgreSQL transaction failure prevents checkpoint advancement and causes safe redelivery; PostgreSQL loss -> stale/503, not invented live state; Redis down -> canonical read; SignalR down -> polling/backfill; a failed outbox handler retries independently without rolling back canonical admission; Foundry down -> labeled deterministic fallback; Service Bus down -> transactional outbox recovery; invalid/late input -> durable quarantine or append correction/revision, never rewrite canonical history.
Monitor: correlation IDs, lag/offset, error/quarantine, transaction retry, replay mismatch, p50/p95/p99 API latency, AI costs/tokens, user-facing staleness, uptime and monthly burn. FinOps approval required for model and ingestion quotas. Restore drills, backup/PITR and immutable rollback are release gates.

## Explicit differences from current prototype
`SyntheticMatchService` remains a fixture-backed application component for test scenarios; it is not registered or resolved by the API routes. Match routes currently return `503 MATCH_DATA_UNAVAILABLE` until API route composition is replaced with a canonical, revisioned read port backed by admitted PostgreSQL data. API service registration binds `InMemoryMatchEventStore`; independent `PostgresMatchEventStore` stores only `MatchEvent`. No live Azure adapters/auth/Foundry workers yet. `docs/engineering/evidence/2026-10-08-DEV-DEPLOYMENT.md` proves a **bounded offline** DEV prototype only. No new DEV deployment authorized by this HLD.
