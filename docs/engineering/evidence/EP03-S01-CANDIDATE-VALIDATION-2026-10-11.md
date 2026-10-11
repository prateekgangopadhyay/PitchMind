# EP03-S01 candidate feed validation — 2026-10-11

**Initial code head:** `d77695f8bf14d550d8cbb583b2eeeb9bd8c99a09` (`feat(EP03-S01): validate source candidate without ingestion`).
**Scope:** manifest-pinned, offline structural and source-conservation checks. This does not admit canonical football facts, certify football realism, or satisfy EP03-S01's complete 90-minute fixture and Product Owner acceptance.

## Input and observed report

- Candidate: `data/simulations/manu-liv-match-dataset.json`.
- Manifest: `data/simulations/manu-liv-candidate-manifest.json`, version `1.0.0`, status `candidate_not_canonical`.
- Source size and SHA-256: 28,601,013 bytes; `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`.
- Validator output status: `structurally_valid_candidate_blocked_for_canonicalization`.
- Observed counts: 2,199 events, one match, two teams, and 48,048 event-attached position records.
- Preserved source sequence: 1..2199. Source `clockSeconds`: 0..5761, retained without normalization. The report's source event-ID ledger digest is `abd6e8ebcd9f7812be5633dd7e600d1b350a3fb3b3ddad6f678372b2be0a4539`; complete ordered-record digest is `8c590d33ccaddc3809b74540fad78186c03e88ab5d3d9b2fbf62238de0e97cd3`.
- Coordinates are reported as raw numeric ranges only. Coordinate units/orientation and pitch dimensions remain unverified; player metrics remain unvalidated; canonical mappings remain unapproved; football review remains required.

## Executed checks

| Requirement | Command | Result |
|---|---|---|
| Candidate source validator positive/negative coverage | `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_validate_manu_liv_candidate -v` | **20 passed, 0 failed** on code head `d77695f8bf14d550d8cbb583b2eeeb9bd8c99a09`. Includes the pinned feed, event/source digest stability, duplicate IDs/JSON keys, sequence/clock/period errors, malformed manifests, unsafe numeric input, and team-scoped duplicate player labels. |
| Full pinned candidate report | `python3 scripts/validate_manu_liv_candidate.py --output /private/tmp/ep03-s01-candidate-validation-restacked.json` | Exit 0; status and counts listed above. The report is outside the repository. |
| Source-agnostic profiler regression | `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_profile_simulation_feed -v` | **5 passed, 0 failed** on the same code head. |
| Profiler run | `python3 scripts/profile_simulation_feed.py data/simulations/manu-liv-match-dataset.json --output /private/tmp/ep03-s01-profile-restacked.json` | Exit 0; status `profiled_not_validated`; output kept outside the repository. |
| API/contract and production-fixture architecture guards | `dotnet test tests/PitchMind.Contracts.Tests/PitchMind.Contracts.Tests.csproj -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false --nologo`; `dotnet test tests/PitchMind.Architecture.Tests/PitchMind.Architecture.Tests.csproj -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false --nologo` | **9 contract tests passed; 20 architecture tests passed**, no failures or skips, on the candidate-only branch. These compile the unchanged fail-closed API and test-only fixture boundary from the current source tree. The isolated checkout used local restored metadata; its full solution restore did not complete, so a clean full solution build/test is not claimed. |
| Council routing | `node scripts/validate_operating_model.mjs` | Passed: 12 epics, 59 stories, 28 Council skills, zero routing cycles. |
| Patch hygiene | `git diff --check ecd5df20d41b58a0b219b5b18824fab693a3ef5f...HEAD` | Passed. |

## Independent review correction — 2026-10-11

- Independent review found that comparing resolved paths did not detect an output hardlink to the source or manifest. A focused temporary-file reproduction showed the CLI could overwrite the input inode.
- Fixed by rejecting both resolved-path aliases and existing same-file aliases, and added regression coverage for hardlinks to both source and manifest. The CLI now returns 2 and leaves both inputs unchanged.
- On follow-up code head `136355be38a1d4ae845f551d4be58528ce716a70`, candidate validator tests passed **21/21**, including the new hardlink case; source profiler tests passed **5/5**; the pinned report command and Council DAG self-test passed; `git diff --check` passed.
- The 9 contract and 20 architecture test results above are from code head `d77695f8bf14d550d8cbb583b2eeeb9bd8c99a09`; the hardlink fix changes only the offline validator and its Python tests. Full solution validation, PostgreSQL, hosted CI, football semantics and PO acceptance remain unverified.

## Architectural and truth boundary

- The Python validator is report-only and is not an application runtime adapter.
- No `src/`, frontend runtime, public API DTO, canonical event mapping, database migration, or broker contract changes are included.
- Ordinary match API routes remain fail-closed with `503 MATCH_DATA_UNAVAILABLE`; the fictional `SyntheticFixtureGenerator` remains in `PitchMind.TestFixtures`.
- No coordinate conversion, player identity inference, event mapping, score certification, or tactical inference occurs.
- The owner confirmed synthetic provenance and redistribution permission. Independent rights/privacy review, coordinate/time semantics, player-metric validity, football-realism review, and Product Owner acceptance remain pending.
- No PostgreSQL integration, hosted CI, Azure validation, Azure resource operation, or deployment is claimed.
