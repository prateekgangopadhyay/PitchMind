# Manchester United–Liverpool candidate feed: structural field dictionary

**Authority:** repository-derived structural observations only. No provider-authoritative data dictionary was supplied. This document records the exact JSON representation and observed value distributions; it does not certify field meaning, football validity, rights/privacy, or source-to-canonical mapping.

- Source: [`data/simulations/manu-liv-match-dataset.json`](data/simulations/manu-liv-match-dataset.json)
- Source SHA-256: `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`; bytes: 28,601,013; event objects: 2,199.
- Pinned manifest: [`data/simulations/manu-liv-candidate-manifest.json`](data/simulations/manu-liv-candidate-manifest.json); manifest must match source bytes before generation.
- Reproduce: `python3 scripts/generate_manu_liv_field_dictionary.py`.
- The repository owner confirmed synthetic provenance and redistribution permission on 2026-10-08; independent rights/privacy review and football approval remain pending.

## Event object fields

| Field path | Observed JSON types | Present / objects | Missing | Null | Structural summary |
|---|---|---:|---:|---:|---|
| `ballX` | number × 2199 | 2199/2199 | 0 | 0 | numeric range 2.0..98.0; distinct values 750 |
| `ballY` | number × 2199 | 2199/2199 | 0 | 0 | numeric range 2.0..98.0; distinct values 676 |
| `clockSeconds` | integer × 2199 | 2199/2199 | 0 | 0 | numeric range 0..5761; distinct values 2188 |
| `endX` | null × 1232, number × 967 | 2199/2199 | 0 | 1232 | numeric range 0.0..100.0; distinct values 427 |
| `endY` | null × 1232, number × 967 | 2199/2199 | 0 | 1232 | numeric range 2.0..98.0; distinct values 490 |
| `eventId` | string × 2199 | 2199/2199 | 0 | 0 | distinct values 2199 |
| `eventType` | string × 2199 | 2199/2199 | 0 | 0 | distinct values 19 |
| `matchId` | string × 2199 | 2199/2199 | 0 | 0 | distinct values 1 |
| `metadata` | object × 2199 | 2199/2199 | 0 | 0 | — |
| `outcome` | string × 2199 | 2199/2199 | 0 | 0 | distinct values 10 |
| `period` | integer × 2199 | 2199/2199 | 0 | 0 | numeric range 1..2; distinct values 2 |
| `pitchX` | number × 2199 | 2199/2199 | 0 | 0 | numeric range 2.0..98.0; distinct values 750 |
| `pitchY` | number × 2199 | 2199/2199 | 0 | 0 | numeric range 2.0..98.0; distinct values 676 |
| `playerName` | null × 2, string × 2197 | 2199/2199 | 0 | 2 | distinct values 25 |
| `playerPositions` | array × 2199 | 2199/2199 | 0 | 0 | — |
| `receiverName` | null × 1601, string × 598 | 2199/2199 | 0 | 1601 | distinct values 23 |
| `sequence` | integer × 2199 | 2199/2199 | 0 | 0 | numeric range 1..2199; distinct values 2199 |
| `teamId` | string × 2199 | 2199/2199 | 0 | 0 | distinct values 2 |
| `teamName` | string × 2199 | 2199/2199 | 0 | 0 | distinct values 2 |

## `playerPositions[]` object fields

Observed event-attached position objects: 48,048. Event-level array lengths: 21 players × 330 events, 22 players × 1869 events. These records do not establish a continuous or fixed-rate tracking stream.

| Field path | Observed JSON types | Present / objects | Missing | Null | Structural summary |
|---|---|---:|---:|---:|---|
| `playerPositions[].distanceCovered` | number × 48048 | 48048/48048 | 0 | 0 | numeric range 0.0..11.81; distinct values 1182 |
| `playerPositions[].heartRate` | integer × 48048 | 48048/48048 | 0 | 0 | numeric range 125..196; distinct values 72 |
| `playerPositions[].heatIndex` | number × 48048 | 48048/48048 | 0 | 0 | numeric range 0.19..1.0; distinct values 82 |
| `playerPositions[].jerseyNumber` | integer × 48048 | 48048/48048 | 0 | 0 | numeric range 1..66; distinct values 18 |
| `playerPositions[].passAccuracy` | number × 48048 | 48048/48048 | 0 | 0 | numeric range 72.0..95.0; distinct values 24 |
| `playerPositions[].playerName` | string × 48048 | 48048/48048 | 0 | 0 | distinct values 24 |
| `playerPositions[].position` | string × 48048 | 48048/48048 | 0 | 0 | distinct values 10 |
| `playerPositions[].speed` | number × 48048 | 48048/48048 | 0 | 0 | numeric range 0.6..33.0; distinct values 325 |
| `playerPositions[].stamina` | number × 48048 | 48048/48048 | 0 | 0 | numeric range 63.0..100.0; distinct values 38 |
| `playerPositions[].teamId` | string × 48048 | 48048/48048 | 0 | 0 | distinct values 2 |
| `playerPositions[].teamName` | string × 48048 | 48048/48048 | 0 | 0 | distinct values 2 |
| `playerPositions[].touches` | integer × 48048 | 48048/48048 | 0 | 0 | numeric range 0..289; distinct values 289 |
| `playerPositions[].x` | integer × 112, number × 47936 | 48048/48048 | 0 | 0 | numeric range 2.0..98.0; distinct values 961 |
| `playerPositions[].y` | integer × 51, number × 47997 | 48048/48048 | 0 | 0 | numeric range 2.0..98.0; distinct values 940 |

## `metadata` object fields

