# EP01-S02 local runtime verification — 2026-10-12

**Scope:** exact code head `07de9e70103baba729af8bcba24731b93f20105d` from draft PR #44, in local branch `codex/ep01-s02-runtime-evidence`. All checks ran locally; no Azure resources, deployments, or credentials were used.

## Results

- `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s scripts -p 'test_*.py' -v` — 24 passed, 0 failed.
- `bash -n scripts/*.sh` — passed.
- `node scripts/validate_operating_model.mjs` and `node scripts/validate_operating_model.mjs --self-test` — passed (12 epics, 59 stories, 28 Council skills, 3 design skills, zero dependency cycles, two approval gates).
- `git diff --check` — passed before this documentation-only evidence update.
- `dotnet test PitchMind.slnx -c Release -m:1 --disable-build-servers -p:NuGetAudit=false` — 46 passed, 0 failed, 5 PostgreSQL integration tests skipped because no test database was configured. The first sandboxed attempt was aborted because VSTest could not bind its loopback socket; the same command completed successfully with local execution permission. NuGet audit was disabled for this run, so no package vulnerability audit result is claimed.
- Local Docker runtime: a uniquely named project `pitchmind-pr44-runtime-review` started PostgreSQL 17 and Redis 7.4 with host ports `127.0.0.1:25432` and `127.0.0.1:26379`. Both containers reported healthy; `dev-health.sh` passed. Container inspection confirmed only those loopback bindings. The generated `docker/.env` had mode `0600` and was gitignored.
- Local API smoke on `http://127.0.0.1:5089`: `/health` and `/api/health` returned 200; all six match reads returned typed `503 MATCH_DATA_UNAVAILABLE`, with correlation and contract headers. This is the current fail-closed API behavior until canonical PostgreSQL read composition exists.
- `dev-down.sh` stopped only the isolated test project and preserved its volumes. The pre-existing `pitchmind-postgres-1` and `pitchmind-redis-1` containers remained healthy and running with `pitchmind_postgres_data` and `pitchmind_redis_data` mounted at their data paths.

## Limitation observed

Running `dev-up.sh` with default ports/project name while the existing `pitchmind` stack owned ports 5432/6379 failed to bind Redis on 6379. The checked-in Compose project name is `pitchmind-local`, while the restored existing containers belong to Compose project `pitchmind`; the default `dev-health.sh` therefore does not discover those existing containers. I removed only the newly created, non-running containers and network from that failed attempt; no existing service or volume was stopped or removed. The isolated alternate-project run proves the bootstrap/runtime path with free ports, but does not prove reuse of the existing project or a fresh-machine workflow.

**Status:** observed local evidence only. Fresh-machine setup, hosted CI, current independent human review, and Product Owner acceptance remain open. EP01-S02 remains unaccepted. No Azure action was performed.
