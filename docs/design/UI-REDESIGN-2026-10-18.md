# EP07 UI redesign evidence — 2026-10-18

## Scope and design decisions

Reviewed the PitchMind mockups, design system, API contracts, EP07 story criteria, and the Taste, Impeccable, Emil, Vercel Web Design Guidelines, Awesome Design, UI/UX, frontend, accessibility, QA, and football analytics skills.

The implementation retains the mockups' dark navy and emerald palette, match-first navigation, selected-time controls, and pitch-led match view. Pages now use the available match APIs as their source. Empty or unavailable data no longer creates pretend statistics, team identities, Key Moments, league tables, fixtures, player metrics, or analyst responses. Those cards in the illustrative mockups remain absent until their APIs provide data.

The canonical match screen shows a recorded pitch only when a rights-approved frame with validated coordinates exists at the selected time. Events and insights render only when the selected match APIs return matching, revision-consistent items. The team marker key uses the team IDs from the displayed frame. Candidate-feed player metrics with unvalidated units are withheld, and a missing player ID is no longer synthesized. The header's candidate simulation control appears only when its candidate match ID matches the active match context.

The Pages remain full width. On narrow screens the top navigation and match-analysis submenu can scroll without widening the document; the observed position table has its own keyboard-scrollable container.

## Visual comparison

The six inspected screen mockups are illustrative and contain detailed EPL examples, team names, metrics, key moments, and AI prose. Those examples are not copied into production. The updated Home and Match screens retain their visual hierarchy and dark pitch palette, but show only fixture and match data currently supplied by APIs. The Teams, Players, Insights, and AI Analyst screens use concise data or service states when their selected-match API responses are empty or unavailable; no empty data cards are shown.

Screenshots below show the UI under test-only API responses. The match fixture is synthetic and exists only in the browser test; it does not represent a real match or validate the candidate source feed.

- [Home at 1440 × 1000](ui-redesign-2026-10-18/home-1440x1000.png)
- [Match at 1440 × 900](ui-redesign-2026-10-18/match-1440x900.png)
- [Match at 375 × 812](ui-redesign-2026-10-18/match-375x812.png)

## Test evidence

- `frontend`: `npm test -- --run` — 15 test files, 134 tests passed.
- `frontend`: `npm run lint` — passed.
- `frontend`: `npm run build` — passed. Vite reported two upstream SignalR `/*#__PURE__*/` annotation warnings and removed those comments.
- `frontend`: `npm run test:e2e` — 11 Playwright tests passed, including axe WCAG A/AA scans at 320 px and 1440 px, keyboard navigation, selected-time replay, rights-gated pitch display, mobile table scrolling, and full-width API-backed pitch screenshots.
- Root: `node scripts/validate_operating_model.mjs` — passed; 59 stories and 28 Council skills validated.
- Root: `node scripts/test_design_skills.mjs` — passed.
- Copy scan: `design-slop-scan.mjs` passed on the changed match, context, and shell page TSX files.
- `git diff --check` — passed.

Vercel guideline review: keyboard names and focus, real button semantics, reduced-motion handling, dark color scheme, narrow-layout overflow, and conditional data sections were checked. Axe and browser keyboard tests ran; manual screen-reader testing was not run.

## Review state and limitations

This is implementation and automated verification evidence only. Product Owner acceptance and independent human UI/UX review remain pending; the story execution register was not changed. No Azure resources were modified and nothing was deployed.
