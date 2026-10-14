# EP03-S01 governed scenario artifact evidence — 2026-10-14

**Scope:** versioned, deterministic JSON serialization and regression assertions for the existing fictional `GovernedSyntheticScenario`. This test-only slice does not import or canonicalize the candidate MANU–LIV feed.

## Revision and artifact

- Base: `main` at `22af6a22c2474a6ab42728a93399097518705dca`.
- Code head: `fa100a8c8e7812836315bbf9a559fc7dbf6da016` (tree `6a0483a8e4fabe3d8fcda0a865bce9d256a12d5a`).
- The compact camel-case JSON envelope embeds format version `pitchmind-synthetic-fixture-json-v1` and the complete seeded scenario, including its fixture, ordered scenario events, frames and possession phases.
- Seed 42 artifact SHA-256: `01c6e9d69b002d6301c3b53a1d850e49ef5ceb31fbf00a80c2567dc235f9935d`, over the exact compact UTF-8 JSON bytes. Tests assert byte-for-byte regeneration and the pinned digest; seed 43 has a distinct match identity and digest.
- Changed files: `tests/PitchMind.TestFixtures/SyntheticFixtureArtifact.cs`, `tests/PitchMind.Domain.Tests/SyntheticFixtureTests.cs`, and this evidence record.
- No production C#/React, public API, canonical domain contract, persistence, architecture boundary, or candidate-feed mapping changed.

## Executed local verification

Runner: .NET SDK 10.0.401. Commands ran in a disposable LOCAL clone based on the recorded `main` revision. NuGet audit was disabled.

- `dotnet test tests/PitchMind.Domain.Tests/PitchMind.Domain.Tests.csproj -c Release -m:1 --disable-build-servers -p:NuGetAudit=false --no-restore` — **15 passed, 0 failed, 0 skipped**.
- `dotnet test tests/PitchMind.Application.Tests/PitchMind.Application.Tests.csproj -c Release -m:1 --disable-build-servers -p:NuGetAudit=false` — **13 passed, 0 failed, 0 skipped**.
- `node scripts/validate_operating_model.mjs` — passed (12 epics, 59 stories, 28 Council skills, zero dependency cycles).
- `node scripts/validate_operating_model.mjs --self-test` — passed dependency DAG, stacked PR trigger, and deliberate gate-failure checks.
- `git diff --check` — passed.

The first focused digest run intentionally used a placeholder assertion and failed while exposing the computed digest; after pinning the seed-42 value above, the full Domain suite passed. VSTest required local loopback socket access. No PostgreSQL integration or full-solution suite was run for this test-only slice.

## Evidence limits and outstanding review

This proves deterministic artifact serialization for the current authored fixture, not football realism or source-feed semantics. It does not establish candidate coordinate units/axes/orientation, player metric definitions, canonical mappings, feed rights/privacy review, PostgreSQL ingest/replay, hosted CI, Product Owner acceptance, or Azure readiness. Independent read-only review of full PR diff at `b28cc3f46cbf156d173c767db9b7404e72da4087` found no P0–P2 issue and confirmed the test and evidence digest agree; reviewer did not run tests. EP03-S01 remains unaccepted. No Azure operation occurred.
