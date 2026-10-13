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
Runtime__Profile=local DOTNET_ENVIRONMENT=Development ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/PitchMind.Api --urls http://127.0.0.1:5080
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
- The local API requires `Development` and `Runtime:Profile=local`. Azure-hosted profiles use `Runtime:Profile=azure`; local profile selection is rejected outside Development.
- The API command above binds to loopback. Do not bind local development endpoints to `0.0.0.0`.
- LOCAL containers and adapters must not be used by deployed Azure environments. Local success is not evidence of Azure integration, deployment readiness, or story acceptance.

## CI quality gates
Azure DevOps validates .NET restore/build/tests, React `npm ci`/build/test/lint, Docker Compose health, repository baseline, and Bicep compile. .NET TRX and frontend JUnit results are published to the pipeline run. No GitHub Actions. Configure GitHub branch protection with Azure Pipelines as a required check. GitHub-hosted PR validation may require explicit Azure DevOps GitHub integration configuration.

## CI limitations
- The committed frontend package lockfile is required by CI; a missing lockfile fails validation instead of falling back to a non-reproducible install.
- Existing Bicep baseline was not validated during M01; compilation may reveal errors to resolve before M02 acceptance.
- No observed Azure DevOps PR run, deliberate failure-gate run, or required-check policy is recorded by this story's local evidence.

## Current limitations and evidence

- The API and workers do not yet use PostgreSQL or Redis as canonical persistence/cache; these containers provide local infrastructure only.
- Ordinary match reads return `503 MATCH_DATA_UNAVAILABLE` (the analyst endpoint may return `429` when rate-limited before its handler). Enabling match reads requires replacing API route composition with a canonical, revisioned PostgreSQL-backed read port. Local startup and health do not certify feed clock, coordinate units/orientation, player metrics, event mappings or football realism.
- A Docker daemon is required to execute the Compose start, health, and teardown flow. Static Compose validation does not substitute for a successful fresh-machine Docker smoke.
- Run `./scripts/validate-all.sh` for repository checks. Run `./scripts/test-postgres.sh` only after local PostgreSQL is healthy; it creates and drops an isolated test database.
