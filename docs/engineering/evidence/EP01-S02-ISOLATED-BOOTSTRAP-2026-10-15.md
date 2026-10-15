# EP01-S02 isolated local bootstrap verification — 2026-10-15

## Scope

Verified the local PostgreSQL and Redis bootstrap from exact source revision
`684574264d7e1f37921f1bc02c834d607068c2e6` in an isolated clone. This is
same-host LOCAL evidence, not fresh-machine acceptance. The test did not modify
the repository's retained `pitchmind_postgres_data` or `pitchmind_redis_data`
volumes.

## Checks performed

- `python3 -m unittest scripts.test_dev_bootstrap -v`: **32 passed**.
- `bash -n scripts/dev-up.sh scripts/dev-health.sh scripts/dev-api-health.sh scripts/dev-down.sh scripts/dev-reset.sh scripts/lib/local-docker-target.sh`: passed.
- `node scripts/validate_operating_model.mjs --self-test`: passed.
- `docker compose --env-file docker/.env.example -f docker/compose.yml config --quiet`: passed. Resolved services published PostgreSQL and Redis only on `127.0.0.1`.
- Docker Desktop **29.8.1** was available. The locally cached `postgres:16-alpine` and `redis:7.4-alpine` images were used; no image pull was required.
- `./scripts/dev-up.sh` created an isolated project named `pitchmind-ep01-s02-20261015`, with unique volumes `pm_ep01_s02_pg_20261015` and `pm_ep01_s02_redis_20261015`. It generated private credentials with file mode `0600`. Host ports were randomly selected as `52231` and `52232`; Docker inspection showed both bindings were loopback-only.
- `./scripts/dev-health.sh` passed with PostgreSQL and Redis healthy. `./scripts/dev-down.sh` stopped and removed only the isolated project containers/network while preserving both test volumes. A second `dev-up.sh` and health check passed.
- Final cleanup removed only the two unique test volumes and restored the tracked Compose file. Post-run read-only inspection confirmed `pitchmind_postgres_data` and `pitchmind_redis_data` still existed; no temporary test containers or `pm_ep01_s02_*` volumes remained.

## Limits and status

No API process was started in this run, so API readiness was not reverified at
this exact revision. No fresh-machine setup was performed. Hosted Azure DevOps
checks, independent current-head review, and Product Owner acceptance remain
pending. EP01-S02 is still unaccepted. No Azure resource or deployment action
occurred.
