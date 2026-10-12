# EP03-S01 strict audit JSON evidence — 2026-10-12

This evidence records local verification of the strict JSON parsing follow-up to the EP03-S01 candidate quality audit. It is tooling evidence only; it does not establish canonical football semantics or acceptance of EP03-S01.

## Changes verified

- The quality audit rejects duplicate object keys in source events, nested metadata, and the manifest.
- The quality audit rejects `NaN`, positive or negative `Infinity`, overflowing floating-point values, and JSON integers over 1,024 decimal digits rather than silently interpreting them. A boundary regression accepts 1,024 digits and rejects 1,025.
- The source profile describes the observed goal rows and score snapshots without claiming which score side maps to which team or that the snapshot is canonical.

## Local commands and results

All commands ran in the local working tree on 2026-10-12.

| Check | Command / comparison | Result |
|---|---|---|
| Focused audit tests | `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_audit_manu_liv_candidate_quality -v` | 13 passed, 0 failed. Includes duplicate-key source and manifest cases, non-standard/overflowing numeric input, and 1,024/1,025-digit boundary cases. |
| Full scripts suite | `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s scripts -p 'test_*.py' -v` | 53 passed, 0 failed. |
| Candidate quality audit | `PYTHONDONTWRITEBYTECODE=1 python3 scripts/audit_manu_liv_candidate_quality.py --output /private/tmp/pitchmind-ep03-strict-audit.json` | Exit 0; generated report compared byte-for-byte with the tracked aggregate JSON. |
| Candidate structural validation | `PYTHONDONTWRITEBYTECODE=1 python3 scripts/validate_manu_liv_candidate.py --output /private/tmp/pitchmind-ep03-strict-candidate.json` | Exit 0; 2,199 events and 48,048 attached positions. Status remains `structurally_valid_candidate_blocked_for_canonicalization`; coordinate units, player metrics, football realism, and canonical mappings remain unverified. |
| Observed dictionary regeneration | `PYTHONDONTWRITEBYTECODE=1 python3 scripts/profile_manu_liv_observed_dictionary.py --output /private/tmp/pitchmind-ep03-strict-dictionary.md` | Output compared byte-for-byte with the tracked observed dictionary. |
| Patch whitespace | `git diff --check` | Passed. |

The change is published as draft PR #164, stacked on PR #80. No canonical import or match API exposure was performed. No Azure resource, deployment, or environment was touched. Independent review is in progress; Product Owner acceptance remains pending.
