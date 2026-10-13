# EP03-S01 seeded fixture identity evidence

**Code commit:** `dc09d0513ae5660533124fedeeee29dfacd35c09`  
**Parent stack:** PR #135 head `cdb5c296d2e9a7de18d1752ffe8ea8a5362618c6`  
**Scope:** test-only fictional fixture identity; candidate source JSON and production application code are unchanged.

## Change

The synthetic fixture generator previously assigned one fixed `MatchId` to every seed even though event IDs and tracking positions vary by seed. The generator now derives the fixture ID from SHA-256 of the versioned input `${Version}:match:${seed}`, uses invariant-culture formatting and the first 128 bits as a `Guid`, and bumps the fixture schema/version from `synthetic-v1` to `synthetic-v2`. Event IDs also use invariant-culture seed inputs, so identity stays stable when the current culture changes. `FixtureId` remains available for the default seed (42). All events in a generated fixture carry that fixture's match ID.

## Verification

On .NET SDK 10.0.401, the focused `PitchMind.Domain.Tests` Release suite passed **5/5, 0 failed, 0 skipped**. The test project was run against the exact changed generator and test-file bytes from the code commit, using locally cached restore assets (`--no-restore`) because the NuGet vulnerability endpoint was unreachable and restore did not complete. VSTest first failed in the restricted sandbox because it could not bind a loopback socket; rerunning with local execution permission passed.

The new regression verifies version and seed metadata, stable match identity/events/tracking for the same seed, distinct match identity and disjoint event GUIDs for another seed, default-seed `FixtureId` compatibility, agreement between every event's `MatchId` and its containing fixture, and repeatability under an Arabic current culture. The pre-existing fixture completeness and halftime-direction tests also passed. The Council dependency-DAG self-test and `git diff --check` passed. An independent read-only code review of the finalized implementation found no code blocker; it verified invariant-culture IDs, cross-seed event-ID disjointness, and repeatability across `ar-SA` current culture.

## Limits

This proves deterministic test-fixture identity behavior only. It does not establish football realism, approved score transitions, source-feed semantics, canonical ingestion, PostgreSQL persistence, full-solution tests, hosted CI, independent human review, Product Owner acceptance, or Azure behavior. The generator remains in `tests/PitchMind.TestFixtures`; the production API remains fail-closed for match reads. EP03-S01 remains unaccepted.
