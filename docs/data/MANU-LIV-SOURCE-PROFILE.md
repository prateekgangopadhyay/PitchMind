# Manchester United–Liverpool source JSON: inspected structural profile
**Source:** `data/simulations/manu-liv-match-dataset.json` on updated PitchMind `main` commit `b97825416af5eb2e323b577942ae99addfe8727e`; Git blob SHA `9f53613d10dcf2e117e708c1b3e9706a49e1eb7c`. **Inspection date:** 2026-10-08.  
**Status:** structural profile from parsed source JSON; **not** canonical ingest, a football-approved simulation, an independent rights assessment, a verified Azure feed, or a passing replay integration test. The repository owner confirmed synthetic provenance and redistribution permission in the Engineering Council execution instruction on 2026-10-08. This owner declaration does not validate football realism, coordinate units, player metrics, or source-to-canonical mappings.

**Reproducibility check (2026-10-08):** `python3 scripts/profile_simulation_feed.py data/simulations/manu-liv-match-dataset.json --output <outside-repository-report>` completed with status `profiled_not_validated`, 28,601,013 bytes and SHA-256 `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`. `git hash-object` returned the blob SHA above. The profiler is metadata-only and deliberately does not validate football semantics or ingest the source.

**Owner confirmation (2026-10-10):** the repository owner confirmed that the dataset is synthetic and that redistribution is permitted. The JSON has no embedded provenance/rights manifest. No authoritative source data dictionary was supplied; this owner declaration does not resolve clock units/policy, coordinate semantics, player-metric definitions, event mapping or football-realism gates below.

## Observed source statistics
| Property | Observed value |
|---|---|
| JSON root | array of event-shaped objects |
| Bytes | 28,601,013 |
| Event count | 2,199 |
| Distinct matches | one, `matchId=b2c3d4e5-f6a7-8901-bcde-f12345678901` |
| Source sequence | 1..2199, contiguous and ascending, no duplicated event IDs observed |
| `clockSeconds` raw field values | 0..5761, nondecreasing. Unit and elapsed/period-local/stoppage-time semantics are **UNSPECIFIED / UNVERIFIED**; the field-name suffix is not source authority. |
| Raw `period` labels | 1: 1035 records, 2: 1164 records. Labels are preserved; their domain meaning is unverified. |
| Teams | Manchester United: 1123 event records; Liverpool: 1076 |
| Events titled Goal | 6: 3 United, 3 Liverpool; final score 3–3 |
| Other major events | 2 substitutions; 1 red card; half/full time markers |
| Player positions | 48,048 observations across 2,199 event-attached `playerPositions` arrays; 24 distinct `teamName + playerName` identities |
| Player/ball coordinate min/max | each axis between 2 and 98; pass `endX` spans 0..100. **Units unspecified**; values look normalized, not metres. |
| Tracking | event-attached frames, most 22 players with population reduced after lineup/card changes. **Not necessarily a dense continuous tracking feed.** |
| Provenance | no explicit `synthetic` or source-license/coordinate-orientation manifest at the top level; owner separately confirmed synthetic provenance and redistribution permission (2026-10-08) |

## Top-level fields (actual)
`eventId,matchId,sequence,clockSeconds,period,eventType,teamId,teamName,playerName,receiverName,pitchX,pitchY,endX,endY,outcome,metadata,playerPositions,ballX,ballY`. `receiverName` is null in 1,601 records; `endX/endY` null in 1,232. `playerName` null in 2. `playerPositions[]` elements contain `playerName,teamName,teamId,jerseyNumber,position,x,y,speed,distanceCovered,heatIndex,passAccuracy,touches,stamina,heartRate`. **The last five numeric attributes are not independently validated biometrics or performance metrics; do not display player-specific heart rates as real.**

## Source event taxonomy (not yet canonicalized)
| eventType | Count | eventType | Count |
|---|---:|---|---:|
| KickOff | 8 | Shot | 124 |
| Foul | 254 | PossessionLost | 117 |
| ProgressivePass | 105 | BallRecovery | 128 |
| ProgressiveCarry | 134 | Pass | 350 |
| Tackle | 247 | Interception | 134 |
| PressureApplied | 252 | Carry | 97 |
| Clearance | 95 | LineBreakingPass | 143 |
| Goal | 6 | HalfTime | 1 |
| Substitution | 2 | RedCard | 1 |
| FullTime | 1 | | |

**Football quality concern:** 124 shot events and 254 fouls in a single match are not a plausible ordinary professional match profile. The simulation requires football-domain review and may need calibrated selection/labeling for a believable replay. Do not silently delete, rewrite or claim the dataset represents a real EPL match; retain the raw input and document any derived/demo replay subset.

