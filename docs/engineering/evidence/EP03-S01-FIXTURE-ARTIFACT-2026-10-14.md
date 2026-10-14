# EP03-S01 fictional fixture artifact evidence — 2026-10-14

**Scope:** deterministic serialization and regression assertions for the existing fictional test fixture only. This does not canonicalize or import the candidate MANU–LIV feed.

## Revision and files

- Initial code commit: `7a344aa6f99c2a6543d2f3089f4555433ffe8ce1`.
- Corrected code commit: `a80c93dad458c889dc0a9dc63a57a16b8e682dba`.
- Branch: `codex/ep03-s01-fixture-artifact`
- Base: `github/main` at `1f97706ec22c9f33fe4ebcfcc1595ddc41bf71af`
- Added `tests/PitchMind.TestFixtures/SyntheticFixtureArtifact.cs` with a compact camel-case JSON envelope containing its format version and a SHA-256 of exact UTF-8 envelope bytes. Construction is limited to the factory so metadata cannot be supplied inconsistently.
- Extended `tests/PitchMind.TestFixtures/SyntheticFixtureGenerator.cs` to assign deterministic seed-specific match IDs (preserving the pre-existing default-seed ID) and ensure every generated event references its fixture's match ID.
- Extended `tests/PitchMind.Domain.Tests/SyntheticFixtureTests.cs` to check synthetic provenance/version, duration and period clocks, unique 22-player frame rosters and synthetic pitch bounds, deterministic same-seed JSON/hash, different-seed hashes and match IDs, embedded format/match metadata, and ordered same-period pressing sequence timestamps.
- No production C#/React, public API, canonical domain contract, persistence, architecture boundary, or candidate-feed mapping changed.

## Executed local verification

Runner: .NET SDK 10.0.401.

- `dotnet test tests/PitchMind.Domain.Tests/PitchMind.Domain.Tests.csproj -c Release -m:1 --disable-build-servers -p:NuGetAudit=false --no-restore` — **6 passed, 0 failed**.
- `dotnet test tests/PitchMind.Application.Tests/PitchMind.Application.Tests.csproj -c Release -m:1 --disable-build-servers -p:NuGetAudit=false --no-restore` — **11 passed, 0 failed**.
- Council operating-model dependency-DAG/failure-probe self-test — passed.
- `node scripts/validate_operating_model.mjs` — passed (12 epics, 59 stories, 28 Council skills, no dependency cycles).
- `git diff --check` — passed.

Both initial VSTest invocations were blocked because the restricted runner could not bind the local testhost loopback socket. Retrying with local testhost socket access completed both commands successfully. The exact generated artifact digest was not separately captured; the tests verify its SHA-256 calculation and same-seed/different-seed behavior. An independent code review found the original JSON lacked an embedded version and seed variants shared a match ID; both issues were corrected and regression-tested. The reviewer noted that the pressing timeline test does not establish a football mechanism; the packet and acceptance status retain that limitation.

## Evidence limits and outstanding review

The application replay tests cover 0–0, 1–0, and 1–1 score transitions and selected-time exclusion of later goals/evidence. This slice does not establish full football realism, card/substitution semantics, candidate source meanings, PostgreSQL ingest/replay, frontend behavior, hosted CI, or cloud readiness. The current canonical event contract lacks card/substitution kinds; no event contract was changed here. Independent technical/football review and Product Owner acceptance remain pending. EP03-S01 remains unaccepted. No Azure operation occurred.
