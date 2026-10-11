# MANU-LIV observed data dictionary

**Classification:** measured structural observations from one pinned candidate JSON file. This is not an authoritative semantic dictionary, football certification, canonicalization approval, or evidence that values are real-world observations.

**Source:** `data/simulations/manu-liv-match-dataset.json`; 28,601,013 bytes; SHA-256 `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`; manifest `data/simulations/manu-liv-candidate-manifest.json` v1.0.0; status `candidate_not_canonical`.

**Provenance:** manifest records synthetic provenance and redistribution permission as repository-owner-confirmed on 2026-10-08. Independent rights/privacy review is `pending`. This declaration does not validate semantics or football realism.

**Reproduce:** from the repository root, run `python3 scripts/profile_manu_liv_observed_dictionary.py`. The command validates manifest size/hash before replacing this Markdown file. Pass `--output /path/outside/repository/report.md` to write elsewhere.

## Interpretation limits

- Field names, JSON types, nulls, ranges, distinct counts, and source-order aggregates below are measured from the pinned bytes. For the tables, “missing keys” means a key is absent in an object; “explicit nulls” means it is present with JSON `null`. Distinct counts exclude nulls and count values as JSON values.
- Numeric ranges are raw values only. Coordinate units, axis/origin/orientation, attack direction, and pitch dimensions are **UNSPECIFIED / UNVERIFIED**. No conversion or distance inference is supported.
- `clockSeconds` is an integer field; the source unit is **UNSPECIFIED / UNVERIFIED**. The `Seconds` suffix is part of the field name and is not an authoritative data dictionary. Whether values denote elapsed match time, period-local time, stoppage time, or the meaning of period markers is also unverified.
- Manifest-pinned clock ordering, period labels, and marker row values are candidate-file structural regression checks only. They do not establish time units, period meanings, football semantics, or approval for other feeds.
- Player metric definitions, units, calculation windows, sampling cadence, identity stability, validity, and real-world status are **UNSPECIFIED / UNVERIFIED**. Do not present these values as measured player performance or biometrics.
- Source event labels are preserved as labels. Event semantics and source-to-domain mappings are **UNSPECIFIED / UNVERIFIED**; the manifest explicitly has no approved mappings. Exact label equality is not semantic equivalence.
- Score snapshot component order/team-side mapping and canonical score authority are **UNSPECIFIED / UNVERIFIED**. The source’s score strings are not certified match facts.
- Football realism is **UNVERIFIED**. Counts are observations, not a plausibility or rules verdict. `playerPositions` is event-attached and does not establish continuous tracking.
- This dictionary preserves source identifiers, sequence, raw clock-field values, coordinates, and ordering as-is. It performs no canonical imports, transformations, or value conversions.

## Record structure

The JSON root is an array with 2,199 event objects and 48,048 event-attached player-position objects. Each event has a `metadata` object. The tables cover every object of each type in the pinned file.

### Event object fields

| Field | Observed JSON type (count) | Missing keys | Explicit nulls | Distinct non-null values | Numeric min–max |
|---|---|---:|---:|---:|---:|
| `ballX` | `number` 2199 | 0 | 0 | 750 | 2.0–98.0 |
| `ballY` | `number` 2199 | 0 | 0 | 676 | 2.0–98.0 |
| `clockSeconds` | `integer` 2199 | 0 | 0 | 2188 | 0–5761 |
| `endX` | `null` 1232, `number` 967 | 0 | 1232 | 426 | 0.0–100.0 |
| `endY` | `null` 1232, `number` 967 | 0 | 1232 | 489 | 2.0–98.0 |
| `eventId` | `string` 2199 | 0 | 0 | 2199 | — |
| `eventType` | `string` 2199 | 0 | 0 | 19 | — |
| `matchId` | `string` 2199 | 0 | 0 | 1 | — |
| `metadata` | `object` 2199 | 0 | 0 | 1018 | — |
| `outcome` | `string` 2199 | 0 | 0 | 10 | — |
| `period` | `integer` 2199 | 0 | 0 | 2 | 1–2 |
| `pitchX` | `number` 2199 | 0 | 0 | 750 | 2.0–98.0 |
| `pitchY` | `number` 2199 | 0 | 0 | 676 | 2.0–98.0 |
| `playerName` | `null` 2, `string` 2197 | 0 | 2 | 24 | — |
| `playerPositions` | `array` 2199 | 0 | 0 | 2199 | — |
| `receiverName` | `null` 1601, `string` 598 | 0 | 1601 | 22 | — |
| `sequence` | `integer` 2199 | 0 | 0 | 2199 | 1–2199 |
| `teamId` | `string` 2199 | 0 | 0 | 2 | — |
| `teamName` | `string` 2199 | 0 | 0 | 2 | — |

### `metadata` object fields

