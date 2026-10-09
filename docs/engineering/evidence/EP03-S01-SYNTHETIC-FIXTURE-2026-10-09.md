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

- The generated fixture has period 1 and period 2 markers, a 90-minute full-time event, two goals resulting in 1–1, one yellow card and two substitutions.
- Tracking frames contain 22 active players, apply configured substitutions, and reverse attack direction at half time.
- Definition validation fails closed for a missing UTC start, null collections/players, mismatched event period/clock, extra or duplicate match markers, invalid active-player bounds and overrides for inactive players.
- Existing positive and negative pressing-sequence and replay tests passed. This is a scripted synthetic evidence scenario, not a validated claim about professional football or managerial intent.
- No source-specific conversion, candidate ingestion, canonical PostgreSQL write, Azure integration, independent football review, independent rights/privacy review or Product Owner acceptance was performed.
