# EP03-S01 source rights-status clarification — 2026-10-12

**Scope:** Correct stale repository wording about the candidate source's provenance and redistribution permission. Documentation-only; this does not implement or accept EP03-S01.

- The repository owner confirmed the `manu-liv-match-dataset.json` feed is synthetic and redistribution is permitted on 2026-10-10. The checked-in candidate manifest records both facts and keeps the source status `candidate_not_canonical`.
- Independent rights/privacy review remains pending. The owner declaration is not a substitute for that review.
- Clock/period semantics, coordinate units/axes/orientation, player metric definitions, canonical mappings, football realism, canonical PostgreSQL admission/replay, and Product Owner acceptance remain unverified or blocked.
- The candidate's observed raw clock maximum is 5,761. Reading this as 96:01 assumes seconds, which are not verified. This candidate is not the separate EP03-S01 acceptance requirement: a fictional, seeded, deterministically regenerated 90-minute fixture.
- Documentation reconciled: simulation feed contract, simulation admission gates, operating model, EP03-S01 packet and execution register. Source bytes, code, API, HLD and EP03-S03 were not changed.

## Observed checks

- `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_validate_manu_liv_candidate scripts.test_profile_manu_liv_observed_dictionary -v` — 22 passed, 0 failed.
- `PYTHONDONTWRITEBYTECODE=1 python3 scripts/validate_manu_liv_candidate.py --output /private/tmp/pitchmind-ep03-s01-rights-profile.json` — completed; report status `structurally_valid_candidate_blocked_for_canonicalization`, 2,199 events, 48,048 attached positions, source SHA-256 `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`, source sequence 1–2,199, raw clock 0–5,761 (unit unverified), independent rights/privacy review `pending`, coordinates unverified, player metrics unvalidated, canonical mappings not approved.
- `node scripts/validate_operating_model.mjs` — passed (12 epics, 59 stories, 28 Council roles, 5 design skills, 0 cycles, 2 approval gates).
- `git diff --check` — passed after the documentation edits.

These are source-structure and repository-contract checks only. No independent rights/privacy or football review, semantic validation, canonical admission, Azure action, or Docker operation was performed.
