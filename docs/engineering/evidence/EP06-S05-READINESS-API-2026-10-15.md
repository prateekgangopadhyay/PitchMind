# EP06-S05 positive PostgreSQL readiness API check — 2026-10-15

**Tested code commit:** `28dff98a7241277a73eda28903b9bbab778fcc0e` (parent `655e20fd47fe4b692c83cf4aa50a6c6b9d91d3c3`). The code tree was tested before this evidence-only documentation update.
**Scope:** An opt-in API integration test and local PostgreSQL harness extension. No production API behavior, route contract, schema, or Azure configuration changed.

## Change

- Added `Current_postgres_schema_is_ready_through_the_health_endpoint`, enabled only when `PITCHMIND_TEST_POSTGRES` is set. It applies the repository's current schema migration to the isolated database, starts the Development/local API using that same test connection, and verifies `GET /health/ready` returns 200 with an empty body.
- Extended `scripts/test-postgres.sh` to execute both the Infrastructure PostgreSQL suite and the Contracts/API suite against the same uniquely named isolated database. It requires a nonempty, zero-skip TRX result for each suite and drops the database in its existing cleanup block.
- Ordinary full-suite runs skip this one opt-in database test when the test connection is absent.

## Verification

| Check | Result |
|---|---|
| Focused readiness test without `PITCHMIND_TEST_POSTGRES` | 0 passed, 1 skipped as configured. |
| `COMPOSE_PROJECT_NAME=pitchmind ./scripts/test-postgres.sh` | Infrastructure: 64 passed, 0 skipped, 0 failed. Contracts/API: 147 passed, 0 skipped, 0 failed. Script reported both suites executed without skips and cleaned up the isolated test database. Existing local services/volumes remained running and unchanged. |
| `dotnet test PitchMind.slnx -c Release -m:1 --disable-build-servers -p:NuGetAudit=false` | 489 passed, 29 skipped, 0 failed. Skips: 28 opt-in Infrastructure PostgreSQL tests plus the new API readiness test because this full-suite invocation had no test connection. |
| `dotnet build PitchMind.slnx -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` | Passed, 0 warnings, 0 errors. |
| `bash -n scripts/test-postgres.sh`, embedded Python syntax parse, `node scripts/validate_operating_model.mjs`, and `git diff --check` | Passed. |

The database-backed run used the already-running local loopback PostgreSQL service. No cloud service was contacted or changed.

## Remaining EP06-S05 gaps

This proves only that `/health/ready` succeeds when the current PostgreSQL schema is reachable in LOCAL. It does not prove Azure readiness, measured latency/availability budgets, metrics export or operator-visible live-update lag, a connected authenticated browser journey, or Product Owner acceptance. EP06-S05 remains unverified/unaccepted.
