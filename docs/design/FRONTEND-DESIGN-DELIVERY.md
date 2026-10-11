# PitchMind frontend design delivery — five specialist skills

**Applies to:** EP07-S01..EP07-S06, EP08-S06 and any UI, design-token, accessibility or product copy change.  
**Roles:** Product Owner accepts the user question; UI/UX reviews visual design; Lead Frontend owns React implementation; Accessibility & QA independently assess; Football Analytics reviews claim wording where needed. These are real humans or independent reviewers when assigned; the agent skills only guide work.

## Required workflow (run in this order)

1. **Source/intent inspection (Product Owner + UI/UX).** Read authoritative story, mockup, `docs/design/PITCHMIND-DESIGN-SYSTEM.md`, typed API and existing TSX/CSS. Note actual API/data/clock capabilities. Write concise story, screen question and screenshot comparison. Never assume a mockup score/team event is real.
2. **Taste generation/refinement (design-taste-frontend).** Apply high-quality hierarchy, type rhythm, spacing, responsive composition and minimal motion to current React/Vite+CSS. Stay within existing dependencies unless approved. Build complete loading/empty/failed/degraded/no-evidence states; every visible button must work or be disabled with reason.
3. **Impeccable shape/audit/polish (impeccable).** Consult the project-authored Impeccable adapter to identify the top three issues in the actual route/component, simplify hierarchy and remove generic UI clutter. Use current screenshots/mockups and accurate match/clock data, and record file:line fixes. The full upstream CLI/detector is **not installed** and not an acceptance requirement; any later upstream installer/hook requires separate trusted review.
4. **Emil design-engineering pass (emil-design-eng).** Review purposeful affordances, state transitions, hover/keyboard parity, motion curves, focus behavior and interruptibility; prefer small CSS changes in the existing 120–220ms motion budget with reduced-motion support. For changes not involving motion, report 'not applicable' rather than invent animation tasks.
5. **Web Design Guidelines review (web-design-guidelines).** Fetch the latest `https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md` when available. Apply to changed TSX, CSS, HTML. Report `file:line — rule — impact — remedy`. If fetching fails, use this documented local baseline and **mark review offline**; never claim to have used the latest. Review semantics, forms, focus, copy, reduced-motion, dark-mode, links, long content, layout, i18n and touch.
6. **Awesome Design independent critique (awesome-design-audit).** Inspect **actual browser render** against mockup and story: anti-slop rubric (8 categories), AI UI quality questions (7 dimensions), premium scoring (40/30/20/10). Include screenshot/viewport links. Run the vendored `scripts/design-slop-scan.mjs` on relevant changed user-facing copy (heuristic, with human false-positive review).
7. **Iterate.** Fix top three aesthetic, content or functional problems, rerender, rerun `file:line` audit and keyboard/focus/screen-reader checks. No output is "Awwwards-ready" from prompting alone.
8. **Engineering QA.** Run `npm ci`, `npm run build`, `npm test`, `npm run lint`, `node scripts/validate_operating_model.mjs`, `node scripts/test_design_skills.mjs`. Then run Playwright/axe/manual AT when available; specifically test selected-time no-lookahead, replay controls, evidence links, error and offline states. If a command cannot run, mark **unverified**.
9. **Council approval and handoff.** Include `EPxx-Syy`, screenshots, tests with commit/runner, source fixture SHA, accessibility findings, taste/rubric score with reasoning, reviewer names or pending, API/ADR/C4 deltas and deviations in PR. Story register remains sole status authority; no acceptance without original criteria, human review and observed tests.

## Mandatory UI outcomes / no-score waiver

- **Match truth:** one selected timestamp and revision across scoreboard, pitch, events, insight, AI; do not leak future football facts when scrubbing history.
- **Source:** no hardcoded match/player/team facts in C# or TSX. Synthetic EPL-context feed provenance and sparse tracking must be explicit.
- **Accessible journey:** WCAG 2.2 AA target, keyboard and screen-reader alternative to pitch and slider; no color-only meaning; reduced motion; valid labels and focus management.
- **Secure UI:** user session, roles and API errors from real BFF contracts; no Foundry secrets in browser; truthful stale/offline indicators.
- **Rights:** only rights-cleared assets; generic club iconography until official EPL/club marks are licensed.
- **Delivery:** Azure DEV changes require explicit owner approval for the exact change set. Three design skills are code-review-time resources only.

## Evidence template for each screen

```text
Story: EPxx-Syy   Screen: <name>   Git SHA: <sha>
Data & revision: <fixture/source SHA, match/time/revision or unavailable>
Mockup baseline: docs/mockups/<file.webp>
Screenshots: desktop <path>, tablet <path>, mobile <path>
Taste/Impeccable: hierarchy, polish, distill and target-specific findings with screenshots
Emil: interaction/motion choices or N/A; reduced-motion and keyboard evidence
Vercel Web Guidelines: fetched <timestamp + source revision> OR offline fallback
Findings: file:line — severity — issue — status — reviewer
Awesome anti-slop (8): Strong/Weak per dimension + reason
Awesome AI UI quality (7): per-dimension 1–5, total /35 (assesses review process)
Awesome premium (100): Taste /40, Tech /30, Process /20, Output /10
Mandatory truth/a11y/privacy failures: YES/NO with test
Tests actually run: command, passed/failed/skipped, env, artifact URL
Result: PASS / CHANGES REQUIRED / BLOCKED / UNVERIFIED
Owner & independent review: names or PENDING
```

## Running the copy instrument

```bash
node scripts/design-slop-scan.mjs path/to/user-facing-copy.md
```

This is intentionally **not** an arbitrary shell runner that downloads and executes scripts. Do not scan third-party/vendor docs or source JSON as UI copy. The scanner may flag legitimate technical language; report explicit reviewed exceptions rather than silently editing domain vocabulary. The instrument is one of several independent checks, not an accessibility, security or legal verdict. All five skills are guidance: do not block ongoing development on an unavailable external tool or claim official Impeccable detector execution from the project-authored adapter.

## Source precedence

1. User/PO story and evidence truth, copyright/privacy/security/accessibility.
2. Architecture/ADR/API, existing React/Vite architecture and source manifest.
3. PitchMind design system and seven product mockups.
4. Fresh Vercel Web Interface Guidelines for audit (offline fallback labeled).
5. Taste for creative implementation; Impeccable for critique/polish; Emil for interaction and restrained motion; Awesome rubrics for independent visual critique.
6. Upstream opinions about frameworks, fonts, icon packs, animation intensity and deployment are suggestions only.
