# Manchester United–Liverpool simulation source — operational acceptance gates

**Source:** `data/simulations/manu-liv-match-dataset.json`. See [observed profile](../data/MANU-LIV-SOURCE-PROFILE.md) and [full source contract](../data/SIMULATION-FEED-CONTRACT.md). The current source contains real EPL club/player names, event attached `playerPositions` and attributes including `heartRate`/stamina. These must not be described as licensed, real player biometrics or actual live Premier League match events.

The repository owner confirmed synthetic provenance and redistribution permission on 2026-10-08. This owner declaration is recorded separately from independent rights/privacy review and football-domain approval; neither independent review has been completed.

## Automated structural gate (implemented tooling; not a canonical adapter)
Run `node scripts/audit_simulation_feed.mjs` against the versioned source JSON in a repository checkout. Read-only output: SHA-256, total events, event kind distribution, source sequence, max clock, player position observations, raw coordinate bounds marked **unit=unknown**, and warnings. The script verifies a single match, no duplicate IDs, ascending contiguous source sequence, nondecreasing elapsed time and typed fields; it never rewrites events or infers tactics.

Run `node scripts/audit_simulation_feed.mjs --self-test` for tiny self-contained unit probes; those checks do not prove the 28.6MB source itself is correct. The CI job runs the structural report after installing Node. **No raw personal attributes, player names or values are printed.**

## Required human-approved manifest to make admission eligible
Place a separate `data/simulations/fixture-manifest.json` only after the Product Owner and data-rights reviewer have signed facts with evidence. The template's `syntheticProvenanceReviewed` and `rightsApproved` fields remain `false` pending that independent sign-off, despite the recorded owner declaration; do not set them `true` solely on the declaration:
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
This is a **template**, deliberately unapproved. Do not commit it as an approved dataset or set fields true without a real review.

After rights/unit review, run `node scripts/audit_simulation_feed.mjs --require-approval --manifest data/simulations/fixture-manifest.json`. This blocks if the source hash differs, provenance/rights are unapproved, coordinate units/pitch dimensions are unspecified or approver identity absent. Manifest validation is a mechanical audit check, not independent verification of legal rights.

## Football domain gating
A source report describing 2,199 events, a final raw `clockSeconds` value of 5,761 (unit and clock interpretation unverified), 19 event kinds, 124 shots, 254 fouls, 48,048 sampled player positions and 3–3 final score is **not acceptance**. Product Owner and Football Analytics decide how synthetic realism is corrected in a versioned *derived scenario*, preserving all raw records, evidence and provenance. Do not silently delete shot/foul events or convert 0–100 coordinates into metres.

Admission requires an Application anti-corruption adapter that maps provider fields, player roster/substitutions, score/period rules, source sequence offset, nullable tracking and event types to canonical versioned envelopes. Test with actual feed, duplicate/gap/failure fixtures, halftime direction and selected-time snapshots, and persist atomically to PostgreSQL before declaring any EP03–EP05 story accepted. The React UI must receive all football facts through the BFF, not source-file imports or literals.
