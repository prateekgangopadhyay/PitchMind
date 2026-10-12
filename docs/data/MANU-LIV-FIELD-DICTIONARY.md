# Manchester United–Liverpool candidate source field dictionary

**Scope:** observed JSON structure for the owner-confirmed synthetic candidate feed. This dictionary records field names, JSON shapes and observed ranges. It does not certify football meaning, canonical mappings, measurement units, identity resolution, or source quality. Field names such as `clockSeconds` are source labels; their meaning is not assumed.

**Pinned input:** `data/simulations/manu-liv-match-dataset.json`  
**SHA-256:** `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`  
**Manifest:** `data/simulations/manu-liv-candidate-manifest.json`, version `1.0.0`, status `candidate_not_canonical`.

## Event record

| Source field | JSON shape / observed values | Meaning and handling |
|---|---|---|
| `eventId` | String; UUID-shaped values | Preserve byte-source value and order. Stability/identity authority is not independently defined. |
| `matchId` | String; one distinct value observed | Preserve as source label. No canonical match mapping is approved. |
| `sequence` | Integer; contiguous values 1–2199 | Source sequence is preserved; no zero-based remapping is performed. |
| `clockSeconds` | Integer; monotone raw values 0–5761 | Unit, clock basis, period reset/stoppage policy, and elapsed-time meaning are **unverified**. The validator reports raw values only and does not convert them. |
| `period` | Integer; values 1 and 2 | Period labels are preserved. Their timing relationship to `clockSeconds` has not been authoritatively specified. |
| `eventType` | String; 19 distinct labels | See the observed vocabulary below. No canonical mapping is approved. |
| `teamId`, `teamName` | String | Two ID/name pairs observed. Canonical team identity is unverified. |
| `playerName`, `receiverName` | String or null | Labels only; not immutable player identifiers. `receiverName` is null in 1,601 events; `playerName` is null in 2. |
| `pitchX`, `pitchY`, `ballX`, `ballY` | Number or null; observed numeric values 2–98 | Coordinate units, axis convention, pitch dimensions and attack direction are **unverified**. No normalization or metre conversion is performed. |
| `endX`, `endY` | Number or null; `endX` observed 0–100 | Coordinate units and whether these represent a destination are **unverified**. No conversion is performed. |
| `outcome` | String | Source value is retained; domain meaning is not certified. |
| `metadata` | JSON object | Source-specific contents are retained in the ordered-record digest; no canonical interpretation is applied. |
| `playerPositions` | Array of position objects; 48,048 rows in total | Event-attached observations only. This is not evidence of continuous tracking. |

## Position record

| Source field | JSON shape / observed values | Meaning and handling |
|---|---|---|
| `playerName`, `teamName`, `teamId` | Strings | Source labels. Do not use these alone as canonical identity. The same name/jersey may occur on opposing teams. |
| `jerseyNumber` | Integer | Preserve raw value; roster uniqueness and time validity are not certified. |
| `position` | String | Source label; role/position vocabulary is not validated. |
| `x`, `y` | Number; observed numeric values 2–98 | Units, axes and orientation are **unverified**. No distance or tactical threshold may use these values. |
| `speed`, `distanceCovered`, `heatIndex`, `passAccuracy`, `touches`, `stamina`, `heartRate` | Numeric values in every observed position row | Definitions, units, collection method, validity and privacy status are **unverified**. Do not present as measured performance or biometric data. |

## Observed event vocabulary and counts

| Event label | Count | Event label | Count |
|---|---:|---|---:|
| `BallRecovery` | 128 | `Carry` | 97 |
| `Clearance` | 95 | `Foul` | 254 |
| `FullTime` | 1 | `Goal` | 6 |
| `HalfTime` | 1 | `Interception` | 134 |
| `KickOff` | 8 | `LineBreakingPass` | 143 |
| `Pass` | 350 | `PossessionLost` | 117 |
| `PressureApplied` | 252 | `ProgressiveCarry` | 134 |
| `ProgressivePass` | 105 | `RedCard` | 1 |
| `Shot` | 124 | `Substitution` | 2 |
| `Tackle` | 247 | | |

Counts describe source labels only. In particular, `PressureApplied` does not establish a press or trap, `LineBreakingPass` does not establish a defensive line was breached, and score-like metadata is not certified canonical score truth. The observed shot and foul counts require football review.

## Admission status

The owner confirmed synthetic provenance and redistribution permission. Coordinate semantics, clock semantics, metric definitions/units, stable identity mapping, event-to-canonical mapping, score semantics, football realism, and independent rights/privacy review remain open. The candidate validator checks source bytes, structure, identifiers, ordering, and reproducible digests. Its success status is `structurally_valid_candidate_blocked_for_canonicalization`; it does not import or rewrite data, create canonical facts, write PostgreSQL, or enable API match reads.
