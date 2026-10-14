# PitchMind — Engineering Council Operating Model (E2E)

**Version:** 1.0, 2026-10-08. **Applies to:** all 59 backlog stories and future changes. **Status:** proposed working agreement, pending owner adoption. **Primary purpose:** consistently deliver the smallest **truthful, operable, secure, accessible** match intelligence increment. This is an accountability model, not a claim that 28 specialists are employed or autonomous agents can approve releases.

## 1. Constitutional invariants

1. Product accepts outcomes, not documents, scaffolding, green individual tests or screenshots.
2. A match's selected timestamp controls score, timeline, pitch, evidence, AI packet and explanation. Later facts do not leak backward.
3. Observed fact, deterministic derivation, evidence-supported pattern, interpretation, hypothesis and simulation are separate truth classes. An AI agent never authors canonical truth.
4. Domain stays pure; ports isolate infrastructure. Match/time/identity/schema, the existing offline demo uses pitch metres [0..105] × [0..68], while the new JSON source has **unapproved coordinate units**; period, orientation, metre conversion and versioned evidence must be confirmed before canonical import.
5. PostgreSQL is canonical; Event Hubs ingests, Service Bus commands, SignalR broadcasts; Redis/search are rebuildable. At-least-once with idempotence; never assert cross-system exactly-once.
6. LOCAL uses Docker and local adapters. DEV/STAGE/PROD use Azure services only. GitHub hosts source; Azure DevOps exclusively builds, validates and releases.
7. Security, privacy, accessibility, observability, resilience, cost and documentation are built into designs rather than final-stage additions.
8. **User/owner permission is required before deploying stories to DEV**; STAGE/PROD additionally require protected-environment approvals. If pipeline automation conflicts, keep the deploy disabled and raise the conflict. Creating a PR is not permission to merge or deploy.
9. The existing synthetic-only DEV demonstration is not proof of canonical cloud functionality. No service is called active because it appears in Bicep or a C4 diagram.
10. The engineering council is advisory; human owners retain responsibility. Specialists must make uncertainty, dissent and missing evidence visible.

## 2. Council structure and delegation

- **Product authority:** Product Owner owns problem, P0/P1/P2 tradeoffs, football copy, acceptance and scope cuts. Football Analytics co-signs claims; Product Owner cannot waive a safety/security policy by changing wording.
- **Architecture authority:** Principal Architect is architecture DRI; Distinguished Engineer is independent reviewer for high-blast-radius or irreversible decisions; Staff/Principal Engineer drives cross-team executable design and integrates contracts. New ADR needed for major tradeoffs.
- **Implementation authority:** Story DRI (usually lead backend or frontend) owns code, tests, scope, blockers and demonstration; domain/data/infra specialists own their bounded interfaces. The DRI may not self-certify production safety.
- **Independent controls:** Security/AppSec, SRE, QA, Accessibility, Football Analytics and FinOps have domain-specific blocking gates with concrete reproduction/evidence; conflicts are escalated to Product Owner + Principal Architect and cannot override an unsafe gate by silence.
- **Release authority:** DevOps runs pipeline mechanics; service owner provides operational sign-off; **repository owner explicitly authorizes DEV deployment**; protected Azure DevOps Environment approval controls STAGE/PROD.
- **Single accountable owner per story and release;** multiple consulted skills, never a 28-person required PR approval queue.

### Specialist engagement routing

| Change surface | Required review partners |
|---|---|
| Match truth, possession, tactical insight, scoring, replay, coordinates | Football Analytics + DDD + QA; principal architect if wire/aggregate boundary changes |
| Event schema, ingestion, ordering, projection/outbox | Data + Event Streaming + Distributed Systems + PostgreSQL + QA |
| API, BFF, identity, SignalR, browser data flow | Backend + Frontend + Security/AppSec + Accessibility |
| Foundry, agents, evidence/claim verifier | AI architect + Football Analytics + AppSec + QA + FinOps |
| VNet, IAM, Bicep, CI/CD, migration, release | Azure Architect + Infra + DevOps + Security + SRE + FinOps |
| UI/UX and narrative change | Product + UX + Accessibility + Frontend + Football Analytics |
| Performance, throughput, reliability | Performance + SRE + owning domain/service lead |
| Documentation/contract | Technical Writer + relevant technical owner |