## Clock, coordinate and identity conversion — REQUIRED
- Current offline C# assumes 0..5400 seconds, one 90-minute fixture, 0-based sequence, a 105×68 **metre** coordinate frame and 8 supported event kinds. **None of these assumptions can be applied directly to this source.** The candidate field is named `clockSeconds`, but its unit and clock semantics remain unverified until supported by a source-owner data dictionary.
- Candidate-manifest checks pin clock order, period labels, and marker row values only as regression guards for this exact source file. They are not approved time/period semantics and do not establish the meaning of the source event labels.
- Conversion should map `sourceSequence 1..2199` to canonical 0..2198 or explicitly version a 1-based contract; preserve original sequence separately. Do not mutate source event IDs.
- Determine pitch axis/attacking-direction convention and whether x/y values are percentages. Only after this is confirmed should `xMetres = xNormalized * pitchLengthMetres / 100` and `yMetres = yNormalized * pitchWidthMetres / 100` be applied, using **manifest-defined pitch dimensions**. Never blindly map `pitchY=98` into 68 metre bounds or calculate a 3-metre presser distance on unitless points.
- The existing `SyntheticMatchService` assumes full-time at 5,400 and rejects later values. This is a legacy implementation assumption, not a specification for this candidate. Its raw terminal clock-field value is 5,761, but the field's unit and meaning are unverified; do not derive an adapter policy from that number. If this source is ever admitted, obtain authoritative clock/period semantics and approve an explicit mapping first. Never use wall-clock time to reconstruct game state.
- Preserve all 19 source event kinds in a versioned mapping registry. Treat kickoff/restart, half-time/full-time, foul, tackle, carry, ball recovery, pressure, possession loss, card and substitution distinctly; do not coerce carry→pass or recovery→turnover just to satisfy current enum.
- Generate stable player IDs from source-provided stable identity if available; otherwise use a documented team/player/jersey disambiguation map with correction handling. Treat names/jersey as attributes, not a reliable immutable player key.
- `Goal` records' metadata `score` values agree with the displayed 3–3 result, but score remains a projection of six accepted canonical goal events with review of disallowed/own goals, corrections and scorer validity.

## Rights, synthetic origin, medical-like attributes and realism
This JSON contains **real club and player names**, location and synthetic-looking speed/stamina/heart-rate values. The repository owner confirmed synthetic provenance and redistribution permission; independent rights/privacy review, metric units/definitions, and privacy implications remain unresolved. That confirmation does not establish realistic coordinates or measured player metrics. Do **not** portray player biometrics as actual performance. "Simulated EPL-context match" must be visible on the product.

## Implementation acceptance items for EP03/04/05/07
1. Approved `FixtureManifest` in `data/simulations/` describing source SHA-256, `synthetic=true`, rights, pitch dimensions/unit/orientation, clock policy, team IDs, source event vocabulary, substitution/roster policy.
2. Source-specific importer parses 2,199 events and event-attached frames without losing IDs/clock/period or inventing extra measurements; typed quarantine for invalid input.
3. PostgreSQL event, tracking, roster, score, projection, provenance and outbox transaction; replay at goal boundaries and red card; repeatable hashes.
4. Football PO reviews exaggerated shot/foul frequencies and identifies acceptable demonstration subsamples/interpretations with audit of original source.
5. Pressing/line-break claims are gated on relevant tracking and event completeness; generic `PressureApplied` alone never proves a trap, and `LineBreakingPass` alone does not prove actual breached defensive lines.
6. API/UI serves *this* match only after canonical adapter verification; source names/scores not hardcoded in C# or React. Frontend matches 96:01, both team labels and score changes, missing footage is explicit.
7. Demonstrated model grounding uses cited source-derived evidence, never real-match knowledge.

**Implemented so far:** read-only [source-agnostic JSON profiler](../../scripts/profile_simulation_feed.py) and manifest-pinned [candidate structural validator](../../scripts/validate_manu_liv_candidate.py), each with standalone tests. The candidate validator emits an aggregate report and preserves source event IDs, sequence, clock, and record ordering in its checks/digests. It does **not** normalize, map, ingest, or make the feed available through match APIs. Source-specific canonical import is **not yet implemented**; ordinary match routes remain fail-closed until admitted canonical PostgreSQL data and a read port exist.

## Follow-up measured football-data audit — 2026-10-11

The full event file was audited at SHA-256 `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c` (Git blob `9f53613d10dcf2e117e708c1b3e9706a49e1eb7c`). The [observed structural data dictionary](MANU-LIV-OBSERVED-DATA-DICTIONARY.md) records measured paths, JSON types, missing-key and explicit-null counts, numeric ranges, distinct-value counts, and selected source-order aggregates from every record in those pinned bytes; regenerate it with `python3 scripts/profile_manu_liv_observed_dictionary.py`. The separate [football-data audit and unresolved questions](../engineering/evidence/EP03-S01-FOOTBALL-DATA-QUALITY-AUDIT-2026-10-11.md) and its [machine-readable aggregate report](../engineering/evidence/EP03-S01-FOOTBALL-DATA-QUALITY-AUDIT-2026-10-11.json) cover coordinate ranges, metric distributions, chronology, score snapshots, roster-frame counts, and exact-name event-label overlap. These are structural observations only: coordinate units/orientation, player metric semantics, event mappings, score-side mapping, football realism, and canonical match facts remain unspecified or unverified.
