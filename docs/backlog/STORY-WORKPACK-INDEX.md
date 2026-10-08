# PitchMind — 59 individually designed development story work packets
**Status:** All design packets created; no blanket implementation-complete claim. Each file preserves the authoritative story, build scope and acceptance, with HLD, LLD, owner role, failure behaviors and verification evidence. [Epic authority](HACKATHON-MVP-EPICS.md) | [Execution](../engineering/COUNCIL-EXECUTION-PLAN.md) | [Current implementation register](STORY-EXECUTION.md).

| Story ID | Title | Priority | Workstream | Work packet |
|---|---|---|---|---|
| EP01-S01 | Enforce layered solution architecture | P0 | Platform Foundations | [Open](../engineering/stories/EP01/EP01-S01.md) |
| EP01-S02 | Reproducible local developer workstation | P0 | Platform Foundations | [Open](../engineering/stories/EP01/EP01-S02.md) |
| EP01-S03 | Azure DevOps validation pipeline | P0 | Platform Foundations | [Open](../engineering/stories/EP01/EP01-S03.md) |
| EP01-S04 | Shared contracts and engineering conventions | P0 | Platform Foundations | [Open](../engineering/stories/EP01/EP01-S04.md) |
| EP01-S05 | Quality, security and developer readiness gate | P0 | Platform Foundations | [Open](../engineering/stories/EP01/EP01-S05.md) |
| EP02-S01 | Reconcile IaC with deployed MVP needs | P0 | Azure Platform | [Open](../engineering/stories/EP02/EP02-S01.md) |
| EP02-S02 | Workload identity and least-privilege access | P0 | Azure Platform | [Open](../engineering/stories/EP02/EP02-S02.md) |
| EP02-S03 | Network, Bicep and preflight validation | P0 | Azure Platform | [Open](../engineering/stories/EP02/EP02-S03.md) |
| EP02-S04 | Deploy and observe a minimal DEV workload | P0 | Azure Platform | [Open](../engineering/stories/EP02/EP02-S04.md) |
| EP02-S05 | Cost and deployment safety gate | P0 | Azure Platform | [Open](../engineering/stories/EP02/EP02-S05.md) |
| EP03-S01 | Govern a synthetic match scenario | P0 | Feed & Streaming | [Open](../engineering/stories/EP03/EP03-S01.md) |
| EP03-S02 | Versioned canonical telemetry contracts | P0 | Feed & Streaming | [Open](../engineering/stories/EP03/EP03-S02.md) |
| EP03-S03 | Synthetic producer + Event Hubs adapter | P0 | Feed & Streaming | [Open](../engineering/stories/EP03/EP03-S03.md) |
| EP03-S04 | Dedupe, ordering and poison-event rules | P0 | Feed & Streaming | [Open](../engineering/stories/EP03/EP03-S04.md) |
| EP03-S05 | Defined 500 events/second burst test | P0 | Feed & Streaming | [Open](../engineering/stories/EP03/EP03-S05.md) |
| EP04-S01 | Govern canonical football aggregates | P0 | Canonical Persistence | [Open](../engineering/stories/EP04/EP04-S01.md) |
| EP04-S02 | PostgreSQL schema, migrations and provenance | P0 | Canonical Persistence | [Open](../engineering/stories/EP04/EP04-S02.md) |
| EP04-S03 | Transactional outbox and resilient projection | P0 | Canonical Persistence | [Open](../engineering/stories/EP04/EP04-S03.md) |
| EP04-S04 | Deterministic replay and point-in-time queries | P0 | Canonical Persistence | [Open](../engineering/stories/EP04/EP04-S04.md) |
| EP04-S05 | Match integrity golden tests | P0 | Canonical Persistence | [Open](../engineering/stories/EP04/EP04-S05.md) |
| EP05-S01 | Formal football concept dictionary | P0 | Football Analytics | [Open](../engineering/stories/EP05/EP05-S01.md) |
| EP05-S02 | Deterministic tactical sequence detector | P0 | Football Analytics | [Open](../engineering/stories/EP05/EP05-S02.md) |
| EP05-S03 | Match context and tactical neutrality | P0 | Football Analytics | [Open](../engineering/stories/EP05/EP05-S03.md) |
| EP05-S04 | Evidence graph and truth classification | P0 | Football Analytics | [Open](../engineering/stories/EP05/EP05-S04.md) |
| EP05-S05 | Key Moment generation and relevance policy | P0 | Football Analytics | [Open](../engineering/stories/EP05/EP05-S05.md) |
| EP05-S06 | Mechanism replay and negative validation suite | P0 | Football Analytics | [Open](../engineering/stories/EP05/EP05-S06.md) |
| EP06-S01 | Match BFF contracts | P0 | API & Realtime | [Open](../engineering/stories/EP06/EP06-S01.md) |
| EP06-S02 | Azure SignalR live event fan-out | P0 | API & Realtime | [Open](../engineering/stories/EP06/EP06-S02.md) |
| EP06-S03 | Reconnect, backfill and historical context | P0 | API & Realtime | [Open](../engineering/stories/EP06/EP06-S03.md) |
| EP06-S04 | Identity, authorization and abuse controls | P0 | API & Realtime | [Open](../engineering/stories/EP06/EP06-S04.md) |
| EP06-S05 | Observability, latency and degradation | P0 | API & Realtime | [Open](../engineering/stories/EP06/EP06-S05.md) |
| EP07-S01 | Product information architecture and design system | P0 | Experience | [Open](../engineering/stories/EP07/EP07-S01.md) |
| EP07-S02 | Home / synthetic match overview | P0 | Experience | [Open](../engineering/stories/EP07/EP07-S02.md) |
| EP07-S03 | Live match header, story and Key Moments | P0 | Experience | [Open](../engineering/stories/EP07/EP07-S03.md) |
| EP07-S04 | Intelligent 2D pitch and synchronized timeline | P0 | Experience | [Open](../engineering/stories/EP07/EP07-S04.md) |
| EP07-S05 | 'Why?' evidence drawer and tactical replay | P0 | Experience | [Open](../engineering/stories/EP07/EP07-S05.md) |
| EP07-S06 | Audience modes and resilient interface states | P0 | Experience | [Open](../engineering/stories/EP07/EP07-S06.md) |
| EP08-S01 | Govern the canonical `MatchIntelligence` AI context | P0 | Grounded Agents | [Open](../engineering/stories/EP08/EP08-S01.md) |
| EP08-S02 | Microsoft Foundry explanation adapter | P0 | Grounded Agents | [Open](../engineering/stories/EP08/EP08-S02.md) |
| EP08-S03 | Meaningful multi-agent handoffs (hackathon differentiator) | P0 | Grounded Agents | [Open](../engineering/stories/EP08/EP08-S03.md) |
| EP08-S04 | Claim validation and trust boundaries | P0 | Grounded Agents | [Open](../engineering/stories/EP08/EP08-S04.md) |
| EP08-S05 | Deterministic fallback and resource governance | P0 | Grounded Agents | [Open](../engineering/stories/EP08/EP08-S05.md) |
| EP08-S06 | AI Analyst UX integration | P0 | Grounded Agents | [Open](../engineering/stories/EP08/EP08-S06.md) |
| EP09-S01 | Rebuildable Azure Managed Redis projections | P1 | Optional Operations | [Open](../engineering/stories/EP09/EP09-S01.md) |
| EP09-S02 | Service Bus background jobs and recovery | P1 | Optional Operations | [Open](../engineering/stories/EP09/EP09-S02.md) |
| EP09-S03 | Azure Functions AutoRecap | P1 | Optional Operations | [Open](../engineering/stories/EP09/EP09-S03.md) |
| EP09-S04 | Permissioned evidence exports and retention | P1 | Optional Operations | [Open](../engineering/stories/EP09/EP09-S04.md) |
| EP10-S01 | One golden end-to-end synthetic match journey | P0 | Certification | [Open](../engineering/stories/EP10/EP10-S01.md) |
| EP10-S02 | Football, evidence, AI and accessibility certification | P0 | Certification | [Open](../engineering/stories/EP10/EP10-S02.md) |
| EP10-S03 | Resilience, performance and security release gates | P0 | Certification | [Open](../engineering/stories/EP10/EP10-S03.md) |
| EP10-S04 | Azure DEV integrated certification and rollback | P0 | Certification | [Open](../engineering/stories/EP10/EP10-S04.md) |
| EP10-S05 | Hackathon public submission assets | P0 | Certification | [Open](../engineering/stories/EP10/EP10-S05.md) |
| EP10-S06 | Judges' evidence map and demo contingency | P0 | Certification | [Open](../engineering/stories/EP10/EP10-S06.md) |
| EP11-S01 | Environment parity and protected release controls | P1 | Release Governance | [Open](../engineering/stories/EP11/EP11-S01.md) |
| EP11-S02 | Backup, recovery and incident drills | P1 | Release Governance | [Open](../engineering/stories/EP11/EP11-S02.md) |
| EP11-S03 | Ongoing security and FinOps controls | P1 | Release Governance | [Open](../engineering/stories/EP11/EP11-S03.md) |
| EP12-S01 | Provenance-aware historical retrieval (M14) | P2 | Advanced Projections | [Open](../engineering/stories/EP12/EP12-S01.md) |
| EP12-S02 | Historical tracking and spatial data exploration (M15) | P2 | Advanced Projections | [Open](../engineering/stories/EP12/EP12-S02.md) |
| EP12-S03 | Model and partner expansion (M16) | P2 | Advanced Projections | [Open](../engineering/stories/EP12/EP12-S03.md) |

**Definition of done:** source and match/evidence provenance; architecture/API/ADR changes in same PR; golden positive/negative/insufficient-data tests; CI security and accessibility gates; traceable revision-specific evidence; PO acceptance. DEV deploy is separately approved by owner. **Feed-specific stories remain blocked pending the requested JSON.**