## 3. Story lifecycle with concrete stage gates

| Gate | State / accountable DRI | Mandatory exit artifact and checks | Block conditions |
|---|---|---|---|
| G0 Intake | Product Owner | Story ID in authoritative backlog, user question, P0 priority rationale, acceptance, source data rights, measurable success, owner | No verifiable football/user outcome; vague 'add AI' |
| G1 Discovery / architecture | Principal Architect + Staff | Current-vs-target diagram, impacted C4/ADR, dependencies, trust and evidence boundaries, risk map, options/tradeoffs; recorded ADR if necessary | Contradictory canonical owner, prohibited local-in-cloud adapter, unbounded service dependency |
| G2 HLD/LLD & contract | Story DRI + relevant specialists | HLD dataflow/deployment/failure paths; LLD types, tables, pseudocode, schemas, migration/rollback and tests; contract examples incl. negatives | Missing sequence/time/coordinate/provenance semantics or testability |
| G3 Ready | Product Owner + story DRI | Definition of Ready: owner, estimate, dependencies, examples, interface owners, acceptance cases, plan for evidence | Dependency unknown, unlicensed input, claimed implementation unverified |
| G4 Implementation / PR | Story DRI | Small feature branch/PR, code, local unit/integration/property/contract tests, telemetry, docs, threat delta, toggles and reversibility | Undeclared architectural bypass or unjustified test omission |
| G5 Independent validation | QA + security/football/accessibility as applicable | Azure DevOps clean build + test receipts; adversarial goldens; data correctness, latency and failure injection; reviewers independent of author | CI not observed, unsupported claim, unresolved P0 accessibility/security flaw |
| G6 Integrated / DEV-ready | SRE + DevOps + service DRI | Immutable image/artifact digest, schema compatibility, Bicep what-if, managed identity/private DNS checks, synthetic end-to-end smoke plan, rollback and budget limit | Missing cloud dependencies, disabled privacy/security gate, absent rollback, uncertain owner approval |
| G7 DEV deploy | **Repository owner** approves; DevOps executes | Explicit recorded approval for this story/change **before** any DEV deployment; approved pipeline deploy by digest; real health + match golden + negative smoke links | No owner approval; automatic main→DEV could deploy without consent |
| G8 STAGE/PROD promotion | Owner + protected Azure DevOps Environment approvers | Same immutable digest, migration compatibility, resilience/performance/security/backup cert, SLO/cost review, canary/rollback, environment approval | Rebuild on promotion, missing independent review or live verification |
| G9 Operate / learn | SRE + Product Owner | Observability dashboards, incident runbook, post-deploy golden, product feedback, error-budget review, follow-up work, docs currency | Silent failure, unowned alarms or undocumented behavior |

**Status vocabulary:** Not started → Design in progress → Ready → Implementation in progress → PR under review → Verified locally/in CI → DEV deployment approved → Verified in DEV → Ready for promotion → Verified in STAGE/PROD. 'Blocked' annotates any state with owner and unblocker. Never infer verification from file existence. Maintain story register updates with evidence refs and exact SHA/revision/environment.

## 4. Parallel execution and sequencing

A. **Critical vertical-slice chain:** governed fixture/event contract → event admission/order/idempotency → PostgreSQL canonical ledger + tracking/projection/outbox → deterministic replay → mechanism/evidence → versioned selected-time BFF → synchronized UI → bounded AI packet/validator/fallback → Azure target integration → release certification.

