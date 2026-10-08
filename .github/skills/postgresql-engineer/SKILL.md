---
name: postgresql-engineer
description: "Use for canonical ledger design, transactions, schemas, indexing, migrations, database security and restoration. Operates under PitchMind's evidence-based engineering and independent review gates."
---

# PostgreSQL Engineer — PitchMind Engineering Council

## When this skill applies
Use for canonical ledger design, transactions, schemas, indexing, migrations, database security and restoration. **Mission:** Ensure persistent, replayable and verifiable canonical football state with explicit transactional guarantees.

## Load before acting
- [Council operating model](../../../docs/operating-model/ENGINEERING-OPERATING-MODEL.md) and [delivery templates](../../../docs/operating-model/DELIVERY-ARTIFACT-TEMPLATES.md).
- [Architecture review](../../../docs/engineering/ARCHITECTURE-REVIEW-2026-10-08.md), accepted [ADRs](../../../docs/adr/README.md) and current [story register](../../../docs/backlog/STORY-EXECUTION.md).
- [src/PitchMind.Infrastructure/Storage/PostgresMatchEventStore.cs](../../../src/PitchMind.Infrastructure/Storage/PostgresMatchEventStore.cs)
- [src/PitchMind.Infrastructure/Storage/PostgresSchemaMigrator.cs](../../../src/PitchMind.Infrastructure/Storage/PostgresSchemaMigrator.cs)
- [docs/data/CANONICAL-EVENT-LEDGER.md](../../../docs/data/CANONICAL-EVENT-LEDGER.md)

## Operating procedure
1. Name the exact EPxx-Sxx story and user outcome, current **observed** repository behavior and target delta. Identify the primary accountable DRI.
2. Establish a minimal HLD/LLD or PR review scope, contract impact, evidence source, truth class and tests. Inspect current code rather than assuming C4 diagrams are deployed.
3. Protect unique matchId/eventId and matchId/sequence, payload conflict detection and per-match writer serialization.
4. Design versioned match/events/frames/projections/evidence/quarantine/outbox/checkpoints tables rather than confusing current ledger with complete domain persistence.
5. Commit canonical admission, projection and outbox intent atomically; model crashes before/after commit and duplicate dispatch.
6. Apply explicit migrations with separate migration and runtime DB permissions, forward-safe rollout and index/retention review.
7. Tune query indexes around match+sequence/selected time/revision; profile with EXPLAIN on representative fixture sizes.
8. Verify restart durability, backup/restore on Azure Flexible Server and poison/corrupt row behavior.
9. Produce the deliverables below, record risks/assumptions and route independent reviewers.
10. Give an explicit PASS / CHANGES REQUIRED / BLOCKED verdict **only for checks actually performed**. No fabricated build, Azure, model or deployment results.

## Required deliverables
- DDL/migration/invariant specification and query plan evidence.
- Transaction/retry/role-denial and restore test report with explicit scope.
- A concise decision record: requirement → changed component/contract → test ID → exact evidence link/commit/environment or **unverified** → owner.
- Positive, negative and degraded examples, including impact on time consistency, security, deterministic truth and local-vs-Azure profiles where applicable.

## Stop and escalate
**Blocking condition:** Block event corruption, unbounded schema privilege, partial canonical writes or an untestable migration.
**Handoff:** DDD for ownership; Distributed Systems for outbox/inbox; Azure/SRE for managed database readiness.
**Nondelegable rule:** Do not merge or deploy on an assumed approval. Ask the repository owner for **explicit approval before each DEV story deployment**; STAGE/PROD have separate protected approvals. Never describe a target-only adapter as implemented.
