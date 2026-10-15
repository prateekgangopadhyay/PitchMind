# EP03-S01 candidate period-field audit — 2026-10-16

**Classification:** source-structure comparison only. This does not establish period semantics, select an authoritative field, certify football realism, approve canonical mapping, or accept EP03-S01.

## Source and observed result

- Rebased verification base: current `main` at `62d5892eb34a9a3219ba1e645f4a5b74ed1eeeb4`.
- Verified worktree commit: `78f7961a753e312b781c5ab5977eaa53bc33fac5`; tree: `70cded1c0b265918edceb49a1d1ab03b33434902`.
- Candidate source SHA-256: `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`; manifest status remains `candidate_not_canonical`.
- Quality-audit v1.1.0 found three records with `metadata.period`: all three nested values are JSON strings paired with top-level integer `period`; exact type/value matches: 0; mismatches: 3. At sequence 1037, values differ (`"1"` versus `2`). Examples are capped at 20 and string values at 128 characters.
- The source JSON, source IDs, sequence, event order, clocks, and manifest were not changed. No canonical mapping or persistence was performed.

## Executed verification after rebase

- `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_audit_manu_liv_candidate_quality -v`: 12 passed, 0 failed.
- `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_validate_manu_liv_candidate scripts.test_profile_manu_liv_observed_dictionary -v`: 23 passed, 0 failed.
- Quality-audit CLI regenerated the checked-in JSON report byte-for-byte; an independent assertion confirmed the pinned source hash and period comparison values above.
- Candidate structural validator completed successfully and reported `structurally_valid_candidate_blocked_for_canonicalization` for 2,199 events and 48,048 attached positions, preserving the source sequence, event-ID ledger and ordered-record digest.
- Council operating-model validator passed (12 epics, 59 stories, 28 Council skills, zero dependency cycles); `git diff --check origin/main...HEAD` passed.

## Remaining gates

The source owner has not provided a semantic data dictionary. Period/time authority, coordinate units/orientation, metric definitions, canonical mappings, football review, independent rights/privacy review, technical review, hosted CI, and Product Owner acceptance remain open. EP03-S01 and EP03-S02 remain unverified/unaccepted. No Azure resources or deployments were changed.
