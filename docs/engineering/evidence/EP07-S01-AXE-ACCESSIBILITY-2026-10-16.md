# EP07-S01 — automated axe accessibility scan

**Result:** Automated scan passed for the scoped route states. EP07-S01 remains unverified and unaccepted.
**Original implementation commit:** `f2c9f50c78eaf8e16a544e03d3ae5a8d9e613d56` on `codex/ep07-s01-accessibility-followup`; the current-main restack is recorded below for PR [#308](https://github.com/prateekgangopadhyay/PitchMind/pull/308).
**Scope:** Add `@axe-core/playwright` as a development-only dependency and scan all seven routed views in the inline test-only fixture journey, including the expanded source disclosure.

## Executed evidence

- `cd frontend && npm ci --offline` — completed; 246 packages installed; npm reported 0 vulnerabilities.
- `cd frontend && npm run test:e2e -- e2e/ep07-s01-axe-accessibility.spec.ts` — passed after the clean install: 1 Playwright test passed. The first sandboxed attempt could not bind the local Vite server (`EPERM`); rerunning with local loopback permission passed.
- `cd frontend && npm run test:e2e` — 10 tests passed, 0 failed (full E2E suite, after the spec was added and before the clean install).
- `cd frontend && npm test -- --run` — 8 files passed; 97 tests passed, 0 failed.
- `cd frontend && npm run lint` — passed.
- `cd frontend && npm run build` — passed; two existing Rollup SignalR pure-annotation warnings were emitted.
- `@axe-core/playwright` and `axe-core` are both 4.13.0; Playwright is 1.63.0.

## Current-main exact-head revalidation

On restacked head `dcc09ac1578dbe7fcba2d591fcee5a9d5c0e4f43`, based on main `9ef3ea977a7bf3fcb1b176e4e08efcb88f7bdb7c`, the coordinator independently ran:

- `cd frontend && npm ci --offline` — completed; 246 packages installed; npm reported 0 vulnerabilities.
- `cd frontend && npm test -- --reporter=dot` — 8 files and 97 tests passed, 0 failed.
- `cd frontend && npm run test:e2e -- --reporter=line` — 10 passed, 0 failed, including the axe spec and all 16 scans. The first sandboxed attempt was denied localhost binding (`EPERM`); rerun with local loopback permission passed.
- `cd frontend && npm run build` and `npm run lint` — passed; build emitted two existing SignalR Rollup annotation warnings.
- `node scripts/validate_operating_model.mjs --self-test` and `git diff --check` — passed.

These exact-head results supersede the historical counts above. The working tree contains no generated Playwright test results.

The automated spec performs 16 scans: Welcome, Home/match list, Home with source details expanded, unavailable Match overview, Teams, Players, Insights, and Analyst, each at 320px and 1440px. It applies axe tags `wcag2a`, `wcag2aa`, `wcag21a`, `wcag21aa`, and `wcag22aa`; each scan returned zero violations. The Playwright test attaches a JSON summary of scan names, widths, and violation counts.

The fixture and failed match reads are test-only mocked responses. This test neither loads nor admits `data/simulations/manu-liv-match-dataset.json`, and it does not verify football facts or feed semantics.

## Limits and remaining gates

An automated axe pass is not a WCAG conformance declaration and does not prove all criteria. Real screen-reader testing, complete manual keyboard testing, browser zoom at 200%, full contrast review, independent human UX/accessibility review, the source-backed viewer journey, and Product Owner acceptance remain pending. EP07-S01 is not accepted. No Azure operation or deployment occurred.
