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

`scripts/validate-all.sh` is executable, matching the documented command in `docs/engineering/M02-LOCAL-DEVELOPMENT.md` and `docs/operating-model/STATIC-VERIFICATION-2026-10-08.md`. This PR adds a fail-closed NuGet advisory gate to the local validation script and Azure DevOps .NET template, explicit `npm audit --audit-level=high` to the local and pipeline frontend gates, and parser tests for advisory findings, malformed report data, and exact solution-project coverage. An independent review found that malformed nested framework data could pass; the parser now validates nested package lists, requires the repository's observed default NuGet audit source, fails on successful CLI warnings, and limits the live NuGet command to 120 seconds.

## 2026-10-09 dependency-gate verification

| Requirement | Command | Observed result |
|---|---|---|
| NuGet advisory report and fail-closed parser | Current revision: `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_check_nuget_vulnerabilities -v`; live attempt: `python3 scripts/check_nuget_vulnerabilities.py PitchMind.slnx` | Parser tests passed 7/7, including malformed nested data, advisory findings, exact source/project validation, warning rejection, and command-timeout handling. The current live audit failed closed with `NuGet vulnerability audit timed out after 120 seconds`; the dependency advisory result is unavailable in this environment. Prior scanner revision reported no advisories across all 12 solution projects using `https://api.nuget.org/v3/index.json`, but that is not current-head evidence. |
| Scanner positive and negative cases (historical) | `python3 -m unittest scripts.test_check_nuget_vulnerabilities -v` on the preceding scanner revision | 4 passed, 0 failed on that revision. The current parser suite is 7/7 as recorded in the row above; this prior test result is retained only as part of the historical full-gate output below. |
| npm advisory threshold | `npm audit --audit-level=high` | Exit 0; 0 vulnerabilities in the locked frontend dependency tree. |
| Updated full local quality gate | `./scripts/validate-all.sh` on the preceding scanner revision | Exit 0; NuGet advisory gate passed for 12 projects; .NET restore/build had 0 warnings/errors; .NET tests 23 passed, 0 failed, 5 PostgreSQL skips; frontend install/build/lint and 3 tests passed; npm audit 0 vulnerabilities; source profiler tests 5 passed; NuGet parser tests 4 passed; operating-model validation and DAG self-test passed. Full output: `/private/tmp/ep01-s05-quality-gate-final-2026-10-09.log`. This full-gate result predates the independent-review parser hardening in the current revision. |

These dependency advisories are current at the execution date and should be rerun in CI. No secret scanner or full threat-model exercise ran in this change. The Azure DevOps pipeline has not been registered or executed in Azure DevOps, and no branch policy/required check was observed. The five aggregate-run PostgreSQL skips are separately covered by earlier local PostgreSQL integration evidence above; this aggregate invocation did not execute them.

## 2026-10-09 current-head revalidation

**Code revision:** `59507d182161b61db0a099dcd6f3abf66bab2c0e` (EP01-S05 feature commits rebased onto main `16733960eb17b6213805ea1242bcd59888132355`; evidence captured before the documentation-only evidence commit). The rebased `scripts/validate-all.sh` retains both dependency-advisory gates and the current-main design-skill validation.

| Requirement | Command | Observed result |
|---|---|---|
| Complete local quality script | `./scripts/validate-all.sh` | Exit 0. Final output: `PASS: .NET, dependency audits, frontend, feed profiler, design and council contract validation`. |
| .NET restore/build and tests | Invoked by the quality script | Restore succeeded; Release build succeeded with 0 warnings and 0 errors; 23 tests passed, 0 failed, 5 skipped. The five skipped tests are PostgreSQL integration tests because this aggregate command had no test database configured. |
| NuGet advisories | `python3 scripts/check_nuget_vulnerabilities.py PitchMind.slnx` and quality-script invocation | PASS: no advisories across all 12 solution projects. This supersedes the earlier timeout recorded above for this branch before the rebase. |
| Frontend dependencies/build/tests/lint | Invoked by the quality script | `npm ci` installed 224 packages; both npm audit invocations found 0 vulnerabilities; Vite production build and ESLint passed; Vitest passed 3/3. npm printed deprecation warnings for `whatwg-encoding` and ESLint 9.39.5. |
| Python validation | Invoked by the quality script | Feed profiler tests passed 5/5; NuGet parser tests passed 7/7. |
| Council/design checks | Operating-model validator, DAG self-test and `node scripts/test_design_skills.mjs` | All passed; validator reported 12 epics, 59 stories, 28 Council skills + 3 design skills, zero dependency cycles, and two approval gates. |
| Diff and shell syntax | `git diff --check`; `bash -n scripts/validate-all.sh` | Passed. |

The aggregate run still skips all five PostgreSQL tests; isolated PostgreSQL evidence remains in the earlier section of this file. No Azure DevOps run, required branch check, deliberate-failure gate, hosted clean-runner result, secret scanner, full threat-model execution, accessibility run, or performance/load test was observed. This local run is not Product Owner acceptance. No Azure resource or deployment action was performed.

## 2026-10-12 current-main integration revalidation

The PR feature branch was rebased without conflicts onto current `origin/main` at `6932cfea490cd8c9257dfb88bc9091219f12cd40`. The tested code revision is `93007332c7fc9b576333883cf3cb37325ccfbccf`; it is a descendant of that exact main revision, and the local branch contains ten feature commits and zero commits behind main.

