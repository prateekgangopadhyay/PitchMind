# EP03-S01 candidate source validation evidence — 2026-10-09

**Scope:** Local structural and source-conservation validation of the owner-confirmed candidate JSON. This is not canonical ingestion, football approval, independent rights/privacy review, or Product Owner acceptance.

## Pinned source

- File: `data/simulations/manu-liv-match-dataset.json`
- SHA-256: `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`
- Byte size: 28,601,013
- Candidate manifest: `data/simulations/manu-liv-candidate-manifest.json`, version `1.0.0`, status `candidate_not_canonical`
- Python runtime: 3.13.7

## Executed checks

| Requirement | Command | Observed result |
|---|---|---|
| Candidate validator positive and negative cases | `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_validate_manu_liv_candidate -v` | 13 passed, 0 failed on code commit `d7c61e7`. Cases cover source-ledger reproducibility, full-record digest sensitivity, duplicate IDs and JSON keys, integer sequence enforcement, sequence gaps, clock/period regression, manifest-pinned halftime/fulltime marker chronology, malformed manifest types, manifest drift, non-finite/overflowing coordinates and pinned-candidate reconciliation. |
| Full candidate data integrity report | `python3 scripts/validate_manu_liv_candidate.py --output /private/tmp/ep03-s01-candidate-validation-final.json` | Exit 0 at code commit `d7c61e7`; status `structurally_valid_candidate_blocked_for_canonicalization`; 2,199 events, 1 match, 2 teams, 48,048 attached position records; source sequence 1..2199 and source clock 0..5761 seconds. |
| Repeatability | Run the validator twice against the pinned source and manifest | Both runs produced source event-ID ledger SHA-256 `abd6e8ebcd9f7812be5633dd7e600d1b350a3fb3b3ddad6f678372b2be0a4539` and complete ordered-record SHA-256 `8c590d33ccaddc3809b74540fad78186c03e88ab5d3d9b2fbf62238de0e97cd3`. Object key order is normalized for this digest; the pinned source SHA preserves exact byte-level identity. |
| Previous aggregate quality gate | `bash scripts/validate-all.sh` | Historical pre-rebase evidence only. It is not relied on as verification of code commit `d7c61e7`; the current commit was checked with the individually listed commands below. |

The historical aggregate run is not a hosted CI/Azure DevOps run. PostgreSQL integration was skipped; no canonical database ingest exists in this story branch.

## Observed data quality and limits

- Event counts include 124 `Shot` and 254 `Foul`; no unapproved professional-football frequency threshold is applied. Football domain review is required.
- Raw coordinate values range from 0 to 100 in some event fields. Units, axis orientation and pitch dimensions remain unverified. No metre conversion or pressure-distance calculation occurs.
- Player metric fields are structurally numeric but their units, provenance and validity are unverified. They are not marked as observed performance.
- All 19 source event kinds are retained as source strings. No canonical event mapping or event rewriting is applied.
- Sequence, source event IDs, source clock, period and complete event records are hashed and reported without normalization. The complete record digest canonicalizes JSON object key ordering only; the source SHA preserves exact bytes. The tool does not write a derived feed, canonical ledger, PostgreSQL row or API response.
- The manifest records owner-confirmed synthetic provenance and redistribution permission; independent rights/privacy review remains pending.
- Product Owner review of realism, score/goal semantics and a complete acceptable replay remains pending. The source runs through 96:01 and is not certified as the requested PO-approved 90-minute scenario.

## Rebased-head verification — 2026-10-09

At rebased head `7f1727175606d5c1e497f5ada323de4c467d7aa3`, the candidate validator tests passed 11/11, feed profiler tests passed 5/5, the validator again returned `structurally_valid_candidate_blocked_for_canonicalization` with the pinned source hash and stable ledger digests, and the operating-model validator, DAG self-test, design-skill self-test and `git diff --check` passed. The integrated `bash scripts/validate-all.sh` was attempted on this rebased head but did not emit output or complete during the observed run; it is not counted as a pass. The earlier full-gate result above belongs to the pre-rebase code head.

