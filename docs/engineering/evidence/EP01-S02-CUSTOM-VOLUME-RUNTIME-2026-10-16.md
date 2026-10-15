# EP01-S02 custom Compose volume runtime verification — 2026-10-16

## Source and environment

- Implementation source: current `main` commit `c8494b918d08167ab721b8229f4ce7db9abd06c8`, checked out detached in a clean tracked-source worktree.
- Docker context: `desktop-linux`.
- Existing `pitchmind-postgres-1` and `pitchmind-redis-1` were healthy before and after the isolated run. Their named data volumes `pitchmind_postgres_data` and `pitchmind_redis_data` were not mounted by the test.
- PostgreSQL 17 Alpine and Redis 7.4 Alpine images were already present locally; no image pull was needed.

## Executed checks

- `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_dev_bootstrap -v`: **34 passed, 0 failed**.
- `bash -n scripts/dev-up.sh scripts/dev-health.sh scripts/dev-down.sh scripts/dev-reset.sh scripts/lib/local-docker-target.sh`: passed.
- `node scripts/validate_operating_model.mjs --self-test`: passed.
- Started `dev-up.sh` with unique project `pm_ep01_s02_override_20261016`, PostgreSQL volume `pm_ep01_s02_override_20261016_pg`, Redis volume `pm_ep01_s02_override_20261016_redis`, and loopback ports 35432/36379. Both services reached healthy status.
- Resolved Compose JSON and Docker inspection confirmed that PostgreSQL mounted the selected custom volume at `/var/lib/postgresql/data`, Redis mounted the selected custom volume at `/data`, and host bindings were exactly `127.0.0.1:35432` and `127.0.0.1:36379`.
- `dev-down.sh` removed the isolated containers/network while preserving both custom volumes. A second `dev-up.sh` and `dev-health.sh` passed against those volumes.
- `dev-reset.sh --force` removed only the isolated containers/network and the two custom test volumes. A post-cleanup check confirmed both retained PitchMind data volumes still existed and both original services remained healthy.
- `git diff --check`: passed.

## Limits and status

This proves same-host LOCAL runtime behavior for the custom project/volume overrides on the recorded source commit. It does not prove fresh-machine image pulls or setup, PostgreSQL database contents, or hosted CI. Independent human review and Product Owner acceptance remain pending. EP01-S02 remains unaccepted. No Azure operation occurred.
