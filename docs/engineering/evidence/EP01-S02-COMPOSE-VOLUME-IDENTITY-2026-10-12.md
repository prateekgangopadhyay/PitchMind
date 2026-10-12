# EP01-S02 Compose project and volume identity evidence — 2026-10-12

**Scope:** locally make Compose resolve to the already-restored `pitchmind` project and named data volumes. This evidence covers source/config consistency only; it does not assert runtime container reuse.

**Stack:** child branch `codex/ep01-s02-compose-volume-identity`, based on PR #44 published head `07de9e70103baba729af8bcba24731b93f20105d`. Code commit: `2c17240a544c57a787ff7a6c7b260d9c080a277d`.

## Change

- Compose project name is `pitchmind`.
- PostgreSQL and Redis data volumes are explicitly named `pitchmind_postgres_data` and `pitchmind_redis_data`.
- The resolved Compose configuration test asserts those identities and retains the existing loopback-only published-port assertions.
- The local infrastructure runbook explains that stop preserves the volumes and reset deletes them only after explicit confirmation.

## Checks run on the code commit

| Check | Result |
|---|---|
| `python3 -m unittest scripts.test_dev_bootstrap -v` | 19 passed, 0 failed. |
| `python3 -m unittest discover -s scripts -p 'test_*.py' -v` | 24 passed, 0 failed. |
| `bash -n scripts/dev-up.sh scripts/dev-health.sh scripts/dev-down.sh scripts/dev-reset.sh` | Passed. |
| `docker compose --env-file docker/.env.example -f docker/compose.yml config --quiet` | Passed; configuration only. |
| `node scripts/validate_operating_model.mjs --self-test` | Passed. |
| `git diff --check` | Passed. |

No containers or volumes were started, stopped, recreated, or removed. The test resolves Compose JSON and asserts the project and volume names, but runtime reuse, health, and credentials against the restored project were not tested on this head.

## Remaining

Fresh-machine acceptance, hosted CI, runtime reuse on this code head, independent current-head review, and Product Owner acceptance remain open. EP01-S02 remains unaccepted. No Azure action occurred.