## Independent re-verification — code commit `d7c61e709f43a113db88eb7bbd2972f1b5e9d8fd`

| Requirement | Command | Observed result |
|---|---|---|
| .NET Release build | `dotnet build PitchMind.slnx --no-restore -c Release -m:1 -nr:false --verbosity:minimal -p:NuGetAudit=false` | Succeeded, 0 warnings, 0 errors. |
| .NET solution tests | `dotnet test PitchMind.slnx -c Release -m:1 -nr:false --verbosity:minimal -p:NuGetAudit=false` | 31 passed, 0 failed, 5 skipped. All five skipped tests require a PostgreSQL test database, which was not configured. |
| Frontend tests/build/lint | `npm test && npm run build && npm run lint` from `frontend/` | 3 tests passed; TypeScript/Vite production build succeeded; ESLint exited 0. The test uses a non-default 3,600-second fixture with 1,800-second halftime and a 120×80 m pitch, and checks slider/pitch metadata rendering. |
| Feed profiler tests | `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s scripts -p 'test_profile_simulation_feed.py' -v` | 5 passed, 0 failed. |
| Operating-model graph validation | `node scripts/validate_operating_model.mjs` | Passed: 12 epics, 59 stories, 28 Council skills, zero routing cycles. |
| Whitespace/error check | `git diff --check` | Passed. |

These checks were run locally at `d7c61e7` before the evidence-only documentation update. `bash scripts/validate-all.sh`, hosted CI, Product Owner acceptance and independent human review were not observed at this head. The local .NET test command used `NuGetAudit=false` because this environment could not reach NuGet's vulnerability endpoint; this did not alter project or repository settings.

## Follow-up verification — code commit `4acc702`

- Candidate validator tests passed 14/14. Added a manifest type check and a default-deny rule for period-two events sequenced before halftime; the candidate manifest explicitly permits only a same-clock `Pass` to preserve the pinned source sequence. The regression test rejects the ordering without the exception, accepts the observed `Pass`, and rejects a same-clock `Shot`.
- The source report again returned `structurally_valid_candidate_blocked_for_canonicalization` for 2,199 events and 48,048 attached positions. Byte SHA `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`, source event-ID ledger SHA `abd6e8ebcd9f7812be5633dd7e600d1b350a3fb3b3ddad6f678372b2be0a4539`, and ordered-record SHA `8c590d33ccaddc3809b74540fad78186c03e88ab5d3d9b2fbf62238de0e97cd3` remained unchanged.
- Full .NET solution tests passed 35, failed 0, skipped 5. Skips are PostgreSQL integration tests requiring an unconfigured test database. Frontend tests passed 3/3; production build and lint exited 0. Feed profiler tests passed 5/5. Operating-model validation passed for 12 epics, 59 stories, 28 Council skills and zero cycles. `git diff --check` passed.
- The follow-up review requested ball-coordinate validation and field-marking dimension fixes. Those changes were included and verified; a fresh independent code re-review is still pending. No hosted CI, PostgreSQL integration, Product Owner acceptance or Azure validation is claimed.

## Final verification — code commit `66841ef`

- Candidate validator suite passed 15/15. Required halftime/fulltime marker clock+period declarations cannot be omitted; the pinned same-clock period-two exception remains limited to `Pass`.
- Pinned candidate report exited 0 with status `structurally_valid_candidate_blocked_for_canonicalization`: 2,199 events; 48,048 attached positions; source SHA `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`; event-ID ledger SHA `abd6e8ebcd9f7812be5633dd7e600d1b350a3fb3b3ddad6f678372b2be0a4539`; ordered-record SHA `8c590d33ccaddc3809b74540fad78186c03e88ab5d3d9b2fbf62238de0e97cd3`. Canonical mappings, coordinate units and player metrics remain unapproved/unverified; football and independent rights/privacy reviews remain pending.
- Full .NET solution tests passed 43, failed 0, skipped 5 (PostgreSQL integration requires an unconfigured test database); frontend 3/3 tests, production build and lint passed; feed profiler 5/5; operating-model validator passed (12 epics, 59 stories, 28 Council skills, zero cycles); `git diff --check` passed.
- Independent review of the previous head identified the missing-marker and pitch-range gaps; fixes are included. A fresh review of this final head is in progress. No hosted CI, PostgreSQL integration, Product Owner acceptance or Azure validation is claimed.

