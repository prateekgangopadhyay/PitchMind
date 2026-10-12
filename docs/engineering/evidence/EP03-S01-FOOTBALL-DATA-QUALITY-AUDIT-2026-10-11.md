# EP03-S01 candidate football-data quality audit — 2026-10-11

**Classification:** measured source observations; not a football certification, canonicalization, or rights decision.  
**Pinned source snapshot:** exact PR #77 head `cb5825458eed37efceed51639f7d79a96be5067f`. This identifies the candidate bytes and application state audited; it is not the revision that contains the offline audit script or its tests.
**Audit implementation and test revision:** `a023104be7d504f13437e8d38a8e9458c96df791` (tree `5aa5acdd4537eea48c55ead752c51f0ae790e559`). This exact local code/test commit includes the audit helper and its regression tests.
**Latest output-protection follow-up:** `17fd8c976a24dc809b836b940e533536f1db2e62` (tree `94a8f630c0862c39187be416b8d318e4274b3b93`), which also protects the supplied domain enum source from report overwrite.
**Source:** `data/simulations/manu-liv-match-dataset.json`, 28,601,013 bytes, SHA-256 `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`, Git blob `9f53613d10dcf2e117e708c1b3e9706a49e1eb7c`. The SHA matches `data/simulations/manu-liv-candidate-manifest.json` v1.0.0. Owner-declared synthetic origin and redistribution permission are recorded there; independent rights/privacy review is pending.

The audit script reads every source event, checks the manifest-pinned bytes, reports aggregate ranges and cross-field observations, and compares event names exactly against the current C# enum. It does not alter source records or infer units, player identity, event mappings, score authority, or football realism. The full aggregate output is [the machine-readable audit report](EP03-S01-FOOTBALL-DATA-QUALITY-AUDIT-2026-10-11.json).

## Executed checks

| Check | Command | Result |
|---|---|---|
| Audit script tests (historical tooling revision `107cfe56015460c2e58667417854d25b058ef6ca`) | `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_audit_manu_liv_candidate_quality -v` | **8 passed, 0 failed** at that tooling revision. This result is not attributed to source snapshot `cb582...`. |
| Current exact-head Python suite (`a023104...`) | `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s scripts -p 'test_*.py' -v` | **49 passed, 0 failed, 0 skipped**. Includes 9 audit tests, 27 candidate validator tests, 8 observed-dictionary profiler tests, and 5 source-agnostic profiler tests. |
| All-event quality audit (`a023104...`) | `PYTHONDONTWRITEBYTECODE=1 python3 scripts/audit_manu_liv_candidate_quality.py --output /private/tmp/pr84-a023-audit.json` and `cmp /private/tmp/pr84-a023-audit.json docs/engineering/evidence/EP03-S01-FOOTBALL-DATA-QUALITY-AUDIT-2026-10-11.json` | Both exited 0; measured all 2,199 records and 48,048 attached positions, and the generated JSON matched the tracked report byte-for-byte. `canonicalizationStatus` remains `blocked_pending_source_semantics_and_football_review`. |
| Candidate structural validator (`a023104...`) | `PYTHONDONTWRITEBYTECODE=1 python3 scripts/validate_manu_liv_candidate.py --output /private/tmp/pr84-a023-candidate.json` | Exit 0; 2,199 events, 48,048 positions, source SHA-256 `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`; status remains `structurally_valid_candidate_blocked_for_canonicalization`. |
| Observed dictionary regeneration (`a023104...`) | `PYTHONDONTWRITEBYTECODE=1 python3 scripts/profile_manu_liv_observed_dictionary.py --output /private/tmp/pr84-a023-dictionary.md && cmp /private/tmp/pr84-a023-dictionary.md docs/data/MANU-LIV-OBSERVED-DATA-DICTIONARY.md` | Exit 0; regenerated output matched the tracked report byte-for-byte. |
| Council routing/operating model (`a023104...`) | `node scripts/validate_operating_model.mjs` and `node scripts/validate_operating_model.mjs --self-test` | Both passed: 12 epics, 59 stories, 28 Council skills, 3 design skills, zero cycles, two approval gates; DAG self-test passed. |
| Patch hygiene (`a023104...`) | `git diff --check` | Passed. |
| Latest exact-head regression suite (`17fd8c9...`) | `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_audit_manu_liv_candidate_quality -v` and `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s scripts -p 'test_*.py' -v` | Audit tests **9/9**; full scripts suite **49/49**, 0 failed, 0 skipped. The existing output-alias test now covers direct, symlink, and hardlink aliases for source, manifest, supplied domain source, and audit script, checking original bytes remain unchanged. |
| Latest candidate/audit/report checks (`17fd8c9...`) | Candidate validator and all-event audit to `/private/tmp/pr84-domain-protect-*.json`, with `cmp` against tracked audit JSON; dictionary regeneration with `cmp` against tracked Markdown | Validator and audit exited 0; 2,199 events and 48,048 positions; both generated reports matched tracked reports byte-for-byte. Canonicalization remains blocked. |
| Latest Council and patch checks (`17fd8c9...`) | `node scripts/validate_operating_model.mjs`; `node scripts/validate_operating_model.mjs --self-test`; `git diff --check` | All passed; validator reports 12 epics, 59 stories, 28 Council skills, 3 design skills, zero cycles, and two approval gates. |

