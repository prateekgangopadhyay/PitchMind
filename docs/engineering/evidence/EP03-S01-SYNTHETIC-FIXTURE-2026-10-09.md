# EP03-S01 synthetic fixture evidence — 2026-10-09

**Scope:** Local deterministic generation from the fictional `synthetic-v2` definition. This does not admit the Manchester United–Liverpool candidate feed or establish independent football/Product Owner acceptance.

## Versioned input

- Definition: `data/simulations/synthetic-fixture-v2.json`
- SHA-256: `ff3ab70d93cb64e793e641d0291f48e11e6992c3b106ef7e3d2ef8b294c04f0a`
- Fixture version: `synthetic-v2`
- Provenance classification: `synthetic`
- Duration: 5,400 seconds; two 2,700-second periods
- The JSON resource contains fictional team/player identities, formation/base positions, event plan, substitution activity windows, pressing-frame overrides and sampling settings. The production C# generator contains no fixture-specific team names, player IDs or scenario clock values.

## Executed checks

| Requirement | Command | Observed result |
|---|---|---|
| Full .NET solution | `dotnet test PitchMind.slnx -c Release -m:1 --disable-build-servers --no-restore` | Exit 0: 30 passed, 0 failed, 5 skipped. The skipped tests are PostgreSQL integration tests requiring a test database. |
| Fixture validation regressions | `dotnet test PitchMind.slnx -c Release -m:1 --disable-build-servers --no-restore` (domain project) | 11 passed, 0 failed; period/clock mismatch, extra kickoff, duplicate terminal markers, missing UTC start, null teams/player, inactive-player frame override and both substitution transitions are covered. |
| Deterministic fixture output | `PitchMind.Domain.Tests.SyntheticFixtureTests.SeedAndFixtureVersionProduceStableIdentitiesAndFrames` | Three runs with the same seed produced the same serialized-frame fingerprint and IDs; a different seed changed match/event IDs. |
| Match state and replay | Application test suite in the full solution command | 11 passed; score before/after each goal and selected-time evidence bounds remain verified. |
| Candidate source validation | `python3 -m unittest discover -s scripts -p 'test_validate_manu_liv_candidate.py' -v` | 11 passed. Source remains `structurally_valid_candidate_blocked_for_canonicalization`. |
| Feed profiler | `python3 -m unittest discover -s scripts -p 'test_profile_simulation_feed.py' -v` | 5 passed. |
| Routing and design checks | `node scripts/validate_operating_model.mjs`; `node scripts/validate_operating_model.mjs --self-test`; `node scripts/test_design_skills.mjs` | All passed; 12 epics, 59 stories and 28 Council skills were recognized. |
| Input and C# checks | `python3 -m json.tool data/simulations/synthetic-fixture-v2.json`; source scan for fixture names/IDs/event times; `git diff --check` | JSON parse and diff check passed; no fixture-specific names, player IDs or scenario times found in production C#. |

## Scenario assertions and limits

## Independent re-verification — code commit `d7c61e709f43a113db88eb7bbd2972f1b5e9d8fd`

- .NET Release build succeeded with 0 warnings and 0 errors.
- Full .NET solution tests: 31 passed, 0 failed, 5 skipped (PostgreSQL integration requires an unconfigured test database). The application suite includes a 3,600-second fixture with an 1,800-second halftime and a 120×80 m pitch, validating replay bounds, status, period, coordinate metadata and rejection of out-of-pitch frames.
- Contract tests verify fixture responses carry the configured duration, halftime and dimensions, and selected-state frames retain the same coordinate convention.
- Frontend: 3 tests passed, TypeScript/Vite production build succeeded, and ESLint exited 0. The user-facing pitch and replay range use fixture metadata instead of fixed match dimensions.
- Candidate source validator tests passed 13/13; candidate report retained the same pinned byte SHA, source event-ID ledger and full ordered-record digest, while validating manifest-pinned halftime/fulltime marker clocks and periods.
- Feed profiler tests passed 5/5; operating-model validator passed with 12 epics, 59 stories, 28 Council skills and zero routing cycles.

These checks were run locally at `d7c61e7` before the evidence-only documentation update. No hosted CI, PostgreSQL integration or Azure evidence is claimed.

## Follow-up verification — code commit `4acc702`

- Full .NET solution tests passed 35, failed 0, skipped 5; PostgreSQL integration tests remain skipped because no test database is configured. New application cases reject NaN, infinity and out-of-pitch ball coordinates.
- Frontend tests passed 3/3, build and lint exited 0. The nonstandard 120×80 m rendering regression also checks penalty-box and goal-area metre dimensions, while the pitch boundary and replay use fixture-provided dimensions.
- Candidate validation passed 14/14 with a source-specific exception for the same-clock pre-halftime `Pass` only; other event types remain rejected. Feed profiler passed 5/5. Operating-model validation passed (12 epics, 59 stories, 28 Council skills, zero cycles). `git diff --check` passed.
- Candidate source semantics and acceptance remain unresolved as listed above; no hosted CI, PostgreSQL integration, Azure or Product Owner acceptance is claimed.

- The generated fixture has period 1 and period 2 markers, a 90-minute full-time event, two goals resulting in 1–1, one yellow card and two substitutions.
- Tracking frames contain 22 active players, apply configured substitutions, and reverse attack direction at half time.
- Definition validation fails closed for a missing UTC start, null collections/players, mismatched event period/clock, extra or duplicate match markers, invalid active-player bounds and overrides for inactive players.
- Existing positive and negative pressing-sequence and replay tests passed. This is a scripted synthetic evidence scenario, not a validated claim about professional football or managerial intent.
- No source-specific conversion, candidate ingestion, canonical PostgreSQL write, Azure integration, independent football review, independent rights/privacy review or Product Owner acceptance was performed.

## Final verification — code commit `66841ef`

- Full .NET solution tests passed 43, failed 0, skipped 5. The application tests reject invalid ball coordinates and field dimensions outside 90–120 m by 45–90 m; domain tests verify invalid JSON field dimensions fail closed.
- Frontend tests passed 3/3, production build and lint passed; the 120×80 m regression verifies standard metre-based marking sizes. Candidate validation passed 15/15; feed profiler 5/5; operating-model validation passed (12 epics, 59 stories, 28 Council skills, zero cycles).
- Five PostgreSQL integration tests remain skipped due to missing test database. No hosted CI, Azure or Product Owner acceptance is claimed.

## Final verification — code commit `7e21ce5`

- Full .NET solution tests passed 43, failed 0, skipped 5. Application and domain tests reject field dimensions outside 90–120 m by 45–90 m; domain tests verify invalid JSON definitions fail closed.
- Frontend tests passed 3/3, production build and lint passed; the 120×80 m regression verifies standard metre-based marking sizes. Candidate validation passed 16/16; feed profiler 5/5; operating-model validation passed (12 epics, 59 stories, 28 Council skills, zero cycles).
- Five PostgreSQL integration tests remain skipped due to missing test database. No hosted CI, Azure or Product Owner acceptance is claimed.
