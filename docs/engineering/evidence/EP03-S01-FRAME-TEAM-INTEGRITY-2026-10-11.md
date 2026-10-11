# EP03-S01 attached-frame team identity check — 2026-10-11

**Parent:** PR #20 candidate-only validator at `7ad1d2242bda1f8246c26d58632dbc8dc09ca86b`.
**Change commit:** `264a6854e49e81f54bd5a26e773b432eb8ac3939` (`fix(EP03-S01): reconcile event teams with position frames`).
**Scope:** Require each source event's declared `(teamId, teamName)` to occur in that event's attached position frame. This checks relational consistency without inferring player identity, coordinate units, metric meaning, event mapping, or football behavior.

## Executed evidence

| Check | Command | Result |
|---|---|---|
| Candidate validation positive and negative tests | `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_validate_manu_liv_candidate -v` | **22 passed, 0 failed** on `264a6854e49e81f54bd5a26e773b432eb8ac3939`. Includes a negative where both source teams reconcile globally but the event's own position frame omits the event team. Existing cross-team name/jersey collisions remain valid. |
| Pinned source candidate report | `python3 scripts/validate_manu_liv_candidate.py --output /private/tmp/ep03-s01-frame-team-integrity-report.json` | Exit 0. Status remains `structurally_valid_candidate_blocked_for_canonicalization`; 2,199 events, 48,048 attached position records; source SHA-256 `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`. Source IDs/order and 1-based sequence are retained; source clock range 0..5761 is unnormalized. |
| Feed profiler regression | `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_profile_simulation_feed -v` | **5 passed, 0 failed** on the same code tree. |
| Patch hygiene | `git diff --check` | Passed before commit. |

## Remaining limits

The 48,048 position rows are structurally linked to event team identities only at the team level. Player labels and jersey numbers are not certified roster identities. Coordinate units/orientation, player metric units/validity, canonical event mappings, source realism, independent rights/privacy review, PostgreSQL ingestion, and Product Owner acceptance remain unresolved. No Azure resources or deployment were accessed.

## UUID identity review follow-up — 2026-10-11

An independent review found that event and match UUIDs were parsed for syntax validation but compared using their raw strings. UUID spellings that differed only in hexadecimal letter case could therefore bypass duplicate event identity checks or inflate the distinct-match count.

The validator now compares parsed `uuid.UUID` values for uniqueness and distinct-match counts. It leaves source event records untouched; source ID and full-record digests still use the original source strings and order.

**Code commit:** `69fcdefe6397b64e5f39798e7872beaa396bf0b8` on the existing draft PR #77 branch.

| Check | Command | Result |
|---|---|---|
| Candidate structural and identity tests | `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_validate_manu_liv_candidate -v` | **25 passed, 0 failed**. Added case-variant duplicate event IDs, case-variant match IDs with a declared count of two, and original ID spelling preservation in the source ID digest. |
| Pinned source report | `python3 scripts/validate_manu_liv_candidate.py --output /private/tmp/ep03-s01-uuid-identity-report.json` | Exit 0; report remains `structurally_valid_candidate_blocked_for_canonicalization` for 2,199 records and 48,048 positions. |
| Feed profiler regression | `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_profile_simulation_feed -v` | **5 passed, 0 failed**. |
| Council routing/operating model | `node scripts/validate_operating_model.mjs` | Passed: 12 epics, 59 stories, 28 Council skills, 3 design skills, zero cycles, two approval gates. |
| Patch hygiene | `git diff --check` | Passed. |

Canonicalization, coordinate semantics, player metric validity, football realism, independent rights/privacy review, PostgreSQL ingestion, and Product Owner acceptance remain open. No Azure resources or deployment were accessed.
