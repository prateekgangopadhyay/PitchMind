# EP07-S03 selected-time insight pagination — 2026-10-16

## Change
The canonical match page follows selected-time insight cursors, bounded to 100 pages of 100 entries. Every page and row is checked against match ID, selected seconds, canonical revision, projection revision, and the selected snapshot sequence watermark. Duplicate insight IDs, cursor loops, empty continuation pages, invalid cursor transitions, and unsafe cutoffs fail closed. The page displays the latest non-empty persisted insight by sequence watermark and links back to its selected-time evidence. No narrative is generated.

## Exact local verification
On current-main restack candidate `codex/restack-pr112-current` (parent EP07-S03 cutoff guard `5660649d3cf409ee3590893ae94b2de55e9b3462`):

- `npm ci --offline`: 246 packages installed, 0 reported vulnerabilities.
- `npm test -- --run`: 117 passed, 0 failed.
- `npm run build`: passed.
- `npm run lint`: passed.
- `node scripts/validate_operating_model.mjs`: passed (12 epics, 59 stories, 28 Council skills, 5 design skills, 0 cycles).
- `git diff --check`: passed.

Tests include selected insight on a later cursor page, selected match/time/revision preservation, cursor-loop rejection, malformed cutoffs, projection/canonical revision divergence, and no stale summary rendering.

## Limits
This verifies frontend behavior against test responses only. The canonical Key Moments endpoint still has no persisted successful projection; a populated canonical fixture, live browser journey, manual screen-reader/accessibility review, exact 55:00/55:09 golden, independent technical review, and Product Owner acceptance remain pending. EP07-S03 remains unverified and unaccepted. No Azure resources or deployments changed.
