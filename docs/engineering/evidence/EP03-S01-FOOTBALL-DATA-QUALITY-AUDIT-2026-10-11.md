# EP03-S01 candidate football-data quality audit — 2026-10-11

**Classification:** measured source observations; not a football certification, canonicalization, or rights decision.  
**Reviewed commit:** exact PR #77 head `cb5825458eed37efceed51639f7d79a96be5067f`.  
**Source:** `data/simulations/manu-liv-match-dataset.json`, 28,601,013 bytes, SHA-256 `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`, Git blob `9f53613d10dcf2e117e708c1b3e9706a49e1eb7c`. The SHA matches `data/simulations/manu-liv-candidate-manifest.json` v1.0.0. Owner-declared synthetic origin and redistribution permission are recorded there; independent rights/privacy review is pending.

The audit script reads every source event, checks the manifest-pinned bytes, reports aggregate ranges and cross-field observations, and compares event names exactly against the current C# enum. It does not alter source records or infer units, player identity, event mappings, score authority, or football realism. The full aggregate output is [the machine-readable audit report](EP03-S01-FOOTBALL-DATA-QUALITY-AUDIT-2026-10-11.json).

## Executed checks

| Check | Command | Result |
|---|---|---|
| Audit script tests | `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_audit_manu_liv_candidate_quality -v` | **8 passed, 0 failed**. Covers pinned hash rejection, raw coordinate ranges without unit assignment, metric variation without semantic claims, exact-name-only mapping comparison, score-chain handling including an unparseable goal score, red-card source-frame presence, and output alias rejection. |
| All-event quality audit | `PYTHONDONTWRITEBYTECODE=1 python3 scripts/audit_manu_liv_candidate_quality.py --output /private/tmp/ep03-s01-quality-audit-after-output-protection.json` | Exit 0; measured all 2,199 records and 48,048 attached positions. Output copied to the machine-readable report linked above. |
| Candidate structural validator | `PYTHONDONTWRITEBYTECODE=1 python3 scripts/validate_manu_liv_candidate.py --output /private/tmp/ep03-s01-candidate-integrity-after-output-protection.json` | Exit 0; source remains `structurally_valid_candidate_blocked_for_canonicalization`. |
| Existing candidate validator regression | `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_validate_manu_liv_candidate -v` | **25 passed, 0 failed**. |
| Source-agnostic profiler regression | `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_profile_simulation_feed -v` | **5 passed, 0 failed**. |
| Council routing/operating model | `node scripts/validate_operating_model.mjs` | Passed: 12 epics, 59 stories, 28 Council skills, 3 design skills, zero cycles, two approval gates. |
| Patch hygiene | `git diff --check` | Passed. |

### Report output overwrite protection

Before reading audit inputs or writing a report, the CLI rejects output paths that resolve to the pinned candidate source or manifest. It checks resolved paths (including symlink targets) and existing-file identity (including hard links); if identity cannot be safely checked, it fails closed. Regression tests exercise direct, symlink, and hard-link aliases for both protected files and verify their original bytes remain unchanged. An independent review reproduced the source-overwrite case with `--output data/simulations/manu-liv-match-dataset.json`; this guard prevents it. A report may be written to a separate path, whose parent directory is created as needed.

## Measured source observations

### Coordinates and units

All coordinate values are numeric and lie in the closed interval 0–100 in this file. `pitchX`, `pitchY`, `ballX`, and `ballY` each have 2,199 values with observed minimum 2 and maximum 98. `endX` and `endY` each have 967 values and 1,232 null/missing values; `endX` ranges 0–100 and `endY` 2–98. The 48,048 player-position x values and 48,048 y values each range 2–98. No measured coordinate value falls outside 0–100.

The candidate manifest explicitly leaves coordinate unit, axis convention, and pitch dimensions in metres null and marks their status `unverified`. The numeric range does not establish metres, percentages, origin, direction, or orientation. No coordinate conversion or distance calculation is justified yet.

### Player metrics and identity

There are 48,048 position observations and 24 source composite labels formed from `(teamId, playerName, jerseyNumber)`: 12 labels per team. The file has no stable player ID field. This composite is useful for audit grouping only; it is not a canonical roster identity.

Every observation contains numeric values for the seven named metrics below. “Adjacent decreases” counts decreases between consecutive observations for the same source composite label in event-sequence order. It describes the source values and does not imply metric meaning or sampling cadence.

| Field | Observed min–max | Distinct values | Labels with changing values | Adjacent decreases in source order |
|---|---:|---:|---:|---:|
| `speed` | 0.6–33.0 | 325 | 24/24 | 23,419 |
| `distanceCovered` | 0.0–11.81 | 1,182 | 24/24 | 0 |
| `heatIndex` | 0.19–1.0 | 82 | 24/24 | 23,212 |
| `passAccuracy` | 72.0–95.0 | 24 | 23/24 | 93 |
| `touches` | 0–289 | 289 | 24/24 | 0 |
| `stamina` | 63.0–100.0 | 38 | 24/24 | 656 |
| `heartRate` | 125–196 | 72 | 24/24 | 23,204 |

