# Local infrastructure (macOS ARM64 supported)

PostgreSQL 16 and Redis 7.4 run in Docker Desktop under Compose project `pitchmind`. The ports bind only to 127.0.0.1. Compose explicitly reuses the `pitchmind_postgres_data` and `pitchmind_redis_data` named volumes so a project-name change cannot silently create empty replacement stores. The PostgreSQL service is pinned to `postgres:16-alpine`, matching the reported existing local database major version. `dev-down.sh` preserves the volumes; `dev-reset.sh` removes them only after explicit confirmation. Compose configuration tests check the image and volume names offline. This config does not migrate data or verify runtime compatibility. All checkouts on this machine using project `pitchmind` share the same containers; `dev-down.sh` from any such checkout stops the shared stack. Existing-service readiness was checked without starting or stopping containers. **These are development-only containers**; Azure environments use managed Azure services.From the repository root:

```bash
./scripts/dev-up.sh
./scripts/dev-health.sh
```

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
