# Synthetic football JSON — feed onboarding, canonical mapping and acceptance
**STATUS: SOURCE RECEIVED; OWNER-DECLARED SYNTHETIC PROVENANCE AND REDISTRIBUTION PERMISSION; INDEPENDENT RIGHTS/PRIVACY REVIEW, NORMALIZATION AND FOOTBALL CERTIFICATION OPEN.** The JSON was committed during review at `data/simulations/manu-liv-match-dataset.json` on updated main (`b978254`). [Profiled source facts](MANU-LIV-SOURCE-PROFILE.md) supersede the initial missing-feed status. This document defines the canonical adapter output and remaining acceptance gates, not evidence that the source was imported to PostgreSQL.

## Source onboarding required before implementation
Owner has supplied `data/simulations/manu-liv-match-dataset.json` in current GitHub main and confirmed synthetic provenance and redistribution permission (2026-10-08). Source byte/profile discovery is complete; coordinate/clock policies, player metric quality, football review, canonical mapping and independent sign-off remain open. The source is **owner-declared synthetic and distributable**; do not describe it as independently rights-reviewed or as football-approved. The read-only profiler records size, SHA-256, JSON structure and field types without importing the source. Preserve source event IDs, original 1-based sequence, clock and provenance; never normalize by guessing. Publish field-by-field source→canonical mapping only after unresolved semantics are reviewed.

## Proposed canonical envelope (adapter output, not guessed source)
`EventEnvelope`: `schemaVersion`, `matchId`, `eventId` stable, `sequence` contiguous per match, `sourceId/version/hash/recordPointer`, `provenance=synthetic`, `period`, `elapsedMatchMilliseconds`, optional source UTC, `eventType`, `teamId/playerId/relatedPlayerId`, `outcome`, `qualifiers`, `possessionId`, optional start/end pitch coordinates **in metres**, `payloadHash`, `supersedesEventId` for corrections.
`TrackingFrame`: `frameId, matchId, elapsedMatchMilliseconds, ball, players [{playerId,x,y,observed,quality}], pitchDimensions, orientationVersion, sourceRecordPointer/hash`. Null/missing positions remain null; interpolated samples are tagged and **never presented as observed**.
`FixtureManifest`: `fixtureId, schemaVersion, sourceFilename, sha256, dataRights, syntheticFlag, competitionContext, teamRoster, periods, matchClockPolicy, pitchMetres, sourceCoordinateSpec, scenarioNames, generatorVersion`.

## Flow and anti-corruption layer
```mermaid
flowchart LR
 J[Approved JSON] --> H[Hash + JSON profiler]
 H --> V{Schema + football validation}
 V -->|Invalid| Q[Quarantine source pointer, reason, payload hash]
 V -->|Valid| A[Source adapter converts time, IDs, pitch coordinates]
 A --> E[Canonical event/frame + provenance]
 E --> L[LOCAL deterministic replay]
 E --> EH[Azure Event Hubs producer]
 L --> PG[PostgreSQL] 
 EH --> W[EventWorker] --> PG
 ```
Ports: `ISimulationFeedReader`, `ISourceFeedMapper`, `IEnvelopeValidator`, `ITrackingFrameMapper`, `IQuarantineSink`, `ISimulationClock`; adapters only in Infrastructure. File path configured (no source file name hardcoded in C#); size/depth bounded and path traversal rejected. Browser reads API not source JSON.

## Football validation
Clock seconds/ms per period vs elapsed (added time explicit), stable sequence for tied timestamps, active roster after substitutions, cards/goal transitions, 11v11 unless send-off, half-time direction change, pitch metre ranges and coordinate transform per team/period. Preserve raw stadium coordinates; team-relative analyses attack positive x with explicit version. Possession requires controlled action/provider evidence, not nearest player. Missing 10 Hz tracking cannot support closing speed or sustained shape from 30-second sparse frames. Never label a one-off turnover 'pressing trap' without trigger/opponent response/geometric evidence. Score is canonical goal projection, not decorative fixture metadata.

## Replay semantics
Approved fixture hash + schema + rule version + ordered event IDs deterministically reconstruct match state. Simulation speed, pause, seek are virtual-clock operations with isolated session state; canonical PostgreSQL immutable. Duplicates with identical payload = idempotent no-op; conflicting payload same ID = quarantine; sequence gaps hold projection and alert; correction = new revision with audit. Producer Event Hubs partition by match ID and resumes from last accepted checkpoint with no unaccounted loss.

## Tests and release gate
Schema positive/negative, real file validation report, source hash equivalence, coordinate mirror/halftime invariance, goals-score match, gap/duplicate/conflicting payload, invalid roster/period, missing tracking, replay hash on N runs, restart/recovery and specified 500 event/sec burst with lag/p95 metrics. No EP03 feed story can be accepted without **actual parser/normalizer tested against the supplied file**, source-rights manifest and PO-approved football golden scenarios. No fake feed or replacement fixture should be claimed as the user's data.
