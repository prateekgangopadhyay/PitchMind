# EP03-S01 candidate field dictionary evidence — 2026-10-13

## Scope and source identity

This is evidence for a candidate-data governance slice, not acceptance of EP03-S01. The dictionary is generated from the repository candidate feed and is pinned by its manifest.

- Source: `data/simulations/manu-liv-match-dataset.json`
- SHA-256: `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`
- Size: 28,601,013 bytes; structural validator observed 2,199 events and 48,048 attached position objects.
- Validator result: `structurally_valid_candidate_blocked_for_canonicalization`.
- Sequence and source record order were preserved; raw clock values were not normalized. The source clock unit is recorded as `unverified`.

## Implemented

- Added a reproducible structural dictionary generator and focused tests.
- The dictionary records observed JSON types, field presence/nullability, numeric ranges and distinct-value counts for event, metadata and attached position objects.
- Counts and ranges are derived from the pinned bytes at generation time. Duplicate JSON object keys, source hash/size drift and output aliases are rejected.
- Updated the manifest, source profile, simulation-feed contract, API source note and EP03-S01 packet to avoid asserting unknown clock semantics and to link the dictionary.
- The dictionary does not define units, coordinate axes/orientation, period/added-time semantics, player metric meaning, canonical mappings, football realism or rights/privacy status. It does not enable canonical ingestion or match API reads.

## Executed checks

| Command | Result |
|---|---|
| `python3 -m unittest scripts.test_generate_manu_liv_field_dictionary -v` | 5 passed, 0 failed |
| `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_validate_manu_liv_candidate -v` | 22 passed, 0 failed |
| `python3 -m unittest scripts.test_profile_simulation_feed -v` | 5 passed, 0 failed |
| `python3 scripts/generate_manu_liv_field_dictionary.py` | Generated dictionary for the pinned SHA above |
| `python3 scripts/validate_manu_liv_candidate.py --output /private/tmp/pitchmind-ep03-s01-candidate-final.json` | Exit 0; report status `structurally_valid_candidate_blocked_for_canonicalization` |
| `node scripts/validate_operating_model.mjs --self-test` | Passed dependency DAG self-test |
| `git diff --check` | Passed |

## Remaining gates

The repository-derived dictionary is not an authoritative source definition. Coordinate units and orientation, clock basis and marker semantics, player metric definitions/units/validity, canonical identity and event mappings, football review, independent rights/privacy review, complete 90-minute fixture behavior and Product Owner acceptance remain open. No .NET suite, PostgreSQL integration, Azure operation or deployment was performed for this documentation/data-governance-only change. EP03-S01 remains unaccepted.


## Restack and generator review follow-up

The child was restacked locally on the current EP03-S01 feed-profile parent after its branch had diverged. Conflict resolution retained the parent's corrected manifest contract (`sourceClockField` plus `sourceClockUnitStatus: unverified`) and preserved the raw clock values. The source profile now distinguishes the existing curated inventory from the generated structural distribution report.

An independent exact-head review found that the generator's nondecreasing-clock statement was not computed. The generator now derives the statement only when every record has an integer clock and the values are ordered; a decreasing-clock fixture verifies it does not claim that result otherwise. On the final restacked head, the generator suite passed 5 tests, candidate validator suite passed 22, and source profiler suite passed 5. The dictionary regenerated from source SHA-256 `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`; its checked-in output matches byte for byte. Council DAG self-test and diff check passed. The first re-review identified a source-profile sentence that treated raw `5761` as a meaningful clock limit; the sentence now explicitly requires preserving raw values until source semantics are approved. A final independent exact-head review of code/doc head `6cf58b028bff0b708675e2cd36e907754b6863d4` found no actionable P0–P2 issues. The reviewer did not run tests or submit a GitHub review. Test results were rerun on implementation head `618bd9d20234e0ceb89e3b111dd5a01d58d78d0c`; the intervening `6cf58b0` commit changed only documentation/evidence. EP03-S01 remains unaccepted; no source semantics, canonical mappings, Azure integration, or deployment are claimed.
