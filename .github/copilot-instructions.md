# PitchMind repository-wide engineering instructions

Operate under [Engineering Council Operating Model](../docs/operating-model/ENGINEERING-OPERATING-MODEL.md); select focused [role skills](skills/README.md) instead of treating every role as a mandatory approver. Start each change from an exact story in docs/backlog/HACKATHON-MVP-EPICS.md and the execution register, not an invented backlog. Read docs/architecture/01-system-context.md through 05-football-lld.md and ADR-001..008 for relevant decisions.

Canonical rules:
- Deterministic football truth stays pure in PitchMind.Domain; AI writes no canonical data.
- One selected elapsed time controls state, events, insight, pitch and explanation; no future leakage.
- The existing offline demo uses 105 × 68 metres, but the new Manchester United–Liverpool JSON contains **unitless 0–100 coordinates** until the feed manifest is approved. Do not silently map or use 3m rules; domain units and halftime transforms must be sourced/versioned. Observed/derived/pattern/interpretation/hypothesis classes never collapse.
- PostgreSQL is canonical; Event Hubs telemetry and Service Bus background commands are distinct; at-least-once, idempotent and replayable; Redis and SignalR noncanonical.
- LOCAL Docker adapters only; DEV/STAGE/PROD Azure-only; GitHub source and Azure DevOps CI/CD.
- Read code and evidence before asserting implemented, tested, deployed or production-ready. The offline synthetic API/DEV demo is not proof of a canonical cloud path.
- Use fictional synthetic demonstration data with traceable provenance and lawful usage. New data/simulations inputs require validation; a filename is not source approval.
- Apply security, accessibility, football review, budget, test and rollback requirements appropriate to the changed surface.
- **Do not deploy stories to DEV without explicit, change-specific repository owner approval.** PR creation/merge is not authorization; STAGE/PROD need separate protected approvals.
- Record status, precise evidence/commit/environment and design changes in the relevant existing docs. Do not invent test results or defer blockers silently.
- Use the [53 current story dependency routes](../docs/operating-model/ALL-STORIES-EXECUTION-ROADMAP.md) and [28-role registry](../docs/operating-model/COUNCIL-SKILL-REGISTRY.json) to choose a DRI and independent reviewers; do not create a second status authority or fake engineer assignments.
- The supplied source JSON at `data/simulations/manu-liv-match-dataset.json` has owner-confirmed synthetic provenance and redistribution permission, but independent rights/privacy and football review, clock/coordinate semantics, and canonical admission remain open. Player `heartRate` etc must not be treated as actual biometrics. No hardcoded football facts in C# / React; manifest and source-specific adapter required.
- In Azure DevOps, `enableCandidateFeed` defaults to true, while PostgreSQL and Redis provisioning remain default-off. *Two mandatory manual gates plus a protected Environment* are required after obtaining the **owner's explicit change-specific approval**; a pipeline resume without that approval is still forbidden.

## EP07 / EP08-S06 — five discoverable design specialist skills

For UI story implementation and review, apply [Taste](skills/design-taste-frontend/SKILL.md) for implementation/refinement, then [Impeccable](skills/impeccable/SKILL.md) for targeted critique/polish/distill, [Emil Design Engineering](skills/emil-design-eng/SKILL.md) for ergonomic interaction/motion decisions, [Vercel Web Design Guidelines](skills/web-design-guidelines/SKILL.md) for actionable file:line UI/a11y findings, then [Awesome Design audit](skills/awesome-design-audit/SKILL.md) for independent, screenshot-grounded anti-generic quality scoring. Their [design registry](../docs/design/DESIGN-SKILL-REGISTRY.json) is separate from the 28 role registry; all 33 must be discoverable. Read [PitchMind design tokens](../docs/design/PITCHMIND-DESIGN-SYSTEM.md) and [review protocol](../docs/design/FRONTEND-DESIGN-DELIVERY.md) first.

The application remains **React 19 + Vite + existing CSS**, not Next.js/Tailwind/Framer by default. Existing screens and current API are authoritative over generic prompts. Respect WCAG 2.2 AA, score/clock revision consistency, source-derived EPL-context synthetic facts, lawful images and disabled deployment. The local Impeccable adapter does not include upstream binaries/hooks and must not auto-install them. A design skill may not introduce dependencies, code-executing external scripts or Azure deployment on its own. Human design review and measured evidence are required.

## Development velocity, continuous integration and CodeGraph

**Priority: keep building functional PitchMind increments fast.** Use the [current story execution directive](../docs/operating-model/AI-EXECUTION-DIRECTIVE.md): parallel specialists share concise cross-epic handoffs while a separate integration owner safely restacks/reviews/merges approved PRs into `main`. Do not mistake stacked PR counts for finished or accepted stories.

CodeGraph is an **optional advisory tool** for code-edit context, callers/dependencies, change impact and related tests. In a trusted agent worktree with the MCP server installed, prefer `codegraph_get_edit_context`, `codegraph_analyze_impact` and `codegraph_find_related_tests` before high-impact edits. Run `bash scripts/codegraph-pr-context.sh <exact-parent-ref>` for stacked PR blast-radius/test-gap analysis; never compare a child blindly against `main`. See [installation and boundaries](../docs/engineering/CODEGRAPH-INTEGRATION.md). If unavailable or slow, immediately continue using ordinary code inspection and real tests; CodeGraph must not block the critical path.

Graph observations are not runtime test, CI, human approval or deployment evidence. GitHub Actions remain forbidden: **Azure DevOps alone runs CI/CD**. No Azure resource mutation or DEV deployment without exact change-specific owner approval.
