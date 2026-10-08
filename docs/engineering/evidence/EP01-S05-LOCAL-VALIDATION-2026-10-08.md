# EP01-S05 local validation evidence — 2026-10-08

**Scope:** Current local baseline only; no Azure or hosted CI activity. The commands ran in the isolated `codex/ep01-s05-quality-gate` worktree based on `764251b39a497a0440ede1a0b87365e330692012`. The effective source tree is recorded in feature commit `fa4e85ea28ac12cc6117790b3bf39b79bc798285`; the subsequent integration/API checks ran at that commit, with documentation-only edits pending. The full quality script ran immediately before that commit against the same executable source and working-tree executable-bit change. Toolchains observed: .NET SDK 10.0.401, Node v22.17.1, npm 11.12.1.

## Executed checks

| Requirement | Command | Observed result |
|---|---|---|
| Complete local quality script | `./scripts/validate-all.sh` | Exit 0; final script pass message observed. The script originally lacked its executable bit; this branch sets it so the documented direct invocation succeeds. |
| .NET restore and Release build | Invoked by `./scripts/validate-all.sh` | Restore succeeded; build succeeded with 0 warnings and 0 errors. |
| .NET tests | Invoked by `./scripts/validate-all.sh` | 23 passed, 0 failed, 5 skipped. All five skipped tests are PostgreSQL ledger integration tests. |
| Frontend locked install, production build, tests, lint | Invoked by `./scripts/validate-all.sh` | `npm ci` installed 224 packages and reported 0 vulnerabilities; build and lint succeeded; Vitest: 1 file, 3 tests passed. npm printed deprecation warnings for `whatwg-encoding` and ESLint 9.39.5. |
| Feed profiler | `python3 -m unittest discover -s scripts -p 'test_profile_simulation_feed.py' -v` | 5 tests passed. |
| Council routing/contract validation | `node scripts/validate_operating_model.mjs` and `node scripts/validate_operating_model.mjs --self-test` | Both passed; 12 epics, 59 stories, 28 skills, no dependency cycles, two approval gates. |
| Shell syntax and Compose file syntax | `bash -n scripts/*.sh` and `docker compose --env-file docker/.env.example -f docker/compose.yml config --quiet` | Both passed. |
| Local service health and PostgreSQL integration | `bash scripts/dev-health.sh` and `bash scripts/test-postgres.sh` | PostgreSQL and Redis reported healthy; all 5 PostgreSQL integration tests passed, with 0 skipped. The script created and removed its uniquely named test database. Existing local services were reused and left running. |
| Local API readiness and HTTP smoke | Start with `dotnet run --project src/PitchMind.Api --urls http://127.0.0.1:5082 --no-build -c Release`, then run `bash scripts/dev-api-health.sh http://127.0.0.1:5082` and `python3 scripts/api-smoke.py http://127.0.0.1:5082` | Both passed against this worktree's API binary. The smoke covered selected-time score boundary, evidence timing, deterministic fallback, invalid audience/time, unknown match, and final score. The API process was stopped after the check. |

## Unverified gates and blockers

- The first Docker check was denied by the default sandbox. After running the authorized local Docker check, existing PostgreSQL/Redis services were healthy and the isolated integration tests passed. They were already running, so a clean-machine first startup path remains unverified.
- No Azure DevOps run, required GitHub branch check, deliberately failing gate, or hosted clean-runner result was observed.
- No security scanner, threat model execution, accessibility run, or performance/load test was run in this validation.
- The API smoke proves the existing offline demo contract only. The current `SyntheticMatchService` still supplies a transitional in-code fixture; it does not prove ingestion of the owner-supplied JSON or canonical PostgreSQL API behavior.
- Direct API startup emitted a warning that `src/PitchMind.Api/wwwroot` was absent. The API routes passed; combined-image static asset serving and a browser journey were not tested here.
- This is local evidence at the stated base revision, not a pipeline run or Product Owner acceptance. EP01-S05 remains unaccepted until hosted pipeline evidence and the remaining baseline checks are independently reviewed.

## Change made

`scripts/validate-all.sh` is now executable, matching the documented command in `docs/engineering/M02-LOCAL-DEVELOPMENT.md` and `docs/operating-model/STATIC-VERIFICATION-2026-10-08.md`. The command was rerun after changing the file mode and completed successfully with the limitations above.
