# EP07-S05 — canonical tracking-frame API slice

**Status:** implementation and local checks recorded; story remains unverified and unaccepted.

## Scope

Based on `main` at `21cd99adc6b0606ac5fcb6e9e4ca1ab49ca3f6ff`, this local slice adds a protected `/api/v1/canonical/matches/{matchId}/frames` read path through an Application port and PostgreSQL adapter. It binds bounded keyset pages to exact selected time, selected projection, canonical admission revision and source hash. The React consumer contract now carries both revision axes and rejects a frame page or item from a different projection.

Persisted frame payloads are checked against indexed identity, source and selected-time metadata, then validated by `TelemetryContractValidatorV1`. Invalid stored data fails closed with a sanitized `503 CANONICAL_DATA_INVALID`. The Azure profile registers the existing unavailable adapter. No candidate-feed admission, synthetic-to-canonical mapping, Azure operation, or visual redesign is part of this slice. The existing EP07-S05 work packet remains the acceptance authority.

## Verification performed

- `dotnet test PitchMind.slnx -c Release -m:1 --disable-build-servers -p:NuGetAudit=false`: **408 passed, 0 failed, 25 skipped**. All skips are opt-in PostgreSQL tests because `PITCHMIND_TEST_POSTGRES` was unset. NuGet audit was disabled.
- `npm test`: **54 passed, 0 failed**. This includes negative React checks for mismatched tracking page and frame projection revisions.
- `npm run build`: passed.
- `npm run lint`: passed.
- `npm run test:e2e -- e2e/ep07-s06-resilient-journey.spec.ts`: **4 passed, 0 failed** after allowing its local Vite server to bind loopback.
- Council operating-model validator, OpenAPI YAML parse, and `git diff --check`: passed.
- Independent read-only code review found no remaining API implementation defect. The reviewer noted missing negative React coverage for mismatched tracking projection revisions; both page-level and item-level cases were added and the full frontend suite passed. Reviewer did not execute tests.

## Remaining evidence and acceptance

- PostgreSQL exact-time/revision/source filtering and pagination integration test was added but skipped; run it against an isolated local PostgreSQL database before treating the adapter as independently verified.
- Frontend evidence drawer/replay acceptance still needs the full EP07-S05 journey, keyboard/screen-reader review, source evidence comparison and Product Owner acceptance.
- The synthetic source's coordinate, clock, metric and canonical mapping semantics remain unverified. This API serves only already-persisted frames and does not admit the candidate feed.
- Hosted CI and Product Owner acceptance are pending. This evidence does not mark EP07-S05 accepted.
- No Azure resources, infrastructure, or deployment were changed.
