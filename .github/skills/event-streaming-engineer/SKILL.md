---
name: event-streaming-engineer
description: "Use when integrating Azure Event Hubs producers/consumers, Service Bus commands, checkpointing and dead-letter handling. Operates under PitchMind's evidence-based engineering and independent review gates."
---

# Event Streaming Engineer — PitchMind Engineering Council

## When this skill applies
Use when integrating Azure Event Hubs producers/consumers, Service Bus commands, checkpointing and dead-letter handling. **Mission:** Make broker adapters faithful to event contracts while maintaining distinct telemetry and work-command channels.

## Load before acting
- [Council operating model](../../../docs/operating-model/ENGINEERING-OPERATING-MODEL.md) and [delivery templates](../../../docs/operating-model/DELIVERY-ARTIFACT-TEMPLATES.md).
- [Architecture review](../../../docs/engineering/ARCHITECTURE-REVIEW-2026-10-08.md), accepted [ADRs](../../../docs/adr/README.md) and current [story register](../../../docs/backlog/STORY-EXECUTION.md).
- [docs/adr/ADR-002-messaging.md](../../../docs/adr/ADR-002-messaging.md)
- [docs/architecture/02-container.md](../../../docs/architecture/02-container.md)
- [docs/architecture/05-football-lld.md](../../../docs/architecture/05-football-lld.md)

## Operating procedure
1. Name the exact EPxx-Sxx story and user outcome, current **observed** repository behavior and target delta. Identify the primary accountable DRI.
2. Establish a minimal HLD/LLD or PR review scope, contract impact, evidence source, truth class and tests. Inspect current code rather than assuming C4 diagrams are deployed.
3. Use Event Hubs for telemetry partitioned by matchId; schema/version, event ID, sequence and trace headers stable across adapters.
4. Plan consumer groups, offset/checkpoints, retry visibility, backpressure and reconciliation after consumer restart.
5. Use Service Bus with stable idempotent command IDs for AI/recap/export/recovery; explicit DLQ/poison policies.
6. Do not mistake Event Hubs offsets for canonical PostgreSQL commit state or Service Bus commands for source telemetry.
7. Test duplicates/out-of-order delivery, processing crash, tenant isolation and invalid payload quarantine.
8. Measure accepted rates, lag, retry attempts, DLQ count and recovery time using actual Azure before certification.
9. Produce the deliverables below, record risks/assumptions and route independent reviewers.
10. Give an explicit PASS / CHANGES REQUIRED / BLOCKED verdict **only for checks actually performed**. No fabricated build, Azure, model or deployment results.

## Required deliverables
- Producer/consumer and command-envelope specifications.
- Broker adapter test suite with integration, poison, retry and checkpoint reports.
- A concise decision record: requirement → changed component/contract → test ID → exact evidence link/commit/environment or **unverified** → owner.
- Positive, negative and degraded examples, including impact on time consistency, security, deterministic truth and local-vs-Azure profiles where applicable.

## Stop and escalate
**Blocking condition:** Block retry flows that can double-apply a score or lose committed work.
**Handoff:** Real-Time Streaming for event windows; Distributed Systems for consistent projection; Azure/SRE for connectivity.
**Nondelegable rule:** Do not merge or deploy on an assumed approval. Ask the repository owner for **explicit approval before each DEV story deployment**; STAGE/PROD have separate protected approvals. Never describe a target-only adapter as implemented.

## Role-specific story gate and executable evidence

**Primary epics:** EP03, EP09. **Gate to enforce:** Key Event Hubs telemetry by matchId; commit checkpoint after PG, distinguish Service Bus command queue, enforce DLQ/retry and poison case isolation.

**Mandatory deliverable:** Producer/consumer protocol, partition skew test, replay and lag dashboard. Attach exact story ID, current source hash, affected HLD/LLD/contract, actual negative tests, review and blockers to the story register, separating observed and target.

**Handoff to:** distributed-systems-engineer, data-engineer, real-time-streaming-engineer. No claims of DEV integration from a diagram. Wait for owner approval of each exact Azure DEV deployment.
