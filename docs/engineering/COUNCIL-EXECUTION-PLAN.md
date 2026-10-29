# PitchMind Engineering Council — Hackathon Execution Programme
**Baseline:** main 287d0b5 (2026-10-08); **target submission:** 2026-10-27 (passed); **status:** historical execution plan. Its schedule and checklist are not current delivery evidence or blanket Product Owner acceptance. Current story states remain in the [execution register](../backlog/STORY-EXECUTION.md); current Azure facts and limitations are in the [DEV integration status](AZURE-DEV-LIVE-INTEGRATION-STATUS.md). Sources: [Hackathon stories](../backlog/HACKATHON-MVP-EPICS.md), [M01–M12](../backlog/MVP-BACKLOG.md), C4/ADRs and design mockups.

## Non-negotiables
- All 53 Hackathon stories remain in scope of the **portfolio**; P0 must be release-certified for the hackathon, P1 tracked and completed in separate gated workstreams. Never mark stories accepted merely because code or a design exists.
- No match/team/player/score/event/tracking/narrative facts hardcoded in C# or React. Data comes from approved JSON, canonical PostgreSQL, and versioned deterministic calculations. No mockup number is a domain truth. Labels and algorithm constants are okay only when football-approved and configured.
- The user-referenced `data/simulations/manu-liv-match-dataset.json` **arrived on main after the first review** (`b978254`). Its observed schema is documented in [feed profile](../data/MANU-LIV-SOURCE-PROFILE.md). Reading/profile is complete, but actual importer/normalizer, manifest for source schema/time/coordinate semantics, canonical event mapping and football realism sign-off remain open; owner-confirmed synthetic provenance and redistribution permission are recorded. Block *release/claim of source correctness*, not source-schema discovery. The former C# `SyntheticFixtureGenerator` has been moved to test-only support; current API match routes fail closed pending canonical feed admission and PostgreSQL-backed reads.
- Hackathon uses synthetic football-realistic data, not actual EPL matches; EPL-context names/images only with provenance clarity and appropriate rights. Show `Simulated match` prominently.
- LOCAL = Docker Compose/local adapters; DEV/STAGE/PROD = Azure services only. Domain has zero external dependencies; PostgreSQL is canonical, Redis rebuildable, Foundry explains but never invents truth. GitHub hosts source; Azure DevOps owns CI/CD.
- **NO DEV DEPLOYMENT without a fresh, specific approval from the owner.** This includes Terraform/Bicep apply, new/changed cloud services, container app revisions, ACR push intended for DEV, migrations and enabling automatic DEV delivery. Local tests and PRs may proceed. STAGE/PROD require separate approvals.

## Council ownership, interfaces and parallel tracks
| Track | Accountable role / council reviewers | Stories | Owned code / output |
|---|---|---|---|
| Platform | Principal Azure, DevOps, SRE, security, FinOps | EP01, EP02, EP10-S03/S04 | `infra/`, `pipelines/`, `docker/`, environment/rollback docs |
| Canonical data | C# principal, PostgreSQL, streaming, DDD | EP03, EP04 | `Domain`, `Infrastructure`, feed import, ledger, outbox |
| Football science | PO, football analytics, QA, distinguished engineers | EP05, EP10-S02 | concept dictionary, mechanisms, evidence, goldens |
| Transport & BFF | API lead, event systems, AppSec | EP06, EP09-S01 | match APIs, SignalR, auth, cursor/backfill |
| Frontend | React lead, accessibility, UI/UX | EP07, EP08-S06 | seven screens, match-time context, design system |
| Agentic AI | Foundry architect, AI safety, security | EP08-S01–S05, EP09-S02/03 | evidence tools, structured agents, validation |
| Release & operations | QA, technical writer, FinOps, PO | EP10, EP09-S04 | evidence manifests, demo, exports, recap |

One real accountable engineer per story is assigned by the owner; 'engineering council' denotes review perspectives, not available headcount or fake signatures. Changes to shared files (Domain IDs/clock, `Program.cs`, API.md, C4) are serialized by an integration owner. Frontend can build against versioned synthetic contract fixtures **without baking their data into React**.

## Waves and dependencies (proposed—not staffed guarantee)
| Wave | Days (2026) | Parallel outputs | Exit gate |
|---|---|---|---|
| G0 | Oct 8–9 | Feed inventory; story assignment; HLD/LLD/API/rights sign-off; protect build baseline | JSON profiled, units/rights signed and mapping contract frozen |
| G1 | Oct 9–12 | Local JSON parser/validator; canonical PostgreSQL/migrations; shell/routing/accessibility skeleton | deterministic fixture replay; no code facts |
| G2 | Oct 12–16 | Event Hubs producer/consumer; 1 reviewed pressing mechanism; evidence/projections; match BFF, pitch | football + sequence/duplicate and evidence goldens |
| G3 | Oct 16–20 | SignalR reconnect; Entra identity; Foundry validation/fallback; seven screen flows; pipeline security | local E2E and fail-closed behavior |
| G4 | Oct 20–23 | security/axe/Playwright, 500 eps defined burst, cost/readiness/rollback tests, video rehearsal | signed local release candidate + what-if (read-only) |
| Approval | **after G4** | Owner receives SHA/digest, Azure DEV diff, cost ceiling, ingress/egress, migration and rollback | **await explicit DEV approval** |
| G5 | Oct 23–26 *only if approved* | Azure DEV integrated deployment, HTTPS smoke, real Foundry and event feed verification | release certification |
| Submit | Oct 27 | Public sanitized repo; short pitch, Microsoft value proposition, public video **under 120 seconds** | PO and security clearance |
| Post-MVP | thereafter or earlier if capacity | Operational extensions (cache, recaps, exports, background jobs) | independent acceptance |

