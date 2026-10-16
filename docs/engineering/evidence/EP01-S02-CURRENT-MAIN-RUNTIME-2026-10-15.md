# EP01-S02 current-main isolated LOCAL runtime evidence — 2026-10-15

**Source revision:** `7847d177b435faa9211aa1223c238ea0c2f8b93c` (`main`).
**Environment:** LOCAL on the existing Docker Desktop host. This verifies a clean tracked-source copy on this host; it is not a fresh-machine certification.

## Executed checks

- `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_dev_bootstrap -v`: **32 passed, 0 failed** on the exact source revision.
- `docker compose --env-file docker/.env.example -f docker/compose.yml config --quiet`: passed.
- Archived tracked source from the exact revision into `/private/tmp` without `.git`, ignored files, or an existing `docker/.env`. In that disposable copy only, changed the Compose project name and both explicit volume names to unique test identities so the test could not use the user's PitchMind data volumes.
- `POSTGRES_IMAGE=postgres:16-alpine POSTGRES_PORT=35432 REDIS_PORT=36379 ./scripts/dev-up.sh`: passed. The script generated an ignored local credential file in the disposable copy; its contents were not recorded.
- `./scripts/dev-health.sh`: passed for PostgreSQL and Redis. Runtime inspection showed PostgreSQL 16 and Redis 7.4 healthy and bound only to `127.0.0.1:35432` and `127.0.0.1:36379`.
- `./scripts/dev-down.sh`: stopped the isolated services while preserving `pm_s02_current_main_20261015_pg` and `pm_s02_current_main_20261015_redis`.
- A second `dev-up.sh` and health check passed against those retained disposable volumes.
- `./scripts/dev-reset.sh --force`: removed only the isolated containers, network, and two disposable volumes. The temporary source copy and generated credentials were removed.
- After cleanup, the pre-existing `pitchmind-postgres-1` and `pitchmind-redis-1` containers were still healthy on loopback ports 5432/6379; `pitchmind_postgres_data` and `pitchmind_redis_data` remained present.
- `dotnet test PitchMind.slnx -c Release -m:1 --disable-build-servers --no-restore -p:NuGetAudit=false`: **427 passed, 24 skipped, 0 failed** across the solution on this exact source revision. The 24 skipped cases are opt-in PostgreSQL integration tests; no PostgreSQL integration test execution is claimed.
- Started the built API on `127.0.0.1:5089` with `Development`, `Runtime:Profile=local`, and the local-only `Identity:DevelopmentHeaderAuthEnabled=true` test setting. `./scripts/dev-api-health.sh http://127.0.0.1:5089` passed. `python3 scripts/api-smoke.py http://127.0.0.1:5089` passed: API health was available and protected match read/replay routes returned typed `503 MATCH_DATA_UNAVAILABLE` while canonical route composition remains unavailable. The process was stopped after the checks.
- `git diff --check`: passed on the evidence update.

## Limits and status

This run used images already cached on the host. It does not prove a first-time image download or a truly fresh-machine setup. PostgreSQL integration tests were skipped because their opt-in test database was not configured. Hosted Azure DevOps CI and Product Owner acceptance remain pending. EP01-S02 remains unaccepted. No Azure resources or deployments were accessed.
