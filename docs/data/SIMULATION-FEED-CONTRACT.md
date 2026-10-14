# Synthetic football JSON — feed onboarding, canonical mapping and acceptance
**STATUS: OWNER-CONFIRMED SYNTHETIC PROVENANCE AND REDISTRIBUTION PERMISSION; INDEPENDENT RIGHTS/PRIVACY, SEMANTIC, FOOTBALL AND CANONICALIZATION GATES OPEN.** The JSON is present at `data/simulations/manu-liv-match-dataset.json`. The repository owner confirmed synthetic provenance and redistribution permission on 2026-10-10; the candidate manifest records that declaration. Independent rights/privacy review remains pending. [Profiled source facts](MANU-LIV-SOURCE-PROFILE.md) supersede the initial missing-feed status. This document defines the canonical adapter output and remaining acceptance gates, not evidence that the source was imported to PostgreSQL.

## Source onboarding and implementation gates
The candidate source is `data/simulations/manu-liv-match-dataset.json`; its owner-confirmed synthetic provenance and redistribution permission are recorded in `data/simulations/manu-liv-candidate-manifest.json`. This owner declaration does not substitute for the still-pending independent rights/privacy review, semantic validation, football review, mapping, or acceptance. The read-only structural profile is recorded in [the source profile](MANU-LIV-SOURCE-PROFILE.md) and [observed data dictionary](MANU-LIV-OBSERVED-DATA-DICTIONARY.md). Preserve the supplied bytes and do not infer field meaning or silently normalize unknown values. Publish a field-by-field source→canonical mapping only after semantic and domain review.

The candidate source's final raw clock value is 5761 (96:01 only if interpreted as seconds), but its unit and period/stoppage semantics are unverified. This source is not the EP03-S01 acceptance fixture: that story separately requires a fictional, seeded, deterministically regenerated 90-minute scenario. Do not treat profiling this 96:01 candidate as proof of the seeded-fixture acceptance.

## Proposed canonical envelope (post-admission output, not guessed source)
Raw candidate records have no `canonicalRevision` and must not be represented as canonical event/frame envelopes or published directly to the canonical telemetry channel. After reviewed mapping and successful PostgreSQL admission, the store assigns a positive, monotonically increasing per-match `canonicalRevision` atomically with the admitted record and its transactional outbox intent. `schemaVersion` is the wire-schema version and never supplies a canonical revision.
`EventEnvelope`: `schemaVersion`, positive store-assigned `canonicalRevision`, `matchId`, `eventId` stable, `sequence` contiguous per match, `sourceId/version/hash/recordPointer`, `provenance=synthetic`, `period`, `elapsedMatchMilliseconds`, optional source UTC, `eventType`, `teamId/playerId/relatedPlayerId`, `outcome`, `qualifiers`, `possessionId`, optional start/end pitch coordinates **in metres**, `payloadHash`, `supersedesEventId` for corrections.
`TrackingFrame`: positive store-assigned `canonicalRevision`, `frameId, matchId, elapsedMatchMilliseconds, ball, players [{playerId,x,y,observed,quality}], pitchDimensions, orientationVersion, sourceRecordPointer/hash`. Null/missing positions remain null; interpolated samples are tagged and **never presented as observed**.
`FixtureManifest`: `fixtureId, schemaVersion, sourceFilename, sha256, dataRights, syntheticFlag, competitionContext, teamRoster, periods, matchClockPolicy, pitchMetres, sourceCoordinateSpec, scenarioNames, generatorVersion`.

## Flow and anti-corruption layer
```mermaid
flowchart LR
 J[Candidate JSON] --> H[Hash + bounded raw parser]
 H --> RR[LOCAL candidate replay; source bytes preserved]
 RR -. no canonical facts .-> G[Semantic and football review]
 G --> V{Reviewed mapper + validation}
 V -->|Invalid| Q[Quarantine source pointer, reason, payload hash]
 V -->|Valid| PG[PostgreSQL canonical admission]
 PG --> O[Transactional outbox intent + assigned revision]
 O --> D[Outbox dispatcher]
 D --> EH[Azure Event Hubs canonical telemetry]
 EH --> W[EventWorker projections]
 ```
Ports: `ISimulationFeedReader`, `ISourceFeedMapper`, `IEnvelopeValidator`, `ITrackingFrameMapper`, `IQuarantineSink`, `ISimulationClock`; adapters only in Infrastructure. PostgreSQL admission and the outbox intent share one transaction; only the dispatcher publishes admitted canonical events. File path configured (no source file name hardcoded in C#); size/depth bounded and path traversal rejected. Browser reads API not source JSON.

## Football validation
Clock seconds/ms per period vs elapsed (added time explicit), stable sequence for tied timestamps, active roster after substitutions, cards/goal transitions, 11v11 unless send-off, half-time direction change, pitch metre ranges and coordinate transform per team/period. Preserve raw stadium coordinates; team-relative analyses attack positive x with explicit version. Possession requires controlled action/provider evidence, not nearest player. Missing 10 Hz tracking cannot support closing speed or sustained shape from 30-second sparse frames. Never label a one-off turnover 'pressing trap' without trigger/opponent response/geometric evidence. Score is canonical goal projection, not decorative fixture metadata.

## Replay semantics
Approved fixture hash + schema + rule version + ordered event IDs deterministically reconstruct match state. Simulation speed, pause, seek are virtual-clock operations with isolated session state; canonical PostgreSQL immutable. Duplicates with identical payload = idempotent no-op; conflicting payload same ID = quarantine; sequence gaps hold projection and alert; correction = new revision with audit. Producer Event Hubs partition by match ID and resumes from last accepted checkpoint with no unaccounted loss.

## Tests and release gate
Schema positive/negative, real file validation report, source hash equivalence, coordinate mirror/halftime invariance, goals-score match, gap/duplicate/conflicting payload, invalid roster/period, missing tracking, replay hash on N runs, restart/recovery and specified 500 event/sec burst with lag/p95 metrics. No EP03 feed story can be accepted without **an actual parser/normalizer tested against the supplied file**, the owner-confirmed source declaration plus completed independent rights/privacy review, and PO-approved football golden scenarios. EP03-S01 additionally requires its separate seeded 90-minute fictional fixture. No fake feed or replacement fixture should be claimed as the user's data.