Metadata keys are sparse and their values are JSON strings or null in this source, including fields whose names look numeric. Numeric-looking strings are not parsed or assigned units here.

| Field path | Observed JSON types | Present / objects | Missing | Null | Structural summary |
|---|---|---:|---:|---:|---|
| `metadata.action` | string × 14 | 14/2199 | 2185 | 0 | distinct values 9 |
| `metadata.card` | string × 1 | 1/2199 | 2198 | 0 | distinct values 1 |
| `metadata.card_color` | string × 1 | 1/2199 | 2198 | 0 | distinct values 1 |
| `metadata.end_x` | string × 967 | 967/2199 | 1232 | 0 | distinct values 426 |
| `metadata.end_y` | string × 967 | 967/2199 | 1232 | 0 | distinct values 489 |
| `metadata.headline` | string × 8 | 8/2199 | 2191 | 0 | distinct values 8 |
| `metadata.impact_score` | string × 7 | 7/2199 | 2192 | 0 | distinct values 7 |
| `metadata.intensity` | string × 2 | 2/2199 | 2197 | 0 | distinct values 1 |
| `metadata.is_goal` | string × 6 | 6/2199 | 2193 | 0 | distinct values 1 |
| `metadata.outcome` | string × 2199 | 2199/2199 | 0 | 0 | distinct values 10 |
| `metadata.passDistanceM` | string × 598 | 598/2199 | 1601 | 0 | distinct values 224 |
| `metadata.passEndX` | string × 598 | 598/2199 | 1601 | 0 | distinct values 325 |
| `metadata.passEndY` | string × 598 | 598/2199 | 1601 | 0 | distinct values 402 |
| `metadata.passStartX` | string × 598 | 598/2199 | 1601 | 0 | distinct values 367 |
| `metadata.passStartY` | string × 598 | 598/2199 | 1601 | 0 | distinct values 383 |
| `metadata.passer` | string × 598 | 598/2199 | 1601 | 0 | distinct values 23 |
| `metadata.period` | string × 3 | 3/2199 | 2196 | 0 | distinct values 2 |
| `metadata.playerName` | string × 2197 | 2197/2199 | 2 | 0 | distinct values 24 |
| `metadata.player_in` | string × 2 | 2/2199 | 2197 | 0 | distinct values 2 |
| `metadata.player_out` | string × 2 | 2/2199 | 2197 | 0 | distinct values 2 |
| `metadata.reason` | string × 2 | 2/2199 | 2197 | 0 | distinct values 2 |
| `metadata.receiver` | string × 598 | 598/2199 | 1601 | 0 | distinct values 22 |
| `metadata.result` | string × 1 | 1/2199 | 2198 | 0 | distinct values 1 |
| `metadata.score` | string × 8 | 8/2199 | 2191 | 0 | distinct values 6 |
| `metadata.teamName` | string × 2199 | 2199/2199 | 0 | 0 | distinct values 2 |
| `metadata.type` | string × 2 | 2/2199 | 2197 | 0 | distinct values 2 |

## Observed labels and unresolved meanings

### Event type counts (source labels; no canonical mapping approved)

| Source `eventType` | Count |
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

### Period and chronology observations

| Field | Structural observation | Semantic status |
|---|---|---|
| `sequence` | integer values 1..2199; 2,199 distinct in source order | Preserve the original 1-based value; canonical offset/mapping is not approved |
| `clockSeconds` | integer values 0..5761; 2,188 distinct; source order nondecreasing | Unit, origin, elapsed-vs-period basis, stoppage-time policy and marker semantics are unverified; the field name is not authoritative evidence of seconds |
| `period` | integer values 1, 2; counts 1: 1035, 2: 1164 | Period labels/boundaries and their relationship to the clock are unverified |
| `eventId` | unique string in every source record | Preserve exact source ID; do not replace with generated canonical identity |
| `matchId`, `teamId`, `teamName` | 1 distinct non-null match IDs; 2 distinct observed team ID/name pairs | Source-to-canonical/competition identity mapping is not approved |

### Coordinates and metrics

| Field group | Structural observation | Meaning status |
|---|---|---|
| Event `pitchX/pitchY`, `endX/endY`, `ballX/ballY`; position `x/y` | Observed numeric ranges: event pitchX `2.0..98.0 (0 null)`, pitchY `2.0..98.0 (0 null)`, endX `0.0..100.0 (1232 null)`, endY `2.0..98.0 (1232 null)`, ballX `2.0..98.0 (0 null)`, ballY `2.0..98.0 (0 null)`; position x `2.0..98.0 (0 null)`, y `2.0..98.0 (0 null)` | Unit, pitch dimensions, axis order, origin, orientation, team attack direction, and whether values are percentages are unverified; do not convert to metres or calculate distances |
| Position `speed`, `distanceCovered`, `heatIndex`, `passAccuracy`, `touches`, `stamina`, `heartRate` | 48,048 position objects; per-field presence, JSON type, range and distinct count are listed above | Definitions, units, sampling basis, derivation, validity and biometric/privacy status are unverified; do not present as measured player performance |
| `position`, `jerseyNumber`, player/team labels | Jersey numeric range `1..66` across 48,048 present values; label types and distributions are listed above | Position taxonomy and stable identity semantics are unverified; names and shirt numbers are not canonical IDs |

## Admission boundary

This dictionary is not a canonical contract and does not approve a source adapter. Preserve source bytes, event IDs, original sequence, raw clock values, provenance and payload order. Coordinate/time normalization, player metric interpretation, event mappings, score certification, football realism, and independent rights/privacy review remain blocked pending evidence and Product Owner/football review. `candidate_not_canonical` stays the manifest status; canonical event creation remains disabled.
