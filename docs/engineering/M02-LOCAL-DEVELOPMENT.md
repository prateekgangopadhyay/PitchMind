# M02 — Reproducible local developer workstation

**Environment:** LOCAL only. PostgreSQL 17 (the default) and Redis 7.4 run in Docker Compose; the offline ASP.NET API and Vite frontend run as local processes. No Azure account, Azure CLI login, or Azure credentials are required. DEV, STAGE, and PRODUCTION continue to use Azure services only.

## Prerequisites

- Docker Desktop with Compose v2, running
- .NET 10 SDK
- Node.js 22+ and npm
- Python 3 for optional API smoke tooling
- OpenSSL for first-run password generation

## First run and readiness

From the repository root, start Docker dependencies and apply the local schema:

```bash
./scripts/dev-up.sh
./scripts/dev-migrate.py
```

Then run each long-lived process in its own terminal from the repository root:

```bash
# Terminal 1
./scripts/start-local-api.sh
```

```bash
# Terminal 2
./scripts/start-local-eventworker.sh
```

```bash
# Terminal 3
npm --prefix frontend run dev -- --host 127.0.0.1
```

In a fourth terminal, check the API and local dependency readiness:

```bash
./scripts/dev-health.sh http://127.0.0.1:5080
```

The API URL must use `http://127.0.0.1` or `http://localhost`; the script checks API readiness. Open the Vite URL printed by the frontend command. Detailed LOCAL candidate-feed setup, authentication behavior and teardown are in [`docker/README.md`](../../docker/README.md).

The default database image is `postgres:17-alpine`. If an existing local data volume was created by another supported PostgreSQL major version, select the matching image explicitly, for example `POSTGRES_IMAGE=postgres:16-alpine ./scripts/dev-up.sh`. The selector accepts only `postgres:<major>-alpine` tags and can also be set in the private `docker/.env` file. For an existing volume, make sure the selected image is already present locally: the startup guard inspects `PG_VERSION` with `--pull=never`, then Compose starts only if the major versions match. A mismatch stops startup without changing the volume. Back up and perform a PostgreSQL major-version migration before changing an existing volume's version.

## Stop, preserve, and reset

```bash
./scripts/dev-down.sh
```

This stops the Compose services and preserves the named data volumes. To delete PostgreSQL and Redis data, run `./scripts/dev-reset.sh` and type `RESET` at the prompt. Automation can explicitly bypass the prompt with `./scripts/dev-reset.sh --force`. Reset retains local credentials; `./scripts/dev-up.sh` recreates empty volumes.

## Security and environment boundary

- Compose publishes PostgreSQL and Redis only on `127.0.0.1`; containers have health checks and named persistent volumes.
- The first start generates separate OpenSSL random passwords in gitignored `docker/.env`, mode `0600`. Existing credential files are also restricted to the current user. Never commit or share this file, and never reuse these passwords in Azure.
- `POSTGRES_IMAGE` is local infrastructure configuration, not a secret. Keep it to the supported versioned Alpine tags; the default remains PostgreSQL 17.
- The local API requires `Development` and `Runtime:Profile=local`. Azure-hosted profiles use `Runtime:Profile=azure`; local profile selection is rejected outside Development.
- The API command above binds to loopback. Do not bind local development endpoints to `0.0.0.0`.
- LOCAL containers and adapters must not be used by deployed Azure environments. Local success is not evidence of Azure integration, deployment readiness, or story acceptance.

## CI quality gates
Azure DevOps validates .NET restore/build/tests, React `npm ci`/build/test/lint, Docker Compose health, repository baseline, and Bicep compile. .NET TRX and frontend JUnit results are published to the pipeline run. No GitHub Actions. Configure GitHub branch protection with Azure Pipelines as a required check. GitHub-hosted PR validation may require explicit Azure DevOps GitHub integration configuration.

## CI limitations
- The committed frontend package lockfile is required by CI; a missing lockfile fails validation instead of falling back to a non-reproducible install.
- Existing Bicep baseline was not validated during M01; compilation may reveal errors to resolve before M02 acceptance.
- No observed Azure DevOps PR run, deliberate failure-gate run, or required-check policy is recorded by this story's local evidence.

## Current implementation boundaries and evidence

- The LOCAL candidate simulation path uses Kafka for source envelopes, PostgreSQL for its durable outbox/history, Redis for the rebuildable latest cursor, and SignalR for cursor-only notifications. Candidate records do not enter canonical football tables.
- The canonical BFF has PostgreSQL-backed readers for persisted canonical projections, events, insights, evidence and tracking frames when its runtime profile, connection, schema and data are configured. Legacy `/api/v1/matches` routes deliberately return `503 MATCH_DATA_UNAVAILABLE`; this does not mean canonical BFF readers are absent. The synthetic Manu–Liverpool candidate feed is not admitted as canonical data.
- Candidate simulation and candidate analyst routes are separate from canonical football analysis. The local API can call an allowlisted Foundry project agent for a bounded source-evidence hypothesis when configured; the separate AgentWorker explanation orchestrator is library/scaffold code and is not a live canonical explanation consumer. Local startup does not verify Azure identities, services or connectivity.
- Coordinate units/orientation, clock and period semantics, player metric meanings, event mappings and football realism for the candidate source remain unverified. Candidate positions are previewed without asserting canonical pitch measurements.
- A Docker daemon is required to execute the Compose start, health, and teardown flow. Static Compose validation does not substitute for a successful fresh-machine Docker smoke.
- Run `./scripts/validate-all.sh` for repository checks. Run `./scripts/test-postgres.sh` only after local PostgreSQL is healthy; it creates and drops an isolated test database. Neither command proves Azure runtime integration or story acceptance.
