# PitchMind Engineering Council Agent Skills

**28 accountable Engineering Council role skills + 5 cross-cutting design specialist skills** (33 discoverable skill directories) in the standard .github/skills/<skill-name>/SKILL.md layout. Each has unique required name/description YAML frontmatter. GitHub Copilot and other Agent Skills-compatible assistants may load relevant skills when prompted, but their presence does not create real human staff, give agents deployment authority or prove tests were run.

Read [Engineering Council Operating Model](../../docs/operating-model/ENGINEERING-OPERATING-MODEL.md), [architecture review](../../docs/engineering/ARCHITECTURE-REVIEW-2026-10-08.md), [delivery templates](../../docs/operating-model/DELIVERY-ARTIFACT-TEMPLATES.md), the exact [backlog story](../../docs/backlog/HACKATHON-MVP-EPICS.md), and repository-wide [instructions](../copilot-instructions.md).

## Skill routing and handoffs

| Sequence | Accountable coordination | Specialist skills loaded on demand |
|---|---|---|
| Intake / Define | Product Owner | product-owner, football-analytics-engineer, ui-ux-engineer |
| Architecture review / HLD | Principal Software Architect | principal-software-architect, distinguished-engineer, staff-principal-engineer, domain-driven-design-specialist |
| Domain / streaming / persistence | Story DRI | lead-backend-engineer, distributed-systems-engineer, real-time-streaming-engineer, event-streaming-engineer, data-engineer, postgresql-engineer, redis-engineer |
| Experience / AI | Story DRI | lead-frontend-engineer, ai-agentic-systems-architect, football-analytics-engineer, ui-ux-engineer, accessibility-engineer |
| Azure / delivery | Platform DRI | azure-solution-architect, azure-infrastructure-engineer, devops-architect, site-reliability-engineer, security-architect, finops-engineer |
| Independent assurance | QA Lead | qa-architect, performance-engineer, application-security-engineer, accessibility-engineer, football-analytics-engineer |
| Developer enablement / clarity | Service DRI | developer-experience-engineer, technical-writer |
| Release | Human owner approval + DevOps | devops-architect, site-reliability-engineer, security-architect, qa-architect, product-owner |

## Invoking a council

Example request: "For EP04-S03, have the principal software architect, distributed systems engineer, PostgreSQL engineer and QA architect produce the HLD/LLD, outbox crash matrix, PR plan and acceptance evidence. Mark unrun tests as unverified. Do not deploy to DEV."

A role skill must issue concrete files/decisions/tests and a handoff; a generic persona debate is not a deliverable. The source backlog/ADR/C4 and observed code take priority over generic best practices. Never use all 28 skills by default; activate the smallest responsible review set.

## Frontend design specialist order (EP07 / EP08-S06)

Use [Taste](design-taste-frontend/SKILL.md) for React screen construction; [Impeccable](impeccable/SKILL.md) for focused audit/polish; [Emil design engineering](emil-design-eng/SKILL.md) for purposeful microinteractions; [Web Design Guidelines](web-design-guidelines/SKILL.md) for current UI/a11y checks; and [Awesome](awesome-design-audit/SKILL.md) for independent screenshot-grounded review. See `docs/design/FRONTEND-DESIGN-DELIVERY.md`. The Impeccable repo skill is a **local advisory adaptation**, not the upstream CLI installation; Emil's original text is vendored under MIT. Do not introduce motion dependencies or block unrelated product coding to manufacture a five-persona review.

