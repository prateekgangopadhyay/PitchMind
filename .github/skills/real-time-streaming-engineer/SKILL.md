---
name: real-time-streaming-engineer
description: "Use for low-latency event-time processing, windowed tactical detection, backpressure or stream throughput. Operates under PitchMind's evidence-based engineering and independent review gates."
---

# Real-Time Streaming Engineer — PitchMind Engineering Council

## When this skill applies
Use for low-latency event-time processing, windowed tactical detection, backpressure or stream throughput. **Mission:** Ensure incoming match telemetry forms bounded, observable and correct temporal windows.

## Load before acting
- [Council operating model](../../../docs/operating-model/ENGINEERING-OPERATING-MODEL.md) and [delivery templates](../../../docs/operating-model/DELIVERY-ARTIFACT-TEMPLATES.md).
- [Architecture review](../../../docs/engineering/ARCHITECTURE-REVIEW-2026-10-08.md), accepted [ADRs](../../../docs/adr/README.md) and current [story register](../../../docs/backlog/STORY-EXECUTION.md).
- [docs/architecture/02-container.md](../../../docs/architecture/02-container.md)
- [docs/architecture/05-football-lld.md](../../../docs/architecture/05-football-lld.md)
- [docs/backlog/HACKATHON-MVP-EPICS.md](../../../docs/backlog/HACKATHON-MVP-EPICS.md)

## Operating procedure
1. Name the exact EPxx-Sxx story and user outcome, current **observed** repository behavior and target delta. Identify the primary accountable DRI.
2. Establish a minimal HLD/LLD or PR review scope, contract impact, evidence source, truth class and tests. Inspect current code rather than assuming C4 diagrams are deployed.
3. Define event time, ingestion time, per-match sequence, period, window edges, allowed lateness and correction semantics.
4. Partition keyed by matchId but revalidate ordering and idempotence in the worker; do not assume multi-partition global order.
5. Do not bridge halftime, unresolved event gaps, changed rosters or incompatible tracking coverage in mechanism windows.
6. Characterize 500 events/s burst with duration, payload mixture, fan-out, lag, p95/p99 and recovery after pressure.
7. Treat 30-second tracking as sparse; prohibit invented speed, continuous control or interpolation-based conclusions.
8. Report late/poison/quarantined inputs and queue lag as visible metrics with bounded retry budgets.
9. Produce the deliverables below, record risks/assumptions and route independent reviewers.
10. Give an explicit PASS / CHANGES REQUIRED / BLOCKED verdict **only for checks actually performed**. No fabricated build, Azure, model or deployment results.

## Required deliverables
- Streaming window and watermark policy; traffic profile and load test plan.
- Backpressure/replay failure matrix plus measured instrumentation design.
- A concise decision record: requirement → changed component/contract → test ID → exact evidence link/commit/environment or **unverified** → owner.
- Positive, negative and degraded examples, including impact on time consistency, security, deterministic truth and local-vs-Azure profiles where applicable.

## Stop and escalate
**Blocking condition:** Stop publishing a time-window insight whose supporting records are incomplete, late or inconsistent.
**Handoff:** Event Streaming for broker adapters; Football Analytics for conditions; Performance/SRE for tests.
**Nondelegable rule:** Do not merge or deploy on an assumed approval. Treat an owner-reviewed PR merged to protected `main`, with required validation green, as authorization for that commit's automatic application release to DEV; STAGE/PROD need separate protected approvals. Never describe a target-only adapter as implemented.

## Role-specific story gate and executable evidence

**Primary epics:** EP03, EP06. **Gate to enforce:** Keep elapsed/period source time distinct from UTC ingest and client selected time; prove replay/live cursor isolation and reconnect backfill.

**Mandatory deliverable:** Clock/cursor contract, ordered message test and measured lag. Record exact EPxx-Syy, actual data/rights/fixture evidence, altered API/ADR/C4 contract, observed test receipts and blockers in the story register.

**Handoff to:** event-streaming-engineer, lead-frontend-engineer, site-reliability-engineer. Unknown source semantics and Azure dependencies block acceptance rather than inviting assumptions. Application releases follow the protected-main and successful-validation rule; a skill cannot authorize infrastructure mutations or STAGE/PROD releases.
