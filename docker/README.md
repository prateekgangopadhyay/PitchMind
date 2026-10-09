# Local infrastructure (macOS ARM64 supported)

PostgreSQL 17 and Redis 7.4 run in Docker Desktop. The ports bind only to 127.0.0.1. Named volumes persist data. **These are development-only containers**; Azure environments use managed Azure services.

From the repository root:

```bash
./scripts/dev-up.sh
./scripts/dev-health.sh
```

In a second terminal, run the local API:

```bash
DOTNET_ENVIRONMENT=Local ASPNETCORE_ENVIRONMENT=Local dotnet run --project src/PitchMind.Api --urls http://127.0.0.1:5080
```

Back at the repository root, check readiness and later stop the services:

```bash
./scripts/dev-health.sh http://127.0.0.1:5080
./scripts/dev-down.sh
```

The first run creates `docker/.env` with independent random passwords and restricts it to the current user. It is gitignored. Never share its contents. The API is a separate local process, bound to loopback by the command above; `/health` is the readiness endpoint. `./scripts/dev-api-health.sh` checks API readiness independently, and the optional URL to `dev-health.sh` checks the API after PostgreSQL and Redis.

To stop the services and preserve data, run `./scripts/dev-down.sh`. To delete both data volumes, run `./scripts/dev-reset.sh` and type `RESET` at its prompt. For automation, `./scripts/dev-reset.sh --force` explicitly skips the prompt. Reset retains `docker/.env`; restarting reuses the local credentials.

The offline synthetic API checks that the resolved .NET environment name is `Local`; it fails startup for `Development`, `Staging`, and `Production` until a canonical read path is wired. This guard is not deployment identity validation: cloud configuration must never set either environment variable to `Local`. Its in-memory event store does not use the PostgreSQL/Redis containers yet. Azure credentials are not required to run the local API.

Troubleshooting: start Docker Desktop, run `docker info`, then `docker compose --env-file docker/.env -f docker/compose.yml ps`. If ports 5432 or 6379 are occupied, edit docker/.env to select free host ports.
