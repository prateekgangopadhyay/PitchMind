# EP07-S05 — canonical tracking-frame API slice

**Status:** API slice locally verified at `ba304a0c85fbce1e097e7c357b2ad42e41e9a2a1`; EP07-S05 remains unverified and unaccepted.

## Scope

Restacked on `main` at `684574264d7e1f37921f1bc02c834d607068c2e6`, this slice adds a protected `/api/v1/canonical/matches/{matchId}/frames` read path through an Application port and PostgreSQL adapter. It binds bounded keyset pages to exact selected time, selected projection, canonical admission revision and source hash. The React consumer contract now carries both revision axes and rejects a frame page or item from a different projection.

Persisted frame payloads are checked against indexed identity, source and selected-time metadata, then validated by `TelemetryContractValidatorV1`. Invalid stored data fails closed with a sanitized `503 CANONICAL_DATA_INVALID`. The Azure profile registers the existing unavailable adapter. No candidate-feed admission, synthetic-to-canonical mapping, Azure operation, or visual redesign is part of this slice. The existing EP07-S05 work packet remains the acceptance authority.

## Verification performed

- `dotnet test PitchMind.slnx -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false`: **408 passed, 0 failed, 25 skipped**. The skips are opt-in PostgreSQL tests; NuGet audit was disabled.
- `TrackingFrameReaderUsesExactTimeRevisionSourceAndStableCompositePagination`: **1 passed, 0 failed, 0 skipped** against the repository's isolated-database harness and the local Docker PostgreSQL service. The harness created and dropped a unique temporary database; it did not alter the retained volume.
- The full opt-in PostgreSQL suite was also executed against an isolated temporary database: **21 passed, 22 failed, 0 skipped** at this head. The same base commit (`6845742`) run returned **20 passed, 22 failed, 0 skipped**. Failures reproduced at base include replay-seed registration and duplicate migration-constraint errors; these remain repository-level integration failures and are not treated as passing evidence for this slice.
- `npm test`: **54 passed, 0 failed**. This includes negative React checks for mismatched tracking page and frame projection revisions.
- `npm run build`: passed.
- `npm run lint`: passed.
- `npm run test:e2e -- e2e/ep07-s06-resilient-journey.spec.ts` could not start because the local install has no `playwright` executable; no E2E tests ran.
- Council operating-model validation passed for 12 epics, 59 stories, 28 Council skills and no dependency cycles; OpenAPI YAML parsing and `git diff --check` passed.
- Independent read-only exact-tree review found no P0–P2 API implementation defect. It reviewed cursor binding, role policy, time/revision/source filtering, payload/index validation, and no candidate-feed or fabricated-fact path. It did not execute tests.

## Remaining evidence and acceptance

- The focused PostgreSQL frame-pagination integration test passed. The broader PostgreSQL integration suite remains red at base and this head; the replay-seed and repeat-migration failures need separate remediation.
- Frontend evidence drawer/replay acceptance still needs the full EP07-S05 journey, keyboard/screen-reader review, source evidence comparison and Product Owner acceptance.
- The synthetic source's coordinate, clock, metric and canonical mapping semantics remain unverified. This API serves only already-persisted frames and does not admit the candidate feed.
- Hosted CI and Product Owner acceptance are pending. This evidence does not mark EP07-S05 accepted.
- No Azure resources, infrastructure, or deployment were changed.
