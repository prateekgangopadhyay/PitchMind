# Manchester United–Liverpool candidate feed data dictionary

**Status:** structural inventory only. The descriptions below report the pinned JSON's observed shape; they are not an authoritative provider dictionary, approval to canonicalize, or football/medical validation.

**Artifact:** `data/simulations/manu-liv-match-dataset.json`

**SHA-256:** `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`

**Observed records:** 2,199 event objects; 48,048 event-attached player-position objects.

## Reading this dictionary

- **Observed shape/range** describes this file only. It does not define units, measurement method, truth, or valid values for another file.
- Field names such as `speed`, `heartRate`, `pitchX`, and `period` do not establish units or real-world measurement.
- Preserve original JSON values, event IDs, source sequence, clock, period, and provenance. Do not map, normalize, overwrite, interpolate, or join by display name unless a reviewed, versioned manifest specifies that operation.
- Unknown meanings block canonical import and user-facing analytics. Missing or ambiguous fields must not be filled from assumptions.

## Event object

| JSON path | Observed shape and coverage | Interpretation / mapping status |
|---|---|---|
| `eventId` | Non-null string; 2,199 distinct values in this file | Source event identity. Preserve exactly; canonical identity policy is a separate contract. |
| `matchId` | Non-null string; one distinct value | Source match label. No approved canonical fixture mapping. |
| `sequence` | Non-null integer; contiguous 1–2,199 | Source order is 1-based. Preserve separately from any canonical sequence. |
| `clockSeconds` | Non-null integer field value; 0–5,761, nondecreasing in file order | The field name is observed; its unit, clock origin, stoppage representation, period reset policy, and relation to elapsed match time are unverified. |
| `period` | Non-null integer; observed values 1 and 2 | Period labels are present; exact clock-boundary semantics and canonical mapping remain unapproved. |
| `eventType` | Non-null string; 19 distinct values (see vocabulary below) | Source vocabulary only. No event type is canonical until an explicit versioned mapping is reviewed. |
| `teamId` | Non-null string; two distinct values | Preserve source ID. Canonical identity crosswalk is unapproved. |
| `teamName` | Non-null string; two distinct labels | Source display labels, not stable identifiers or proof of a real fixture. |
| `playerName` | String or null; null in 2/2,199 records | Optional source label; not a stable player key. Identity mapping is unapproved. |
| `receiverName` | String or null; null in 1,601/2,199 records | Optional label; no stable receiver ID is supplied in this field. |
| `pitchX`, `pitchY` | Non-null JSON numbers; each observed range 2–98 | Coordinate units, axis origin, pitch dimensions, orientation, and attacking direction are unknown. Do not interpret as metres or calculate distances. |
| `endX`, `endY` | Both null in 1,232/2,199 records; otherwise JSON numbers. `endX` range 0–100, `endY` range 2–98 | Endpoint semantics and coordinate convention are unverified. Do not infer a missing endpoint. |
| `outcome` | Non-null string; observed values: `Successful`, `Foul`, `Incomplete`, `Blocked`, `Saved`, `Goal`, `Won`, `Whistle`, `Substitution`, `Card` | Event-specific meanings and score effects are not established. Do not update canonical score from this field alone. |
| `metadata` | Non-null object; property set varies | Opaque source payload. Preserve raw content and apply a versioned schema before interpreting any property. Conflicting duplicate values require review/quarantine. |
| `playerPositions` | Non-null array; each event has 21 or 22 objects | Event-attached samples are not proof of continuous tracking or a uniform sampling cadence. Join/time semantics remain unverified. |
| `ballX`, `ballY` | Non-null JSON numbers; each observed range 2–98 | Ball coordinate units/origin/orientation are unknown, like player coordinates. |

### Observed `eventType` values

| Source token | Count |
|---|---:|
| `Pass` | 350 |
| `Foul` | 254 |
| `PressureApplied` | 252 |
| `Tackle` | 247 |
| `LineBreakingPass` | 143 |
| `ProgressiveCarry` | 134 |
| `Interception` | 134 |
| `BallRecovery` | 128 |
| `Shot` | 124 |
| `PossessionLost` | 117 |
| `ProgressivePass` | 105 |
| `Carry` | 97 |
| `Clearance` | 95 |
| `KickOff` | 8 |
| `Goal` | 6 |
| `Substitution` | 2 |
| `HalfTime` | 1 |
| `RedCard` | 1 |
| `FullTime` | 1 |

These are literal counts, not a football-quality verdict. The unusually high shot and foul counts are flagged for football review in the [source profile](MANU-LIV-SOURCE-PROFILE.md).

### Observed metadata property names

`action`, `card`, `card_color`, `end_x`, `end_y`, `headline`, `impact_score`, `intensity`, `is_goal`, `outcome`, `passDistanceM`, `passEndX`, `passEndY`, `passStartX`, `passStartY`, `passer`, `period`, `playerName`, `player_in`, `player_out`, `reason`, `receiver`, `result`, `score`, `teamName`, `type`.

Names such as `passDistanceM`, `impact_score`, `score`, or `is_goal` do not certify units, formula, event validity, or authority. Cross-field reconciliation rules are not supplied.

## `playerPositions[]` object

All listed nested properties were present and non-null for the 48,048 observed objects.

| JSON path | Observed shape/range | Interpretation / mapping status |
|---|---|---|
| `playerName` | String; 24 distinct labels | Display label only; not a stable identity. |
| `teamId` | String; two values | Source identity; canonical crosswalk unapproved. |
| `teamName` | String; two labels | Source display label; not authoritative identity. |
| `jerseyNumber` | Integer; observed range 1–66 | Attribute only; not an immutable player key. |
| `position` | String; values include `GK`, `RB`, `CB`, `LB`, `DM`, `CM`, `AM`, `RW`, `LW`, `ST` | Source role label. Taxonomy and whether it is nominal position or a time-varying role are unverified. |
| `x`, `y` | JSON number; each observed range 2–98 | Coordinate units, origin, pitch dimensions, and orientation unknown. Do not treat as metres. |
| `speed` | JSON number; observed range 0.6–33.0 | Unit, sampling interval, smoothing, and derivation unknown. Not validated velocity. |
| `distanceCovered` | JSON number; observed range 0.0–11.81 | Unit, accumulation window, and derivation unknown. |
| `heatIndex` | JSON number; observed range 0.19–1.0 | Definition, scale, and derivation unknown. |
| `passAccuracy` | JSON number; observed range 72.0–95.0 | Denominator, time window, source, and whether it is a percentage are unverified. |
| `touches` | Integer; observed range 0–289 | Counting rule, time window, and reset behavior unknown. |
| `stamina` | JSON number; observed range 63.0–100.0 | Definition, scale, and derivation unknown; do not present as a measured condition. |
| `heartRate` | Integer; observed range 125–196 | Synthetic-looking, unvalidated field. It is not certified biometric data and must not be presented as a real player's heart rate. |

## Canonicalization gate

This inventory does not resolve source semantics. Before an adapter can create canonical events or frames, an approved, versioned manifest must define coordinate units/axes/orientation and pitch dimensions; source-clock and period/stoppage semantics; team/player identity mappings; event and outcome mappings; frame join and sampling/coverage policy; null handling; and metric definitions/units or explicit exclusion. Football review must address the event distribution and demonstrate realistic, deterministic scenarios with counterexamples. Preserve raw event IDs, source sequence, provenance, and match-time values through any separately versioned canonical representation.