| Requirement | Command | Observed result |
|---|---|---|
| Focused NuGet report parser tests | `python3 -m unittest discover -s scripts -p 'test_check_nuget_vulnerabilities.py' -v` | Passed 7/7. |
| Feed profiler tests | `python3 -m unittest discover -s scripts -p 'test_profile_simulation_feed.py' -v` | Passed 5/5. |
| Council route and approval validation | `node scripts/validate_operating_model.mjs`; `node scripts/validate_operating_model.mjs --self-test`; `node scripts/test_design_skills.mjs` | All passed. The operating-model validator reported 12 epics, 59 stories, 28 Council skills, zero dependency cycles, and two approval gates. |
| Exact-main diff whitespace validation | `git diff --check origin/main...HEAD` | Passed before the evidence-only documentation update. |
| Complete local quality script | `./scripts/validate-all.sh` | Started at the tested code revision. The command emitted no output while its first step, `dotnet restore PitchMind.slnx`, was running. A cancellation request did not return during observation. Restore/build/tests and all later script stages are therefore unverified on this rebased head; no aggregate pass is claimed. |

The preceding 2026-10-09 full local quality-script and isolated PostgreSQL results apply to their recorded older feature revisions only. They do not establish current-main aggregate validation. This integration rebase does not constitute story acceptance; independent review and Product Owner acceptance remain pending. No Azure command, resource mutation, deployment, PR-ready transition, or PR merge was performed.

## 2026-10-12 latest-main integration revalidation

After the prior rebase, `main` advanced again by merging PR #116. The PR branch was rebased conflict-free onto the exact new base `de8298e1fd836c97037977bada1b7c643d926ddc`; the code tree verified is `30bc33dff68ee9c17675ec23b23bdfa2d1296289`. The branch is a descendant of that base with eleven feature commits ahead and zero behind.

| Requirement | Command | Observed result |
|---|---|---|
| Focused NuGet report parser tests | `python3 -m unittest discover -s scripts -p 'test_check_nuget_vulnerabilities.py' -v` | Passed 7/7. |
| Feed profiler tests | `python3 -m unittest discover -s scripts -p 'test_profile_simulation_feed.py' -v` | Passed 5/5. |
| Council route and approval validation | `node scripts/validate_operating_model.mjs` | Passed; 12 epics, 59 stories, 28 Council skills, five design skills, zero dependency cycles, and two approval gates. |
| Council DAG self-test | `node scripts/validate_operating_model.mjs --self-test` | Passed. |
| Five design-skill registry tests | `node scripts/test_design_skills.mjs` | Passed. |
| Exact-base ancestry and whitespace | `git rev-list --left-right --count HEAD...origin/main`; `git diff --check origin/main...HEAD` | `11 0`; diff check passed. The local rebase and merge-tree reported no conflicts. |

The full local quality script was not rerun after this latest rebase. The preceding attempt ran on the prior `6932cfea` base, remained silent during its first `dotnet restore PitchMind.slnx` step, and exited 1 without diagnostics. Therefore no current-head .NET restore/build/test, NuGet live audit, frontend, PostgreSQL, security, accessibility, or performance result is claimed. The current PR remains open and unmerged; story acceptance and a current-head independent review are pending. No Azure actions were performed.

## 2026-10-12 full gate on current PR head

After the cache/offline diagnosis, the unchanged validation script completed at PR head `16e980e565cee1621e627faad81d3d2ddf2032de`. This head contains the tested code tree at `30bc33dff68ee9c17675ec23b23bdfa2d1296289` plus the evidence-only commit `16e980e`.

| Requirement | Command | Observed result |
|---|---|---|
| Complete local quality script | `./scripts/validate-all.sh` | Exit 0; final output: `PASS: .NET, dependency audits, frontend, feed profiler, design and council contract validation`. Run with approved local execution permissions; the script and security thresholds were unchanged. |
| .NET restore | Invoked by the quality script | All projects up-to-date; restore passed. |
| NuGet vulnerability audit | Invoked by the quality script | No advisories across all 12 solution projects. |
| Release build and tests | Invoked by the quality script | Build succeeded with 0 warnings and 0 errors. 23 tests passed; 0 failed; 5 PostgreSQL integration tests skipped because no test database was configured. |
| Frontend dependency audit, build, tests and lint | Invoked by the quality script | `npm ci` installed 224 packages; both npm audits found 0 vulnerabilities; Vite production build and ESLint passed; Vitest passed 3/3. npm printed deprecation warnings for `whatwg-encoding` and ESLint 9.39.5. |
| Feed profiler and NuGet parser | Invoked by the quality script | Feed profiler tests 5/5; NuGet parser tests 7/7. |
| Operating model, DAG and design skills | Invoked by the quality script | All passed; validator reported 12 epics, 59 stories, 28 Council skills, five design skills, zero dependency cycles and two approval gates. |

The initial default-sandbox attempt did not demonstrate a code defect. A focused restore in that sandbox produced `NuGet.targets(198,5): error : Access to the path '/Users/prateekgangopadhyay/.codex/worktrees/ep01-s05-quality-gate/PitchMind/src/PitchMind.Domain/obj/<guid>.tmp' is denied. Operation not permitted`. A sandboxed request to the configured NuGet endpoint also failed DNS resolution: `curl: (6) Could not resolve host: api.nuget.org`. The approved local retry allowed the normal restore/audit to run; the complete unchanged gate passed. No code or security-gate changes were needed. This local pass is not Azure DevOps evidence, independent review, or Product Owner acceptance. PostgreSQL integration, security scanning, threat-model, accessibility, performance, hosted clean-runner and deliberate-failure gates remain unverified. No Azure command or resource mutation was performed.
