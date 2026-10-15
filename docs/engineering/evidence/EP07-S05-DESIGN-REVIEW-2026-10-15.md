# EP07-S05 source revision disclosure — design review

**Result: UNVERIFIED.** This is a local design review of the implementation on `codex/ep07-s05-source-revision-integrity`, stacked on PR #264 head `e2710931ad40c09a2092e4e1d6e035913bc0990a`. The story is not accepted. Independent exact-diff review of the compact-width/keyboard E2E addition found no actionable findings. Hosted CI, manual assistive-technology testing, axe/WCAG checks, and Product Owner acceptance remain outstanding.

## Scope and user outcome

The evidence panel must expose its source canonical revision in visible text and the link’s accessible name, and must hide evidence if the source revision is non-positive, non-integral, unsafe to represent exactly in JavaScript, or newer than the insight revision. The phone navigation must keep all route links visible. Changes are in `frontend/src/CanonicalEvidenceDisclosure.tsx`, `frontend/src/styles.css`, the evidence component tests, the App focus-restoration fixture, and the EP07-S06 Playwright journey.

The E2E response uses only test fixture IDs and is labeled `synthetic-test-only`. It does not add match data to production code. The follow-up adds a bounded exact `frameId` query to the existing canonical tracking-frame API, with a PostgreSQL lookup scoped to the selected match/time/revision; the API contract and OpenAPI document are updated. No new service boundary or C4 change was made.

## Browser evidence

Playwright captured the same selected-time evidence disclosure at desktop (1440×1000), tablet (768×1024), mobile (375×812), and compact (320×800). Screenshots show source revision 4 in the visible evidence row; navigation links remain within the viewport at both phone widths. The browser test also opens the native disclosure and follows its citation using keyboard Enter, then verifies replay time, revision, event fragment, and focus target.

- [Desktop — 1440×1000](ep07-s05-source-revision/desktop-1440x1000.png)
- [Tablet — 768×1024](ep07-s05-source-revision/tablet-768x1024.png)
- [Mobile — 375×812](ep07-s05-source-revision/mobile-375x812.png)
- [Compact — 320×800](ep07-s05-source-revision/compact-320x800.png)

The desktop screenshot also shows substantial unused space below the pitch panel beside the evidence column. This layout predates this diff and remains a design follow-up; this change does not claim to resolve it. Independent code review also found that sequence-only frame citations could select a different frame when sequences repeat. The follow-up now encodes the frame ID in the replay fragment, requests that exact ID from the API, and pins the cited source canonical revision in the URL; replay selects only the exact ID/revision pair. A missing or stale citation does not silently substitute another frame. Vitest covers two frame citations at the same sequence, and an API contract test rejects mixing exact frame lookup with a cursor.

## Eight anti-slop areas

| Area | Result | Evidence |
|---|---|---|
| Identity | Strong | PitchMind navy/emerald system is retained; desktop screenshot. |
| Hierarchy | Strong | Match score/time precedes evidence detail; source revision is readable in the expanded evidence row. |
| Typography | Strong | Existing scale and weight remain consistent; no new type treatment. |
| Spacing and layout | Weak | Phone navigation now wraps; however, desktop has a large unused lower-left region visible in the screenshot. |
| Color and contrast | Strong, visual-only | Existing tokens and non-color text labels are preserved. No contrast tool was run. |
| Interaction polish | Weak / unverified | Automated browser coverage opens the native disclosure and follows its citation by keyboard; manual keyboard and assistive-technology testing remains outstanding. |
| System thinking | Strong | Revision detail uses the existing evidence row and disclosure patterns. |
| Product specificity | Strong | Evidence remains tied to match, sequence, selected time, and canonical source revision. |

## Seven quality dimensions

Scores are a heuristic review of this rendered state, not a score of the design-skill resource.

| Dimension | Score (1–5) | Basis |
|---|---:|---|
| Taste | 4 | Existing domain-specific PitchMind styling is coherent. |
| Clarity | 4 | Evidence row exposes time, sequence, and source revision. |
| Consistency | 4 | Uses the existing evidence component and tokens. |
| Interaction detail | 3 | Responsive nav and native disclosure are covered in browser; manual assistive checks are pending. |
| Reusability | 4 | Evidence metadata is rendered from the API payload. |
| Cross-tool usefulness | 3 | Standard React/CSS and Playwright; review artifact is repo-specific. |
| Audit value | 4 | Regression rejects future source revisions and checks all mobile nav link bounds. |

**Total: 26/35 — high-signal, with accessibility and layout follow-ups.**

## Premium gate (0–100)

