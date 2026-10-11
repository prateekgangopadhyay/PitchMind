# EP03-S01 observed-dictionary input validation — 2026-10-11

**Parent:** observed data dictionary PR #84 at exact head `c720db649a9fcbc40dcd2e297723ebd18b817483`.
**Scope:** harden the source-only dictionary generator against malformed nested JSON shapes and invalid aggregate field types. No source bytes, manifest semantics, domain model, API contract, migration, canonical admission, or application data path changed.

## Defect and correction

Before this change, a candidate event with `playerPositions: null` escaped the generator's intended input rejection and raised an uncaught Python `TypeError` (`'NoneType' object is not iterable`). The generator now rejects non-array `playerPositions`, non-object metadata/position rows, non-string event labels, and non-integer sequence/clock/period fields with explicit `ValueError` messages before computing profiles. Empty input is also rejected explicitly.

This is structural validation for the pinned candidate report. It does not establish clock units, period semantics, coordinate units or orientation, player metric definitions/validity, event mappings, score interpretation, or football realism.

## Executed evidence

| Check | Command | Result |
|---|---|---|
| Full Python script test suite | `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s scripts -p 'test_*.py' -v` | **43 passed, 0 failed, 0 skipped**; includes malformed nested rows and aggregate-field type negatives. |
| Dictionary regeneration | `PYTHONDONTWRITEBYTECODE=1 python3 scripts/profile_manu_liv_observed_dictionary.py --output /private/tmp/ep03-s01-dictionary-after-input-guard.md` then `cmp` against the tracked dictionary | Passed; generated content was byte-identical. |
| Candidate report | `PYTHONDONTWRITEBYTECODE=1 python3 scripts/validate_manu_liv_candidate.py --output /private/tmp/ep03-s01-candidate-after-input-guard.json` | Exit 0; status remains `structurally_valid_candidate_blocked_for_canonicalization`, 2,199 events, 48,048 positions, source SHA-256 `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`. |
| Council route validation | `node scripts/validate_operating_model.mjs` | Passed: 12 epics, 59 stories, 28 Council skills, 3 design skills, zero routing cycles, two approval gates. |
| Patch hygiene | `git diff --check` | Passed. |

## Remaining limits

The generated dictionary is complete for its declared measured structural observations (object fields and types, null/missing counts, distinct values, numeric ranges, and selected source-order aggregates) over the manifest-pinned bytes. It is not an authoritative semantic dictionary. No source-owner data dictionary exists. Coordinate units/axes/orientation, clock/period meanings, player metrics/identity, canonical team/player/event mapping, score-side mapping/canonical score truth, football realism, independent rights/privacy review, canonical PostgreSQL ingestion, and Product Owner acceptance remain unresolved. Synthetic provenance and redistribution permission are owner-confirmed; this does not clear the outstanding semantics or quality gates. No Azure operation was performed.
