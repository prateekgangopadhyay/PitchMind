# EP07-S02 Home design and implementation review

## Exact-restack verification — 2026-10-10

This candidate is restacked on the local EP07-S01 repair commit `d64828256f56de81d8b6962d598ab784420194fa`, which itself is based on the exact prior PR #53 head `ef62f11f4e351bf64a3aa9fa7ef9d454d103869b`. It preserves the parent provenance label on Home cards and keeps the EP07-S01 shell in the parent candidate. The browser review and design scores below were recorded on the earlier S02 candidate before this restack; they are historical evidence, not a visual pass on the current exact stack.

On the exact restack, frontend dependency installation and code checks are recorded after the implementation. Starting Vite for a fresh browser inspection failed with `listen EPERM` on `127.0.0.1:5173`; this run therefore has no new screenshots, browser keyboard actions, viewport observations or current AX-tree evidence. Acceptance remains unverified.

**Review scope:** Home / synthetic match overview on local restack candidate `repair/ep07-s02-stacked`

**Review date:** 2026-10-10
**Current exact-restack result: UNVERIFIED.** The earlier UI critique marked the prior candidate CHANGES REQUIRED; this fresh restack has not been browser-rendered, independently reviewed by a human, or accepted by the Product Owner.

## Evidence and data boundary

- Mockup baseline: [`docs/mockups/02-home.webp`](../../mockups/02-home.webp), a visual concept whose match names, scores, competition standings, insights and moments are placeholders.
- Historical rendered review before restacking: local browser, 1280×720. The earlier candidate inspection covered canonical-service unavailable state and a two-card disposable loopback API preview. It used synthetic-shaped test DTOs, not the candidate JSON or PostgreSQL. Screenshots were viewed but not saved as repository image artifacts. This review does not prove the current exact restack's rendered appearance. 320px, 768px and 1440px screenshots remain unverified.
- The production component reads only the canonical fixture BFF. The contract returns match ID, canonical revision, selected elapsed seconds, sequence, phase, score, provenance, algorithm version and source hash. It does not return team display names, competition, an explanatory match summary or ranked key moments. The UI makes these gaps explicit and does not copy the mockup's football claims.
- Vercel Web Interface Guidelines were retrieved from the official repository on 2026-10-10. Static source findings are scoped below; this is not a WCAG conformance verdict.

## Taste and visual review

The Home screen keeps the dark navy and emerald product system, one primary question, a prominent phase filter and compact persisted-state cards. Score is visually distinct; revision, elapsed time, source and match ID remain inspectable. The desktop render stays restrained and avoids decorative crests or unsupported summary claims. The absent team names and key moments materially reduce the screen's product specificity; this is an API/content blocker, not permission to borrow mockup data.

## Awesome Design rubrics

### Anti-slop review (Strong / Weak)

| Dimension | Assessment | Evidence |
|---|---|---|
| Identity | Strong | Navy/emerald system and PitchMind shell match the documented brand. |
| Hierarchy | Strong | “Choose a match” and the phase filter lead; score is the card focal point. |
| Typography | Strong | Clear heading/body distinction and tabular score numerals. |
| Layout | Strong at inspected desktop size | Two-column cards use whitespace and bounded source metadata; smaller viewports were not captured. |
| Color and contrast | Weak / unverified | Semantic badge and phase text are present; contrast ratios were not measured. |
| Interaction | Strong for implemented controls | Phase filter is keyboard-operated and URL-backed; pagination, match links and retry are real controls. |
| System states | Strong for local cases inspected | Loading, empty, unavailable, retry, no-filter-results and pagination states are explicit; no demo fallback. |
| Product specificity | Weak | Team names, competition, summary and ranked moments are absent from the current canonical contract. |

### AI UI quality rubric (1–5; process-oriented, not usability certification)

| Taste | Clarity | Consistency | Interaction | Reuse | Cross-tool compatibility | Audit value | Total |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 4 | 4 | 5 | 4 | 4 | 3 | 5 | 29/35 |

### Premium gate (guidance only)

| Taste | Tech | Process | Output | Total |
|---:|---:|---:|---:|---:|
| 29/40 | 24/30 | 14/20 | 5/10 | 72/100 |

The score cannot waive missing canonical content, mobile/browser evidence, contrast measurement, independent review or Product Owner acceptance.

## Vercel Web Interface Guidelines findings

- `frontend/src/ShellPages.tsx:109` — labeled native select and URL-backed filter — pass; `frontend/src/routes.ts:76` updates the `phase` query parameter and notifies subscribers.
- `frontend/src/ShellPages.tsx:121` — polite result-count announcement — pass.
- `frontend/src/ShellPages.tsx:133` — native anchor with a match-specific accessible name — pass; match ID, selected seconds and canonical revision are in the destination URL.
- `frontend/src/ShellPages.tsx:117` — service error includes a working retry action — pass in unit coverage; live PostgreSQL behavior unverified.
- `frontend/src/styles.css:1,18-38,64-65,91-103,107-122` — visible focus, 44px controls, hover feedback, dark native controls and reduced-motion rules — code inspected; full screen-reader and contrast results remain unverified.
- `frontend/src/styles.css:109-125` — responsive grid and narrow-screen heading/filter stacking are defined; actual narrow-viewport rendering was not available in this browser session.

## Checks actually performed

**Exact current restack candidate**

- `npm ci --offline` — installed 224 packages; audit reported 0 vulnerabilities.
- `npm test` — 11 passed, 0 failed.
- `npm run build` — passed (`tsc -b` and Vite production build).
- `npm run lint` — passed.
- `node scripts/validate_operating_model.mjs` — passed (12 epics, 59 stories, 28 Council skills, no routing cycles).
- `node scripts/test_design_skills.mjs` — passed.
- `node scripts/design-slop-scan.mjs frontend/src/ShellPages.tsx` — PASS, no warnings.
- `git diff --check` and `git diff --cached --check` — passed.
- Fresh browser review was attempted by starting Vite, but failed with `listen EPERM` on `127.0.0.1:5173`. No current-restack browser keyboard test, screenshot, viewport observation or AX-tree check is claimed.

**Historical checks on the earlier S02 candidate only**

- Vitest 9/9, build, lint, Council validator, design-skill self-test, and copy scan passed before restacking.
- At 1280×720, the earlier browser session inspected an unavailable-service view and two disposable test cards. It used no candidate JSON or PostgreSQL; screenshots were not retained.
- The earlier browser keyboard check changed the phase filter to “First half” with ArrowDown/Enter. The current source still contains unit coverage for URL filter state and cursor navigation, but no current-stack browser interaction was run.

## Pending owners and blockers

- Independent Frontend/UX and Accessibility reviews: pending.
- Product Owner acceptance: pending.
- Canonical API owner: provide reviewed team identity, competition, summary and ranked key-moment contracts before those Home modules can be implemented.
- QA: run browser/axe/assistive-technology and 320/375/768/1440 responsive checks with repository-retained screenshots.
- Story remains unaccepted; no Azure calls or changes were made.
