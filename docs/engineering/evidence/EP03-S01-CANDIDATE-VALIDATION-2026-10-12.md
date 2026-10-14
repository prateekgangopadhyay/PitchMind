# EP03-S01 candidate validation evidence — 2026-10-12

**Scope:** source-only structural validation and dictionary for the owner-confirmed candidate JSON. This slice does not complete EP03-S01, admit canonical events, or provide a football realism decision.

**Local code commit:** `d237c36c71db8272e68de0c4b4914e3f368e1308`, based on the exact current-main tree `08bd8effa3b778f76603573fb00119affea77d39` (main commit `8d952a8af7ac0f1ba92c87b9063340488ddd7975`).

## Input and local checks

- Source: `data/simulations/manu-liv-match-dataset.json`
- Source SHA-256: `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`
- Manifest: `data/simulations/manu-liv-candidate-manifest.json`, version `1.0.0`, `candidate_not_canonical`
- Command: `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_validate_manu_liv_candidate -v`
- Result: 20 passed, 0 failed.
- The pinned-feed test also asserts that the aggregate report omits sampled raw event IDs and team/player labels.
- Command: `PYTHONDONTWRITEBYTECODE=1 python3 scripts/validate_manu_liv_candidate.py --output /private/tmp/pitchmind-ep03-s01-candidate-validation-report.json`
- Result: exit 0; `structurally_valid_candidate_blocked_for_canonicalization`; 2,199 source events, one match label, two team labels, 48,048 attached position rows.
- Command: `git diff --check`
- Result: passed.

The report preserves source sequence 1–2199, raw values from the source `clockSeconds` field (0–5761, unit unverified), source event IDs, exact source byte hash, event-ID ledger SHA-256 `abd6e8ebcd9f7812be5633dd7e600d1b350a3fb3b3ddad6f678372b2be0a4539`, and ordered-record SHA-256 `8c590d33ccaddc3809b74540fad78186c03e88ab5d3d9b2fbf62238de0e97cd3`. Repeated runs in the test produced stable ledger digests.

## Limits and outstanding gates

- The validator applies structural, byte-integrity, sequence, period-order, duplicate-key, bounds and manifest-consistency checks. It does not map event labels, convert coordinates or clock values, resolve player identities, infer tactics, write a canonical ledger, persist to PostgreSQL, or enable match API routes.
- Coordinate units, axes, orientation and dimensions remain unverified; player metrics remain unvalidated and must not be shown as observed performance.
- The high shot/foul frequencies, source score semantics, roster/identity mapping and realism require football-domain review. Independent rights/privacy review and Product Owner acceptance remain pending.
- PostgreSQL, hosted CI, Azure validation and deployment were not exercised. No Azure resource or deployment action was performed.
- No C4, ADR or public API contract changed: this is an isolated read-only validation tool and documentation addition.

## Independent local re-run on current-main descendant

An isolated worktree at `6c8fe25aae00ea7449441fda212fe556c5bf0eba` reproduced the candidate checks without modifying the source or validator:

- `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest scripts.test_validate_manu_liv_candidate` — 20 passed, 0 failed.
- `PYTHONDONTWRITEBYTECODE=1 python3 scripts/validate_manu_liv_candidate.py --output /private/tmp/pitchmind-ep03-s01-field-dictionary-report.json` — exit 0; result remains `structurally_valid_candidate_blocked_for_canonicalization`.
- Re-run report: 2,199 events; source sequence 1–2,199; raw `clockSeconds` range 0–5,761 with unit `unverified`; 48,048 attached positions; source SHA-256 `e3743ccc17c63792f39858299c0b22e9b610f26686603f049347f3eb262edf8c`; event-ID ledger SHA-256 `abd6e8ebcd9f7812be5633dd7e600d1b350a3fb3b3ddad6f678372b2be0a4539`; ordered-record SHA-256 `8c590d33ccaddc3809b74540fad78186c03e88ab5d3d9b2fbf62238de0e97cd3`.
- `git diff --check` — passed after the evidence-only update.

This re-run does not change any semantic or acceptance gate: clock policy, coordinate units/axes/orientation, metric definitions/units, source-to-canonical identity and event mapping, football realism, independent rights/privacy review, full seeded 90-minute regeneration, PostgreSQL behavior and Product Owner acceptance remain unresolved or unverified.
