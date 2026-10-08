# M02 — Reproducible local developer workstation

**Environment:** LOCAL only. PostgreSQL 17 and Redis 7.4 run in Docker Compose; the offline ASP.NET API and Vite frontend run as local processes. No Azure account, Azure CLI login, or Azure credentials are required. DEV, STAGE, and PRODUCTION continue to use Azure services only.

## Prerequisites

- Docker Desktop with Compose v2, running
- .NET 10 SDK
- Node.js 22+ and npm
- Python 3 for optional API smoke tooling
- OpenSSL for first-run password generation

## First run and readiness

From the repository root:

```bash
./scripts/dev-up.sh
./scripts/dev-health.sh
dotnet run --project src/PitchMind.Api --urls http://127.0.0.1:5080
```

In another terminal, verify all three local processes/services:

```bash
./scripts/dev-health.sh http://127.0.0.1:5080
```

The optional API URL must use `http://127.0.0.1` or `http://localhost`; the script checks the API `/health` readiness endpoint. Run `./scripts/dev-api-health.sh` to check the API independently. Start the frontend separately with `cd frontend && npm ci && npm run dev` when needed.

## Stop, preserve, and reset

```bash
./scripts/dev-down.sh
```

This stops the Compose services and preserves the named data volumes. To delete PostgreSQL and Redis data, run `./scripts/dev-reset.sh` and type `RESET` at the prompt. Automation can explicitly bypass the prompt with `./scripts/dev-reset.sh --force`. Reset retains local credentials; `./scripts/dev-up.sh` recreates empty volumes.

## Security and environment boundary

- Compose publishes PostgreSQL and Redis only on `127.0.0.1`; containers have health checks and named persistent volumes.
- The first start generates separate OpenSSL random passwords in gitignored `docker/.env`, mode `0600`. Existing credential files are also restricted to the current user. Never commit or share this file, and never reuse these passwords in Azure.
- The API command above binds to loopback. Do not bind local development endpoints to `0.0.0.0`.
- LOCAL containers and adapters must not be used by deployed Azure environments. Local success is not evidence of Azure integration, deployment readiness, or story acceptance.

## Current limitations and evidence

- The API and workers do not yet use PostgreSQL or Redis as canonical persistence/cache; these containers provide local infrastructure only.
- The offline API remains a fixture-backed demonstration. This workstation workflow does not certify football source rights, coordinate units, or feed realism.
- A Docker daemon is required to execute the Compose start, health, and teardown flow. Static Compose validation does not substitute for a successful fresh-machine Docker smoke.
- Run `./scripts/validate-all.sh` for repository checks. Run `./scripts/test-postgres.sh` only after local PostgreSQL is healthy; it creates and drops an isolated test database.
