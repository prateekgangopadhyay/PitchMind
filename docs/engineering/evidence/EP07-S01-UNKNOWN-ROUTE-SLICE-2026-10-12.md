# EP07-S01 — unknown-route navigation slice evidence

**Scope:** Keep unrecognized URLs truthful and recoverable within the existing six-section application shell. This does not implement another product screen or complete EP07-S01.

## Observed gap and change

At base `373faf5bec7604487c297cd15bfd029f24b7d014`, the route resolver mapped every unrecognized pathname or hash to `/match`. The URL could remain `#/old-route` while the Match question and Match active-navigation state were shown. The resolver now returns an unavailable route for unknown URLs, strips query strings before resolving known sections (so `/analyst?matchId=...` remains Analyst), and listens for `hashchange` and `popstate` route changes. The unavailable state names the problem, has no falsely selected primary-nav item, focuses the page heading after client-side navigation, and provides a working `Go to Home` link.

No football facts, API/domain contracts, authentication behavior, or match state were changed. No mockup placeholder content was copied.

## Verification performed

Environment: Node `v22.17.1`, npm `11.12.1`; clean lockfile install with `npm ci --offline` (224 packages, audit reported 0 vulnerabilities).

| Check | Result |
|---|---|
| `npm test` in `frontend/` | PASS — 6 tests, 6 passed; includes unknown URL, no false Match state/current item, focus on Home recovery, query-bearing Analyst deep link, and a jsdom test that dispatches a `popstate` event after changing the URL with `pushState` |
| `npm run build` in `frontend/` | PASS — TypeScript and Vite production build completed |
| `npm run lint` in `frontend/` | PASS |
| `node scripts/validate_operating_model.mjs` | PASS — 12 epics, 59 stories, 28 Council roles, 5 design skills, no dependency cycles |
| `node scripts/validate_operating_model.mjs --self-test` | PASS — DAG, stacked-PR trigger and deliberate gate-failure probe self-tests |
| `node scripts/test_design_skills.mjs` | PASS — design-skill registry and copy-instrument self-test |
| `node scripts/design-slop-scan.mjs frontend/src/App.tsx` | PASS — no warnings or hard failures |
| `git diff --check` | PASS |

## Still unverified / open

This is not a browser or screen-reader certification: desktop/mobile screenshots, 320px reflow, contrast measurement, real assistive-technology sampling, fresh Vercel guideline retrieval, and independent Awesome/UI/UX/accessibility/QA review were not performed for this slice. Story readiness also remains unproven: although the routing graph lists EP01-S04 as its dependency, the EP07-S01 Definition of Ready separately requires EP06 OpenAPI and mocked canonical fixtures, and this work did not establish that both are complete. EP07-S01 still lacks the full seven-screen implementation, a canonical source-backed fixture, the golden Home → match → evidence → analyst journey, mobile and screen-reader evidence, independent human review, and Product Owner acceptance. The story remains unaccepted.
