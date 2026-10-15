# EP03-S01 candidate period-field audit — 2026-10-16

**Classification:** source-structure comparison only. This does not establish period semantics, select an authoritative field, certify football realism, approve canonical mapping, or accept EP03-S01.

## Source and observed result

- Rebased verification base: current `main` at `62d5892eb34a9a3219ba1e645f4a5b74ed1eeeb4`.
- Verified code commit: `e186944575f63b491e3df7508c0ea2e33ad45574`; tree: `f14615676bb50c8b9d683e14ece30ea3da1aa916`.
- Candidate source SHA-256: `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`; manifest status remains `candidate_not_canonical`.
- Quality-audit v1.1.0 found three records with `metadata.period`: all three nested values are JSON strings paired with top-level integer `period`; exact type/value matches: 0; mismatches: 3. At sequence 1037, values differ (`"1"` versus `2`). Examples are capped at 20 and string values at 128 characters.
- The source JSON, source IDs, sequence, event order, clocks, and manifest were not changed. No canonical mapping or persistence was performed.

## Executed verification after rebase

- `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_audit_manu_liv_candidate_quality -v`: 13 passed, 0 failed, including a regression that serializes malformed non-finite period observations as valid JSON strings with an explicit encoding marker.
- `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_validate_manu_liv_candidate scripts.test_profile_manu_liv_observed_dictionary -v`: 23 passed, 0 failed.
- Quality-audit CLI regenerated the checked-in JSON report byte-for-byte; an independent assertion confirmed the pinned source hash and period comparison values above.
- Candidate structural validator completed successfully and reported `structurally_valid_candidate_blocked_for_canonicalization` for 2,199 events and 48,048 attached positions, preserving the source sequence, event-ID ledger and ordered-record digest.
- Council operating-model validator passed (12 epics, 59 stories, 28 Council skills, zero dependency cycles); `git diff --check origin/main` passed.
- Independent exact-head review identified the non-finite-number serialization edge case; it was fixed and covered by the new regression. Independent exact-head re-review at `72a76fb87dc1d8deaa30187fc9635a012eb04e0e` confirmed the fix and found no further actionable issue; the reviewer did not rerun tests or submit a GitHub review.

## Remaining gates

The source owner has not provided a semantic data dictionary. Period/time authority, coordinate units/orientation, metric definitions, canonical mappings, football review, independent rights/privacy review, technical review, hosted CI, and Product Owner acceptance remain open. EP03-S01 and EP03-S02 remain unverified/unaccepted. No Azure resources or deployments were changed.
