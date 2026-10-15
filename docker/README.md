# Local infrastructure (macOS ARM64 supported)

PostgreSQL 17 and Redis 7.4 run in Docker Desktop. The ports bind only to 127.0.0.1. Compose uses the stable `pitchmind` project name and explicitly named `pitchmind_postgres_data` / `pitchmind_redis_data` volumes by default, so normal local starts reuse the existing PitchMind data. Set `POSTGRES_DATA_VOLUME` and `REDIS_DATA_VOLUME` to choose distinct volumes. **These are development-only containers**; Azure environments use managed Azure services.

The explicit volume names are shared across Compose project names. For an isolated runtime check, export unique project and volume names before starting services, and keep them set for health, stop, and reset commands. Changing only the project name still uses the PitchMind data volumes.

```bash
export COMPOSE_PROJECT_NAME=pitchmind-isolated
export POSTGRES_DATA_VOLUME=pitchmind_isolated_postgres_data
export REDIS_DATA_VOLUME=pitchmind_isolated_redis_data
./scripts/dev-up.sh
./scripts/dev-health.sh
./scripts/dev-down.sh
```

The PostgreSQL version guard resolves the effective Compose configuration before startup and probes the exact configured data volume. When changing volume names, preserve the same environment variables for `./scripts/dev-reset.sh --force` so reset targets only the isolated stack.

From the repository root:

```bash
./scripts/dev-up.sh
./scripts/dev-health.sh
```

In a second terminal, run the local API:

```bash
set -a
source docker/.env
set +a
Runtime__Profile=local Realtime__Provider=local \
  ConnectionStrings__PitchMind="Host=127.0.0.1;Port=${POSTGRES_PORT:-5432};Database=${POSTGRES_DB:-pitchmind};Username=${POSTGRES_USER:-pitchmind};Password=${POSTGRES_PASSWORD}" \
  DOTNET_ENVIRONMENT=Development ASPNETCORE_ENVIRONMENT=Development \
  dotnet run --project src/PitchMind.Api --urls http://127.0.0.1:5080
```

Back at the repository root, check readiness and later stop the services:

```bash
./scripts/dev-health.sh http://127.0.0.1:5080
./scripts/dev-down.sh
```

The first run creates `docker/.env` with independent random passwords and restricts it to the current user. It is gitignored. Never share its contents. On each start, the bootstrap requires exactly one distinct 48-character lowercase hexadecimal password for PostgreSQL and Redis, each containing at least 12 unique hex digits. This rejects the committed example placeholders and low-diversity or ambiguous values. If validation fails, replace both values locally with strong random values and rerun the command. The values are never printed by the bootstrap. The API is a separate local process, bound to loopback by the command above; `/health` is the readiness endpoint. `./scripts/dev-api-health.sh` checks API readiness independently, and the optional URL to `dev-health.sh` checks the API after PostgreSQL and Redis.

To stop the services and preserve data, run `./scripts/dev-down.sh`. To delete both data volumes, run `./scripts/dev-reset.sh` and type `RESET` at its prompt. For automation, `./scripts/dev-reset.sh --force` explicitly skips the prompt. Reset retains `docker/.env`; restarting reuses the local credentials.

The local API requires the resolved .NET environment name to be `Development` and `Runtime:Profile=local`. Local realtime and outbox dispatch additionally require `Realtime:Provider=local` and the loopback PostgreSQL connection shown above. The command loads credentials from the gitignored `docker/.env`; keep that file private. EP06-S04 adds Entra authentication for the Azure profile, which requires `Production` plus `Runtime:Profile=azure` and Entra authority/audience. These local guards are not deployment identity validation. The API currently has no canonical match read adapter: its match routes return typed `503 MATCH_DATA_UNAVAILABLE` until validated source data and canonical PostgreSQL read composition are available. Starting PostgreSQL and Redis here does not configure those adapters. Azure credentials are not required to run the local API.

Troubleshooting: start Docker Desktop, run `docker info`, then `docker compose --env-file docker/.env -f docker/compose.yml ps`. If ports 5432 or 6379 are occupied, edit docker/.env to select free host ports.
