# EP07-S05 — exact source revision follow-up

**Code revision:** `698e42141a1e8d781039638933a2fb2671dcac7a`, merged to `main` as `f1301984dc0cd4ae0e0a68438f69835219527bb0`.
**Scope:** Resolve the independent review finding that a frame citation's `sourceRevision` was not passed into the canonical tracking lookup. This is an implementation evidence update, not story acceptance.

## Change and evidence

- The replay UI now sends `sourceRevision` with a targeted `frameId` lookup.
- The API rejects non-positive revisions, revisions without `frameId`, and revisions above the selected canonical revision.
- The Application reader port carries an optional exact canonical revision. PostgreSQL applies that exact predicate while retaining match, selected-time, sequence-cutoff, and source-hash constraints. Omitting `sourceRevision` preserves the existing bounded lookup.
- API/OpenAPI documentation describes the new query behavior. Frontend and API contract tests cover propagation and invalid requests.
- The PostgreSQL primary key prevents duplicate `(match_id, frame_id)` rows in the current schema. Its integration test verifies an older source revision resolves at that exact revision and that a mismatched revision returns no row.

## Verification on code revision `698e4214`

- Frontend Vitest: **59 passed**.
- Frontend production build and lint: **passed**.
- Full .NET Release suite: **439 passed, 27 skipped, 0 failed**. The skips are opt-in PostgreSQL tests in the general run.
- Isolated local PostgreSQL harness: **50 passed, 0 skipped, 0 failed**. It created and dropped a unique temporary database.
- Council story/skill validator and `git diff --check`: **passed**.
- Independent exact-head read-only review of the full diff: **no actionable findings**. The reviewer did not execute tests.
- GitHub combined status: **no hosted status checks**; submitted GitHub PR reviews: **none**. The Playwright 5/5 result is from the prior head and was not rerun after this fix.

## Story status and remaining evidence

EP07-S05 remains **unverified and unaccepted**. Manual keyboard and assistive-technology review, axe/WCAG checks, source evidence comparison, a current-head Playwright run, hosted CI, integrated story acceptance, and Product Owner acceptance remain pending. Candidate-feed coordinate, clock, metric, and canonical mapping semantics remain unresolved. No Azure resources, infrastructure, or deployments were changed.