## Final verification — code commit `7e21ce5`

- Candidate validator suite passed 16/16. Two-period manifests must declare exactly one HalfTime and FullTime event count and pin each marker clock+period; tests reject omission of either marker and simultaneous removal of the count plus marker. The same-clock exception remains limited to `Pass`.
- Pinned candidate report exited 0 with status `structurally_valid_candidate_blocked_for_canonicalization`: 2,199 events; 48,048 attached positions; source SHA `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`; event-ID ledger SHA `abd6e8ebcd9f7812be5633dd7e600d1b350a3fb3b3ddad6f678372b2be0a4539`; ordered-record SHA `8c590d33ccaddc3809b74540fad78186c03e88ab5d3d9b2fbf62238de0e97cd3`. Canonical mappings, coordinate units and player metrics remain unapproved/unverified; football and independent rights/privacy reviews remain pending.
- Full .NET solution tests passed 43, failed 0, skipped 5 (PostgreSQL integration requires an unconfigured test database); frontend 3/3 tests, production build and lint passed; feed profiler 5/5; operating-model validator passed (12 epics, 59 stories, 28 Council skills, zero cycles); `git diff --check` passed.
- A fresh independent re-review of this final head completed and found no code blocker; formal GitHub review and Product Owner acceptance remain open. No hosted CI, PostgreSQL integration, Product Owner acceptance or Azure validation is claimed.

## Follow-up hardening — local working tree after reviewed head `112fd06`

- A fresh adversarial check found that a JSON number such as `1e999` in uninterpreted metadata could decode to infinity and escape the validator's intended stable error handling. The strict JSON parser now rejects floating-point overflow as a `CandidateValidationError`; a regression test covers this opaque-metadata path.
- Candidate validator suite passed 17/17; feed profiler tests passed 5/5. The full .NET suite (43 passed, 0 failed, 5 skipped), frontend (3/3 tests, build, lint), and operating-model validation were previously run against `112fd06`; this follow-up only changes the Python validator, its Python tests, and evidence documents, so those prior results are not represented as rerun at the follow-up working tree.
- The pinned candidate report remains `structurally_valid_candidate_blocked_for_canonicalization` with 2,199 events and 48,048 positions. Source, event-ID ledger, and ordered-record digests are unchanged from the report above. Coordinate units, player-metric semantics, canonical mappings, football realism, and independent rights/privacy approval remain outstanding.
- The new hardening has not yet received an independent review or formal GitHub review. Product Owner acceptance, PostgreSQL-backed integration, and Azure validation/deployment are not claimed.

## Parser-boundary follow-up — local working tree after `0579f69`

- Independent review confirmed the floating-point overflow fix and identified a second raw parser failure for integers beyond Python's supported conversion length. The parser now converts that `ValueError` to `CandidateValidationError`; a 5,000-digit integer in uninterpreted metadata is covered by a regression test.
- Candidate validator suite passed 18/18; feed profiler tests passed 5/5; `git diff --check` passed. Candidate source validation continues to return `structurally_valid_candidate_blocked_for_canonicalization`; source/ledger/ordered-record digests remain unchanged.
- Independent review of this integer-boundary follow-up, formal GitHub review, and Product Owner acceptance are pending. Prior .NET/frontend/operating-model checks predate these Python-only changes and are not claimed as rerun here.