Schedule is a delivery proposal. All 53 Hackathon stories cannot honestly be declared complete until individually proven; P1 items do not silently become P0.

## Story execution lifecycle
`Not started → Ready → In progress → PR review → Verified → PO accepted`; blockers are explicit, separate from `not started`. Every EPxx-Syy story must include actor/football question, contract, HLD, LLD, positive/negative/insufficient-data fixture, privacy/auth, performance budget, test paths, owner, dependencies, rollback and evidence link. Acceptance text in the authoritative backlog is immutable absent PO review.

1. Register one issue per stable story ID (not required to be created all at once); link to `HACKATHON-MVP-EPICS.md` and per-story work packet. Use `feat/epxx-syy-*` branches; small PRs and no unreviewed main writes.
2. Contract-first: JSON profile → canonical envelopes / tracking → PostgreSQL transaction/outbox → materialized match projection and evidence → REST BFF/SignalR → UI → Foundry. Interface consumers develop in parallel with contract tests.
3. PR checks: .NET restore/build/test (fail on skipped mandatory integration), negative football goldens, DB role tests, OpenAPI compatibility, Node build/lint/test, Playwright + axe, Bicep compilation/read-only what-if, SAST/SCA/SBOM/secret checks, localized errors and reduced-motion tests.
4. Major boundary change must update C4 L1–L3, sequence, LLD, ADR, API.md and runbook **in the same PR**. Ban silent contract drift.
5. Proof: link exact test command/result and output artifact, build SHA/digest, fixture+schema+detector versions, input hash, reviewed football scenario, threats, deploy evidence where applicable. Attach to `STORY-EXECUTION.md`.
6. Failed/unknown evidence means `In progress` or `Blocked`, never `Accepted`.

## Workstream handshake
- **Data → domain:** EventEnvelope v1, period/time/coordinate metadata, stable IDs and source hash, source pointer, quarantine reason.
- **Domain → BFF:** canonical revision/last sequence, snapshot at `selectedTime`, key moments, truth-classified evidence IDs and no-lookahead guarantees.
- **BFF → React:** explicit match/time context and revision, authoritative timestamps, structured missingness, cursor and auth; React NEVER recalculates football truth.
- **Domain → agents:** bounded read-only MatchIntelligence snapshot, authoritative event/evidence IDs and limitations, prompt-injection-isolated source text.
- **AI → UX:** validated structured response or labeled deterministic fallback; never fabricate stats/coach intent, even if Foundry is unavailable.
- **Ops → team:** LOCAL e2e fixtures, environment parity matrix, immutable image manifest; DEV deployment remains disabled until approval.

## Release evidence gates
G0 source licensed and synthetic; G1 canonical durable replay/ordering/outbox; G2 tactical positive, negative and missing-frame cases and evidence resolution; G3 accessible synchronized user journey + authentication; G4 Foundry grounding/failure + security and load tests; G5 authorized Azure-only DEV connectivity, perf, observability, restore/rollback and budget; G6 public video under two minutes and public sanitized repo. No P0 story closes on a code-only test.

## Blocking items and owner decisions
**Current open items (reviewed 2026-10-29):** **B01** Candidate JSON has owner-confirmed synthetic provenance and redistribution permission; independent rights/privacy review, clock and coordinate semantics, football review, and canonical admission remain open. **B02** Candidate data remains isolated from canonical match records. **B03** Canonical PostgreSQL API routes are implemented, but Azure connectivity/readiness and current data availability require runtime verification. **B04** Azure DevOps pipeline is present and guarded; the self-hosted Mac agent is currently stopped, while service connections and protected server-side checks require verification in Azure DevOps. **B05** The reported DEV 503 issues, including the Foundry response-size failure, are resolved. The Foundry bound was corrected in main commit `dbd04f1f`; continue routine API and feed health checks for regressions. **B06** Current candidate source and UI remain labeled synthetic/non-canonical; EPL-context names and any imagery still require rights-aware review. **B07** Do not treat historical suite results or the 2026-10-17 completion report as current blanket story verification or acceptance. **B08** Verify each new DEV release by its deployed digest and observed health/data path; prior prototype evidence does not transfer to a later image. See [DEV integration status](AZURE-DEV-LIVE-INTEGRATION-STATUS.md).

## Mandatory DEV authorization request after G4
Provide: exact branch/SHA + container digest, subscription/resource group/region, Bicep what-if diff and deleted/created/modified resource list, database migration/rollback, managed identities, public ingress, expected monthly budget and AI quota, smoke tests and stop conditions. Then ask the owner **"Do you approve deploying this exact change set to Azure DEV?"** No cloud-mutating operation before affirmative approval.

## Execution governance route (2026-10-08)
This plan has a machine-checkable [53-story dependency DAG](../operating-model/STORY-EXECUTION-ROUTING.json) with work packet, role owner, consulted skill, priority and all-story DEV approval flag. Use the [human-readable implementation roadmap](../operating-model/ALL-STORIES-EXECUTION-ROADMAP.md) to assign **actual named engineers**; until then `assignedHuman=null` is correct. The 28 skills are advisory expertise rather than autonomous staff. Source live feed [already present](../data/MANU-LIV-SOURCE-PROFILE.md), but license/coordinate/realism/canonical adapter acceptance remains open. No Azure deployment is authorized by the documentation or validation scripts.