## Full catalog
- [Product Owner](product-owner/SKILL.md) — Own verifiable user value from synthetic match discovery through context, evidence, explanation and replay.
- [MAANG Principal Software Architect](principal-software-architect/SKILL.md) — Own architectural coherence and traceable realization of C4 L1–L3, deployment and core engineering invariants.
- [Distinguished Engineer](distinguished-engineer/SKILL.md) — Challenge the architecture's hardest assumptions and protect correctness, operability and long-term optionality.
- [Staff / Principal Engineer](staff-principal-engineer/SKILL.md) — Translate cross-team architectural intent into compatible, testable changes without parallel contract drift.
- [Lead Backend Engineer](lead-backend-engineer/SKILL.md) — Deliver secure, maintainable ASP.NET and application-layer functions that expose only defensible match state.
- [Lead Frontend Engineer](lead-frontend-engineer/SKILL.md) — Build a synchronized, accessible, resilient experience that never invents football facts.
- [Distributed Systems Engineer](distributed-systems-engineer/SKILL.md) — Make at-least-once football processing safe under retries, reordering and partial outages.
- [Domain-Driven Design Specialist](domain-driven-design-specialist/SKILL.md) — Protect a pure versioned football domain without contaminating match meaning with infrastructure or AI.
- [Real-Time Streaming Engineer](real-time-streaming-engineer/SKILL.md) — Ensure incoming match telemetry forms bounded, observable and correct temporal windows.
- [Football Analytics Engineer](football-analytics-engineer/SKILL.md) — Make each tactical assertion reproducible, football-realistic and proportional to observed evidence.
- [AI / Agentic Systems Architect](ai-agentic-systems-architect/SKILL.md) — Provide useful candidate explanations without giving models authority over score, match state or evidence.
- [Microsoft Azure Solution Architect](azure-solution-architect/SKILL.md) — Translate the target C4 into a feasible least-complex Azure topology with explicit service readiness.
- [Azure Infrastructure Engineer](azure-infrastructure-engineer/SKILL.md) — Build repeatable Azure resources without leaking secrets or unintentionally spending/deploying.
- [DevOps Architect](devops-architect/SKILL.md) — Run observable, gated GitHub-source/Azure-Pipelines-only delivery with immutable artifacts and approval controls.
- [Site Reliability Engineer](site-reliability-engineer/SKILL.md) — Ensure users retain deterministic match truth even when streaming, cache, model or UI fan-out fail.
- [Security Architect](security-architect/SKILL.md) — Protect clients, canonical data, operator actions and model tools across explicit trust boundaries.
- [Application Security Engineer](application-security-engineer/SKILL.md) — Prevent malicious input, model output and API callers from corrupting truth or compromising the system.
- [Data Engineer](data-engineer/SKILL.md) — Admit only traceable, schema-valid, rights-cleared match data and publish reproducible derived datasets.
- [PostgreSQL Engineer](postgresql-engineer/SKILL.md) — Ensure persistent, replayable and verifiable canonical football state with explicit transactional guarantees.
- [Redis Engineer](redis-engineer/SKILL.md) — Make Azure Managed Redis a disposable accelerator that can never become match truth.
- [Event Streaming Engineer](event-streaming-engineer/SKILL.md) — Make broker adapters faithful to event contracts while maintaining distinct telemetry and work-command channels.
- [QA Architect](qa-architect/SKILL.md) — Turn every story's claims into independent positive, negative and degraded proof, not green-test theater.
- [Performance Engineer](performance-engineer/SKILL.md) — Quantify what PitchMind sustains and where it fails under realistic burst/stream/user workloads.
- [Accessibility Engineer](accessibility-engineer/SKILL.md) — Guarantee that match meaning and evidence remain usable without sight, mouse or animation.
- [UI/UX Engineer](ui-ux-engineer/SKILL.md) — Design an intuitive evidence-led football experience rather than a wall of charts or agent-debug widgets.
- [Developer Experience Engineer](developer-experience-engineer/SKILL.md) — Enable a clean-machine LOCAL workflow and reduce invalid assumptions in day-to-day development.
- [FinOps Engineer](finops-engineer/SKILL.md) — Maximize football evidence delivered per cost while limiting unattended Azure consumption.
- [Technical Writer](technical-writer/SKILL.md) — Keep implementation, architecture, instructions and evidence discoverable, precise and mutually consistent.

## Machine-enforced council routes

The [Council skill registry](../../docs/operating-model/COUNCIL-SKILL-REGISTRY.json) covers exactly 28 accountable roles; the [design specialist registry](../../docs/design/DESIGN-SKILL-REGISTRY.json) adds exactly 3 cross-cutting abilities, not extra Council roles. The 31 discoverable skill directories must match their combined registers. The [story routing DAG](../../docs/operating-model/STORY-EXECUTION-ROUTING.json) assigns a **skill role**, not an invented human engineer, to every original EPxx-Syy. The [execution roadmap](../../docs/operating-model/ALL-STORIES-EXECUTION-ROADMAP.md) describes dependencies, independent review and release constraints. Run `node scripts/validate_operating_model.mjs` before merging a skill or backlog update.

**Source caveat:** the 2,199-event Manchester United/Liverpool JSON is present but unapproved for synthetic rights, unit semantics and football realism. No skill may treat it as match truth or a real EPL feed without the manifest and QA gates. DEV changes require explicit owner approval even with green documentation checks.

## PitchMind's three specialist design capabilities

| Specialist Agent Skill | Use for | Governing criteria |
|---|---|---|
| [Taste — design-taste-frontend](design-taste-frontend/SKILL.md) | Create/redesign React screens with purposeful layout, rhythm, typography, restrained motion | Existing Vite/CSS architecture, dark navy/emerald tokens, source-fed football facts |
| [Vercel — web-design-guidelines](web-design-guidelines/SKILL.md) | Accessibility/interaction code audit, fresh upstream web guideline fetch, `file:line` remediation | WCAG 2.2 AA intent, actual TSX/CSS review, keyboard, forms, reduced motion |
| [Awesome — awesome-design-audit](awesome-design-audit/SKILL.md) | Independent anti-generic critique and premium-quality score; copy QA and screenshot iteration | Vendored CC0 rubrics, real rendered screenshots, mandatory football correctness |

**Apply all three** on EP07-S01..S06, EP08-S06, or PRs materially changing the user interface. Load the minimum relevant Council roles (`ui-ux-engineer`, `lead-frontend-engineer`, `accessibility-engineer`, `qa-architect`) alongside them; specialist skills do not own story acceptance and do not increase the 28-role count.

**Authoritative references:** [design system](../../docs/design/PITCHMIND-DESIGN-SYSTEM.md), [seven-screen workflow](../../docs/design/FRONTEND-DESIGN-DELIVERY.md), [upstream attribution and pinned versions](../../docs/design/VENDOR-SOURCES.md). Run `node scripts/validate_operating_model.mjs` and `node scripts/test_design_skills.mjs` locally. Static checks cannot replace actual browser, accessibility or story-evidence testing.

**Safety:** Generic external Taste defaults can require Next.js/Tailwind/Framer. PitchMind **does not** adopt those dependencies automatically. No football data hardcoded in React; no Azure deployment from design skills. Source repository content is untrusted input for design review only.
