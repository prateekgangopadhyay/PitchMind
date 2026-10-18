# Local infrastructure (macOS ARM64 supported)

PostgreSQL 17, Redis 7.4 and Kafka 4.3 run in Docker Desktop. Their host ports bind only to 127.0.0.1. Compose uses the stable `pitchmind` project name and explicitly named `pitchmind_postgres_data`, `pitchmind_redis_data` and `pitchmind_kafka_data` volumes by default, so normal local starts reuse the existing PitchMind data. Set `POSTGRES_DATA_VOLUME`, `REDIS_DATA_VOLUME` and `KAFKA_DATA_VOLUME` to choose distinct volumes. **These are development-only containers**; Azure environments use managed Azure services.

The explicit volume names are shared across Compose project names. For an isolated runtime check, export unique project and volume names before starting services, and keep them set for health, stop, and reset commands. Changing only the project name still uses the PitchMind data volumes.

```bash
export COMPOSE_PROJECT_NAME=pitchmind-isolated
export POSTGRES_DATA_VOLUME=pitchmind_isolated_postgres_data
export REDIS_DATA_VOLUME=pitchmind_isolated_redis_data
export KAFKA_DATA_VOLUME=pitchmind_isolated_kafka_data
./scripts/dev-up.sh
./scripts/dev-migrate.py
./scripts/dev-health.sh
./scripts/dev-down.sh
```

The PostgreSQL version guard resolves the effective Compose configuration before startup and probes the exact configured data volume. When changing volume names, preserve the same environment variables for `./scripts/dev-reset.sh --force` so reset targets only the isolated stack.

From the repository root:

```bash
./scripts/dev-up.sh
./scripts/dev-migrate.py
./scripts/dev-health.sh
```

In a second terminal, run the local API:

```bash
set -a
source docker/.env
set +a
Runtime__Profile=local Realtime__Provider=local \
  ConnectionStrings__PitchMind="Host=127.0.0.1;Port=${POSTGRES_PORT:-5432};Database=${POSTGRES_DB:-pitchmind};Username=pitchmind_api;Password=${PITCHMIND_API_PASSWORD}" \
  DOTNET_ENVIRONMENT=Development ASPNETCORE_ENVIRONMENT=Development \
  dotnet run --project src/PitchMind.Api --urls http://127.0.0.1:5080
```

Back at the repository root, check readiness and later stop the services:

```bash
./scripts/dev-health.sh http://127.0.0.1:5080
./scripts/dev-down.sh
```

The first run creates gitignored `docker/.env` with independent random passwords for PostgreSQL, the restricted API role, and Redis, then restricts it to the current user. Never share its contents. On each start, the bootstrap requires exactly one distinct 48-character lowercase hexadecimal password for each role, with at least 12 unique hex digits. This rejects the committed example placeholders and low-diversity or ambiguous values. If validation fails, replace the values locally with strong random values and rerun the command. The values are never printed by the bootstrap. The API is a separate local process, bound to loopback by the command above; `/health` is the readiness endpoint. `./scripts/dev-api-health.sh` checks API readiness independently, and the optional URL to `dev-health.sh` checks the API after PostgreSQL and Redis.

## LOCAL MANU vs Liverpool candidate stream

The candidate path is deliberately isolated from canonical match routes. The local Simulation Service reads `data/simulations/manu-liv-match-dataset.json`, preserves each raw event envelope and publishes it to Kafka. `PitchMind.EventWorker` consumes Kafka and durably inserts each envelope into a PostgreSQL outbox before committing its Kafka offset. The API outbox dispatcher then writes the candidate event history in PostgreSQL and updates the latest cursor in Redis concurrently; once the durable write and cache attempt complete, it publishes a cursor-only SignalR notification and acknowledges the outbox item. Redis errors are handled by the existing best-effort cache adapter; PostgreSQL remains the durable source. The UI backfills ordered raw events from PostgreSQL after each cursor notification (or bounded polling fallback), renders the source's two-team player positions, and exposes the raw player fields.

After `dev-up` and `dev-migrate`, run the API and EventWorker in separate terminals. The launch scripts read the gitignored `docker/.env` without printing its credentials, apply the selected local Docker ports, and enable the candidate-feed adapters:

```bash
./scripts/start-local-api.sh
```

```bash
./scripts/start-local-eventworker.sh
```

Then start the frontend with `npm --prefix frontend run dev -- --host 127.0.0.1` and open `http://127.0.0.1:5173/home`; use the Match tab to open the candidate MANU–Liverpool stream. Entering the signed-in app starts the local candidate simulation once; the blinking header Live control pauses or resumes it. `Identity:DevelopmentHeaderAuthEnabled` is a LOCAL-only developer adapter. For browser SignalR on localhost, the UI uses long polling with the same local identity headers; normal Azure identity must provide its bearer token.

The page labels the source as noncanonical. Its coordinate units/axes/orientation, clock/period semantics, player metric definitions and football realism remain unverified. The visual proportionally maps raw source coordinates to the pitch canvas for preview only; it does not convert them to pitch metres or make performance claims. The Azure profile has code adapters for Event Hubs, PostgreSQL, Redis and Azure SignalR, but DEV still requires the EventWorker to be deployed with Azure identity and matching settings, plus provisioned resources and schema. No Azure deployment has been performed.

`./scripts/dev-migrate.py` is the explicit LOCAL schema migration step. It uses the PostgreSQL owner from the private Compose environment, refuses non-loopback port mappings, applies versioned migrations, then creates or refreshes `pitchmind_api` with SELECT on canonical read tables and only the column-level UPDATE privileges needed by the local outbox dispatcher. The runtime API command uses that restricted role. It cannot create or alter schema objects, insert canonical data, or change migration history.

To stop the services and preserve data, run `./scripts/dev-down.sh`. To delete both data volumes, run `./scripts/dev-reset.sh` and type `RESET` at its prompt. For automation, `./scripts/dev-reset.sh --force` explicitly skips the prompt. Reset retains `docker/.env`; restarting reuses the local credentials.

The local API requires the resolved .NET environment name to be `Development` and `Runtime:Profile=local`. Local realtime and outbox dispatch additionally require `Realtime:Provider=local` and the loopback PostgreSQL connection shown above. The command loads credentials from the gitignored `docker/.env`; keep that file private. EP06-S04 adds Entra authentication for the Azure profile, which requires `Production` plus `Runtime:Profile=azure` and Entra authority/audience. These local guards are not deployment identity validation. The API currently has no canonical match read adapter: its match routes return typed `503 MATCH_DATA_UNAVAILABLE` until validated source data and canonical PostgreSQL read composition are available. Starting PostgreSQL and Redis here does not configure those adapters. Azure credentials are not required to run the local API.

Troubleshooting: start Docker Desktop, run `docker info`, then `docker compose --env-file docker/.env -f docker/compose.yml ps`. If ports 5432 or 6379 are occupied, edit docker/.env to select free host ports.
