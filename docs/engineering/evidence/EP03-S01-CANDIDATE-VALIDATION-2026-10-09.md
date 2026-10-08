# EP03-S01 candidate source validation evidence — 2026-10-09

**Scope:** Local structural and source-conservation validation of the owner-confirmed candidate JSON. This is not canonical ingestion, football approval, independent rights/privacy review, or Product Owner acceptance.

## Pinned source

- File: `data/simulations/manu-liv-match-dataset.json`
- SHA-256: `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`
- Byte size: 28,601,013
- Candidate manifest: `data/simulations/manu-liv-candidate-manifest.json`, version `1.0.0`, status `candidate_not_canonical`
- Python runtime: 3.13.7

## Executed checks

| Requirement | Command | Observed result |
|---|---|---|
| Candidate validator positive and negative cases | `python3 -m unittest scripts.test_validate_manu_liv_candidate -v` | 8 passed, 0 failed. Cases cover source-ledger reproducibility, duplicate IDs, sequence gaps, clock/period regression, manifest drift, non-finite/overflowing coordinates and pinned-candidate reconciliation. |
| Full candidate data integrity report | `python3 scripts/validate_manu_liv_candidate.py --output /private/tmp/ep03-s01-candidate-validation.json` | Exit 0; status `structurally_valid_candidate_blocked_for_canonicalization`; 2,199 events, 1 match, 2 teams, 48,048 attached position records; source sequence 1..2199 and source clock 0..5761 seconds. |
| Repeatability | Run the validator twice against the pinned source and manifest | Both runs produced source event-ID ledger SHA-256 `abd6e8ebcd9f7812be5633dd7e600d1b350a3fb3b3ddad6f678372b2be0a4539` and ordered event semantics SHA-256 `3c4a3dad03cab8acfdd5dc600b0c8893c402ae2ffe866ef896f86ced437f46d6`. |
| Full local quality gate | `bash scripts/validate-all.sh` | Exit 0. .NET restore/build succeeded with 0 warnings/errors; solution tests 23 passed, 0 failed, 5 skipped (all PostgreSQL integration tests lacked a test database). Frontend install/build/lint passed and 3 tests passed; npm reported 0 vulnerabilities. Source profiler tests 5 passed; candidate validator tests 8 passed; candidate validation, operating-model validator and dependency self-test passed. |

The full local quality log was captured outside the repository at `/private/tmp/ep03-s01-validate-all-2026-10-09.log`. It is not a hosted CI/Azure DevOps run. PostgreSQL integration was skipped by this aggregate command; no canonical database ingest exists in this story branch.

## Observed data quality and limits

- Event counts include 124 `Shot` and 254 `Foul`; no unapproved professional-football frequency threshold is applied. Football domain review is required.
- Raw coordinate values range from 0 to 100 in some event fields. Units, axis orientation and pitch dimensions remain unverified. No metre conversion or pressure-distance calculation occurs.
- Player metric fields are structurally numeric but their units, provenance and validity are unverified. They are not marked as observed performance.
- All 19 source event kinds are retained as source strings. No canonical event mapping or event rewriting is applied.
- Sequence, source event IDs, source clock and period are hashed and reported without normalization. The tool does not write a derived feed, canonical ledger, PostgreSQL row or API response.
- The manifest records owner-confirmed synthetic provenance and redistribution permission; independent rights/privacy review remains pending.
- Product Owner review of realism, score/goal semantics and a complete acceptable replay remains pending. The source runs through 96:01 and is not certified as the requested PO-approved 90-minute scenario.
