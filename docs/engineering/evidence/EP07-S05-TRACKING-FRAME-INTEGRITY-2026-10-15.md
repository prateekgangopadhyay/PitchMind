# EP07-S05 — tracking-frame integrity follow-up

**Scope:** Stacked child of PR #260, based on parent branch commit `c2840a4869a72024fc35d1ee2a1f7bbb3c5a24e5`. Code commit: `1cc437dd9ec89876cf59ef05c076574992a8686e`. The child addresses two P2 findings from an independent read-only review of the parent implementation. It does not complete or accept EP07-S05.

## Changes

- PostgreSQL now returns tracking payload and quality text only when their encoded lengths are within their respective 32 KiB and 4 KiB limits. Oversized values are represented as null and rejected before client string access, avoiding transfer/materialization of oversized JSON text in the API process.
- The frontend frame contract includes the nested canonical revision, and the selected-context integrity check requires it to equal the indexed frame revision.
- Added integration coverage for oversized payload and quality JSON, plus a frontend negative test for mismatched nested canonical revision.

## Verification performed

- `dotnet test PitchMind.slnx -c Release -m:1 --disable-build-servers --no-restore -p:NuGetAudit=false`: **434 passed, 27 skipped, 0 failed** (461 total). Skips are opt-in PostgreSQL integration cases in this general run; NuGet audit was disabled.
- `PITCHMIND_TEST_RESULTS_DIRECTORY=/private/tmp/pitchmind-ep07-s05-integrity-postgres-20261015 ./scripts/test-postgres.sh`: **50 passed, 0 skipped, 0 failed** against a unique temporary database. This includes the two new oversized-JSON cases. The harness removed its temporary database.
- `npm test -- --run`: **55 passed, 0 failed**.
- `npm run build`: passed.
- `npm run lint`: passed.
- `node scripts/validate_operating_model.mjs`: passed (12 epics, 59 stories, 28 Council skills, no dependency cycles).
- `git diff --check`: passed before the code commit.
- Independent exact-head review of this child is pending; the review that raised the findings inspected the parent, not this child. These local test results are not submitted GitHub review or hosted CI.

## Remaining evidence and acceptance

- The whole EP07-S05 evidence drawer/replay journey remains incomplete. Keyboard/screen-reader review, source evidence comparison, browser E2E, hosted CI, submitted independent review and Product Owner decision remain pending.
- The source feed's coordinate, clock, metric and canonical mapping semantics remain unresolved; these changes do not admit the candidate feed.
- Routing remains `unverified`, and the PO acceptance ledger remains pending. No Azure resources, infrastructure, or deployment were changed.
