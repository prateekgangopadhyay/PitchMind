# EP03-S01 observed-dictionary duplicate JSON key guard — 2026-10-12

**Story:** EP03-S01, Govern a synthetic match scenario.
**Code commit tested:** `5812ffe862bd86260679072d20b8e356c813c71e`.
**Parent:** current EP03-S01 dictionary review head `14ee544ce7ac77d543f772ba730b43fd39f9c811` (PR #143 head).
**Source:** manifest-pinned `data/simulations/manu-liv-match-dataset.json`, SHA-256 `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`.

## Change

Python's default JSON object parser silently keeps the last value when an object repeats a key. The observed-dictionary generator now rejects duplicate keys recursively in both the candidate and manifest JSON, preventing a structural report from silently omitting an earlier member. A baseline reproducer with duplicate `eventType` keys was accepted and rendered only the last label; the regression now verifies the generator raises `ValueError` for duplicate source and manifest members.

This is parser integrity only. It does not reinterpret, convert, normalize, or edit the candidate feed. The generated dictionary remained byte-for-byte identical for the pinned source.

## Exact-head evidence

- `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s scripts -p 'test_*.py' -v` — **46 passed, 0 failed, 0 skipped**.
- `PYTHONDONTWRITEBYTECODE=1 python3 scripts/profile_manu_liv_observed_dictionary.py --output /private/tmp/ep03-s01-strict-json-dictionary-final.md` followed by `cmp` against `docs/data/MANU-LIV-OBSERVED-DATA-DICTIONARY.md` — passed; generated report is byte-identical.
- `PYTHONDONTWRITEBYTECODE=1 python3 scripts/validate_manu_liv_candidate.py --output /private/tmp/ep03-s01-strict-json-candidate-final.json` — exit 0; 2,199 events, 48,048 attached positions; status `structurally_valid_candidate_blocked_for_canonicalization`; source SHA-256 matches the manifest.
- `PYTHONDONTWRITEBYTECODE=1 python3 scripts/audit_manu_liv_candidate_quality.py --output /private/tmp/ep03-s01-strict-json-audit-final.json` — exit 0.
- `node scripts/validate_operating_model.mjs` — passed: 12 epics, 59 stories, 28 Council skills, 3 design skills, zero routing cycles, two approval gates.
- `git show --check --oneline HEAD` — passed.

## Limits and gates

This does not validate coordinate units/axes/orientation, clock/period meaning, player-metric definitions or validity, canonical event/team/player mappings, score authority, football realism, rights/privacy, or Product Owner acceptance. The candidate remains blocked from canonical admission; EP03-S01 remains unaccepted. No application data path, shared domain model, migration, public API, or Azure resource changed.
