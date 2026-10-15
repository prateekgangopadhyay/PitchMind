# EP03-S01 source player-label consistency audit — 2026-10-16

**Classification:** descriptive observations from the pinned synthetic candidate. This is not a canonical roster identity decision, football-rules verdict, or Product Owner acceptance.

**Source:** `data/simulations/manu-liv-match-dataset.json`, SHA-256 `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`, 2,199 event records and 48,048 event-attached position rows. The owner confirmed synthetic provenance and redistribution permission; independent rights/privacy review remains pending.

**Code commit:** `fdff100d42dd03935f3f9904fc9054e8a4b68ffb`. Quality-audit schema version 1.2.0 adds source-label consistency aggregates and rejects malformed team/player/jersey label types with a bounded audit error. It does not normalize or mutate source fields.

## Observed label consistency

The audit groups only exact source `(teamId, playerName)` and `(teamId, jerseyNumber)` labels. It observed:

- 24 distinct team/player-name labels and 24 distinct team/jersey-number labels.
- Zero team/player-name labels associated with multiple jersey numbers.
- Zero team/jersey labels associated with multiple player names.
- Zero repeated team/jersey slots within an event-attached frame.

The same jersey number remains valid across different teams. These counts do not establish stable player identity, roster legality, or source semantics. The full aggregate is in the [version 1.2.0 machine-readable audit](EP03-S01-FOOTBALL-DATA-QUALITY-AUDIT-2026-10-16.json).

## Verification performed

- `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_audit_manu_liv_candidate_quality scripts.test_validate_manu_liv_candidate scripts.test_profile_manu_liv_observed_dictionary -v` — **38 passed, 0 failed**.
- The audit tests cover ambiguous same-team name/jersey associations, repeated same-team jersey slots, allowed cross-team reuse, and malformed label fields.
- `python3 scripts/audit_manu_liv_candidate_quality.py --output /private/tmp/ep03-s01-player-label-audit-20261016.json` — passed; generated output was copied to this evidence record and verified byte-for-byte.
- `python3 scripts/validate_manu_liv_candidate.py --output /private/tmp/ep03-s01-player-label-validation-20261016.json` — `structurally_valid_candidate_blocked_for_canonicalization`, 2,199 events, 48,048 positions, source sequence 1–2,199 preserved.
- Council operating-model validator and DAG self-test passed; `git diff --check origin/main` passed.

## Limits and remaining gates

This audit records internal source-label consistency only. It does not establish coordinate units/orientation, clock or period semantics, player metric definitions/units, canonical identity mappings, event/score mappings, football realism, or rights/privacy approval. The candidate remains blocked from canonicalization. EP03-S01 remains unverified and unaccepted pending the seeded-fixture and football-domain acceptance, independent rights/privacy review, and Product Owner decision. No application ingestion, PostgreSQL write, API exposure, Azure resource, or deployment was involved.
