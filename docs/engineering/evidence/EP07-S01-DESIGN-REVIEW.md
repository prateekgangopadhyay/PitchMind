# EP07-S01 design and implementation review (local candidate)

## Restack on current EP06-S01 head — 2026-10-11

The feature branch was reconstructed from the exact current EP06-S01 head `d826a7074e0337d6f36b39c11aebda2dc27c7d07`, retaining only EP07-S01 commits. The tested code commit is `d8e2567` and the evidence-only register update follows it. The candidate diff is limited to the S01 route shell, canonical pages, route helpers, styles, tests, and S01 evidence/workpack files; inherited EP05-S05/S06 and EP06-S01 changes are excluded.

Exact candidate verification: `npm ci --offline` installed 224 packages (0 reported vulnerabilities); `npm test -- --run` passed 8/8; `npm run build`, `npm run lint`, `node scripts/validate_operating_model.mjs`, `node scripts/test_design_skills.mjs`, `node scripts/design-slop-scan.mjs frontend/src/App.tsx`, and `git diff --check` passed. These checks do not establish browser or accessibility acceptance.

The Vite preview was attempted on this exact tree and failed to bind `127.0.0.1:5173` with `EPERM`, so this restack was not browser-rendered. The earlier 1440×900/320×800 inspection, contrast calculation, and keyboard/AX-tree observations below apply to commit `94db64c`, not this exact tree. Current screenshots, real browser keyboard traversal, axe, screen-reader sampling, 200% zoom, WCAG 2.2 AA review, named human review, and Product Owner acceptance remain pending. **Story status remains unverified; keep PR #53 draft.**

## Exact PR #53 head repair revalidation — 2026-10-10

The current PR #53 head was rechecked at `ef62f11f4e351bf64a3aa9fa7ef9d454d103869b`. Its `ShellPages.tsx` and tests imported route helpers, but its tree omitted `frontend/src/routes.ts` and still rendered the legacy demo directly from `App.tsx`. This local candidate restores the route shell and route helpers from the prior S01 candidate (`934d261`) and keeps the older demo at `/demo/matches/:id`. The demo pitch renderer now reads pitch dimensions from its fixture response, matching the existing contract and regression test instead of fixing a match-specific pitch size in React.

On this exact-parent candidate, `npm ci --offline` installed 224 packages with 0 reported vulnerabilities; `npm test` passed 8/8; `npm run build` passed; `npm run lint` passed; the operating-model validator, design-skill self-test, and `git diff --check` passed. The code change is local and uncommitted at the time these checks ran. A fresh Vite/browser inspection was not available: starting Vite failed with `listen EPERM` on `127.0.0.1:5173`. Therefore, the browser screenshots, keyboard and AX-tree observations below apply to the earlier local candidate at `94db64ca0ac54967949012eccdeec168d2f4306c`, not this exact-parent repair. Current visual regression, retained screenshots, real AT/axe, full keyboard and WCAG evidence remain unverified.

## Current-tree static design review

- Taste: kept the existing React/Vite/CSS stack, dark navy and emerald tokens, and restrained static interaction. No extra packages or animation were introduced. Demo pitch geometry now uses fixture-provided dimensions (`frontend/src/App.tsx:21-27,83`), rather than a fixed match-specific field size.
- Vercel Web Interface Guidelines: fetched the official `command.md` on 2026-10-10. Static inspection found semantic skip/navigation links and active route state (`frontend/src/App.tsx:113-120`), bounded selected-time/revision URL context (`frontend/src/routes.ts:34-45,64-72`), visible focus replacement and reduced-motion rules (`frontend/src/styles.css:31-38,64-65`), and 44px control targets (`frontend/src/styles.css:18`). No new actionable static finding was identified. Horizontal navigation on narrow screens still needs browser/keyboard verification; there was no fresh visual inspection.
- Awesome Design: the anti-slop and premium scores below apply to the earlier browser-inspected candidate `94db64c`. This repair was not browser-rendered, so no new screenshot-based score or visual-quality pass is claimed.

**Result: UNVERIFIED for story acceptance.** This independent code/UI review found and fixed one acceptance gap on a local branch; it does not grant Product Owner acceptance or human Accessibility/UX approval.