B. **Parallel lanes:** UX prototypes may use an explicit synthetic adapter; infrastructure can build IaC/identity while data contracts stabilize; QA prepares golden fixtures early; security builds threat model before HTTP and agent exposure. A lane can merge only if compatibility tests for shared contracts pass.

C. **Integration fence:** each lane publishes the same matchId/eventId/sequence, elapsed time/period, event and algorithm versions, truth class and provenance. Coordinate convention is stadium metres, with team-relative transformation only at the boundary.

D. **Story slicing:** prefer one end-to-end causal path rather than all optional Azure services. Unnecessary Redis/ADX/AI Search/Functions/Front Door are explicitly deferable. No blind parallel implementation of competing canonical stores.

E. **Dependency management:** story DRI maintains dependency DAG, risk register, external quotas/rights and blocked interfaces; owners renegotiate plan weekly when blockers appear.

## 5. Engineering artifacts and definitions

Use DELIVERY-ARTIFACT-TEMPLATES.md for a Story Dossier, HLD/LLD, verification matrix and release manifest. The authoritative backlog stays docs/backlog/HACKATHON-MVP-EPICS.md; execution state stays docs/backlog/STORY-EXECUTION.md. Do not generate a second competing backlog.

**HLD content:** C4 impact, bounded contexts/owners, API/events, trust boundaries, flow and failure diagrams, consistency guarantees, scalability/capacity, observability, cost, deployment/runtime identity, degradation and tradeoffs.

**LLD content:** contracts, domain types/reducer invariants, versioned JSON schema, database migration and indexes, concurrency/idempotency, time/coordinate transformations, pseudocode, authN/authZ, cache keys, outbox/inbox/cursor, error paths, tests and feature flags.

**Definition of Done:** user-visible result demonstrated; supporting proof traceable to branch SHA and environment; no false claim; representative negative/failure cases pass; service/infrastructure docs and runbook updated; owner accepts. A rollback plan is not proof a rollback succeeded.

## 6. Test & evidence pyramid

- Domain: pure tests, invariant/property testing, historical timestamp/no-future-leak goldens, mirrored spatial transforms and match-rule invalids.
- Application: port contract tests, same-event twice/no double goal, gap/conflict quarantine, selected-time API snapshots, evidence graph resolution.
- Infrastructure: actual PostgreSQL integration and migration/runtime role denial, outbox crash/retry, Event Hubs duplicate/lag, Service Bus DLQ, Redis flush/rebuild, SignalR reconnect/backfill; local adapters cannot prove Azure contracts.
- AI: fixed evidence packet, supported/unsupported claims, malicious tool output and prompt injection, selected-time boundaries, Foundry outage; determinism never depends on model.
- UI: Playwright flow of Discover → Understand → Why → Ask → Replay, WCAG 2.2 AA-targeted checks, keyboard/focus/reduced motion, offline/stale/loading/error.
- Nonfunctional: 500 events/s with specified burst duration/payload, latency p50/p95/p99, queue lag and recovery, auth abuse tests, cost budget and restore drill, captured on actual target environment before certification.

Each test result records command, runner/environment, commit SHA, dataset+schema+detector version, timestamp, pass/fail, limitations and link to raw output. Do not label static review as CI execution.

## 7. PR, escalation and review protocol

**Story kickoff:** Product Owner publishes problem, accepted source data, examples, priority and deadline. Principal Architect routes specialists. Story DRI attaches design and a **decision log** capturing options/rejected approaches.

**During build:** share contracts early; parallel agents only modify owned bounded paths or coordinate handoffs; record incompatible assumptions as blockers instead of silently inventing behavior. Run local commands before claiming success.

**PR review:** self-review is insufficient for risky changes. Use CODEOWNERS or explicit reviewers for surfaces; domain reviewer checks football meaning, QA checks tests, AppSec checks boundaries, SRE checks failure paths. Nonblocking comments have remediation ticket and owner; blockers require fix or documented safe deferment.

