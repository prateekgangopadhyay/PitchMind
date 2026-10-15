# EP07-S05 source revision disclosure — design review

**Result: UNVERIFIED.** This is a local review of the uncommitted diff on `codex/ep07-s05-source-revision-integrity`, based on PR #264 head `e2710931ad40c09a2092e4e1d6e035913bc0990a`. The story is not accepted. Independent exact-diff review of the compact-width/keyboard E2E addition found no actionable findings. Hosted CI, manual assistive-technology testing, axe/WCAG checks, and Product Owner acceptance remain outstanding.

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
| Process | 15/20 | Five-design-skill review and screenshot iteration completed; independent review and manual accessibility review are pending. |
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
- Evidence captured against an uncommitted local diff; current diff remains uncommitted; final commit/tree identity will be recorded after the sandbox permits a local commit.
