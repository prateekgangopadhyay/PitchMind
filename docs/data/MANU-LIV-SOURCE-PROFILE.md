# MANU-LIV candidate source profile

**Source:** `data/simulations/manu-liv-match-dataset.json`, a candidate JSON array. **Measured bytes:** 28,601,013. **SHA-256:** `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`. **Observed dictionary:** [field-by-field structural profile](MANU-LIV-OBSERVED-DATA-DICTIONARY.md). **Status:** `candidate_not_canonical`.

The repository owner confirmed that the source is synthetic and redistribution is permitted (2026-10-10). This is the extent of the owner confirmation recorded here. Independent rights/privacy review remains pending. The confirmation does not establish field semantics, football realism, canonical identity mappings, or that the values represent real players or a real match.

## Observed structure

The pinned file contains 2,199 event objects and 48,048 event-attached player-position objects. It has 19 observed `eventType` labels. Event IDs are distinct in the file; source sequence values are contiguous and ascending from 1 to 2,199. The raw `clockSeconds` values range from 0 to 5,761; there are 11 adjacent equal-clock pairs and no adjacent clock regressions. The source `period` labels are 1 (1,035 event rows) and 2 (1,164 event rows). The coordinate-valued fields have observed ranges listed in the dictionary. These are measurements of the pinned bytes, not football validation.

See the [observed dictionary](MANU-LIV-OBSERVED-DATA-DICTIONARY.md) for every event, metadata, and player-position field: observed JSON types and counts, missing-key and explicit-null counts, distinct non-null values, raw numeric ranges, categorical vocabularies/counts, and source-order checks.

## Interpretation limits and required evidence

No authoritative vendor data dictionary was supplied. Field names and measured values do not supply the missing definitions. The named artifacts below are required evidence; none is provided by this profile.

| Unresolved area | Current evidence | Evidence needed before interpretation or canonical use |
|---|---|---|
| Clock unit and time reference | Raw integer `clockSeconds` values 0–5,761 and period labels 1–2 are observed. | **Source-owner temporal contract:** define clock unit, whether time is elapsed-match or period-local, stoppage-time rules, period semantics, and the meaning/order of half-time and full-time markers. |
| Clock manifest discrepancy | The manifest on the current PR #115 stack says `sourceClockUnit: seconds`; the current main manifest marks the unit `unverified`. This profile does not change either manifest. | **Manifest reconciliation review:** align the stacked manifest with the current main manifest and the source-owner temporal contract. Until then, do not treat the older branch value as evidence of units. |
| Coordinates and pitch frame | Numeric values and ranges are listed in the dictionary; units and geometry are not established by those values. | **Source-owner coordinate specification:** define units/scale, axis names, origin, orientation, attack direction by team/period, pitch dimensions and any transform. **Football frame golden:** verify the specified transform for both teams and halves. |
| Player metric fields | Field names, JSON types, row counts, and raw ranges are observed. | **Source-owner player-metric dictionary:** define each metric, units, calculation window, sampling/cadence, validity/missingness and whether values are wholly synthetic. **Identity specification:** define stable source player identity and roster/correction behavior. |
| Event labels, outcomes, and metadata | Exact source strings, types, presence, nullability and counts are observed and preserved. | **Source event taxonomy and mapping registry:** define each label and outcome, required context, canonical mapping (or explicit unsupported status), and versioned review. Same-name labels do not prove same semantics. |
| Team/player identities and mappings | Source ID strings and labels are present; observed cardinalities are in the dictionary. | **Source identity contract and canonical identity mapping:** define stable IDs, team/player scope, collision/correction handling and reviewed mappings. Preserve original source IDs regardless. |
| Score and score snapshots | Score-like metadata strings and goal-label counts are observed, not certified. | **Score interpretation specification:** define component ordering/team-side mapping, own-goal/disallowed-goal/correction treatment and authority. Canonical score must be derived from accepted, reviewed canonical events. |
| Football realism and tactical suitability | Structural counts only; sparse event-attached positions do not establish continuous tracking or pressure distance. | **Football Analytics and Product Owner review record:** assess match chronology, event frequencies, roster/substitution/card consistency, mechanism evidence and acceptable demo use with explicit counterexamples. |
| Rights and privacy | Owner-confirmed synthetic provenance and redistribution permission are recorded; independent review is pending. | **Independent rights/privacy review record:** assess source rights, use of club/person names and privacy implications of biometric-like fields. Owner confirmation alone does not complete this review. |

## Data preservation boundary

Keep the candidate bytes immutable. During profiling, mapping or transport, preserve each original `eventId`, the source `sequence`, event order, exact raw `clockSeconds` value, `period`, and the source file SHA-256/provenance. Do not reorder, renumber, normalize time, convert coordinates, infer missing facts, or canonicalize from this profile. If a reviewed mapper is later approved, retain raw source values and record all derived values with mapper version and source lineage.

The manifest status is `candidate_not_canonical` and disallows creating canonical events. This structural profile does not approve import, PostgreSQL admission, API reads, or product presentation of score, clock, coordinates, player metrics, or tactical claims. EP03-S02 story acceptance and Product Owner acceptance remain separate and unverified.

**Branch note:** this documentation child is based on PR #115's feature branch. Its ancestor manifest contains the older clock-unit declaration described above; this docs-only change does not edit the JSON manifest or source file. Reconcile that branch with current main before relying on the stacked result.