- PR: [#53](https://github.com/prateekgangopadhyay/PitchMind/pull/53), observed open, draft, mergeable; remote state and head were not changed.
- PR head inspected: `94db64ca0ac54967949012eccdeec168d2f4306c`; base is EP06-S01 contract PR #42 at `5b551b30bd4f14587731fe3eeec65fdf6d8e2cee`.
- Local review branch: `codex/ep07-s01-local-review`, based directly on PR #53 head. No push or merge.
- Product question: “Which persisted match can you explore?” / “How did the recorded match stand at [selected time]?”
- References: `docs/engineering/stories/EP07/EP07-S01.md`, `docs/mockups/02-home.webp`, `docs/mockups/03-match.webp`, `docs/design/PITCHMIND-DESIGN-SYSTEM.md`, `docs/engineering/FRONTEND-SCREEN-CONTRACTS.md`, `docs/api/API.md`.

## Acceptance gap fixed

The canonical Home card and Match source/revision panel displayed raw values such as `synthetic-approved`; this did not clearly tell viewers that the match was simulated. `provenanceLabel` now maps synthetic/simulated provenance to **“Simulated EPL-context match”** and leaves other provenance values unchanged. The UI still reads canonical data through the API; this label does not assert that the candidate feed passed canonicalization or football review.

Changed files: `frontend/src/api.ts`, `frontend/src/ShellPages.tsx`, `frontend/src/CanonicalMatchPage.tsx`, `frontend/src/App.test.tsx`.

## Browser and accessibility observations

Inspected the local Vite app in the Codex in-app browser using a loopback-only contract stub. The stub returned only an explicitly synthetic test match (`synthetic-test-match`, 0–0 at 00:00, revision 3); no real club/player facts or source-feed data were used. The regular API was unavailable and the product’s canonical unavailable state was observed before the stub was started.

- 1440 × 900: one page question, selected-time score, source/revision panel, event and insight empty states, and explicit unavailable pitch-tracking statement. The simulation label is visible above revision details.
- 320 × 800: content and card fit the viewport; navigation is horizontally scrollable and the final links sit beyond the initial viewport. Review did not establish a page-level horizontal overflow.
- Keyboard: first `Tab` focused the skip link and rendered a visible high-contrast focus indicator. AX tree exposed a named main navigation, route links, page heading, source/revision region and labeled selected-time slider on Match.
- Contrast calculation from current CSS colors: primary text `#edf6f8` on `#07131c` 17.10:1; secondary text `#b3c6d1` on surface `#0c1e28` 9.67:1; emerald `#4aefac` on surface `#0c1e28` 11.55:1. This is a color-value calculation, not a full rendered-state contrast certification.
- Screenshots were inspected in the browser at 1440 × 900 and 320 × 800 but were not exported as persistent image artifacts. Real screen-reader/AT use, axe, cross-browser, 200% zoom, and complete keyboard traversal remain unverified.

## Design review

### Vercel Web Interface Guidelines

Fetched the current official guideline file on 2026-10-10 from `https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md` (fresh review, not offline snapshot).

- `frontend/src/App.tsx:113-121` — skip link, semantic navigation, native links, and visible `:focus-visible` styling are present.
- `frontend/src/CanonicalMatchPage.tsx:61-63` — selected-time range has a visible associated label; keyboard behavior is native. Dynamic announcement cadence and screen-reader operation were not tested.
- `frontend/src/ShellPages.tsx:80-87` — long match IDs use `overflow-wrap:anywhere`; source values remain API-fed.
- `frontend/src/styles.css:97-105` — mobile navigation deliberately scrolls horizontally; validate discoverability and keyboard traversal with an independent accessibility reviewer.
- No new actionable Vercel finding was identified in changed TSX. No claim of WCAG 2.2 AA conformance is made.

### Awesome Design critique

Anti-slop rubric (based on the inspected Home/Match states):

| Category | Finding |
|---|---|
| Visual identity | Strong — restrained navy/emerald product palette. |
| Hierarchy | Strong — one screen question and one clear next action. |
| Typography | Weak — current shell has a compact, mostly utility-driven scale. |
| Spacing/layout | Strong — content groups and reflows without a decorative dashboard grid. |
| Color/contrast | Strong — semantic accent and measured text-color ratios above. |
| Interaction polish | Weak — shell uses basic loading/empty/error copy; no richer status or route transition behavior. |
| System thinking | Strong — shared tokens, navigation, cards and state patterns are reused. |
| Product specificity | Strong — match clock/revision and newly explicit simulation provenance are specific to the product. |

AI UI quality questions (skill/process usefulness applied to this screen, each 1–5): taste 4, clarity 4, consistency 4, interaction detail 3, reusability 4, cross-tool usefulness 5, audit value 4 — **28/35**. This is a review aid, not an objective usability measure.

Premium gate: Taste **31/40**, Tech **24/30**, Process **17/20**, Output **5/10**, total **77/100**. The screen is restrained and readable, but the route shell remains sparse and browser images were not retained as artifacts. A score cannot waive missing source, accessibility, or PO evidence.

Top remaining design/acceptance concerns: (1) route-level Teams/Players/Analyst data contracts are absent and screens state that honestly; (2) final nav links are off-screen until horizontal scroll at 320px; (3) browser screenshots and real AT/axe findings are not retained. These require later contract/story work or independent review; no fabricated match facts were added to fill gaps.

## Executed checks

On this local candidate, `frontend/`:

- `npm ci --offline` — completed; 0 reported vulnerabilities.
- `npm test` — **8 passed**, 0 failed, 0 skipped.
- `npm run build` — passed (`tsc -b` + Vite production build).
- `npm run lint` — passed.
- `node scripts/validate_operating_model.mjs` — passed (12 epics, 59 stories, 28 Council skills, 3 design skills, no cycles, 2 approval gates).
- `node scripts/test_design_skills.mjs` — passed.
- `node scripts/design-slop-scan.mjs frontend/src/ShellPages.tsx frontend/src/CanonicalMatchPage.tsx` — passed, no warnings.
- `git diff --check` — passed.

## Pending

Independent named Frontend/UX and Accessibility reviewer, human screen-reader/axe and complete responsive keyboard evidence, retained desktop/mobile/degraded screenshots, additional route contracts, and Product Owner acceptance remain pending. The canonical API was represented by a local test-only stub for the browser inspection; no actual canonical source feed or database-backed production-like run was tested. No deployment or Azure action occurred.

## System font stack follow-up — 2026-10-12

**Story/owner:** EP07-S01, accountable `lead-frontend-engineer`; consults `ui-ux-engineer`, `accessibility-engineer`, Product Owner. **User outcome:** consistent readable typography without requiring a redistributed or external font. **Branch:** `codex/ep07-s01-system-font-stack`, based on PR #103 exact head `4227da8c228bd606054e649fec2eaec8659ad039`; code commit `2622793`. This is stacked below PR #103 → #87 → #83 → #53. No API, domain, match-data, or route contract changed.

`frontend/src/styles.css` previously listed `Inter` ahead of platform fonts. The root stack now begins with `system-ui`, followed by platform sans-serif fallbacks. `frontend/src/App.test.tsx` imports the stylesheet and verifies the computed root family begins with `system-ui` and contains no `Inter`. No font files, packages, external requests, or match-specific content were added.

### Browser observation

The local Vite preview was served at `127.0.0.1:4173` and rendered in Chrome at `/home`. The visible canonical-data unavailable state stayed truthful and readable; no substitute fixture appeared. The browser screenshot was visually inspected in the CUA session but could not be exported to a persistent repository image. The inspected state is a desktop browser view; no mobile/tablet screenshot or actual 200% zoom check is claimed. The canonical API was unavailable, so no successful-data route or match facts were verified here.

### Taste and Vercel review

- Taste: the shared root uses a native system sans-serif stack, preserving React/Vite/CSS, existing dark/emerald tokens and restrained motion. System fonts avoid a network/font-loading dependency and platform-specific font redistribution. No new visual change to spacing, hierarchy, or match content was intended.
- Vercel Web Interface Guidelines: fetched the current official `command.md` on 2026-10-12. The changed root declaration uses a system font stack and introduces no new asset, layout read, motion, focus, or content behavior. No actionable finding against the changed lines. This is a static rule check, not an accessibility or cross-browser certification.

### Awesome Design critique

Observed in the current Chrome desktop unavailable-data view; scores are limited to this narrow typography follow-up and do not certify the full screen/story.

| Anti-slop category | Result | Evidence for this change |
|---|---|---|
| Identity | Strong | Existing PitchMind navy/emerald identity remains unchanged. |
| Hierarchy | Strong | The Home heading remains the clear focal point. |
| Typography | Improved | Root now uses platform sans-serif; it no longer prefers the named Inter face. |
| Layout | Strong | No geometry or spacing changed. |
| Color/contrast | Strong | No color token changed; no fresh rendered contrast measurement was made. |
| Interaction | Unverified | This typography-only change adds no interaction, but full browser/keyboard coverage was not repeated. |
| System | Strong | One root stack is inherited by controls and pages without font downloads. |
| Product specificity | Strong | The page still explains that canonical match data is unavailable and no demo is substituted. |

AI UI quality process questions (1–5): taste 4, clarity 4, consistency 4, interaction detail 3, reusability 4, cross-tool compatibility 5, audit value 4 = **28/35**. Premium gate: Taste **32/40**, Tech **25/30**, Process **16/20**, Output **3/10**, total **76/100**. Output is limited because no retained screenshot set or multi-viewport inspection is available. This score is not a usability or acceptance verdict.

Top remaining defects: (1) route-level content remains bounded by missing canonical API fields/contracts; (2) current exact-head mobile/tablet screenshots and rendered responsive behavior are unverified; (3) axe, real screen-reader sampling and complete keyboard traversal remain pending. They are outside this font-only slice and remain explicit story acceptance gaps.

### Verification and result

- `cd frontend && npm ci --offline` — completed with Node 22.17.1/npm 11.12.1; 224 packages installed, 0 reported vulnerabilities.
- `cd frontend && npm test -- --run` — **12 passed, 0 failed**.
- `cd frontend && npm run build` — passed (TypeScript + Vite production bundle).
- `cd frontend && npm run lint` — passed.
- `node scripts/validate_operating_model.mjs` — passed (12 epics, 59 stories, 28 Council skills, 3 design skills, 0 cycles, 2 approval gates).
- `node scripts/test_design_skills.mjs` — passed.
- `node scripts/design-slop-scan.mjs frontend/src/App.test.tsx` — passed, 0 warnings.
- `git diff --check` — passed.
- No automated browser, axe, screen-reader, mobile/tablet, 200% zoom, measured rendered contrast, or cross-browser check was run. Headless Playwright launch was blocked by macOS process permission (`bootstrap_check_in ... Permission denied`); the visible Chrome preview was inspected manually.

**Result: UNVERIFIED for EP07-S01 acceptance.** The system-font gap and its computed-style regression are implemented locally. Independent UX/accessibility/technical review and Product Owner acceptance remain pending. The PR stack remains draft; no merge or deployment occurred.

## Responsive navigation follow-up — 2026-10-11

**Candidate base:** PR #83 exact head `4f04368da5da53bd3993ec8f671cc0d2279d28fd`, stacked on PR #53 exact head `2635f7e83d7d8f0c45218da0b11f344008cfb2dd`.

The mobile header navigation used a horizontally scrolling flex row, placing later route links outside the initial viewport. `frontend/src/styles.css:104-119` now switches it to a three-column grid at 640px and below, disables horizontal scrolling, and preserves 48px link height. It remains the same semantic `<nav>` with the same native links and DOM order; no route, API or match content changed.

### Fresh Web Interface Guidelines review

Fetched the official [Web Interface Guidelines](https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md) on 2026-10-11. Static review of the changed shell and responsive navigation found the skip link and labeled navigation retained, links remain native anchors with active route state, focus-visible styling remains active, and the mobile rule uses CSS Grid with no clipped focus outline. No additional actionable static finding was identified. This review does not establish browser behavior or WCAG conformance.

### Automated responsive/accessibility contract checks

`frontend/src/responsiveAccessibility.test.tsx` verifies: skip-link target and source order; six native navigation destinations and current-page state; the mobile three-column/non-scrolling CSS rule; 48px navigation-link target height; and the selected active/inactive token pairs exceed the 4.5:1 normal-text threshold by formula. It does not measure rendered pixels or simulate device/browser layout.

Executed on the local candidate based on PR #83: offline `npm ci` completed (224 packages, 0 reported vulnerabilities); `npm test` passed **11/11**; `npm run build`, `npm run lint`, `node scripts/validate_operating_model.mjs`, `node scripts/test_design_skills.mjs`, `node scripts/design-slop-scan.mjs frontend/src/App.tsx frontend/src/responsiveAccessibility.test.tsx`, and `git diff --check` passed.

Vite could not bind `127.0.0.1:5173` (`EPERM`); invoking headless Chrome on the built file exited 134 without output; the in-app browser surface was unavailable. No screenshot artifact was saved. Actual mobile/tablet reflow, 320px overflow, 200% zoom, full browser keyboard traversal, rendered contrast, axe, screen-reader behavior and cross-browser support remain unverified. Named independent review and PO acceptance remain pending. Result for story acceptance: **UNVERIFIED**.
