# EP03-S01 observed source dictionary frequency coverage — 2026-10-12

**Branch:** `codex/ep03-s01-dictionary-frequency`
**Stack:** draft child of PR #100, based on its exact published head `7e8a2c554d3c54f77aa0e921b1f5fa83e0e9707e`; PR #100 is a child of observed-dictionary PR #84.
**Tested implementation commit:** `2277f7b48e1ea7f6163c38123003c78ca4164b26`.
**Scope:** complete the candidate structural dictionary’s array-coverage frequency observations and link that evidence from the source profile and EP03-S01 packet. No candidate bytes, manifest policy, domain behavior, event mapping, canonical data, API, database, or Azure configuration changed.

## Measured observation

The pinned source SHA-256 remains `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c` (28,601,013 bytes). The generated dictionary records `playerPositions` array lengths per event: 21 rows in 330 event records, and 22 rows in 1,869 event records. This is the frequency of row counts across event objects. It is not a time-based sampling frequency, a cadence measurement, or evidence of continuous tracking.

The existing field tables report each observed field’s JSON type and occurrence count, missing-key count, explicit-null count, distinct non-null value count, and raw numeric range where applicable. Their interpretations remain structural only. Coordinate units, axes, origin/orientation and attack direction; clock unit and period semantics; player metric definitions/units; event mappings; and stable player identity remain **UNSPECIFIED / UNVERIFIED**. The user confirmed no authoritative source-owner data dictionary exists. Canonical admission remains blocked; this document does not certify football realism, score truth, rights/privacy, or canonical event mappings.

## Checks performed on implementation commit

- `python3 scripts/profile_manu_liv_observed_dictionary.py --output /private/tmp/ep03-s01-observed-dictionary-check.md` — passed and validated pinned manifest size/hash.
- `cmp docs/data/MANU-LIV-OBSERVED-DATA-DICTIONARY.md /private/tmp/ep03-s01-observed-dictionary-check.md` — passed; tracked dictionary matches generated output.
- `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_profile_manu_liv_observed_dictionary -q` — 3 passed, 0 failed.
- `node scripts/validate_operating_model.mjs` — passed (12 epics, 59 stories, 28 Council skills, 3 design skills, 0 cycles, 2 approval gates).
- `git diff --check` — passed.

No broader Python suite, canonical import, PostgreSQL test, football-domain certification, Product Owner acceptance, or cloud operation was performed for this documentation slice. EP03-S01 remains unaccepted.
