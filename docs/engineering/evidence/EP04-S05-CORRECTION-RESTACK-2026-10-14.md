# EP04-S05 — Mainline fixture-correction restack evidence

**Story:** EP04-S05, Match integrity golden tests (P0). **Status:** Partial implementation; not accepted.
**Base:** `main` at `5e92e657572b45ea4e4c8c551e405adcb91fdf1e`.
**Initial code commit:** `f799dffd54b4132e55d8b61e3ce78090f21140dc`.
**Initial code tree:** `0c43bffcec5bd1eb0904e1394940cd8913fbcc4e`.
**Current code commit:** `412fb55e20b2ff35a72962aca4449aea2e5f90fe`.

## Change

Adds a pure Domain `FixtureCorrectionHistory` overlay for generated fixtures. It keeps source event order and source provenance, attaches correction revision/ID/provenance to replaced events, chooses the greatest correction revision, and rejects malformed/conflicting correction records. Tests cover selected state after a corrected home goal, deterministic repeat replay, immutable prior fixture input, preserved event ID/match/sequence/period/clock/occurrence time, same-revision conflicts, unknown targets, goal-envelope contradictions, and a missing replacement payload.

The change is fixture-only. It does not persist corrections, perform canonical admission, update a PostgreSQL canonical revision, or provide ledger-to-state correction replay. It does not use or certify `data/simulations/manu-liv-match-dataset.json`. Existing synthetic-v3 candidate replay hashes are regression fingerprints pending Product Owner approval.

## Verification

- Command: `dotnet test tests/PitchMind.Domain.Tests/PitchMind.Domain.Tests.csproj -c Release -m:1 --disable-build-servers -p:NuGetAudit=false`.
- Result on initial code tree: **39 passed, 0 failed, 0 skipped** on `0c43bffcec5bd1eb0904e1394940cd8913fbcc4e`.
- After the independent review identified a null replacement-payload diagnostic gap, commit `412fb55e20b2ff35a72962aca4449aea2e5f90fe` added a fail-closed guard and regression test. The same command then passed **40 passed, 0 failed, 0 skipped** on the working tree containing that commit.
- The first sandboxed invocation built the projects but VSTest aborted before running tests because it could not bind its local loopback socket. The same command was rerun with local test-runner permission and passed.
- NuGet vulnerability audit was disabled. Full solution tests, PostgreSQL integration, hosted CI, API smoke and Azure verification were not run for this slice.
- `git diff --check` passed for the implementation changes before documentation was added.
- The independent review of prior PR #210 identified that its migration changes downgraded schema v8 to v7. This restack reapplies only the fixture-correction Domain/test slice on top of main and carries no `PostgresSchemaMigrator` changes; main’s v6 competition, v7 replay and v8 replay-context migration sequence remains intact.

## Review and remaining gates

Independent exact-head re-review after the payload guard is pending. Canonical correction admission/persistence, PostgreSQL correction integration, approved football golden snapshots, candidate-feed semantic certification, hosted CI and Product Owner pass/fail remain outstanding. No story acceptance, deployment or Azure resource change is claimed.
