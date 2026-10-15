# EP07-S05 — canonical tracking-frame API slice

**Status:** API slice locally verified at code commit `2b93466879551587ca4045fa614f3413cbf86b2a`, restacked on `main` `7847d177b435faa9211aa1223c238ea0c2f8b93c`. EP07-S05 remains unverified and unaccepted.

## Scope

This slice adds a protected `/api/v1/canonical/matches/{matchId}/frames` read path through an Application port and PostgreSQL adapter. It binds bounded keyset pages to exact selected time, selected projection, canonical admission revision and source hash. The React consumer contract carries both revision axes and rejects a frame page or item from a different projection.

Persisted frame payloads are checked against indexed identity, source and selected-time metadata, then validated by `TelemetryContractValidatorV1`. Invalid stored data fails closed with a sanitized `503 CANONICAL_DATA_INVALID`. The Azure profile registers the existing unavailable adapter. No candidate-feed admission, synthetic-to-canonical mapping, Azure operation, or visual redesign is part of this slice. The existing EP07-S05 work packet remains the acceptance authority.

## Verification performed

- `dotnet test PitchMind.slnx -c Release -m:1 --disable-build-servers --no-restore -p:NuGetAudit=false`: **434 passed, 25 skipped, 0 failed** (459 total). Skips are opt-in PostgreSQL integration cases; NuGet audit was disabled.
- `PITCHMIND_TEST_RESULTS_DIRECTORY=/private/tmp/pitchmind-ep07-s05-parent-postgres-20261015 ./scripts/test-postgres.sh`: **48 passed, 0 skipped, 0 failed** against a unique temporary database. The harness removed its temporary database; retained local Docker volumes were not changed.
- `npm test -- --run`: **54 passed, 0 failed**.
- `npm run build`: passed.
- `npm run lint`: passed.
- Council operating-model validation passed for 12 epics, 59 stories, 28 Council skills and no dependency cycles; OpenAPI YAML parsing and `git diff --check` passed.
- E2E could not start in the earlier attempt because the local install had no `playwright` executable; no E2E tests ran.
- Independent read-only exact-tree code review found no P0–P2 API implementation defect. The reviewer inspected cursor binding, role policy, time/revision/source filtering, payload/index validation, and candidate-feed/fabricated-fact paths; it did not execute tests. Its two P2 follow-ups are tracked in the stacked child PR for bounded JSON materialization and nested revision validation.

## Remaining evidence and acceptance

- Frontend evidence drawer/replay acceptance still needs the full EP07-S05 journey, keyboard/screen-reader review, source evidence comparison and Product Owner acceptance.
- The synthetic source's coordinate, clock, metric and canonical mapping semantics remain unverified. This API serves only already-persisted frames and does not admit the candidate feed.
- Hosted CI, independent submitted GitHub review and Product Owner acceptance are pending. This evidence does not mark EP07-S05 accepted.
- No Azure resources, infrastructure, or deployment were changed.
