# EP07-S05 live PostgreSQL/API/browser journey — 2026-10-15

## Scope

This is a local verification increment for the existing EP07-S05 evidence drawer. It uses a unique disposable PostgreSQL database and explicitly synthetic test-only rows. It does not use `data/simulations/manu-liv-match-dataset.json`, certify any football source semantics, or change story acceptance.

The first live journey exposed a response-contract defect: the API returned an insight without its persisted conditions and omitted `limitations`, while the drawer assumed both properties existed. Opening the evidence disclosure raised a browser `TypeError` before a citation could render.

## Changes

- Return the stored PostgreSQL `conditions_json` as the insight `conditions` object.
- Return `limitations: null` because the canonical schema does not persist limitation records. The UI can truthfully say that limitations are not recorded.
- Reject persisted conditions whose JSON root is not an object and map that integrity failure to sanitized `503 CANONICAL_DATA_INVALID` responses.
- Add OpenAPI and API documentation for both fields.
- Add a Playwright case that uses real PostgreSQL-backed canonical summary, event, insight and evidence routes without route interception, then follows the citation to the selected event and verifies match time, projection revision and focused replay event.
- Add a Python runner that reuses the current local Postgres service, creates a random database, runs the migration/seed test, starts the local Development API on loopback, runs the Playwright case, stops the API, and drops the database.

## Verification performed

- `PITCHMIND_DOCKER_ENV=/path/to/docker/.env ./scripts/test-ep07-s05-live-api-e2e.py`: PostgreSQL fixture/migration seed **1 passed, 0 skipped**; API Release build **0 warnings, 0 errors**; Playwright local PostgreSQL → API → browser journey **1 passed**.
- `dotnet test tests/PitchMind.Contracts.Tests/PitchMind.Contracts.Tests.csproj --configuration Release --no-restore --nologo --verbosity minimal --maxcpucount:1 --disable-build-servers -p:UseSharedCompilation=false`: **140 passed, 0 failed**.
- `dotnet test tests/PitchMind.Api.Tests/PitchMind.Api.Tests.csproj --configuration Release --no-restore --nologo --verbosity minimal --maxcpucount:1 --disable-build-servers -p:UseSharedCompilation=false`: **12 passed, 0 failed**.
- Full `dotnet test PitchMind.slnx --configuration Release --no-restore --nologo --verbosity minimal --maxcpucount:1 --disable-build-servers -p:UseSharedCompilation=false`: **469 passed, 28 opt-in PostgreSQL tests skipped, 0 failed**. The PostgreSQL-to-API/browser path separately executed its targeted database migration and seed test against real local PostgreSQL.
- Frontend Vitest: **83 passed, 0 failed**.
- Frontend production build and lint passed. Vite emitted only its existing Rollup annotation warnings for the SignalR dependency.
- `node scripts/validate_operating_model.mjs`: **passed**, 12 epics, 59 stories, 28 Council skills, 5 design skills, no dependency cycles, 2 approval gates.
- `git diff --check`: **passed**.

## Remaining gates

The change is implementation and local verification only. Independent exact-head review, hosted CI, manual keyboard/screen-reader review, axe/WCAG review, comparison with approved football evidence, and Product Owner acceptance are **pending**. The source feed's coordinate, clock, metric and canonical-mapping semantics remain unresolved. This evidence does not mark EP07-S05 verified or accepted and does not authorize deployment.

## Local environment recovery

The saved Docker volume is PostgreSQL 16 data. The current Compose default selected PostgreSQL 17, which could not open the existing volume. The local service was restored with `POSTGRES_IMAGE=postgres:16-alpine`, and the ignored `docker/.env` now pins that compatible image. The named volume was retained. The test runner itself does not run `compose up`; it only uses the already-running service and its disposable database.
