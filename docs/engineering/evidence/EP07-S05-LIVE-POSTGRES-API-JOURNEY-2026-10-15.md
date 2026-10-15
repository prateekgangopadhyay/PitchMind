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
- Launch the API in its own process group and stop the full group before dropping the database. The runner verifies port 5080 is released; the probe allows TCP `TIME_WAIT` reuse while still detecting a live listener.

## Verification performed

- `PITCHMIND_DOCKER_ENV=/path/to/docker/.env ./scripts/test-ep07-s05-live-api-e2e.py`: PostgreSQL fixture/migration seed **1 passed, 0 skipped**; API Release build **0 warnings, 0 errors**; Playwright local PostgreSQL → API → browser journey **1 passed**.
- `dotnet test tests/PitchMind.Contracts.Tests/PitchMind.Contracts.Tests.csproj --configuration Release --no-restore --nologo --verbosity minimal --maxcpucount:1 --disable-build-servers -p:UseSharedCompilation=false`: **140 passed, 0 failed**.
- `dotnet test tests/PitchMind.Api.Tests/PitchMind.Api.Tests.csproj --configuration Release --no-restore --nologo --verbosity minimal --maxcpucount:1 --disable-build-servers -p:UseSharedCompilation=false`: **12 passed, 0 failed**.
- Full `dotnet test PitchMind.slnx --configuration Release --no-restore --nologo --verbosity minimal --maxcpucount:1 --disable-build-servers -p:UseSharedCompilation=false`: **469 passed, 28 opt-in PostgreSQL tests skipped, 0 failed**. The PostgreSQL-to-API/browser path separately executed its targeted database migration and seed test against real local PostgreSQL.
- Frontend Vitest: **83 passed, 0 failed**.
- Frontend production build and lint passed. Vite emitted only its existing Rollup annotation warnings for the SignalR dependency.
- `node scripts/validate_operating_model.mjs`: **passed**, 12 epics, 59 stories, 28 Council skills, 5 design skills, no dependency cycles, 2 approval gates.
- `git diff --check`: **passed**.

## Independent review follow-up

The first exact-head review found that terminating only the `dotnet run` parent could leave the API child running. The runner now starts the API in a dedicated process group, terminates and escalates against the entire group, and checks the loopback port before database cleanup. A follow-up independent read-only review found no remaining findings in that process cleanup change.

PR #273 is merged to `main` at `bad13b3f4dc618841f1f541474aaf4d7f2ee5d55`. Post-merge verification against that integrated code passed the fixture/migration seed (1/1), API Release build (0 warnings/errors), and live PostgreSQL → API → Playwright journey (1/1). The final runner cleanup-scope review found no actionable findings; the reviewer did not run tests. The ordinary Playwright suite then exposed that the live-only spec was included without the isolated runner. PR #274, merged to `main` at `dd417c2d142c659580b022690373ead8b6540b05`, excludes the live spec by default and opts into it through the dedicated runner after provisioning succeeds. The PR #274 code passed ordinary Playwright 5/5 and the dedicated live journey 1/1 before merge; no second test run was performed after merge because only the reviewed documentation/SHA correction was added following the test run. Full PR #274 head review found no remaining actionable findings; the reviewer did not execute tests.

## Remaining gates

The follow-up test-scope change is implementation and local verification only. Exact-head review of its current diff found no actionable findings; hosted CI, manual keyboard/screen-reader review, axe/WCAG review, comparison with approved football evidence, and Product Owner acceptance are **pending**. The source feed's coordinate, clock, metric and canonical-mapping semantics remain unresolved. This evidence does not mark EP07-S05 verified or accepted and does not authorize deployment.

## Local environment recovery

The saved Docker volume is PostgreSQL 16 data. The current Compose default selected PostgreSQL 17, which could not open the existing volume. The local service was restored with `POSTGRES_IMAGE=postgres:16-alpine`, and the ignored `docker/.env` now pins that compatible image. The named volume was retained. The test runner itself does not run `compose up`; it only uses the already-running service and its disposable database.
