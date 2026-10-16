# Local infrastructure (macOS ARM64 supported)

PostgreSQL 17 and Redis 7.4 run in Docker Desktop. The ports bind only to 127.0.0.1. Named volumes persist data. **These are development-only containers**; Azure environments use managed Azure services.

From the repository root:

```bash
./scripts/dev-up.sh
./scripts/dev-health.sh
```

In a second terminal, run the local API:

```bash
ASPNETCORE_ENVIRONMENT=Development Runtime__Profile=local dotnet run --project src/PitchMind.Api --urls http://127.0.0.1:5080
```

In the local profile, fixture-backed demo GETs are anonymous and marked `X-PitchMind-Data-Class: synthetic-demo`. Canonical match routes remain protected; without a configured local identity or Entra authority they fail closed. For local authorization tests only, set `Identity__DevelopmentHeaderAuthEnabled=true` and send `X-Dev-Subject` plus `X-Dev-Roles: fan|analyst|admin`. This adapter is rejected outside `Development` with `Runtime__Profile=local` and is never a cloud identity mechanism.

Azure runtime configuration must set `Runtime__Profile=azure`, an HTTPS `Identity__Authority`, and `Identity__Audience`; the API fails startup when these are absent. Do not set the local runtime profile in Azure.

Back at the repository root, check readiness and later stop the services:

```bash
./scripts/dev-health.sh http://127.0.0.1:5080
./scripts/dev-down.sh
```

The first run creates `docker/.env` with independent random passwords and restricts it to the current user. It is gitignored. Never share its contents. The API is a separate local process, bound to loopback by the command above; `/health` is the readiness endpoint. `./scripts/dev-api-health.sh` checks API readiness independently, and the optional URL to `dev-health.sh` checks the API after PostgreSQL and Redis.

To stop the services and preserve data, run `./scripts/dev-down.sh`. To delete both data volumes, run `./scripts/dev-reset.sh` and type `RESET` at its prompt. For automation, `./scripts/dev-reset.sh --force` explicitly skips the prompt. Reset retains `docker/.env`; restarting reuses the local credentials.

The M01 API and workers still use scaffold adapters; these database/cache containers are not yet wired into canonical persistence or caching. Azure credentials are not required to run the local API.

Troubleshooting: start Docker Desktop, run `docker info`, then `docker compose --env-file docker/.env -f docker/compose.yml ps`. If ports 5432 or 6379 are occupied, edit docker/.env to select free host ports.
