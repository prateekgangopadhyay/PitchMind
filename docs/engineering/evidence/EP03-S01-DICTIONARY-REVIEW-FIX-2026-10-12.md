# EP03-S01 observed dictionary review-fix evidence — 2026-10-12

## Scope

Independent review of draft PR #84 at exact head `c720db649a9fcbc40dcd2e297723ebd18b817483` found two documentation/reporting defects:

1. The observed dictionary serialized JSON values for distinctness, so numeric `2` and `2.0` were counted separately. Numeric-value distinct counts for player coordinates were therefore overstated as x=963 and y=941 instead of x=961 and y=940.
2. The source profile stated that shot/foul label counts were implausible without an authoritative definition or counting methodology, while the quality audit correctly treated counts as a review prompt only.

No authoritative source dictionary is available. This follow-up does not assert coordinate units/orientation, time semantics, player metric meaning, event mappings, score truth, or football plausibility.

## Change and exact tested revision

Code head: `4e250cea9e42be88dd6006916e13f21c4771a1cc` (parent `7e8a2c554d3c54f77aa0e921b1f5fa83e0e9707e`). Numeric values now compare by numeric equality while booleans remain distinct from numbers; the Markdown defines the counting behavior. The source profile calls the counts a review prompt and says the counts alone do not establish plausibility.

## Executed evidence

- `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s scripts -p 'test_*.py' -v` — 44 passed, 0 failed, 0 skipped.
- `PYTHONDONTWRITEBYTECODE=1 python3 scripts/profile_manu_liv_observed_dictionary.py --output /private/tmp/pitchmind-ep03-updated-dictionary.md` — succeeded; `cmp` with the tracked dictionary passed. Reported coordinate cardinalities are x=961 and y=940.
- `PYTHONDONTWRITEBYTECODE=1 python3 scripts/validate_manu_liv_candidate.py --output /private/tmp/pitchmind-ep03-review-candidate.json` — succeeded; 2,199 events, 48,048 positions, status `structurally_valid_candidate_blocked_for_canonicalization`; coordinate units, player metrics, and canonical mappings remain unverified and football review remains required.
- `git diff --check` — passed on the code head.

Canonical admission and Product Owner acceptance remain pending. No application data path or Azure resource was changed.