**Dissent protocol:** specialist cites a contract/invariant, proposed violating change, concrete failure case and safer option. DRI proposes resolution; unresolved P0/security/truth issues pause merge/deploy. Record ADR for durable disagreements.

**Deployment change control:** PR merge, infrastructure what-if, provisioning, paid service creation, identity/RBAC mutation, DEV release, STAGE and PROD release are distinct actions. No inferred permission. For DEV story deployment obtain the owner's affirmative approval referencing change scope, digest and rollback; if no answer, do not deploy.

## 8. Measures and cadences

Daily async: story state, blocker, evidence URLs, integration readiness, risk. Twice-weekly architecture clinic: only cross-boundary changes and dissent. Weekly product demo: show an end-to-end user question answered with evidence. Release readiness: change-specific independent gate signoffs. Post-incident: blameless documented learning and executable regression.

Metrics: lead time for change, deployment frequency, failed change rate, time to restore; story acceptance/cycle time; golden football correctness and unsupported AI claim rate; ingest lag, dropped/quarantined events, selected-time consistency, API latency/availability, accessibility escape defects; Azure spend per simulated match and Foundry request. Baselines and SLOs must be measured and explicitly agreed, never fabricated.

## 9. Skill orchestration

Each role under ../../.github/skills/ is a **task-loaded capability**, not a pretend human employee. Prompt it using a story ID and artifacts. Skills read this model and the source ADRs before giving recommendations. The Principal Architect identifies required reviewers; Product Owner names a single DRI; QA verifies evidence; owner retains deployment approval. Skills cannot silently alter canonical football policy, approve themselves, or assert external execution they did not observe.
## 10. Original EPL-context source and zero-hardcoded-data transition

The Manchester United–Liverpool JSON (2,199 events; final raw `clockSeconds` value 5,761, with unit and clock interpretation unverified) exists and was structurally profiled. The repository owner confirmed synthetic provenance and redistribution permission on 2026-10-08; independent rights/privacy review and football approval remain open. Before EP03/04/05 or screenshots can make source-specific claims: review coordinate units and orientation, clock/stoppage and roster/correction rules, player tracking quality and exaggerated event-frequency realism. Do not expose heartRate-like attributes as actual measured player health data. Source normalization must preserve original event IDs and 1-based sequence alongside canonical 0-based sequence. This is tracked in [simulation admission gates](SIMULATION-DATA-ADMISSION.md).

**No club/player/score/event/position/narrative facts may be hardcoded in production C# or React.** Migrate the current demonstration fixture behind a file-fed application port, versioned canonical data and BFF contracts. Do not pass a mockup or seed literal as accepted football truth. Unitless positions cannot satisfy a 3-metre pressure rule. Until the manifest is signed, continue read-only structural development/tests and keep source-specific football claims blocked.

**Operating model review closure:** [all 59 story routes](ALL-STORIES-EXECUTION-ROADMAP.md), [28 skill registry](COUNCIL-SKILL-REGISTRY.json), [static evidence](STATIC-VERIFICATION-2026-10-08.md) and [current/target diagrams](../architecture/README.md). An approval to merge docs or create CI gates must never auto-authorize Azure provisioning, image push, migrations or application deployment.

## Frontend engineering council: three design skill passes
For the seven planned screens (EP07-S01..S06 plus EP08-S06), the accountable frontend/UI roles **must call on all three specialist skills**: Taste for visual generation/refinement, Vercel Web Design Guidelines for independent code/a11y review, and Awesome Design for rubric-scored critique against actual browser output. Apply [source and test precedence](../design/FRONTEND-DESIGN-DELIVERY.md); archive screenshots, change-focused `file:line` findings, 8-category anti-generic review, premium 100-point gate and real accessibility/performance evidence. Design quality cannot authorize invented EPL match content or a DEV deployment.
