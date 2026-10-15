# EP03-S01 candidate football-data quality audit — 2026-10-12

**Classification:** measured structure and cross-field observations from the pinned candidate. This is not a football certification, canonical mapping approval, rights/privacy review, or source semantic dictionary.

**Source:** `data/simulations/manu-liv-match-dataset.json`, 28,601,013 bytes, SHA-256 `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`; candidate manifest v1.0.0. The manifest records owner-confirmed synthetic provenance and redistribution permission on 2026-10-10. Independent rights/privacy review remains pending.

**Machine report:** [all measured aggregates](EP03-S01-FOOTBALL-DATA-QUALITY-AUDIT-2026-10-12.json). The audit reads source data and reports aggregate structure. It does not normalize time or coordinates, infer immutable player identities, map source events to domain events, or certify football realism.

## Executed checks

| Check | Command | Result |
|---|---|---|
| Quality-audit tests | `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_audit_manu_liv_candidate_quality -v` | 9 passed, 0 failed. Includes clock-unit wording, raw coordinate reporting, descriptive-only metric comparisons, exact event-name comparison, source hash validation, score snapshots, source-frame roster comparisons, and report-output alias protection. |
| Candidate structural validator | `PYTHONDONTWRITEBYTECODE=1 python3 scripts/validate_manu_liv_candidate.py --output /private/tmp/pitchmind-candidate-structure-2026-10-12.json` | Exit 0; source status `structurally_valid_candidate_blocked_for_canonicalization`; source, ordered-record, and event-ID ledger digests recorded. |
| Validator and dictionary regression | `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_validate_manu_liv_candidate scripts.test_profile_manu_liv_observed_dictionary -v` | 22 passed, 0 failed. |
| All-event quality audit | `PYTHONDONTWRITEBYTECODE=1 python3 scripts/audit_manu_liv_candidate_quality.py --output docs/engineering/evidence/EP03-S01-FOOTBALL-DATA-QUALITY-AUDIT-2026-10-12.json` | Exit 0; read 2,199 events and 48,048 attached positions, pinned source hash matched. |

## Observed structure and unresolved meaning

- Event IDs are present and distinct for all 2,199 records; source sequence is contiguous 1–2,199. The validator records ordered-record and event-ID ledger hashes. No ID or source order was rewritten.
- The source has period labels 1 and 2 with counts 1,035 and 1,164. `clockSeconds` raw values span 0–5,761, have no adjacent regressions and contain 11 equal adjacent pairs. Sequence 1,036 is a period-2 `Pass` at raw value 2,700; sequence 1,037 is the `HalfTime` marker at the same raw value. The manifest says the clock unit is **unverified**; these numbers do not prove seconds, elapsed-match semantics, stoppage-time rules, or the meaning of the half-time marker.
- Raw coordinate ranges are 2–98 for `pitchX/Y`, `ballX/Y`, and attached player `x/y`; `endX` is 0–100 and `endY` 2–98 across 967 non-null values each. The source manifest leaves physical unit, axes, origin, orientation, and pitch dimensions unspecified. No metre conversion, team attack direction, mirror-invariance check, or distance calculation is supported.
- Seven player metric fields are populated on all 48,048 attached rows. Raw ranges include `speed` 0.6–33.0, `distanceCovered` 0–11.81, `heartRate` 125–196, `heatIndex` 0.19–1.0, `passAccuracy` 72–95, `stamina` 63–100, and `touches` 0–289. Their definitions, units, cadence, validity, identity stability and real-world status are unverified; these values must not be described as measured player performance or biometrics.
- There are 19 source event labels. Only six exactly match current enum names (`Foul`, `FullTime`, `Goal`, `KickOff`, `Pass`, `Shot`); exact spelling is not semantic equivalence. Manifest mapping status remains `not_approved`; no canonical mapper is implied.
- Counts include 124 `Shot`, 254 `Foul`, 252 `PressureApplied`, and 247 `Tackle` rows. Definitions and counting rules are missing, so these counts flag a need for football and Product review and do not alone establish plausibility or implausibility.
- Position arrays are attached to events and contain 22 rows for 1,869 events and 21 rows for 330. Team/row-count changes around the one red card and two substitutions are internally consistent in adjacent records. These checks do not validate roster identity or the football rules used to produce the fixture.
- Six goal rows have parseable score strings with five adjacent one-step nondecreasing transitions; the full-time score metadata is `3-3`. Component order, event adjudication, own-goal handling, corrections, and canonical score authority remain unverified.

## Admission decision

**Structural audit: passed for these checks. Canonical admission: blocked.** Continue to preserve raw event IDs, sequence, raw clock values, coordinate values and provenance. Before mapping or presenting football facts, obtain definitions for clock/period semantics, coordinate frame and attack orientation, player metric fields and identity, all event labels and score authority; complete independent football and rights/privacy review. Synthetic confirmation and redistribution permission establish owner-provided provenance only, not those semantics or reviews.

## Period-field integrity follow-up — 2026-10-16

Quality-audit version 1.1.0 adds an exact JSON type-and-value comparison for top-level `period` and nested `metadata.period`. On the same pinned source bytes, the audit found three records with the nested field, zero exact type/value matches, and three mismatch examples: sequences 1, 1037 and 1038. All nested fields are strings and all top-level fields are integers; at sequence 1037, the displayed values additionally differ (`"1"` vs `2`). This records structural inconsistency only; it does not decide which field controls period interpretation. The generated machine report was refreshed at [the JSON evidence file](EP03-S01-FOOTBALL-DATA-QUALITY-AUDIT-2026-10-12.json). Focused tests and exact-source regression results are recorded in [the 2026-10-16 evidence note](EP03-S01-PERIOD-FIELD-AUDIT-2026-10-16.md).
