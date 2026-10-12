# Local infrastructure (macOS ARM64 supported)

PostgreSQL 16 and Redis 7.4 run in Docker Desktop under Compose project `pitchmind`. The ports bind only to 127.0.0.1. The default project uses the existing `pitchmind_postgres_data` and `pitchmind_redis_data` volumes. When `COMPOSE_PROJECT_NAME` is overridden for an isolated test stack, volume names follow that project so test containers cannot attach to the default project's data. The PostgreSQL service defaults to `postgres:16-alpine`, matching the reported existing local database major version; set `POSTGRES_IMAGE` in `docker/.env` when an existing volume requires another major version. `dev-down.sh` preserves the volumes; `dev-reset.sh` removes them only after explicit confirmation. Compose configuration tests check the image and volume names offline. This config does not migrate data or verify runtime compatibility. All checkouts on this machine using project `pitchmind` share the same containers; `dev-down.sh` from any such checkout stops the shared stack. Existing-service readiness was checked without starting or stopping containers. **These are development-only containers**; Azure environments use managed Azure services.

PostgreSQL data directories are major-version-specific. The default project keeps the existing volume names while using PostgreSQL 16, matching the restored local volume verified on this machine. On first bootstrap, `POSTGRES_IMAGE=postgres:16-alpine` is written to `docker/.env`; Compose uses that local setting and ignores an inherited shell override. If an existing volume was initialized by another PostgreSQL major version, do not reset or remove it and do not start it with an incompatible image. Preserve the volume and change `POSTGRES_IMAGE` in `docker/.env` to the matching image tag, or perform a separately planned and verified database migration before changing the image major. This repository does not automate that migration. The resolved-Compose tests verify both the default PostgreSQL 16 image and an explicit PostgreSQL 17 selection.

From the repository root:

```bash
./scripts/dev-up.sh
./scripts/dev-health.sh
```

For an isolated test stack, override the project name and use free loopback ports. Compose derives separate volume names from that project, so these commands do not attach to the default `pitchmind` data:

```bash
PROJECT=pitchmind-validation
COMPOSE_PROJECT_NAME="$PROJECT" POSTGRES_PORT=25432 REDIS_PORT=26379 ./scripts/dev-up.sh
COMPOSE_PROJECT_NAME="$PROJECT" POSTGRES_PORT=25432 REDIS_PORT=26379 ./scripts/dev-health.sh
COMPOSE_PROJECT_NAME="$PROJECT" ./scripts/dev-down.sh
```

The isolated test volumes remain after `dev-down.sh`; remove them only when they contain disposable test data, using `COMPOSE_PROJECT_NAME="$PROJECT" ./scripts/dev-reset.sh --force`.

In a second terminal, run the local API:

```bash
Runtime__Profile=local DOTNET_ENVIRONMENT=Development ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/PitchMind.Api --urls http://127.0.0.1:5080
```

Back at the repository root, check readiness and later stop the services:

```bash
./scripts/dev-health.sh http://127.0.0.1:5080
./scripts/dev-down.sh
```

The first run creates `docker/.env` with independent random passwords and restricts it to the current user. It is gitignored. Never share its contents. On each start, the bootstrap requires exactly one distinct 48-character lowercase hexadecimal password for PostgreSQL and Redis, each containing at least 12 unique hex digits. This rejects the committed example placeholders and low-diversity or ambiguous values. If validation fails, replace both values locally with strong random values and rerun the command. The values are never printed by the bootstrap. The API is a separate local process, bound to loopback by the command above; `/health` is the readiness endpoint. `./scripts/dev-api-health.sh` checks API readiness independently, and the optional URL to `dev-health.sh` checks the API after PostgreSQL and Redis.

To stop the services and preserve data, run `./scripts/dev-down.sh`. To delete both data volumes, run `./scripts/dev-reset.sh` and type `RESET` at its prompt. For automation, `./scripts/dev-reset.sh --force` explicitly skips the prompt. Reset retains `docker/.env`; restarting reuses the local credentials.

The local API requires the resolved .NET environment name to be `Development` and `Runtime:Profile=local`. EP06-S04 adds Entra authentication for the Azure profile, which requires `Production` plus `Runtime:Profile=azure` and Entra authority/audience. These local guards are not deployment identity validation. The API currently has no canonical match read adapter: its match routes return typed `503 MATCH_DATA_UNAVAILABLE` until validated source data and canonical PostgreSQL read composition are available. Starting PostgreSQL and Redis here does not configure those adapters. Azure credentials are not required to run the local API.

Troubleshooting: start Docker Desktop, run `docker info`, then `docker compose --env-file docker/.env -f docker/compose.yml ps`. If ports 5432 or 6379 are occupied, edit docker/.env to select free host ports.
