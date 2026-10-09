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
| Candidate validator positive and negative cases | `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_validate_manu_liv_candidate -v` | 12 passed, 0 failed. Cases cover source-ledger reproducibility, full-record digest sensitivity, duplicate IDs and JSON keys, integer sequence enforcement, sequence gaps, clock/period regression, malformed manifest types, manifest drift, non-finite/overflowing coordinates and pinned-candidate reconciliation. |
| Full candidate data integrity report | `python3 scripts/validate_manu_liv_candidate.py --output /private/tmp/ep03-s01-candidate-validation.json` | Exit 0; status `structurally_valid_candidate_blocked_for_canonicalization`; 2,199 events, 1 match, 2 teams, 48,048 attached position records; source sequence 1..2199 and source clock 0..5761 seconds. |
| Repeatability | Run the validator twice against the pinned source and manifest | Both runs produced source event-ID ledger SHA-256 `abd6e8ebcd9f7812be5633dd7e600d1b350a3fb3b3ddad6f678372b2be0a4539` and complete ordered-record SHA-256 `8c590d33ccaddc3809b74540fad78186c03e88ab5d3d9b2fbf62238de0e97cd3`. Object key order is normalized for this digest; the pinned source SHA preserves exact byte-level identity. |
| Full local quality gate | `bash scripts/validate-all.sh` | Exit 0. .NET restore/build succeeded with 0 warnings/errors; solution tests 23 passed, 0 failed, 5 skipped (all PostgreSQL integration tests lacked a test database). Frontend install/build/lint passed and 3 tests passed; npm reported 0 vulnerabilities. Source profiler tests 5 passed; candidate validator tests 11 passed; candidate validation, operating-model validator and dependency self-test passed. |

The full local quality log was observed in the terminal for the 2026-10-09 review-fix run. It is not a hosted CI/Azure DevOps run. PostgreSQL integration was skipped by this aggregate command; no canonical database ingest exists in this story branch.

## Observed data quality and limits

- Event counts include 124 `Shot` and 254 `Foul`; no unapproved professional-football frequency threshold is applied. Football domain review is required.
- Raw coordinate values range from 0 to 100 in some event fields. Units, axis orientation and pitch dimensions remain unverified. No metre conversion or pressure-distance calculation occurs.
- Player metric fields are structurally numeric but their units, provenance and validity are unverified. They are not marked as observed performance.
- All 19 source event kinds are retained as source strings. No canonical event mapping or event rewriting is applied.
- Sequence, source event IDs, source clock, period and complete event records are hashed and reported without normalization. The complete record digest canonicalizes JSON object key ordering only; the source SHA preserves exact bytes. The tool does not write a derived feed, canonical ledger, PostgreSQL row or API response.
- The manifest records owner-confirmed synthetic provenance and redistribution permission; independent rights/privacy review remains pending.
- Product Owner review of realism, score/goal semantics and a complete acceptable replay remains pending. The source runs through 96:01 and is not certified as the requested PO-approved 90-minute scenario.

## Rebased-head verification — 2026-10-09

At rebased head `7f1727175606d5c1e497f5ada323de4c467d7aa3`, the candidate validator tests passed 11/11, feed profiler tests passed 5/5, the validator again returned `structurally_valid_candidate_blocked_for_canonicalization` with the pinned source hash and stable ledger digests, and the operating-model validator, DAG self-test, design-skill self-test and `git diff --check` passed. The integrated `bash scripts/validate-all.sh` was attempted on this rebased head but did not emit output or complete during the observed run; it is not counted as a pass. The earlier full-gate result above belongs to the pre-rebase code head.
