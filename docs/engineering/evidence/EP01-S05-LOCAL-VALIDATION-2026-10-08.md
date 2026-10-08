# EP01-S05 local validation evidence — 2026-10-08

**Scope:** Current local baseline only; no Azure or hosted CI activity. The commands ran in the isolated `codex/ep01-s05-quality-gate` worktree at base revision `764251b39a497a0440ede1a0b87365e330692012`. Toolchains observed: .NET SDK 10.0.401, Node v22.17.1, npm 11.12.1.

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

## Unverified gates and blockers

- `docker info` failed with permission denied on the local Docker socket. PostgreSQL and Redis were not started. The five database integration tests remained skipped, and no live API/service smoke ran.
- No Azure DevOps run, required GitHub branch check, deliberately failing gate, or hosted clean-runner result was observed.
- No security scanner, threat model execution, accessibility run, or performance/load test was run in this validation.
- This is local evidence at the stated base revision, not a pipeline run or Product Owner acceptance. EP01-S05 remains unaccepted until hosted pipeline evidence and the remaining baseline checks are independently reviewed.

## Change made

`scripts/validate-all.sh` is now executable, matching the documented command in `docs/engineering/M02-LOCAL-DEVELOPMENT.md` and `docs/operating-model/STATIC-VERIFICATION-2026-10-08.md`. The command was rerun after changing the file mode and completed successfully with the limitations above.