| Dimension | Score | Notes |
|---|---:|---|
| Taste | 31/40 | Product-specific context, readable evidence hierarchy, and existing brand tokens; desktop whitespace remains. |
| Tech | 23/30 | Production build, lint, unit and browser tests pass; contrast, axe and manual assistive-tech checks remain unverified. |
| Process | 15/20 | Five-design-skill review and screenshot iteration completed; latest independent exact-diff review found no actionable findings, while manual accessibility review remains pending. |
| Output | 8/10 | Inspectable screenshots and regression coverage; full accessibility and responsive range are not verified. |
| **Total** | **77/100** | Below the 95 target; score does not waive the outstanding accessibility blocker. |

## Web Interface Guidelines findings

Fresh official guideline snapshot retrieved 2026-10-15 from [Vercel Web Interface Guidelines](https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md). Findings for changed files:

- `frontend/src/CanonicalEvidenceDisclosure.tsx` — native `<a>` retains a descriptive accessible name including time, sequence, and source revision; no semantic-control change required.
- `frontend/src/styles.css` — mobile navigation wraps at 640px and removes horizontal clipping; Playwright checks every navigation link’s horizontal bounds at 375px and 320px.
- Localization/long translated labels, contrast measurement, and manual assistive-technology coverage remain unverified; compact 320px reflow and keyboard citation activation pass automated browser assertions.

## Checks actually performed

- `frontend`: `npm test -- --run` — **59 passed, 0 failed**.
- `frontend`: `npm run build` — passed (TypeScript and Vite production build).
- `frontend`: `npm run lint` — passed.
- `frontend`: `npm run test:e2e` — **5 passed, 0 failed**, including responsive evidence screenshots at 1440px, 768px, 375px, and 320px; every phone navigation link remains within bounds, and keyboard activation opens evidence and follows the selected event citation. The normal sandbox run could not bind `127.0.0.1:4173`; the same command then passed with approved local execution access.
- Repository: `node scripts/test_design_skills.mjs` — passed.
- Repository: `node scripts/design-slop-scan.mjs frontend/src/CanonicalEvidenceDisclosure.tsx` — pass, no warnings.
- Repository: `node scripts/validate_operating_model.mjs` — passed (12 epics, 59 stories, 28 Council roles, 0 graph cycles).
- `dotnet test PitchMind.slnx -c Release -m:1 --disable-build-servers --no-restore -p:NuGetAudit=false` — **436 passed, 27 skipped, 0 failed** (463 total). The 27 skips are opt-in PostgreSQL cases in this generic run. The run compiled the API, infrastructure, all test projects, and exercised 128 contract tests including exact frame lookup and cursor incompatibility.
- `PITCHMIND_TEST_RESULTS_DIRECTORY=/private/tmp/pitchmind-ep07-s05-frame-target-postgres ./scripts/test-postgres.sh` — **50 passed, 0 skipped, 0 failed** against local PostgreSQL; includes exact-ID lookup where two frames share a sequence. The harness removed its unique temporary test database.
- `git diff --check` — passed after the code/doc edits.

No axe run, manual keyboard session, screen-reader session, measured contrast, hosted CI, or Product Owner acceptance is claimed. No Azure resources or deployments were touched.

## Review and acceptance

- Independent review of the earlier implementation found two P2 integrity issues: duplicate-sequence frame citation ambiguity and first-page-only lookup; exact ID API lookup and stable frame-ID replay corrected both. A fresh review then found an unsafe JavaScript integer edge case in source-revision validation; changed the guard to `Number.isSafeInteger` and added a regression case. The final exact-diff review of compact-width/keyboard E2E and evidence-artifact additions found no actionable findings. Reviewers did not execute tests; test evidence is from the local runs recorded above.
- Accessibility reviewer: pending.
- UI/UX reviewer: pending.
- Product Owner acceptance: pending.
- Evidence and checks are recorded from the local feature branch; the compact-width and keyboard test changes are committed on PR #265. EP07-S05 remains unaccepted pending the listed accessibility, hosted CI, and Product Owner gates.

## Current-main integration revalidation — 2026-10-15

The current integrated review target is `main` commit `dd417c2d142c659580b022690373ead8b6540b05`. It includes PR #260 (tracking-frame API; merge `052dd6976c4c65cf535a70b10b236d393fdd2e77`), PR #273 (persisted PostgreSQL/API/browser evidence journey; merge `bad13b3f4dc618841f1f541474aaf4d7f2ee5d55`), and PR #274 (live-spec test isolation and runner cleanup; merge `dd417c2d142c659580b022690373ead8b6540b05`). The decision remains Pending; no PO review or human accessibility approval is claimed.

Pre-PR #274 baseline checks on PR #273's integrated main recorded:

