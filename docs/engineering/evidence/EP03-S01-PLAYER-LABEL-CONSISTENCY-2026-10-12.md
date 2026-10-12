# EP03-S01 source player-label consistency audit — 2026-10-12

**Classification:** aggregate observations from the pinned synthetic candidate JSON. This report is not a canonical roster identity decision, a football-rules verdict, or Product Owner acceptance.

**Source:** `data/simulations/manu-liv-match-dataset.json`, SHA-256 `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`, 2,199 event records and 48,048 event-attached position rows. The owner confirmed synthetic provenance and redistribution permission; independent rights/privacy review remains pending.

**Code head:** `322c568afe7a34ded78b1a8a54dfffdd1d37a5c9`. The audit report schema is version `1.1.0`; the machine-readable aggregate output is in [the football-data audit report](EP03-S01-FOOTBALL-DATA-QUALITY-AUDIT-2026-10-11.json).

## Observed label consistency

The audit groups labels only by exact source `teamId`, `playerName`, and `jerseyNumber` values. Across all source position rows it observed:

- 24 distinct `(teamId, playerName)` labels and 24 distinct `(teamId, jerseyNumber)` labels.
- Zero team/name labels associated with multiple jersey numbers.
- Zero team/jersey labels associated with multiple player names.
- Zero repeated team/jersey slots within an individual event-attached frame.

The audit intentionally permits the same jersey number across opposing teams. It reports source-label relationships only; names and shirt numbers do not establish canonical player IDs or rules-compliant rosters. Substitution/card rules, identity stability, provider identifiers, and correction behavior remain unverified.

## Verification performed

- `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s scripts -p 'test_*.py' -v` — **45 passed, 0 failed**.
- The football-domain regression includes ambiguous same-team name/jersey associations, a repeated same-team shirt number in one frame, and legitimate cross-team shirt-number reuse.
- `python3 scripts/validate_manu_liv_candidate.py data/simulations/manu-liv-match-dataset.json --output /private/tmp/ep03-roster-label-candidate-check.json` — completed; status `structurally_valid_candidate_blocked_for_canonicalization`, 2,199 events, source sequence 1–2,199 preserved.
- Regenerated the full quality-audit JSON to `/private/tmp` and compared it byte-for-byte with the tracked report — passed.
- `node scripts/validate_operating_model.mjs` — passed (12 epics, 59 stories, 28 Council skills, 0 cycles, 2 approval gates).
- `git diff --check` — passed.

## Limits and remaining gates

This audit adds descriptive evidence about internal source-label consistency. It does not validate coordinate units/orientation, clock or period semantics, player metric definitions/units, event or score mappings, player identity, football realism, or rights/privacy. The candidate remains blocked from canonicalization. EP03-S01 remains unaccepted pending the original fixture/domain review and Product Owner acceptance. No application ingestion, PostgreSQL write, API exposure, Azure resource, or deployment was involved.