### Report output overwrite protection

Before reading audit inputs or writing a report, the CLI rejects output paths that resolve to the pinned candidate source, manifest, supplied domain source, or audit script itself. It checks resolved paths (including symlink targets) and existing-file identity (including hard links); if identity cannot be safely checked, it fails closed. Regression tests exercise direct, symlink, and hard-link aliases for each protected file and verify original bytes remain unchanged. A report may be written to a separate path, whose parent directory is created as needed.

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

The source sequence is contiguous from 1 through 2,199. Period-label counts are 1,035 for label 1 and 1,164 for label 2; their meaning is unverified. `clockSeconds` has integer values from 0 to 5,761, with no source-order regressions, 11 adjacent equal-clock pairs, and a median adjacent delta of 2 clock-value units. The field-name suffix is not authoritative: the source unit and clock semantics remain unverified. These are event rows, not a regular tracking cadence. The row labelled `HalfTime` is sequence 1,037 at clock value 2,700 and period label 2; sequence 1,036 is a `Pass` row with that same period label/value. The row labelled `FullTime` is sequence 2,199 at value 5,761 and period label 2. Manifest checks of these patterns are candidate-file regression guards only, not a semantic interpretation. Confirm with the source owner whether this is elapsed match time, how added time is encoded, and what the marker/period labels mean.

The six `Goal` score snapshots, in source order, are `1-0`, `1-1`, `1-2`, `2-2`, `2-3`, `3-3`; each adjacent pair increases exactly one nondecreasing component, and the full-time metadata says `3-3` / `Draw`. Goal-row event team, player, and outcome strings equal the corresponding metadata strings in all six rows. Score-component order is not independently declared, and none of these checks establishes canonical score truth or covers own goals, corrections, or disallowed goals.

Each event-attached frame contains 22 positions through sequence 1,869; 330 later frames contain 21. Liverpool has 11 position rows in all 2,199 frames. Manchester United has 11 in 1,869 and 10 in 330. The source red-card row at sequence 1,869 has 11 Manchester United position labels, and the next frame has 10 with the participant label absent. For both substitutions (sequences 1,585 and 1,628), the metadata's incoming/outgoing labels are reflected in the adjacent source frames: before, the outgoing label is present and incoming absent; at and after the event, incoming is present and outgoing absent, with 11 team rows. These are internal row relationships only; stable identity and rules compliance remain unverified.

### Event vocabulary and realism review

The source contains 19 event-type labels. The current `MatchEventType` enum contains eight labels. Exact name overlap is six: `Foul`, `FullTime`, `Goal`, `KickOff`, `Pass`, and `Shot`. Thirteen source names have no exact enum name; `Pressure` and `Turnover` have no exact source names. The manifest says mapping status `not_approved`. This audit creates no semantic mapping.

Observed counts include 124 `Shot` rows (57 Manchester United, 67 Liverpool), 254 `Foul` rows (132, 122), 252 `PressureApplied` rows, 247 `Tackle` rows, eight `KickOff` rows, six `Goal` rows, two substitutions, and one red card. These counts are a football-review prompt, not a plausibility verdict: source definitions and counting rules are missing, so the labels alone do not establish what qualifies as a shot, foul, pressure, or restart. The source profile already flags shot/foul frequency for review. Football realism and any acceptable demo interpretation require independent football/Product Owner review.

## Questions required before canonical admission

1. What are the units, pitch length/width, axis origin, coordinate directions, and attack orientation for each period? What do `pitchX/Y`, `ballX/Y`, `endX/Y`, and position `x/y` represent, and when are they sampled?
2. What are the exact formulas, units, windows, cadence, and valid ranges for all seven player metrics? Which values are cumulative, per-event, simulated, or sourced from a measured system? How should heart-rate-like data be treated for privacy and display?
3. What is the authoritative unit of `clockSeconds`? Does it continue across halves as elapsed match time? How are stoppage time, repeated timestamps, half-time, and raw value 5,761 represented? Is `sequence` the provider tie-break order?
4. What does each source event type mean, what qualifiers distinguish subtypes, and how were the 124 `Shot`, 254 `Foul`, 252 `PressureApplied`, eight `KickOff`, and other rows counted?
5. What is the declared team order for score metadata? How are own goals, disallowed goals, corrections, or scorer changes represented?
6. Are team/name/jersey labels stable player identities? Where are roster, lineup, role, substitution, and disciplinary rules specified, and what stable player identifier should canonical records use?
7. Which field-by-field source-to-domain event mappings, if any, are approved and versioned? How do qualifiers and unsupported types enter quarantine?
8. What independent football and rights/privacy reviews approve the fixture's use and any user-visible interpretation?

**Outcome:** the source is hash-pinned and structurally measurable, but canonicalization remains blocked. Do not use these fields as canonical facts or present the metrics as observed performance until the questions and review gates above are answered.
