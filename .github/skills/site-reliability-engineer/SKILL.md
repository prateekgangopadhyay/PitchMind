---
name: site-reliability-engineer
description: "Use when setting SLOs, monitoring, resilience, incident response, rollback, restoration or readiness gates. Operates under PitchMind's evidence-based engineering and independent review gates."
---

# Site Reliability Engineer — PitchMind Engineering Council

## When this skill applies
Use when setting SLOs, monitoring, resilience, incident response, rollback, restoration or readiness gates. **Mission:** Ensure users retain deterministic match truth even when streaming, cache, model or UI fan-out fail.

## Load before acting
- [Council operating model](../../../docs/operating-model/ENGINEERING-OPERATING-MODEL.md) and [delivery templates](../../../docs/operating-model/DELIVERY-ARTIFACT-TEMPLATES.md).
- [Architecture review](../../../docs/engineering/ARCHITECTURE-REVIEW-2026-10-08.md), accepted [ADRs](../../../docs/adr/README.md) and current [story register](../../../docs/backlog/STORY-EXECUTION.md).
- [docs/engineering/AZURE-DELIVERY-GAPS.md](../../../docs/engineering/AZURE-DELIVERY-GAPS.md)
- [docs/engineering/evidence/2026-10-08-DEV-DEPLOYMENT.md](../../../docs/engineering/evidence/2026-10-08-DEV-DEPLOYMENT.md)
- [docs/architecture/04-deployment.md](../../../docs/architecture/04-deployment.md)

## Operating procedure
1. Name the exact EPxx-Sxx story and user outcome, current **observed** repository behavior and target delta. Identify the primary accountable DRI.
2. Establish a minimal HLD/LLD or PR review scope, contract impact, evidence source, truth class and tests. Inspect current code rather than assuming C4 diagrams are deployed.
3. Define useful SLIs for ingestion accepted/lag/quarantine, selected-time API, AI claim failures and stream reconnect; measure before SLO claims.
4. Create service-specific liveness/readiness and dependency smoke; /health alone does not prove canonical readiness.
5. Test worker restart, duplicate event, gap, Redis flush, SignalR disconnect, Foundry outage and backfill.
6. Specify backup/recovery targets then run actual PostgreSQL restore; distinguish proposed RPO/RTO from results.
7. Tag metrics/traces with match/revision/correlation without exposing personal data; instrument retry storm and dead-letter counts.
8. Document rollback by immutable digest, schema compatibility, operations playbooks and incident escalation.
9. Produce the deliverables below, record risks/assumptions and route independent reviewers.
10. Give an explicit PASS / CHANGES REQUIRED / BLOCKED verdict **only for checks actually performed**. No fabricated build, Azure, model or deployment results.

## Required deliverables
- SLO/alert dashboard design, golden smoke and fault-injection report.
- Runbook with ownership, rollback and tested restoration evidence.
- A concise decision record: requirement → changed component/contract → test ID → exact evidence link/commit/environment or **unverified** → owner.
- Positive, negative and degraded examples, including impact on time consistency, security, deterministic truth and local-vs-Azure profiles where applicable.

## Stop and escalate
**Blocking condition:** Block promotion of a path with no recovery or data-integrity visibility.
**Handoff:** DevOps for release gates; PostgreSQL for restore; Performance for load; Security for incident boundaries.
**Nondelegable rule:** Do not merge or deploy on an assumed approval. Treat an owner-reviewed PR merged to protected `main`, with required validation green, as authorization for that commit's automatic application release to DEV; STAGE/PROD need separate protected approvals. Never describe a target-only adapter as implemented.

## Role-specific story gate and executable evidence

**Primary epics:** EP02, EP06, EP09, EP10, EP11. **Gate to enforce:** Prove PostgreSQL errors cannot become fabricated live score, SignalR fallback/backfill, Redis purge replay invariance, Foundry degradation and observed RPO/RTO.

**Mandatory deliverable:** Versioned SLIs, outage drills, incident runbook and recovery metrics. Attach exact story ID, current source hash, affected HLD/LLD/contract, actual negative tests, review and blockers to the story register, separating observed and target.

**Handoff to:** performance-engineer, devops-architect, postgresql-engineer. No claims of DEV integration from a diagram. Follow the protected-main and successful-validation release rule for application changes; live infrastructure mutations still need scoped owner approval.