All observed `touches` and `heartRate` values are JSON integers; the other five fields are JSON floating-point values. The manifest marks metric units/validity `unverified` and forbids presenting them as observed performance. In particular, the monotone source-order patterns for `distanceCovered` and `touches` do not prove cumulative semantics. Definitions, units, windows, cadence, and validity are absent.

### Chronology, event counts, score fields, and roster frames

The source sequence is contiguous from 1 through 2,199. Period counts are 1,035 in period 1 and 1,164 in period 2. The manifest declares the `clockSeconds` unit as seconds; values run from 0 to 5,761, with no source-order regressions, 11 adjacent equal-clock pairs, and a median adjacent delta of 2 seconds. These are event rows, not a regular tracking cadence. The half-time marker is sequence 1,037 at clock 2,700 and period 2; sequence 1,036 is a period-2 `Pass` at the same clock. Full time is sequence 2,199 at 5,761 and period 2. Confirm with the source owner whether this is elapsed match time, how added time is encoded, and why the marker's period is 2.

The six `Goal` score snapshots, in source order, are `1-0`, `1-1`, `1-2`, `2-2`, `2-3`, `3-3`; each adjacent pair increases exactly one nondecreasing component, and the full-time metadata says `3-3` / `Draw`. Goal-row event team, player, and outcome strings equal the corresponding metadata strings in all six rows. Score-component order is not independently declared, and none of these checks establishes canonical score truth or covers own goals, corrections, or disallowed goals.

Each event-attached frame contains 22 positions through sequence 1,869; 330 later frames contain 21. Liverpool has 11 position rows in all 2,199 frames. Manchester United has 11 in 1,869 and 10 in 330. The source red-card row at sequence 1,869 has 11 Manchester United position labels, and the next frame has 10 with the participant label absent. For both substitutions (sequences 1,585 and 1,628), the metadata's incoming/outgoing labels are reflected in the adjacent source frames: before, the outgoing label is present and incoming absent; at and after the event, incoming is present and outgoing absent, with 11 team rows. These are internal row relationships only; stable identity and rules compliance remain unverified.

### Event vocabulary and realism review

The source contains 19 event-type labels. The current `MatchEventType` enum contains eight labels. Exact name overlap is six: `Foul`, `FullTime`, `Goal`, `KickOff`, `Pass`, and `Shot`. Thirteen source names have no exact enum name; `Pressure` and `Turnover` have no exact source names. The manifest says mapping status `not_approved`. This audit creates no semantic mapping.

Observed counts include 124 `Shot` rows (57 Manchester United, 67 Liverpool), 254 `Foul` rows (132, 122), 252 `PressureApplied` rows, 247 `Tackle` rows, eight `KickOff` rows, six `Goal` rows, two substitutions, and one red card. These counts are a football-review prompt, not a plausibility verdict: source definitions and counting rules are missing, so the labels alone do not establish what qualifies as a shot, foul, pressure, or restart. The source profile already flags shot/foul frequency for review. Football realism and any acceptable demo interpretation require independent football/Product Owner review.

## Questions required before canonical admission

1. What are the units, pitch length/width, axis origin, coordinate directions, and attack orientation for each period? What do `pitchX/Y`, `ballX/Y`, `endX/Y`, and position `x/y` represent, and when are they sampled?
2. What are the exact formulas, units, windows, cadence, and valid ranges for all seven player metrics? Which values are cumulative, per-event, simulated, or sourced from a measured system? How should heart-rate-like data be treated for privacy and display?
3. Does `clockSeconds` continue across halves as elapsed match time? How are stoppage time, repeated timestamps, half-time, and 5,761 seconds represented? Is `sequence` the provider tie-break order?
4. What does each source event type mean, what qualifiers distinguish subtypes, and how were the 124 `Shot`, 254 `Foul`, 252 `PressureApplied`, eight `KickOff`, and other rows counted?
5. What is the declared team order for score metadata? How are own goals, disallowed goals, corrections, or scorer changes represented?
6. Are team/name/jersey labels stable player identities? Where are roster, lineup, role, substitution, and disciplinary rules specified, and what stable player identifier should canonical records use?
7. Which field-by-field source-to-domain event mappings, if any, are approved and versioned? How do qualifiers and unsupported types enter quarantine?
8. What independent football and rights/privacy reviews approve the fixture's use and any user-visible interpretation?

**Outcome:** the source is hash-pinned and structurally measurable, but canonicalization remains blocked. Do not use these fields as canonical facts or present the metrics as observed performance until the questions and review gates above are answered.
