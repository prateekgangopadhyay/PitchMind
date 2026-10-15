# Manchester United–Liverpool candidate source profile

**Source:** `data/simulations/manu-liv-match-dataset.json`, pinned by [candidate manifest](../../data/simulations/manu-liv-candidate-manifest.json) v1.0.0. **Inspection:** 2026-10-12.
**Status:** structural inspection of a candidate synthetic JSON feed. The repository owner confirmed synthetic provenance and redistribution permission on 2026-10-10. Independent rights/privacy review is pending. This is not canonical football data, a football-approved simulation, or a verified Azure feed.

## Observed structure

| Property | Observed value |
|---|---|
| Root and size | JSON array; 28,601,013 bytes |
| Records | 2,199 event-shaped objects; 48,048 event-attached player-position objects |
| Source IDs and sequence | 2,199 distinct event IDs; sequence 1–2199, contiguous and ascending |
| Clock field | `clockSeconds` integer values 0–5761; 11 adjacent equal pairs, no adjacent regressions |
| Period labels | 1: 1,035 records; 2: 1,164 records |
| Event vocabulary | 19 distinct labels; see the complete [observed data dictionary](MANU-LIV-OBSERVED-DATA-DICTIONARY.md) |
| Coordinate fields | Observed coordinate values range from 0 to 100; units, axes, origin, orientation and pitch dimensions are unverified |
| Player-position rows | Event-attached; this does not establish dense or continuous tracking |
| Provenance | Manifest records owner-confirmed synthetic provenance and redistribution permission; independent rights/privacy review is pending |

These measurements describe the pinned file only. The [observed data dictionary](MANU-LIV-OBSERVED-DATA-DICTIONARY.md) lists field types, missing and null counts, numeric ranges, cardinalities, event/outcome counts, and source-order checks. The [candidate football-data quality audit](../engineering/evidence/EP03-S01-FOOTBALL-DATA-QUALITY-AUDIT-2026-10-12.md) records aggregate metric, score, event-type, and substitution/card-frame observations while keeping their meaning explicitly unverified.

An additional structural inconsistency is present in `metadata.period`: it appears on only three records, and at source sequence 1037 (`HalfTime`, raw `clockSeconds` 2700) the metadata string is `"1"` while the top-level integer `period` is `2`. The two other records with `metadata.period` match the top-level value. This does not establish which field is authoritative; both raw values and their original records must be preserved until a source definition and football review resolve the conflict. The source profiler does not currently emit this cross-field discrepancy automatically.

## Semantics and football review

No authoritative vendor data dictionary was supplied. Field names and observed values do not establish what clocks, coordinates, player metrics, score snapshots, event labels or identities mean. In particular:

- Clock unit and semantics, period behavior, and stoppage-time interpretation are **unverified**.
- Coordinate units, axis convention, origin, attack direction, pitch dimensions and transforms are **unverified**. Do not convert positions to metres or infer distances.
- Player metric definitions, units, sampling windows, validity, identity stability and real-world status are **unverified**. Do not present them as measured performance or biometrics.
- All 19 event labels are preserved as source labels; source-to-domain mappings and score authority are **not approved**.
- Event-attached positions do not prove continuous tracking or pressing distance. Football realism and event-frequency suitability require Football Analytics and Product review.
- Preserve source event IDs, order, sequence, raw clock values and provenance. Do not rewrite the source to make it fit current domain assumptions.

The manifest is marked `candidate_not_canonical` and explicitly disallows canonical event creation. The source-specific validator checks structural conservation; it does not canonicalize, import, persist or enable match API reads. Its evidence is recorded in [EP03-S01 candidate validation evidence](../engineering/evidence/EP03-S01-CANDIDATE-VALIDATION-2026-10-12.md).

## Implementation gates

1. Obtain reviewed definitions for clock and period semantics, coordinate frame and pitch dimensions, player metric fields, identities, event mappings and score interpretation.
2. Complete independent rights/privacy review and football-domain review of event frequencies and simulation suitability.
3. Keep raw input and any future derived/demo subset auditable; document any filtering or interpretation without mutating source records.
4. Admit canonical records only through a reviewed, versioned mapper and PostgreSQL transaction after the above gates pass.
5. Keep API and UI match reads fail-closed until canonical PostgreSQL data and source mappings are verified. Do not hardcode match facts in C# or React.
6. Any later display must label synthetic context and avoid presenting unverified clock, score, player metrics, or tactical conclusions as facts.

**Implemented evidence:** the repository contains a source-agnostic JSON profiler, source-pinned structural validator and the [observed data dictionary](MANU-LIV-OBSERVED-DATA-DICTIONARY.md), each with standalone checks or evidence linked from the story register. Source-specific canonical import, PostgreSQL admission/replay and Product Owner acceptance remain unverified.
