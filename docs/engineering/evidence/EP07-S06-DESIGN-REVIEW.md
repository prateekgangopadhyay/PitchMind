# EP07-S06 design and frontend review evidence

**Result:** UNVERIFIED — local implementation and static review only; visual and accessibility release gates remain open.
**Implementation commit:** `bd088a8646e02ee77a185355526066335d53a6d7`
**Parent:** exact PR #79 head `6dabf182378c617daa230fd0da6212fb1998bd86`
**Worktree/branch:** `/private/tmp/pitchmind-ep07-s06-restack`, `codex/ep07-s06-resilient-ux`
**Story/screen:** EP07-S06, canonical Match / Replay
**Mockup baseline:** [03 — Match / Intelligent Pitch](../../../mockups/03-match.webp); concepts only, with no match facts copied into implementation.
**Data/revision:** source-fed canonical BFF; the S06 test fixture is test-only synthetic metadata. No candidate-feed hash or real-match journey was used for acceptance.

## Change and taste refinement

The Fan/Analyst switch only reveals or hides persisted detector details; it keeps the same canonical truth class, summary, conditions, limitations, evidence references, match ID, selected time and revision. Optional event/insight failures receive separate status copy while the required canonical summary and other available panels remain usable. Required summary failures include a typed user message and retry action. The S06 port retains PR #73's shared `CanonicalEvidenceDisclosure` and event/frame replay links, including its hash-target focus behavior. No public API, domain or architecture boundary changed.

Taste was applied within the existing React/Vite/TypeScript and CSS stack: dark navy/emerald tokens, restrained layout, native controls, no new package, and no additional motion. The new audience buttons use a 44px minimum target and adapt to a two-column narrow layout. Long conditions JSON wraps within its container. This was code-level refinement only; no before/after browser image is available.

## Current official Vercel Web Interface Guidelines review

**Fetched:** 2026-10-11 from [the official raw guideline source](https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md). The source is a mutable `main` URL; no immutable upstream commit identifier was returned by the fetch.

- `frontend/src/CanonicalMatchPage.tsx:180` — required summary error uses `role="alert"` and a specific retry button; the next action is visible. The retry path is covered by Vitest.
- `frontend/src/CanonicalMatchPage.tsx:204-208` — audience options are native buttons in a named group, expose `aria-pressed`, and keep keyboard behavior native. Existing global `:focus-visible` styling is in `frontend/src/styles.css:1`.
- `frontend/src/CanonicalMatchPage.tsx:247,260` — optional event and insight failures are announced as status updates; each section has its own status instead of replacing available match context.
- `frontend/src/CanonicalMatchPage.tsx:118-126,227-230` — event/frame hash targets are programmatically focusable after evidence navigation; this preserves PR #73's deep-link flow. A Vitest test exercises event focus after following the evidence link.
- `frontend/src/styles.css:127,172-173` — the audience controls have a 44px minimum height, `touch-action: manipulation`, and a two-column narrow-screen layout. `frontend/src/styles.css:130` wraps long stored conditions.
- `frontend/src/CanonicalMatchPage.tsx:32-43,114-116,198-202` — the existing reduced-motion preference continues to disable automatic replay; the selected-time slider remains available.
- `frontend/src/CanonicalMatchPage.tsx:193-196` — selected-time range input has a native label, keyboard interaction, and an accessible value string.

**Non-blocking design note:** Audience choice remains local presentation state and is not encoded in the URL. This keeps it separate from canonical match/evidence context; the guideline describes URL synchronization as a consideration for stateful controls. If shareable explanation depth is desired, it needs Product direction and a separate screen-contract decision.

**Unverified guideline outcomes:** Actual focus-ring visibility while navigating, touch hit area in a rendered browser, 320px reflow, 200% zoom, contrast ratios, error announcements in assistive technology, and cross-browser behavior were not measured. Static review is not WCAG conformance evidence.

## Tests and current evidence

- `npm ci --offline` — installed 224 lockfile packages; audit reported 0 vulnerabilities (Node `v22.17.1`, npm `11.12.1`).
- `npm test` — Vitest: 35 passed, 0 failed.
- `npm run build` — TypeScript and Vite production build passed.
- `npm run lint` — ESLint passed.
- `node scripts/validate_operating_model.mjs` — passed: 12 epics, 59 stories, 28 Council skills, 3 design skills, 0 cycles, 2 approval gates.
- `node scripts/test_design_skills.mjs` — passed.
- `node scripts/design-slop-scan.mjs frontend/src/CanonicalMatchPage.tsx` — passed, 0 warnings.
- `git diff --check` — passed on the final source and documentation candidate before commit.

The four new Vitest cases cover optional insight outage with score/events retained, audience-detail invariance, event focus through PR #73's evidence deep link, and required-summary retry without demo substitution plus focus restoration after success. Vitest/jsdom does not prove a real keyboard-only journey.

## Awesome Design review and release gate

- Anti-slop rubric, 8 categories: **UNVERIFIED** (no rendered desktop/mobile/error screenshots to inspect).
- AI UI quality rubric, 7 dimensions: **UNVERIFIED** (no rendered review artifact; no numeric score assigned).
- Premium gate, 40/30/20/10: **UNVERIFIED** (no browser screenshots; no score assigned).
- `node scripts/design-slop-scan.mjs` is a copy heuristic only; it is not visual or accessibility evidence.
- No screenshots, Playwright, axe, real keyboard-only browser journey, manual screen-reader, zoom, or contrast-tool run is claimed.
- Independent human technical review and Product Owner acceptance: **PENDING**.
- Azure: no resources, deployments, or configuration changed.
