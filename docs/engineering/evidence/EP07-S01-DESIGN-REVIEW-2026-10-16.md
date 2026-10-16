# EP07-S01 — shell language, information hierarchy, and responsive review

**Result:** This local implementation slice is verified. EP07-S01 remains unverified and unaccepted.
**Implementation commits:** `b400d830dcfb228870d029e9055ba63e4635f2f7` and `ef4bf372fa9df6d248110438b9a9873165f142b3` on `codex/ep07-s01-accessibility-followup`, based on `main` at `5f6f3367743bfac754d9b6ba7144091f1baed9a`.
**Review PR:** [#308](https://github.com/prateekgangopadhyay/PitchMind/pull/308), draft and pending independent review. The final documentation commit is recorded after it is created. No Product Owner acceptance is recorded.
**Scope:** Replace visible implementation jargon with viewer-facing language, progressively disclose match/source identifiers, sanitize a selected-moment mismatch, keep long source hashes within narrow cards, expose the match collection as a named semantic list, and give its CTA a concise accessible name. No match facts, public API fields, data persistence, or architecture boundaries changed.

## Intent and observed delta

The Home, Teams, Players, Insights, Analyst, and unavailable Match views previously exposed terms such as “canonical service,” “current API,” “contract,” “Foundry candidate generation,” and “persisted state.” Home cards also showed match IDs, revisions, algorithms, and a 64-character source hash in the default view. The shell now uses plain-language states and puts lineage details behind native disclosures. When the source hash is expanded at 320px, it wraps within the card.

No team/player name or match fact is inferred to fill fields the current fixture contract does not provide. A test-only canonical response is defined inline in `frontend/e2e/ep07-s01-information-architecture.spec.ts`; it is not the candidate feed and does not establish the feed’s semantics or provenance.

## Screenshots

Captured from the local Vite app by Playwright with the inline test-only response. Home and Analyst states are shown at 1440px, 768px, 375px, and 320px. The expanded source-details screenshot verifies the wrapped hash at 320px.

- Home: [desktop](ep07-s01-shell-ia/home-desktop-1440.png), [tablet](ep07-s01-shell-ia/home-tablet-768.png), [mobile 375px](ep07-s01-shell-ia/home-mobile-375.png), [mobile 320px](ep07-s01-shell-ia/home-mobile-320.png)
- Analyst: [desktop](ep07-s01-shell-ia/analyst-desktop-1440.png), [tablet](ep07-s01-shell-ia/analyst-tablet-768.png), [mobile 375px](ep07-s01-shell-ia/analyst-mobile-375.png), [mobile 320px](ep07-s01-shell-ia/analyst-mobile-320.png)
- Expanded source details: [mobile 320px](ep07-s01-shell-ia/home-source-details-open-mobile-320.png)

## Design and interaction review

### Taste

Kept React/Vite/TypeScript and the existing navy/emerald CSS system. The screen question and next action remain ahead of data lineage. The seven-route journey is tested against truthful unavailable states; no visual value from the conceptual mockup was treated as match data.

### Impeccable

Five concrete defects were found and corrected:

1. Internal platform/API vocabulary was visible in user copy. It now describes what a viewer can do and what is unavailable.
2. Source hashes and pipeline identifiers competed with the match score. They are in a native, keyboard-operable “Match and source details” disclosure.
3. An unbroken source hash overflowed its mobile card when expanded. `overflow-wrap:anywhere` now keeps it inside the content width, with a browser assertion on the line’s actual `scrollWidth`.
4. The match collection was a generic labelled `div`, which did not provide list semantics. It is now a named native list with list items, asserted by role in Playwright.
5. The “Open match” link’s accessible name repeated an opaque UUID. The visible action text is now its accessible name; the identifier remains available in the details disclosure.

### Emil design engineering

Motion is **not applicable**: this change adds no animation. Native anchors and `<details>/<summary>` retain ordinary keyboard behavior; no motion library or runtime dependency was added.

### Vercel Web Interface Guidelines

Reviewed against the official guideline file retrieved on 2026-10-16 from [the Vercel Web Interface Guidelines](https://raw.githubusercontent.com/vercel-labs/web-interface-guidelines/main/command.md). The changed routes use native links and disclosures, the shell retains a skip link and visible `:focus-visible` ring, errors stay sanitized, and the expanded hash wraps on mobile. No actionable guideline finding remains in the changed scope.

The review is a code/browser review, not a WCAG conformance declaration. A real screen reader and axe were not run.

### Awesome Design audit

Anti-slop rubric from the inspected screenshots and changed code:

| Area | Assessment | Evidence |
| --- | --- | --- |
| Visual identity | Strong | Navy/emerald shell and PitchMind mark in Home and Analyst screenshots. |
| Hierarchy | Strong | One page question, one state message, then a specific action. |
| Typography | Strong | Short headings, clear body copy, tabular match score, balanced wrapping. |
| Spacing and layout | Strong | Home controls and cards reflow from desktop to 320px without page overflow. |
| Color and contrast | Strong for measured roles | Representative token ratios below; full contrast sweep remains unverified. |
| Interaction polish | Strong in tested path | Keyboard traverses skip link, main content and CTA; visible focus ring measured at 3px; native details toggles by Enter. |
| System thinking | Strong | Same header, navigation, content width and state treatment across routed views. |
| Product specificity | Strong | Match score, simulated label, selected clock and source lineage remain explicit without adding football claims. |

Representative WCAG contrast calculations using the current rendered color tokens: primary text/canvas 17.10:1; secondary text/canvas 10.65:1; eyebrow/canvas 12.48:1; selected navigation/canvas 12.72:1; button text/control 14.07:1. These calculations do not cover every rendered state or prove WCAG conformance.

The process-quality rubric (taste / clarity / consistency / interaction / reuse / cross-tool compatibility / audit value, each 1–5) is **4 / 5 / 4 / 4 / 4 / 4 / 4 = 29/35**. It describes this review process, not measured usability.

The premium rubric is **Taste 32/40, Tech 26/30, Process 14/20, Output 8/10 = 80/100**. This is a modest design score; it cannot waive the outstanding assistive-technology and product acceptance gates.

## Executed evidence

- `cd frontend && npm ci --offline` — completed; 244 packages installed, npm reported 0 vulnerabilities.
- `cd frontend && npm test -- --run` — 8 files passed; 97 tests passed, 0 failed.
- `cd frontend && npm run test:e2e` — 9 tests passed, 0 failed. This includes the seven-screen journey at 1440px / 768px / 375px / 320px, keyboard route activation, 3px skip-link focus outline, preserved match context, simulation labeling, a named Matches list with one list item, the concise “Open match” accessible name, unavailable Match/Insights states, collapsed source details, and expanded 320px hash wrapping. The journey confirms the failed Match read displays no score.
- `cd frontend && npm run build` — passed. Rollup emitted two existing SignalR pure-annotation warnings.
- `cd frontend && npm run lint` — passed.
- `node scripts/validate_operating_model.mjs` — passed: 12 epics, 59 stories, 28 Council skills, 5 design skills, 0 dependency cycles.
- `node scripts/test_design_skills.mjs` — passed.
- `node scripts/design-slop-scan.mjs frontend/src/App.tsx`, `frontend/src/CanonicalMatchPage.tsx`, and `frontend/src/ShellPages.tsx` — each passed with no warnings.
- `git diff --check` — passed.

## Unverified / pending

- No real candidate feed was admitted or used; the full source-backed viewer journey remains unavailable.
- No axe run, real screen-reader session, 200% zoom review, or complete manual keyboard audit was performed. Automated tests check the tested keyboard path and measured token pairs only.
- Independent human frontend/accessibility review and Product Owner acceptance remain pending. This slice does not accept EP07-S01.
- Hosted CI, Azure, deployment, and any cloud operation were not run.
