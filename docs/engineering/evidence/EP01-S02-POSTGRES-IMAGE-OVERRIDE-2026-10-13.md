# EP01-S02 PostgreSQL image override evidence

**Code revision:** `5508915f6e64c3f254287366c2543a18263efe81`
**Scope:** local PostgreSQL image selection for existing Compose volumes; no Azure resources or deployment.

## Verification performed

- `python3 -m unittest discover -s scripts -p 'test_*.py' -v`: 30 passed, 0 failed.
- `bash -n scripts/dev-up.sh scripts/dev-down.sh scripts/dev-reset.sh scripts/dev-health.sh`: passed.
- `docker compose --env-file docker/.env.example -f docker/compose.yml config --quiet`: passed.
- `node scripts/validate_operating_model.mjs --self-test`: passed.
- `git diff --check`: passed.
- End-to-end local override: a disposable Compose project used a fresh, project-scoped volume, `POSTGRES_IMAGE=postgres:17-alpine` in its private `docker/.env`, and a conflicting shell `POSTGRES_IMAGE=postgres:16-alpine`. `dev-up.sh` started PostgreSQL 17 and Redis 7.4; `dev-health.sh` reported both healthy at loopback ports 35433 and 36380. Container inspection confirmed `postgres:17-alpine`. The disposable project, network, volumes, and env file were removed afterward. The shared `pitchmind` stack remained healthy on loopback ports 5432 and 6379.

## Independent review and limits

An independent read-only diff review found no correctness or security blocker in the setting wiring. It confirmed the default is persisted in the generated env file, Compose selection is tested, and inherited shell `POSTGRES_IMAGE` is cleared before Compose runs. The review found and reported a stale “pinned” description, corrected in this revision.

The override is a manual compatibility path, not automatic major-version detection or migration. Developers must inspect the existing volume's major version and set `POSTGRES_IMAGE` in `docker/.env` before startup. Existing env files without the setting resolve to the PostgreSQL 16 default. Starting an incompatible major may leave the database unavailable until the matching image is restored; this code does not migrate or remove data. The verification used disposable fresh volumes and does not prove compatibility or integrity of another machine's volume.

This is same-machine LOCAL evidence. Hosted CI, current-head GitHub review, clean-machine bootstrap, and Product Owner acceptance remain pending. EP01-S02 remains unaccepted.
