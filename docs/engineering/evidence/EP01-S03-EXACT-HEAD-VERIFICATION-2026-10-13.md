# EP01-S03 exact-head verification — 2026-10-13

**Story:** EP01-S03, Azure DevOps validation pipeline
**Tested integrated code revision:** `44819ef473241c5938e441627b45cc9ec699f5bc` (PR #21 head at verification time)
**Environment:** disposable local clone, macOS; .NET 10 and Node 22 toolchains.
**Scope:** local validation of the integrated story implementation; not an Azure DevOps hosted run.

## Observed results

- `dotnet test PitchMind.slnx -c Release -m:1 --disable-build-servers -p:NuGetAudit=false`: **40 passed, 0 failed, 5 skipped**. The five skipped cases are PostgreSQL integration tests; this run had no test database configured.
- Focused gate probe, opt-out: `env -u PITCHMIND_GATE_FAILURE_PROBE dotnet test tests/PitchMind.Architecture.Tests/PitchMind.Architecture.Tests.csproj -c Release --no-build --filter FullyQualifiedName~DeliberateGateFailureProbe`: **1 passed**.
- The same focused test with `PITCHMIND_GATE_FAILURE_PROBE=1` failed **1/1** and returned exit code **1**, as intended. The verification wrapper checked that the failing test was reported and treated that result as successful probe validation.
- `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s scripts -p 'test_*.py' -v`: **13 passed, 0 failed**.
- `npm ci --offline`: completed; npm reported **0 vulnerabilities** in its audit summary. In `frontend/`, `npm test -- --run`: **3 passed**; lint and production build passed.
- The Azure Pipelines YAML parsed with PyYAML 6.0.3 and contained two stages. Daemonless Compose config validation passed.
- `node scripts/validate_operating_model.mjs`: passed for 12 epics, 59 stories and 28 Council roles; `node scripts/validate_operating_model.mjs --self-test`: passed for the dependency graph, stacked-PR trigger and deliberate failure-probe mutations.
- `git diff --check 44819ef^ 44819ef`: passed.

VSTest required local loopback socket permission; tests ran after permission was granted. NuGet vulnerability audit was disabled for the .NET command.

## Limits and outstanding gates

No Docker containers or volumes were started, stopped, or changed. No Azure DevOps pipeline run, GitHub required-check/branch-policy binding, or hosted deliberate-failure blocking behavior was observed. The five PostgreSQL tests remain unverified in this run. EP01-S03 remains unaccepted pending hosted validation, current-head independent human review, required-check binding evidence, and Product Owner acceptance. PR #21 remains a draft. No Azure resources were provisioned or changed and no deployment was attempted.
