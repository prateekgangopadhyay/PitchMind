# EP07-S01 design and implementation review (local candidate)

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
