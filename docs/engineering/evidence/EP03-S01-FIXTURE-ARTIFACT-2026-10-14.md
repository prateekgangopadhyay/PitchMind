# EP03-S01 fictional fixture artifact evidence — 2026-10-14

**Scope:** deterministic serialization and regression assertions for the existing fictional test fixture only. This does not canonicalize or import the candidate MANU–LIV feed.

## Revision and files

- Initial code commit: `7a344aa6f99c2a6543d2f3089f4555433ffe8ce1`.
- Corrected code commit: `a80c93dad458c889dc0a9dc63a57a16b8e682dba`.
- Spatial fixture regression and event-linkage follow-up: `bcdb11e3195f336d63b560c78f4b97577efde040`.
- Default-seed envelope hash golden follow-up: `38482bf2735c2bd2bacb01545b694e1f1d882703`.
- Branch: `codex/ep03-s01-fixture-artifact`
- Base: `github/main` at `1f97706ec22c9f33fe4ebcfcc1595ddc41bf71af`
- Added `tests/PitchMind.TestFixtures/SyntheticFixtureArtifact.cs` with a compact camel-case JSON envelope containing its format version and a SHA-256 of exact UTF-8 envelope bytes. Construction is limited to the factory so metadata cannot be supplied inconsistently.
- Extended `tests/PitchMind.TestFixtures/SyntheticFixtureGenerator.cs` to assign deterministic seed-specific match IDs (preserving the pre-existing default-seed ID) and ensure every generated event references its fixture's match ID.
- Extended `tests/PitchMind.Domain.Tests/SyntheticFixtureTests.cs` to check synthetic provenance/version, duration and period clocks, unique 22-player frame rosters and synthetic pitch bounds, deterministic same-seed JSON/hash, different-seed hashes and match IDs, embedded format/match metadata, ordered same-period pressing sequence timestamps, designed press/regain proximity, event-to-frame actor linkage, a separated counterexample, and a pinned default-seed envelope digest.
- No production C#/React, public API, canonical domain contract, persistence, architecture boundary, or candidate-feed mapping changed.

## Executed local verification

Runner: .NET SDK 10.0.401.

- `dotnet test tests/PitchMind.Domain.Tests/PitchMind.Domain.Tests.csproj -c Release -m:1 --disable-build-servers -p:NuGetAudit=false --no-restore` — **6 passed, 0 failed**.
- `dotnet test tests/PitchMind.Application.Tests/PitchMind.Application.Tests.csproj -c Release -m:1 --disable-build-servers -p:NuGetAudit=false --no-restore` — **11 passed, 0 failed**.
- After adding the geometry and event-linkage assertions, the Domain test command passed **7/7** on 2026-10-14. The Application suite was not rerun after this test-only change.
- After pinning the default-seed envelope digest, the Domain test command again passed **7/7** on 2026-10-14.
- Council operating-model dependency-DAG/failure-probe self-test — passed.
- `node scripts/validate_operating_model.mjs` — passed (12 epics, 59 stories, 28 Council skills, no dependency cycles).
- `git diff --check` — passed.

Both initial VSTest invocations were blocked because the restricted runner could not bind the local testhost loopback socket. Retrying with local testhost socket access completed both commands successfully. The default-seed envelope's pinned SHA-256 is `a10b2e7e1e7a57e82d94acd5d407a9535f1fbc62ac0938e96522059dda0f3cf4`; it covers the exact compact camel-case JSON UTF-8 bytes, including the embedded artifact format version. A hash change requires review of fixture content, serializer bytes and artifact-version policy rather than a blind golden refresh. An independent code review found the original JSON lacked an embedded version and seed variants shared a match ID; both issues were corrected and regression-tested. A later reviewer confirmed the spatial assertions match the authored fixture and identified an event-to-frame linkage gap; the test now asserts the matching Pressure/Turnover types and actors. These assertions cover synthetic geometry and identity linkage only, not football realism or candidate-source coordinate semantics.

## Evidence limits and outstanding review

The application replay tests cover 0–0, 1–0, and 1–1 score transitions and selected-time exclusion of later goals/evidence. This slice does not establish full football realism, card/substitution semantics, candidate source meanings, PostgreSQL ingest/replay, frontend behavior, hosted CI, or cloud readiness. The current canonical event contract lacks card/substitution kinds; no event contract was changed here. Independent technical/football review and Product Owner acceptance remain pending. EP03-S01 remains unaccepted. No Azure operation occurred.
