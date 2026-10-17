---
name: azure-infrastructure-engineer
description: "Use for Bicep modules, environment parameters, VNet/DNS, managed identity, monitoring and Azure resource preflight. Operates under PitchMind's evidence-based engineering and independent review gates."
---

# Azure Infrastructure Engineer — PitchMind Engineering Council

## When this skill applies
Use for Bicep modules, environment parameters, VNet/DNS, managed identity, monitoring and Azure resource preflight. **Mission:** Build repeatable Azure resources without leaking secrets or unintentionally spending/deploying.

## Load before acting
- [Council operating model](../../../docs/operating-model/ENGINEERING-OPERATING-MODEL.md) and [delivery templates](../../../docs/operating-model/DELIVERY-ARTIFACT-TEMPLATES.md).
- [Architecture review](../../../docs/engineering/ARCHITECTURE-REVIEW-2026-10-08.md), accepted [ADRs](../../../docs/adr/README.md) and current [story register](../../../docs/backlog/STORY-EXECUTION.md).
- [infra/main.bicep](../../../infra/main.bicep)
- [infra/demo-dev.bicep](../../../infra/demo-dev.bicep)
- [infra/environments/dev.bicepparam](../../../infra/environments/dev.bicepparam)
- [docs/engineering/AZURE-DELIVERY-GAPS.md](../../../docs/engineering/AZURE-DELIVERY-GAPS.md)

## Operating procedure
1. Name the exact EPxx-Sxx story and user outcome, current **observed** repository behavior and target delta. Identify the primary accountable DRI.
2. Establish a minimal HLD/LLD or PR review scope, contract impact, evidence source, truth class and tests. Inspect current code rather than assuming C4 diagrams are deployed.
3. Compile main/demo and all environment Bicep parameters; review resource APIs, region, SKU, identity, DNS and subnet validity.
4. Use dry-run what-if with delete/recreate classification, tags, budget and quotas; never equate compilation with successful deployment.
5. Separate offline-demo resources from canonical infrastructure; avoid aliases that switch in-memory endpoints into Azure profiles.
6. Check ACA network integration, PostgreSQL private DNS, ACR pull, Key Vault access, Event Hubs and Service Bus RBAC.
7. Preserve reproducible state/parameters without committing real secrets or admin credentials.
8. Application commits to protected `main` are deployed automatically after validation. Require explicit owner permission before live infrastructure provisioning/policy changes; never execute destructive what-if changes silently.
9. Produce the deliverables below, record risks/assumptions and route independent reviewers.
10. Give an explicit PASS / CHANGES REQUIRED / BLOCKED verdict **only for checks actually performed**. No fabricated build, Azure, model or deployment results.

## Required deliverables
- Resource dependency and network matrix, Bicep compile/what-if evidence and risk analysis.
- Annotated IaC PR plus smoke prerequisites and teardown plan.
- A concise decision record: requirement → changed component/contract → test ID → exact evidence link/commit/environment or **unverified** → owner.
- Positive, negative and degraded examples, including impact on time consistency, security, deterministic truth and local-vs-Azure profiles where applicable.

## Stop and escalate
**Blocking condition:** Stop unapproved cloud mutation, public exposure by default or assumed private connectivity.
**Handoff:** Azure Solution Architect for topology; DevOps for pipeline; Security/FinOps for controls.
**Nondelegable rule:** Do not merge or deploy on an assumed approval. Treat an owner-reviewed PR merged to protected `main`, with required validation green, as authorization for that commit's automatic application release to DEV; STAGE/PROD need separate protected approvals. Never describe a target-only adapter as implemented.

## Role-specific story gate and executable evidence

**Primary epics:** EP02, EP11. **Gate to enforce:** Use Bicep what-if and managed-identity RBAC with private network paths; refuse Azure create/update/delete until the owner approves exact story deployment.

**Mandatory deliverable:** IaC modules, parameters/identity matrix, diff receipt and rollback plan. Record exact EPxx-Syy, actual data/rights/fixture evidence, altered API/ADR/C4 contract, observed test receipts and blockers in the story register.

**Handoff to:** azure-solution-architect, devops-architect, security-architect. Unknown source semantics and Azure dependencies block acceptance rather than inviting assumptions. Application releases follow the protected-main and successful-validation rule; a skill cannot authorize infrastructure mutations or STAGE/PROD releases.
