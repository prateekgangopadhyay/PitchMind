# EP05-S02 continuity guard verification — 2026-10-15

## Scope

Local feature branch `codex/ep05-s02-interrupted-chain-guard-restacked`, originally based on `main` `6178d656283fb375711e8836fd09e93e46877989`, first restacked onto `740d4d947d46646c55f24467d71a61de1af752f2` after PR #48 merged, then restacked onto `7bbe438436d57c45129c459b7aaf963e4b5ddbc2` after PR #253 merged. This slice advances the simulated detector policy from `press-sequence-v2` to `press-sequence-v3`. It rejects a candidate chain when an intervening turnover or completed pass appears before the expected pressured response or regain. The policy remains `unapproved-demo` and `publishable=false`.

Application callers, persisted replay test pins, and deterministic v3 replay hashes were updated. Domain tests cover completed circulation before the pressured error, a turnover between error and regain, and a completed pass between error and regain. The football LLD and EP05-S02 story packet describe the continuity rule. No public API shape or architecture boundary changed.

## Executed verification

Command: `dotnet test PitchMind.slnx --configuration Release --no-restore --verbosity minimal -m:1 --disable-build-servers -p:NuGetAudit=false`

Historical result on the first restack at base `740d4d947d46646c55f24467d71a61de1af752f2`: **325 passed, 0 failed, 24 skipped**. Per project: API 12/12; Application 93/93; Architecture 22/22; Contracts 110/110; Domain 70/70; Infrastructure 18 passed and 24 skipped. Skips are PostgreSQL integration tests, including the persisted replay path. They were not executed against PostgreSQL.

Restore assets were reused from the local current-main audit worktree; this command did not perform a package restore. `node scripts/validate_operating_model.mjs` passed with 12 epics, 59 stories, 28 Council skills, 5 design skills, 33 discoverable skills, no dependency cycles, and 2 approval gates. `git diff --check` passed. NuGet audit was disabled for this offline test run.

Current-base rerun on the restacked code at base `7bbe438436d57c45129c459b7aaf963e4b5ddbc2` (code head `28a2224cd5b6cf3d0a32134b6c584c608bfe9916`): **328 passed, 0 failed, 24 skipped**. Per project: API 12/12; Application 93/93; Architecture 24/24; Contracts 110/110; Domain 71/71; Infrastructure 18 passed and 24 skipped. All 24 skips remain opt-in PostgreSQL integration tests. The same validator and its DAG self-test passed, as did `git diff --check`. This run required local loopback socket access for VSTest; the sandboxed attempt aborted before executing tests. No package restore ran and NuGet audit was disabled.

## Remaining gates

The historical suite ran after restacking onto `740d4d947d46646c55f24467d71a61de1af752f2`; the current-base rerun used detector code commit `2ed609127faf1778060ca155331175306ecfac36` on base `7bbe438436d57c45129c459b7aaf963e4b5ddbc2`. Independent exact-head review found no code P0–P2 findings; its documentation consistency finding is corrected in this evidence update. PostgreSQL runtime integration, EP05-S01/EP04-S04 dependency verification, football-domain/Product Owner approval, source-feed semantic certification, and story acceptance remain pending. This evidence does not authorize deployment.
