# EP01-S02 clean-source local bootstrap evidence — 2026-10-14

**Source revision:** `1f97706ec22c9f33fe4ebcfcc1595ddc41bf71af` (`main`).
**Environment:** LOCAL on the existing Docker Desktop host. This is same-host evidence, not clean-machine acceptance.

## Method

Archived the tracked source at the exact revision into a temporary directory, without `.git`, ignored build output, or an existing `docker/.env`. In that disposable copy only, changed the Compose project name and both explicit PostgreSQL/Redis volume names so the test could not select the user's existing `pitchmind` resources. The checked-in bootstrap scripts were unmodified. The repeatable run used Compose project `pitchmind-ep01-s02-audit-20261014`, PostgreSQL volume `pm_ep01_s02_audit_pg_20261014`, and Redis volume `pm_ep01_s02_audit_redis_20261014`.

Used already-local images `postgres:16-alpine` and `redis:7.4-alpine`, with environment overrides `POSTGRES_IMAGE=postgres:16-alpine`, `POSTGRES_PORT=35432`, and `REDIS_PORT=36379`.

The executed command sequence in that archived copy was:

```sh
POSTGRES_IMAGE=postgres:16-alpine POSTGRES_PORT=35432 REDIS_PORT=36379 ./scripts/dev-up.sh
./scripts/dev-health.sh
docker compose --env-file docker/.env -f docker/compose.yml ps --format 'table {{.Name}}\t{{.Image}}\t{{.Service}}\t{{.Status}}\t{{.Ports}}'
docker volume inspect pm_ep01_s02_audit_pg_20261014 pm_ep01_s02_audit_redis_20261014 --format '{{.Name}}'
./scripts/dev-down.sh
docker volume inspect pm_ep01_s02_audit_pg_20261014 pm_ep01_s02_audit_redis_20261014 --format '{{.Name}}'
POSTGRES_IMAGE=postgres:16-alpine POSTGRES_PORT=35432 REDIS_PORT=36379 ./scripts/dev-up.sh
./scripts/dev-health.sh
./scripts/dev-reset.sh --force
```

After reset, `docker volume inspect` returned those unique test volumes as absent; inspection of `pitchmind_postgres_data` and `pitchmind_redis_data` returned both original names. `docker ps` showed the original `pitchmind-postgres-1` and `pitchmind-redis-1` containers healthy on `127.0.0.1:5432` and `127.0.0.1:6379`. No generated credential value was recorded.

## Observed results

- First `./scripts/dev-up.sh` generated the ignored local credentials file; filesystem mode was `0600`.
- `./scripts/dev-health.sh` passed for PostgreSQL and Redis.
- `docker compose ps` showed PostgreSQL 16 and Redis 7.4 healthy, published only on `127.0.0.1:35432` and `127.0.0.1:36379`.
- `./scripts/dev-down.sh` stopped the test containers and preserved both uniquely named test volumes.
- A second `dev-up.sh` and `dev-health.sh` passed against the preserved test volumes.
- `./scripts/dev-reset.sh --force` removed only the uniquely named test containers, network, and volumes. Follow-up inspection confirmed both original `pitchmind_postgres_data` / `pitchmind_redis_data` volumes and original PostgreSQL/Redis containers remained present and healthy on loopback.
- The temporary credentials file and source archive were removed after the check.

An independent read-only reviewer inspected the exact source revision and this isolation method. It found no P0–P2 bootstrap implementation issue and confirmed the project-plus-two-volume-name isolation is sound. The reviewer did not execute Docker commands; the runtime results above are from the coordinator's local run.

## Limits

This verifies the current scripts against a clean tracked-source copy on the existing host, using cached images and an isolated Compose configuration. It does not prove first-time image acquisition, a truly fresh machine, API startup, PostgreSQL application integration, full `validate-all.sh`, hosted Azure DevOps CI, human approval, or Product Owner acceptance. No Azure resource or deployment was accessed. EP01-S02 remains unaccepted.
