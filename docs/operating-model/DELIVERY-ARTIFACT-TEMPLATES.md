# Delivery artifact contracts

Copy the relevant sections into a story-specific design document or PR description. Do not fill unknowns with guesses.

## Story Dossier (G0/G3)
- **Backlog ID + permalink / priority / owner / target environment:**
- **Actor / question / user-visible behavior:**
- **Why now / business and football meaning / out of scope:**
- **Input source:** fixture/provider/license/schema/version/provenance:
- **Truth classification and observable evidence:** required events, frames, selected time, completeness:
- **Acceptance:** positive, negative, degraded, accessibility and nonfunctional:
- **Dependencies/affected components:** APIs, ports, migrations, services, permissions:
- **Risks, estimate, blocker owner, escalation/approvals:**
- **Proof ledger:** commit SHA, exact tests, pipeline runs, environment, demo URL/screenshot if available:

## HLD package (G1/G2)
1. Context diagram annotated CURRENT vs TARGET, affected C4 levels, ADR alignment.
2. Requirement → component/owner → contract → test traceability.
3. Happy path + failure path with consistency, idempotency, replay, data/claim trust boundaries.
4. Environment-specific component graph: LOCAL adapters and Azure-only DEV/STAGE/PROD, identity/network/regions.
5. Quantitative workload assumptions and cost/latency/SLO **hypotheses** versus measured values.
6. Tradeoff/alternative table, threat model, unknowns, migration/rollback and rollout safety.

## LLD package (G2)
1. Domain entities/aggregates/invariants and time/period/coordinate/provenance conventions.
2. Request/response or message schema samples with versioning and compatibility policy.
3. Reducer/detector/processor pseudocode and edge cases; no implicit manager intent.
4. PostgreSQL DDL, indexes, read/write transaction, migration/runtime permissions, outbox/inbox/cursors.
5. Read-only AI context, validation and fallback if applicable; identity/auth matrix.
6. Error/retry/dead-letter/backfill/cache invalidation and tracing behaviors.
7. Unit, golden, property, integration, load, access-control and failure-injection matrix.
8. Task breakdown by directory/component and explicit interface handoff points.

## Verification matrix (G5)
| Requirement/claim | Test ID | Command or pipeline run | Fixture/algorithm/schema | Environment + SHA | Result | Evidence URI | Owner |
|---|---|---|---|---|---|---|---|
| Example: replay at 55:08 suppresses 55:09 goal | fill | fill | fill | fill | unverified until run | fill | fill |

Always include tests proving a forbidden claim or a duplicate input **does not** pass.

## PR review checklist (G4/G5)
- [ ] Story ID and user question, changed architecture and source constraints linked
- [ ] Contracts stable; Domain no infrastructure dependency; no unsupported football inference
- [ ] Historical selected time/score/pitch/evidence/AI consistency proven
- [ ] Dedupe, partial ordering, crash recovery and read-only tools verified where relevant
- [ ] AuthN/authZ, secrets, supply chain, cost and privacy risk covered
- [ ] Keyboard/screen reader/reduced motion, empty/error/stale UI reviewed as applicable
- [ ] Clean Azure DevOps CI evidence linked; local output labeled as local
- [ ] Security/football/QA/SRE reviewer comments dispositioned
- [ ] Migration, rollback, observability and documentation addressed
- [ ] No DEV deployment was triggered without explicit owner approval

## Release manifest and approval request (G6/G7/G8)
- Story set; Git commit; image **immutable digest**; deployment target and expected impact.
- Contracts: schema version, event/fixture/detector/model revision, migration/backfill and backward compatibility.
- Actual Azure network/RBAC/managed dependency check; Bicep what-if summary and cost delta.
- Test links: clean CI, real database, end-to-end smoke, football/AI negatives, perf/resilience, security/accessibility.
- Rollback artifact/digest and data rollback or forward-fix decision; runbook and monitoring/alerts.
- Application DEV authorization: owner-reviewed PR merge to protected `main` with required validation. Record the commit, immutable digest, deployment and smoke evidence. Infrastructure and STAGE/PROD changes require separate scoped owner/protected-environment approvals.
- Post-deploy verification: Azure resource/revision, timestamp, smoke outcomes and exceptions.

**Never pre-check a box or invent an Azure test result.**
## Source-admission and football realism template (G0 / EP03)
- Raw source path, SHA256 and documented number of event rows / version and proof of synthetically generated provenance.
- Redistribution/licensing approval, real-name/team branding approval, privacy/biometric-like field review; separate rejected fields.
- Source coordinate unit/orientation and pitch size, period/stoppage clock semantics, 1-based source and 0-based canonical sequence mapping.
- Football plausibility report for unusually frequent shot/foul types, substitutions/reds, score transitions and event-attached tracking sufficiency.
- Positive/negative fixture goldens with source event IDs, no future leaking, missing-data response and approved case narratives.
- Data Engineer + Football Analytics + Product Owner named independent sign-offs; no auto-certification based on structural schema passes.