- `dotnet test PitchMind.slnx --configuration Release --no-restore --maxcpucount:1 --disable-build-servers` — 469 passed, 27 skipped, 0 failed. The skips are opt-in PostgreSQL integration facts; they did not execute in this generic full-solution run.
- `./scripts/test-postgres.sh` — 50 passed, 0 skipped, 0 failed against the healthy local Docker PostgreSQL service; its uniquely named temporary database was removed by the harness.
- `npm test -- --reporter=dot` — 83 passed, 0 failed. The first attempt found the local dependency tree incomplete; `npm ci --offline` restored the lockfile dependencies before this successful run.
- `npm run build` and `npm run lint` — passed. Vite emitted only third-party Rollup annotation warnings for `@microsoft/signalr`; the build completed.
- `npm run test:e2e` — 5 passed, 0 failed after the sandbox denied its loopback bind and the same command ran with local loopback permission. This Playwright suite uses test-only stubbed canonical API responses, not the live PostgreSQL-backed API. The fixtures identify themselves as synthetic test data; they are not source-feed or football acceptance evidence.
- `node scripts/test_design_skills.mjs`, `node scripts/design-slop-scan.mjs frontend/src/CanonicalEvidenceDisclosure.tsx`, and `node scripts/validate_operating_model.mjs` — passed. No current changes were made to the UI copy or runtime UI code.

The following checks ran locally against PR #273's integrated main and the exact tested code later merged by PR #274:

- `dotnet test PitchMind.slnx --configuration Release --no-restore --nologo --verbosity minimal --maxcpucount:1 --disable-build-servers -p:UseSharedCompilation=false -p:NuGetAudit=false` — **469 passed, 28 skipped opt-in PostgreSQL cases, 0 failed** (rerun with host permission after the sandbox denied VSTest's loopback bind).
- `./scripts/test-postgres.sh` — **51 passed, 0 skipped, 0 failed**.
- On the local PR #274 tree, the dedicated live runner passed fixture/migration seed **1/1**, API Release build **0 warnings/errors**, and live PostgreSQL → API → Playwright journey **1/1**.
- The first ordinary Playwright run on PR #273's integrated main exposed a test isolation issue: the live API spec was included without its dedicated fixture/API runner. PR #274 excludes that spec by default and opts in only from the dedicated runner; its exact code passed ordinary `npm run test:e2e` **5/5** and the live journey **1/1** before merge. Independent exact-head review of the full PR found no remaining actionable findings; the reviewer did not run tests.
- PR #274 is now merged at `dd417c2d142c659580b022690373ead8b6540b05`. The merged implementation code is the exact locally verified tree; no second test run was performed after merge because only the reviewed documentation/SHA correction was added after the test run.

Fresh official Vercel Web Interface Guidelines were retrieved on 2026-10-15. The desktop, tablet, mobile, and compact screenshots below were captured on the prior UI code at configured viewport widths 1440, 768, 375, and 320 pixels using test-only stubbed API responses. PR #273 and PR #274 did not modify production UI code, so these remain representative visual fixture evidence, not a live API screenshot or source-feed evidence. At the selected time with no tracking frame, the UI truthfully shows that positions are unavailable and not estimated. The selected test fixture has no pitch frame, leaving visible unused space below the compact empty-state panel; this is a layout follow-up, not a data-correctness defect. The screen remains substantially simpler than the conceptual mockup because the stubbed response contains no verified spatial tracking and key moments are unavailable; adding illustrative positions or tactical decoration would violate the truth contract.

- [Desktop screenshot](ep07-s05-current-main/desktop.png)
- [Tablet screenshot](ep07-s05-current-main/tablet.png)
- [Mobile screenshot](ep07-s05-current-main/mobile.png)
- [Compact screenshot](ep07-s05-current-main/compact.png)

### Current render critique and gate

| Anti-slop area | Current observation |
|---|---|
| Identity | Strong — navy/emerald tokens are consistent with the existing PitchMind system. |
| Hierarchy | Strong — score and selected clock precede events and evidence; truth class and source lineage are visible. |
| Typography | Strong — score and clock use tabular numerals; evidence values wrap at compact width. |
| Spacing/layout | Weak in this empty-frame test — a large desktop lower-left area remains unused; mobile panels reflow without horizontal overflow. |
| Color/contrast | Visual-only pass — no contrast measurement was run. Team/event labels do not depend only on color. |
| Interaction | Automated pass for disclosure and keyboard citation/replay; manual assistive-technology checks remain unverified. |
| System | Strong — panels, disclosure, buttons, and status use the same token system. |
| Product specificity | Strong — synthetic labeling, observed truth class, selected time, sequence and source revision are surfaced. |

The seven-dimensional process-quality heuristic remains 26/35 on this unchanged design. Premium gate remains **77/100**: Taste 31/40, Tech 23/30, Process 15/20, Output 8/10. The score is not a pass. The local design review remains **UNVERIFIED** because axe, measured contrast, manual screen-reader/keyboard testing, hosted CI, source-feed evidence review, and PO acceptance were not performed. The existing five-skill registry/self-test passed, but this evidence refresh did not constitute a new implementation/refinement iteration by Taste or a separate independent human UI/Accessibility review. No UI code fix was warranted from the current empty-frame screenshot; the unused-space finding remains visible for PO/design follow-up.
