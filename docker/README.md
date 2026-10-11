# Local infrastructure (macOS ARM64 supported)

PostgreSQL 17 and Redis 7.4 run in Docker Desktop. The ports bind only to 127.0.0.1. Named volumes persist data. **These are development-only containers**; Azure environments use managed Azure services.

From the repository root:

```bash
./scripts/dev-up.sh
./scripts/dev-health.sh
```

In a second terminal, run the local API:

```bash
dotnet run --project src/PitchMind.Api --urls http://127.0.0.1:5080
```

Back at the repository root, check readiness and later stop the services:

```bash
./scripts/dev-health.sh http://127.0.0.1:5080
./scripts/dev-down.sh
```

The first run creates `docker/.env` with independent random passwords and restricts it to the current user. It is gitignored. Never share its contents. The API is a separate local process, bound to loopback by the command above; `/health` is the readiness endpoint. `./scripts/dev-api-health.sh` checks API readiness independently, and the optional URL to `dev-health.sh` checks the API after PostgreSQL and Redis.

To stop the services and preserve data, run `./scripts/dev-down.sh`. To delete both data volumes, run `./scripts/dev-reset.sh` and type `RESET` at its prompt. For automation, `./scripts/dev-reset.sh --force` explicitly skips the prompt. Reset retains `docker/.env`; restarting reuses the local credentials.

The local EventWorker can poll the PostgreSQL outbox and persist deliveries to a separate `pitchmind_local` sink table. This is a durable local handoff used to exercise retries and idempotency; it is not Event Hubs/Service Bus and is not registered for Azure profiles. `./scripts/dev-up.sh` idempotently creates that LOCAL-only table and a least-privilege `pitchmind_worker` role, including on an existing Docker volume. A fresh volume does not contain the canonical `pitchmind.outbox` schema until the explicit migration command runs.

For a fresh local database, run `./scripts/dev-migrate.py` after `./scripts/dev-up.sh` and before starting the worker. This explicit command uses the local database owner/migration identity to apply versioned schema migrations; it is separate from the worker process and its runtime identity. To start the local dispatcher, use `python3 scripts/dev-event-worker.py`. It reads the resolved loopback PostgreSQL endpoint and the restricted `pitchmind_worker` credentials from the gitignored Compose environment in memory and starts the worker with `Runtime:Profile=local` and `Outbox:Provider=postgres-local`. EventWorker never applies migrations and fails closed for other profiles/providers or a missing connection string. The API still uses its existing scaffold/demo read adapters; this local worker does not import the candidate football JSON or change public API behavior. Azure credentials are not required for local services.

Troubleshooting: start Docker Desktop, run `docker info`, then `docker compose --env-file docker/.env -f docker/compose.yml ps`. If ports 5432 or 6379 are occupied, edit docker/.env to select free host ports.
