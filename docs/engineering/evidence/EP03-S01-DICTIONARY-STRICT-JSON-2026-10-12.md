# EP03-S01 observed dictionary strict-JSON review follow-up — 2026-10-12

## Scope

Independent review of draft PR #143 noted that Python's default JSON parser accepts the non-standard `NaN` and `Infinity` constants, and that the numeric-cardinality regression did not exercise the `False == 0` / `True == 1` equality collision.

## Change and exact tested revision

Code commit: `3e252336d6c725c18c84399eab05de45df521a53` (tree `3ad169c3eb93d389e6618ecffaf6a2d9be772c58`), based on PR #143 head `bec4b6b81f966de569638b9472d66ed209264678`. The observed dictionary generator now rejects non-standard JSON constants in both its candidate and manifest inputs. The cardinality test uses `0`, `0.0`, `False`, `1`, and `True`, expecting four values: numeric equality is preserved while booleans remain separate.

## Executed evidence

- `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s scripts -p 'test_*.py' -v` — 45 passed, 0 failed, 0 skipped.
- Regenerated the observed dictionary to a temporary output and compared it byte-for-byte with the tracked file — passed; the pinned report did not change.
- `git diff --check` — passed.

This hardening validates JSON grammar and cardinality reporting only. The generated document remains an observed structural dictionary, not an authoritative semantic data dictionary. Coordinate units/orientation, clock semantics, player metric definitions/units, event mappings, and football realism remain unverified. Canonical admission and Product Owner acceptance remain pending. No application data path or Azure resource was changed.
