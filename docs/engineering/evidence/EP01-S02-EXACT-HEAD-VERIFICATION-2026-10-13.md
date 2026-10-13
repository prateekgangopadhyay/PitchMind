# EP01-S02 exact-head verification — 2026-10-13

**Story:** EP01-S02, reproducible local developer workstation  
**Tested code revision:** `ae6908c408fc82edd39f93d542e41975c1c3b976` (PR #44 head at verification time)  
**Environment:** disposable local clone, macOS, .NET 10 / Node 22 toolchains.  
**Scope:** independent local validation of the current PR code; this is not a clean-machine or cloud run.

## Observed results

- `dotnet test PitchMind.slnx -c Release -m:1 --disable-build-servers -p:NuGetAudit=false --no-restore`: **46 passed, 0 failed, 5 skipped**. The five skipped tests are PostgreSQL integration tests because this run had no configured test database.
- `python3 -m unittest discover -s scripts -p 'test_*.py' -v`: **24 passed, 0 failed**.
- `npm ci --offline`: completed; npm reported **0 vulnerabilities** in its audit summary.
- In `frontend/`, `npm test -- --run`: **3 passed, 0 failed**; `npm run lint`: passed; `npm run build`: passed.
- `bash -n scripts/dev-up.sh scripts/dev-health.sh scripts/dev-down.sh scripts/dev-reset.sh`: passed.
- `docker compose --env-file docker/.env.example -f docker/compose.yml config --quiet`: passed as daemonless configuration validation.
- `node scripts/validate_operating_model.mjs --self-test`: passed.
- `git diff --check`: passed after this evidence change was prepared.

The first sandboxed VSTest attempt aborted before executing tests because the runner could not bind its loopback communication socket. The same command was rerun with local socket permission and completed with the results above. The initial abort is not counted as a test failure.

## Limits and outstanding gates

No Docker container or volume was started, stopped, recreated, or removed in this run. PostgreSQL integration tests were skipped; no API smoke, fresh-machine bootstrap, hosted Azure DevOps run, human independent review of this evidence update, or Product Owner acceptance is claimed. Runtime startup/restart evidence recorded for earlier code heads is not silently attributed to this exact code revision. EP01-S02 remains unaccepted and PR #44 remains a draft pending current-head independent review and the separate Product Owner decision. No Azure resources were provisioned or changed and no deployment was attempted.
