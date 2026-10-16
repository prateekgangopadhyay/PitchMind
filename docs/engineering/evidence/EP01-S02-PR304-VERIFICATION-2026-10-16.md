# EP01-S02 PR #304 current-head verification — 2026-10-16

**Scope:** Focused LOCAL bootstrap verification for the existing draft PR #304. This evidence does not accept EP01-S02.

**Exact tested worktree head:** `8968b7f9df14fc0276bc2b6f83ebd1e21d9a7277` (documentation-only head; application/bootstrap code is inherited from base main `5f6f3367743bfac754d9b6ba7144091f1baed9a`).

## Results

- `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_dev_bootstrap -v` — **34 passed, 0 failed**.
- `bash -n scripts/dev-up.sh scripts/dev-down.sh scripts/dev-reset.sh scripts/dev-health.sh scripts/lib/local-docker-target.sh` — passed.
- `docker compose --env-file docker/.env.example -f docker/compose.yml config --quiet` — passed; configuration-only, no containers started.
- `node scripts/validate_operating_model.mjs` — passed: 12 epics, 59 stories, 28 Council skills, 5 design skills, 33 discoverable skills, 0 dependency cycles, 2 approval gates.
- `git diff --check` — passed.
- `dotnet restore PitchMind.slnx -p:NuGetAudit=false --disable-parallel` — passed for all projects.
- `dotnet test PitchMind.slnx -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` — **522 passed, 0 failed, 38 skipped**. The skips are opt-in PostgreSQL tests (37 Infrastructure plus one API readiness test); no test database was configured. VSTest required permission for local test-host IPC sockets. The first sandboxed invocation was unable to bind those local sockets; it is not a test failure.
- Read-only Docker inspection — PostgreSQL and Redis containers reported healthy, bound to `127.0.0.1:5432` and `127.0.0.1:6379`; mounts were `pitchmind_postgres_data:/var/lib/postgresql/data` and `pitchmind_redis_data:/data`. No Docker lifecycle, migration, or data inspection operation was run.
- Isolated clean-source Compose exercise — with unique project `pm-ep01s02-20261016`, throwaway volumes, ports `35432`/`36379`, and generated temporary credentials, `dev-up.sh` created PostgreSQL 16 and Redis 7.4 and waited for both health checks. `dev-health.sh` passed. `dev-migrate.py` applied migrations through schema version 10 and provisioned the restricted API role on this isolated database.
- Local API smoke against the isolated database — the Release API listened only on `127.0.0.1:5183`, with `Realtime:Provider=disabled` so no outbox worker was registered. `dev-health.sh http://127.0.0.1:5183` passed; `/health/ready` returned 200 after the schema migration.
- Lifecycle/restart checks — `dev-down.sh` stopped and removed the isolated containers/network while preserving both throwaway volumes. `dev-up.sh` restarted from those volumes and passed its PG major-version preflight and health checks. `dev-reset.sh --force` then removed only the isolated resources; follow-up volume listing confirmed both original `pitchmind_*` volumes still existed. The temporary `docker/.env` was deleted.
- Existing local database diagnostic — against the original user volumes, API liveness `/health` returned 200 but database-backed `/health/ready` returned 503. Read-only PostgreSQL probes found the `pitchmind.schema_versions` relation absent and the `pitchmind_api` role missing. No migration, credential change, or data mutation was performed against the original volumes; this remains a local setup blocker for that existing database.

The focused suite covers local credential validation/generation, loopback-only ports, local Docker target guards, PostgreSQL image/volume major compatibility, isolated project/volume names, and safe stop/reset behavior through controlled command stubs.

## Limits

The isolated stack proves a clean-source local start, migration, readiness, stop-preserves-volumes, restart, and explicit cleanup path on this host. It is not a fresh-machine trial; it reused local Docker images and caches. PostgreSQL integration tests, frontend suites, hosted CI, independent human review, and Product Owner acceptance remain open. The original `pitchmind_*` volumes are still in place and unmodified; their missing schema/role requires owner direction before migration. EP01-S02 remains unaccepted. No Azure resources or deployments were changed.
