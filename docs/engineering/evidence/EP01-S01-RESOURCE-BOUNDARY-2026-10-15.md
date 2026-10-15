# EP01-S01 production fixture-resource boundary — 2026-10-15

**Scope:** Keep the versioned synthetic-v2 fixture out of production assemblies and prevent evaluated source projects from embedding test/simulation fixtures. This is local implementation evidence for EP01-S01, not Product Owner acceptance.

**Initial base:** `main` at `6178d656283fb375711e8836fd09e93e46877989`

**Restacked base:** `main` at `740d4d947d46646c55f24467d71a61de1af752f2` after PR #48 merged

**Code commit before restack:** `71386e9cb1825c6d8461202fa2b83c313220a693`

**Restacked code commit:** `3dc0408d45b9a1dc392d5d4d1b165437cb2993b3`

**Branch:** `codex/ep01-s01-fixture-boundary`

**Environment:** LOCAL, .NET SDK 10.0.401

## Change

- Moved `synthetic-fixture-v2.json` byte-for-byte from `data/simulations/` to `tests/PitchMind.TestFixtures/Fixtures/`; its SHA-256 remains `ff3ab70d93cb64e793e641d0291f48e11e6992c3b106ef7e3d2ef8b294c04f0a`.
- Embedded the resource in the test-only `PitchMind.TestFixtures` assembly and added `EmbeddedSyntheticFixture.OpenRead()` for tests.
- Added an architecture check over evaluated `EmbeddedResource` items in every `src/*.csproj`. It rejects resources under test/simulation paths and fixture, scenario, or golden resource names/metadata. Negative coverage rejects the former synthetic fixture path and a generic test-data path while allowing a contract schema resource.
- Added a test proving the JSON parses from the test-support assembly and is absent from the Domain assembly.
- Updated the C# project-boundary documentation. No production container or API boundary changed, so the C4 diagrams did not need modification.

## Executed local checks

| Command | Result |
|---|---|
| `dotnet clean PitchMind.slnx -c Release -m:1 --disable-build-servers` | Passed, 0 warnings/errors. |
| `dotnet build PitchMind.slnx -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` | Passed, 0 warnings/errors. |
| `dotnet test PitchMind.slnx -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false --logger 'console;verbosity=minimal'` | Passed on the restacked tree: 325, failed: 0, skipped: 24. All skips are opt-in PostgreSQL integration tests without a configured test connection. The restacked Architecture suite passed 24/24; Domain suite passed 68/68; Application suite passed 93/93. |
| `node scripts/validate_operating_model.mjs` | Passed: 12 epics, 59 stories, 28 Council skills, five design skills, 33 discoverable skills, zero routing cycles. |
| `node scripts/validate_operating_model.mjs --self-test` | Passed. |
| `python3 -m json.tool tests/PitchMind.TestFixtures/Fixtures/synthetic-fixture-v2.json` | Passed; fixture JSON parses. |
| `git diff --check` | Passed. |

## Evidence limits and acceptance

After the initial verification, the branch was restacked onto the PR #48 merge commit and the full suite was rerun on the restacked tree. The isolated branch reused existing local `obj/project.assets.json` files copied from prior local audit worktrees for projects whose assets were missing. No NuGet restore ran; `NuGetAudit` was disabled. The build therefore verifies the exact source tree against cached dependencies, not a fresh-machine restore or package vulnerability audit. The 24 PostgreSQL tests remain unverified in this run. Hosted CI and a human GitHub review were not observed. An independent exact-head code review of the original head found no P0–P2 issue; the rebased head still needs re-review. Product Owner acceptance remains pending; EP01-S01 is not accepted. No Azure resources or deployments were changed.
