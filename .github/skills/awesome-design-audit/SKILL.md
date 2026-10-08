---
name: awesome-design-audit
description: "Use for independent PitchMind UI design critique, premium-quality evidence scoring, anti-generic interface audit and visual regression review. Based on the CC0 Awesome Design Agent Skills rubric collection."
---

# Awesome Design — PitchMind Design Critic and Quality Gate

This is a **PitchMind adaptation** of the CC0-licensed [Awesome Design Agent Skills](https://github.com/frankxai/awesome-design-agent-skills/tree/9a6b8fe9f0ccf8b851863485b76143c54f33df7e) *curated rubric collection*, not a copied nonexistent upstream `SKILL.md`, not an installer of all listed third-party skills, and not permission to run arbitrary external tools.

## Read before review
- `docs/design/PITCHMIND-DESIGN-SYSTEM.md` and `docs/design/FRONTEND-DESIGN-DELIVERY.md`.
- `docs/mockups/README.md` plus the applicable WebP mockup and the **actual rendered** UI (desktop, mobile, empty and degraded).
- `docs/design/vendor/awesome-ai-ui-quality-rubric.md`, `docs/design/vendor/awesome-anti-slop-rubric.md`, `docs/design/vendor/awesome-premium-generation-gate.md`.
- `docs/engineering/FRONTEND-SCREEN-CONTRACTS.md`; current `frontend/` TSX/CSS and typed API contracts.

## Apply all three Awesome rubrics
1. **Anti-slop:** For each of the eight categories (identity, hierarchy, typography, layout, color/contrast, interaction, system, product specificity), mark Strong/Weak with a screenshot, file:line or reproducible browser state. A score must be justified; do not declare "premium" by intuition alone.
2. **AI UI Quality:** Score 1–5 for taste, clarity, consistency, interaction, reuse, cross-tool compatibility and audit value. Record the score and flaws. This rubric evaluates a **skill or process**; apply only its relevant quality questions to UI, and don't pretend the total is an objective usability measure.
3. **Premium generation gate:** Score taste (40), tech (30), process (20), output (10) after actual browser inspection; target 95+, but maintain a **hard blocker** for wrong match facts/time/revision, missing evidence, inaccessible controls, synthetic labeling or unresolved rights. No sum can waive correctness.
4. **Copy quality instrument:** run `node scripts/design-slop-scan.mjs <changed-user-facing-file>` where appropriate. It is a **heuristic copy checker**, not a frontend linter or accessibility conformance test; inspect warnings and skip test fixtures/vendor documents. Use before-and-after copy review for false positives.
5. **Visual critique loop:** screenshot → compare with screen's product question and approved mockup → identify top 3 defects → fix → rerun screenshot and keyboard/error-state checks → independent UI/UX + Accessibility + QA review.

## PitchMind overrides
Dark navy + emerald, football-first narrative/evidence, one selected timestamp/revision, all facts loaded via BFF, no copyrighted official football visuals without authorization. Source guidelines or aesthetic preferences must not require Tailwind, Next.js, third-party motion libraries or a Vercel deployment. Treat the seven mockups as aspirational; do not hardcode their example results.

## Output
Produce `docs/engineering/evidence/EPxx-Syy-DESIGN-REVIEW.md` (or update PR evidence) containing screenshot references, 8-category anti-slop notes, 7-category quality assessment, premium gate score by dimension, Vercel Web Guidelines findings, WCAG/keyboard tests performed, change IDs, reviewers, exceptions and an explicit `PASS / CHANGES REQUIRED / BLOCKED / UNVERIFIED` result. Never claim screenshots or audits that were not performed.

**Rights:** Awesome Design materials copied under upstream CC0 1.0. Original rubric files are preserved in `docs/design/vendor/` and source commit is recorded in `docs/design/VENDOR-SOURCES.md`. External rankings are a discovery index; new third-party downloads need separate security/license review.

**Deployment:** this skill is *design-time only*. It cannot authorize Azure DEV, STAGE, PROD or any published release.
