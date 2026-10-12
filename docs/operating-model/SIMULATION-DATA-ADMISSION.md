# Manchester United–Liverpool simulation source — operational acceptance gates

**Source:** `data/simulations/manu-liv-match-dataset.json`. See [observed profile](../data/MANU-LIV-SOURCE-PROFILE.md) and [full source contract](../data/SIMULATION-FEED-CONTRACT.md). The current source contains real EPL club/player names, event attached `playerPositions` and attributes including `heartRate`/stamina. These must not be described as licensed, real player biometrics or actual live Premier League match events.

## Automated structural gate (implemented tooling; not a canonical adapter)
Run `node scripts/audit_simulation_feed.mjs` against the versioned source JSON in a repository checkout. Read-only output: SHA-256, total events, event kind distribution, source sequence, max clock, player position observations, raw coordinate bounds marked **unit=unknown**, and warnings. The script verifies a single match, no duplicate IDs, ascending contiguous source sequence, nondecreasing elapsed time and typed fields; it never rewrites events or infers tactics.

Run `node scripts/audit_simulation_feed.mjs --self-test` for tiny self-contained unit probes; those checks do not prove the 28.6MB source itself is correct. The CI job runs the structural report after installing Node. **No raw personal attributes, player names or values are printed.**

## Candidate declaration and admission manifest
The checked-in `data/simulations/manu-liv-candidate-manifest.json` records the repository owner's confirmation (2026-10-10) that this feed is synthetic and redistribution is permitted. That declaration is distinct from the independent rights/privacy review, which remains pending, and it does not approve semantic interpretation or canonical admission.

Canonical admission still requires a separate, versioned review manifest tied to the exact source hash, with independent rights/privacy review, coordinate/clock/period policy, mapping, and approver identity. A candidate manifest must remain `candidate_not_canonical`; do not change it to approved merely to make the mechanical gate pass. The current preflight's `syntheticProvenanceReviewed` and `rightsApproved` flags represent outstanding review approvals, not whether the repository owner already made the source declaration:
```json
{
  "sourceSha256": "<actual SHA256 of 28.6MB JSON>",
  "syntheticProvenanceReviewed": false,
  "rightsApproved": false,
  "coordinateUnit": "unknown",
  "pitch": {"lengthMetres": null, "widthMetres": null},
  "periodClockPolicy": "pending review",
  "attackingDirections": "pending review",
  "approvedBy": ""
}
```
This is a **review-state example**, deliberately unapproved. Owner-confirmed synthetic provenance and redistribution permission are documented above; keep those source declarations distinct from independent approval flags and do not set review or approval fields without evidence.

After rights/unit review, run `node scripts/audit_simulation_feed.mjs --require-approval --manifest data/simulations/fixture-manifest.json`. This blocks if the source hash differs, provenance/rights are unapproved, coordinate units/pitch dimensions are unspecified or approver identity absent. Manifest validation is a mechanical audit check, not independent verification of legal rights.

## Football domain gating
A source report describing 2,199 events, a raw final clock value of 5,761 (96:01 only if interpreted as seconds), 19 event kinds, 124 shots, 254 fouls, 48,048 event-attached player positions and a 3–3 score field is **not acceptance**. Clock semantics and score authority remain unverified. This candidate is distinct from EP03-S01's accepted requirement for a fictional, seeded, deterministically regenerated 90-minute fixture. Product Owner and Football Analytics decide whether any versioned derived scenario is suitable, preserving auditable provenance. Do not silently delete shot/foul events or convert 0–100 coordinates into metres.

Admission requires an Application anti-corruption adapter that maps provider fields, player roster/substitutions, score/period rules, source sequence offset, nullable tracking and event types to canonical versioned envelopes. Test with actual feed, duplicate/gap/failure fixtures, halftime direction and selected-time snapshots, and persist atomically to PostgreSQL before declaring any EP03–EP05 story accepted. The React UI must receive all football facts through the BFF, not source-file imports or literals.