| Field | Observed JSON type (count) | Missing keys | Explicit nulls | Distinct non-null values | Numeric min–max |
|---|---|---:|---:|---:|---:|
| `action` | `string` 14 | 2185 | 0 | 9 | — |
| `card` | `string` 1 | 2198 | 0 | 1 | — |
| `card_color` | `string` 1 | 2198 | 0 | 1 | — |
| `end_x` | `string` 967 | 1232 | 0 | 426 | — |
| `end_y` | `string` 967 | 1232 | 0 | 489 | — |
| `headline` | `string` 8 | 2191 | 0 | 8 | — |
| `impact_score` | `string` 7 | 2192 | 0 | 7 | — |
| `intensity` | `string` 2 | 2197 | 0 | 1 | — |
| `is_goal` | `string` 6 | 2193 | 0 | 1 | — |
| `outcome` | `string` 2199 | 0 | 0 | 10 | — |
| `passDistanceM` | `string` 598 | 1601 | 0 | 224 | — |
| `passEndX` | `string` 598 | 1601 | 0 | 325 | — |
| `passEndY` | `string` 598 | 1601 | 0 | 402 | — |
| `passStartX` | `string` 598 | 1601 | 0 | 367 | — |
| `passStartY` | `string` 598 | 1601 | 0 | 383 | — |
| `passer` | `string` 598 | 1601 | 0 | 23 | — |
| `period` | `string` 3 | 2196 | 0 | 2 | — |
| `playerName` | `string` 2197 | 2 | 0 | 24 | — |
| `player_in` | `string` 2 | 2197 | 0 | 2 | — |
| `player_out` | `string` 2 | 2197 | 0 | 2 | — |
| `reason` | `string` 2 | 2197 | 0 | 2 | — |
| `receiver` | `string` 598 | 1601 | 0 | 22 | — |
| `result` | `string` 1 | 2198 | 0 | 1 | — |
| `score` | `string` 8 | 2191 | 0 | 6 | — |
| `teamName` | `string` 2199 | 0 | 0 | 2 | — |
| `type` | `string` 2 | 2197 | 0 | 2 | — |

### `playerPositions[]` object fields

| Field | Observed JSON type (count) | Missing keys | Explicit nulls | Distinct non-null values | Numeric min–max |
|---|---|---:|---:|---:|---:|
| `distanceCovered` | `number` 48048 | 0 | 0 | 1182 | 0.0–11.81 |
| `heartRate` | `integer` 48048 | 0 | 0 | 72 | 125–196 |
| `heatIndex` | `number` 48048 | 0 | 0 | 82 | 0.19–1.0 |
| `jerseyNumber` | `integer` 48048 | 0 | 0 | 18 | 1–66 |
| `passAccuracy` | `number` 48048 | 0 | 0 | 24 | 72.0–95.0 |
| `playerName` | `string` 48048 | 0 | 0 | 24 | — |
| `position` | `string` 48048 | 0 | 0 | 10 | — |
| `speed` | `number` 48048 | 0 | 0 | 325 | 0.6–33.0 |
| `stamina` | `number` 48048 | 0 | 0 | 38 | 63.0–100.0 |
| `teamId` | `string` 48048 | 0 | 0 | 2 | — |
| `teamName` | `string` 48048 | 0 | 0 | 2 | — |
| `touches` | `integer` 48048 | 0 | 0 | 289 | 0–289 |
| `x` | `integer` 112, `number` 47936 | 0 | 0 | 963 | 2.0–98.0 |
| `y` | `integer` 51, `number` 47997 | 0 | 0 | 941 | 2.0–98.0 |

## Observed categorical values and order checks

| `eventType` label | Rows |
|---|---:|
| `BallRecovery` | 128 |
| `Carry` | 97 |
| `Clearance` | 95 |
| `Foul` | 254 |
| `FullTime` | 1 |
| `Goal` | 6 |
| `HalfTime` | 1 |
| `Interception` | 134 |
| `KickOff` | 8 |
| `LineBreakingPass` | 143 |
| `Pass` | 350 |
| `PossessionLost` | 117 |
| `PressureApplied` | 252 |
| `ProgressiveCarry` | 134 |
| `ProgressivePass` | 105 |
| `RedCard` | 1 |
| `Shot` | 124 |
| `Substitution` | 2 |
| `Tackle` | 247 |

| `outcome` label | Rows |
|---|---:|
| `Blocked` | 71 |
| `Card` | 1 |
| `Foul` | 254 |
| `Goal` | 6 |
| `Incomplete` | 93 |
| `Saved` | 53 |
| `Substitution` | 2 |
| `Successful` | 1713 |
| `Whistle` | 2 |
| `Won` | 4 |

| Check | Observed result |
|---|---|
| Source sequence endpoints | 1–2199; 2,199 distinct |
| Sequence contiguous and ascending | True |
| `clockSeconds` raw range | 0–5761 |
| Adjacent equal clock pairs | 11 |
| Adjacent clock regressions | 0 |
| `period` counts | 1: 1035, 2: 1164 |

## Provenance and use boundary

The source ID, sequence, clock and value spellings are data provenance. The manifest status is `candidate_not_canonical`; it states that canonical events may not be created from this file. Preserve original event IDs, source order and clock semantics verbatim during any future reviewed work. Canonical admission remains blocked until coordinate/time semantics, player metrics and identities, event mappings, score interpretation, football realism, and independent rights/privacy review have the required evidence.

**Generator:** `scripts/profile_manu_liv_observed_dictionary.py`. It uses only the Python standard library and refuses to emit output when the source bytes do not match the manifest.
