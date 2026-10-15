# EP01-S02 PostgreSQL volume compatibility evidence — 2026-10-16

## Scope

PR #44's Compose file selected PostgreSQL 17 regardless of the local `POSTGRES_IMAGE` setting, and Compose's implicit volume name could differ from the restored local volume. This change keeps the Compose image configurable and fixes the named local volumes to the existing `pitchmind_postgres_data` and `pitchmind_redis_data` names.

## Change

- Compose reads `POSTGRES_IMAGE`, defaulting to `postgres:17-alpine`; `.env.example` documents the setting, and a newly generated local `.env` records the default explicitly.
- Before starting services, `dev-up.sh` resolves the actual Compose PostgreSQL volume name and checks whether that exact volume exists.
- For an existing volume, it reads `PG_VERSION` using the selected local image with `--pull=never`, `--network none`, and a read-only volume mount. Unknown/unreadable versions and major-version mismatches stop before Compose startup.
- Networkless fake-Docker tests cover a matching PostgreSQL 16 volume, reject PostgreSQL 16 against PostgreSQL 17 data before startup, and fail closed when `PG_VERSION` cannot be read.

## Observed verification

- `python3 -m unittest scripts.test_dev_bootstrap -v`: 22 passed, 0 failed.
- `python3 -m unittest discover -s scripts -p 'test_*.py' -v`: 27 passed, 0 failed.
- `bash -n scripts/dev-up.sh`: passed.
- `docker compose --env-file docker/.env.example -f docker/compose.yml config --quiet`: passed (static configuration only).
- `node scripts/validate_operating_model.mjs` and `node scripts/validate_operating_model.mjs --self-test`: passed.
- `git diff --check`: passed.
- Read-only Compose resolution against the local ignored `.env` reported `postgres:16-alpine`, `pitchmind_postgres_data`, `pitchmind_redis_data`, and loopback-only ports `127.0.0.1:5432` and `127.0.0.1:6379`; the command output omitted credentials.

Docker API access was denied during `docker compose ps`, so this run does not claim current container health, startup, restart, or volume contents. No container startup, stop, volume mutation, or Azure action was performed as part of these checks.

## Status and remaining evidence

The tested implementation tree is `cd2fcdeac0c6a5183d52a1b1b9574f7c172ba84d` (local verification commit `9b780c5a662c04407bf3aa07b2b4e73efd635297`), now published on draft PR #44 as commit `ca5d80ed75065a92859d253143936abccc0d2c87`. The PR remains draft. Hosted CI, independent technical review, fresh-machine bootstrap, and Product Owner acceptance remain pending; EP01-S02 remains unaccepted.
