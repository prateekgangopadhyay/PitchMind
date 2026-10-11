# PitchMind — fast development, concurrent integration and CodeGraph directive

**Operating instruction:** Implement and independently verify all **59 PitchMind stories across 12 epics**, using the Engineering Council's existing skill-to-story routing and authoritative workpacks. Treat this as an execution directive, not a new source of story acceptance. Preserve the 49 P0 / 7 P1 / 3 P2 priorities and the October 27 hackathon delivery path.

## First priority: develop the product fast

The **FIRST PRIORITY is to continue building functional product increments as quickly as safely possible**. Do not halt the engineering agents merely to install tooling, produce review paperwork or pursue optional architecture extensions. Use the smallest applicable group of Engineering Council specialists for each story. Run genuinely independent, non-conflicting story tasks in parallel. Each specialist agent must share implementation decisions, changing contracts, found risks, test evidence and concrete handoff actions with the other affected agents and with the integrating Principal Engineer.

## Development + integration must run concurrently

- Continue development on local, short-lived feature branches. Use **stacked PRs only for real dependencies**, each based on the current exact parent head. Keep commits and diffs focused. Avoid overlapping ownership of the same files unless coordinated by the responsible integrator.
- Assign an **integration owner** responsible for the current parent/child dependency map, regular rebase/restack, actual contract compatibility, targeted integration tests and keeping `main` consolidating incrementally.
- **Merge promptly when independently verified and allowed by the existing human/code-owner, CI, security, football and Product Owner gates.** Merge in dependency order. Never infer that a draft or GitHub's `mergeable=true` flag alone authorizes merging. Do not bulk merge unresolved PRs; protect `main`.
- Execute a local Docker-backed end-to-end golden path repeatedly: approved source admission -> PostgreSQL canonical state/outbox -> deterministic tactical insight and evidence -> selected-time APIs -> synchronized frontend -> verified Foundry explanation or explicitly labeled deterministic fallback. Track blockers against existing stories.
- Prefer runnable working product, contract fixes, deterministic tests, accessible seven-screen paths, real user-visible behavior and submission readiness over more speculative scaffolding. Continue all 59 stories in the portfolio while protecting P0 dependencies and release gates first.

## CodeGraph: accelerate agents without blocking delivery

Use the repository CodeGraph MCP configuration and [operator guide](../engineering/CODEGRAPH-INTEGRATION.md) where a trusted local engine is available. Before high-blast-radius changes, query symbol/edit context, caller/dependency impact and relevant tests; for every stacked PR compare `codegraph_pr_context` to its **actual base**. Share bounded impact findings and concrete test commands across working agents. Graph results supplement but never substitute for source inspection, real tests, independent review or Product Owner acceptance. If CodeGraph is absent, stale, broken or slow, **fall back immediately** to ordinary code inspection and continue delivery. Do not delay product implementation to wait for indexing.

## UI craftsmanship without slowing parallel engineering

For EP07 and EP08-S06, load the five `docs/design/DESIGN-SKILL-REGISTRY.json` design specialists as relevant: Taste for initial structure, Impeccable for critique/polish, Emil for affordance/motion quality, Vercel for UI accessibility/usability and Awesome for independent screenshots. Scope comments to the actual changed UI and do not hold unrelated delivery work while awaiting optional Impeccable installers or nonessential animation. Existing React/Vite/CSS, truthful canonical football data, keyboard/focus/reduced motion, screenshots and real tests always take precedence.

## Truth, environments, security and acceptance

- Preserve Hexagonal/Clean Architecture, Domain's zero-external-dependency rule, versioned contracts, selected-time/revision correctness and independent evidence provenance. Never invent match/player/score/tracking facts or infer the unapproved source feed's clock, coordinates, event mappings or rights.
- LOCAL runs Docker with local adapters. DEV, STAGE and PRODUCTION use **Azure services only**, under the existing protected pipeline and environment approvals. Azure DevOps remains the sole CI/CD orchestrator; do not add GitHub Actions.
- **Never deploy to Azure DEV or mutate any Azure resource without the repository owner's exact, explicit, change-specific approval.** Creating/merging a PR or receiving a past approval is not authorization. No bypass of required security, release, football or privacy review.
- For every story and integration PR record exact commit/branch, what changed, actual tests run, observed successes/skips/failures, cross-epic impact, unresolved blockers and requested decision authority in the existing register/workpacket. Distinguish implemented, reviewed, merged, independently verified, Azure-tested and formally accepted. Do not fabricate reviewer identities or green CI.
- Run targeted tests immediately and broader solution/integration tests before merging; re-run affected checks after dependency merges. Route blocked work to independent parallel agents while integration proceeds.

## Continuous execution cadence

Maintain three coordinated lanes: **(A) P0 feature development, (B) dependency-safe integration and reviews, (C) QA/security/football/release evidence**. Share written handoffs at contracts and PR boundaries; unblock each other proactively. The next work item is chosen by user-visible MVP impact and critical-path dependencies, not by PR count. Report evidence-based progress and integration readiness; stop only for real decisions/approvals that cannot be inferred.
