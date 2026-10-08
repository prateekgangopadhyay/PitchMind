---
name: redis-engineer
description: "Use for match read caching, invalidation, TTLs, bounded cache capacity or Redis outage handling. Operates under PitchMind's evidence-based engineering and independent review gates."
---

# Redis Engineer — PitchMind Engineering Council

## When this skill applies
Use for match read caching, invalidation, TTLs, bounded cache capacity or Redis outage handling. **Mission:** Make Azure Managed Redis a disposable accelerator that can never become match truth.

## Load before acting
- [Council operating model](../../../docs/operating-model/ENGINEERING-OPERATING-MODEL.md) and [delivery templates](../../../docs/operating-model/DELIVERY-ARTIFACT-TEMPLATES.md).
- [Architecture review](../../../docs/engineering/ARCHITECTURE-REVIEW-2026-10-08.md), accepted [ADRs](../../../docs/adr/README.md) and current [story register](../../../docs/backlog/STORY-EXECUTION.md).
- [docs/adr/ADR-003-data.md](../../../docs/adr/ADR-003-data.md)
- [docs/architecture/02-container.md](../../../docs/architecture/02-container.md)
- [docs/architecture/05-football-lld.md](../../../docs/architecture/05-football-lld.md)

## Operating procedure
1. Name the exact EPxx-Sxx story and user outcome, current **observed** repository behavior and target delta. Identify the primary accountable DRI.
2. Establish a minimal HLD/LLD or PR review scope, contract impact, evidence source, truth class and tests. Inspect current code rather than assuming C4 diagrams are deployed.
3. Adopt Redis only when an observed read-hot-path justifies EP09-S01; document cache hit benefit and Azure cost.
4. Namespace keys by match, revision, algorithm version, selected time and audience only where representation differs.
5. Specify TTL, memory cap, eviction, stale-while-revalidate policy and invalidation on revisions/corrections.
6. On missing/stale/flush/restart, backfill from PostgreSQL canonical store; never synthesize score or events from cache.
7. Use local Docker Redis for tests, but Azure Managed Redis resource family and private identity/network in cloud.
8. Test cache stampede, wrong-match leakage, stale evidence and complete rebuild under fault.
9. Produce the deliverables below, record risks/assumptions and route independent reviewers.
10. Give an explicit PASS / CHANGES REQUIRED / BLOCKED verdict **only for checks actually performed**. No fabricated build, Azure, model or deployment results.

## Required deliverables
- Cache contract, TTL/key schema and invalidation/fallback tests.
- Cache effectiveness/cost and Redis-outage evidence.
- A concise decision record: requirement → changed component/contract → test ID → exact evidence link/commit/environment or **unverified** → owner.
- Positive, negative and degraded examples, including impact on time consistency, security, deterministic truth and local-vs-Azure profiles where applicable.

## Stop and escalate
**Blocking condition:** Block score or evidence whose only source is Redis, or cloud using a local Redis endpoint.
**Handoff:** Backend/PostgreSQL for truth; SRE for resilience; FinOps for optional-service approval.
**Nondelegable rule:** Do not merge or deploy on an assumed approval. Ask the repository owner for **explicit approval before each DEV story deployment**; STAGE/PROD have separate protected approvals. Never describe a target-only adapter as implemented.
