# Football Intelligence Detailed LLD — canonical match truth to explainable tactical insight
**Status:** target LLD; reviewed references: [current core LLD](05-football-lld.md), [legacy football vocabulary](https://github.com/prateekgangopadhyay/PitchMind_Old/blob/main/docs/domain/football-intelligence-ubiquitous-language.md). Legacy source is advisory, not imported.
**Football truth invariants:** Position ≠ Role; Formation ≠ Shape; Event ≠ Tactical Mechanism; Sequence ≠ Causation; possession ≠ territory; a detector never guesses unobserved speed/pressure.
**Approved demo prerequisite:** owner-supplied JSON is now present on newer main, containing 2,199 event records and event-level player positions (see [source profile](../data/MANU-LIV-SOURCE-PROFILE.md)); no source-specific **canonical importer or licensed/provenance certification** exists yet.

## A. DDD aggregates and typed objects
```mermaid
classDiagram
 class Match {
  +MatchId id
  +FixtureVersion version
  +Period period
  +MatchClock clock
  +Score score
  +CanonicalRevision revision
  +Apply(ValidatedEnvelope)
 }
 class MatchEvent {
  +EventId id
  +CanonicalRevision admittedRevision
  +long sequence
  +EventType type
  +TeamId? team
  +PlayerId? actor
  +EventOutcome outcome
  +MatchTime time
  +PitchPoint? start
  +PitchPoint? end
  +PayloadHash provenance
 }
 class TrackingFrame {
  +FrameId id
  +CanonicalRevision admittedRevision
  +MatchTime time
  +PitchPoint? ball
  +ObservedPosition[] players
  +Coverage quality
 }
 class Possession {
  +PossessionId id
  +TeamId? controller
  +MatchTime from
  +MatchTime? to
  +EvidenceId[] sources
 }
 class MechanismCandidate {
  +MechanismId id
  +DetectorVersion version
  +TruthClass truth
  +ConditionResult[] conditions
  +EvidenceRef[] evidence
 }
 Match --> MatchEvent
 Match --> TrackingFrame
 Match --> Possession
 Match --> MechanismCandidate
 ```
**Value types:** `MatchId, EventId, PlayerId, TeamId, PeriodId, MatchTime(elapsedMs,period,periodClockMs,addedTimeMs), PitchPoint(xMetres,yMetres,orientationVersion), TrackingQuality, SourcePointer, PayloadHash, AlgorithmVersion, CanonicalRevision, TruthClass, EvidenceRef, InsightId`. Domain never references Npgsql, AI SDK, Azure, JSON schema vendor types or UI.

**Aggregate boundaries:** `MatchLedger` append-only input event+frame history; `MatchProjection` versioned materialized state, active roster, substitutions, cards, score, possession, match phases; `TacticalInsight` immutable derived candidate referencing exact canonical revision and evidence; `Explanation` separately validated narrative with source provenance. Never store LLM output as observed match fact.

Competition context is explicit and optional: `competitionId`, `rulesVersion`, and `maximumSubstituteReentriesPerPlayer`. A missing limit means unknown and re-entry fails closed; when supplied, the aggregate tracks re-entry count per player and rejects returns above the versioned limit. Never derive a competition or rule from team names or fixture data.

## B. Clock/period/point-in-time model
- PostgreSQL canonical admission allocates a positive, monotonically increasing per-match `canonicalRevision` in the same transaction as the immutable event/frame and outbox intent. `schemaVersion` versions the transport shape; provider/source schema revisions stay in provenance. A source candidate before admission has no canonical revision and cannot be serialized as `EventEnvelopeV1` or `TrackingFrameV1`. A correction appends under a new revision and leaves the previously admitted row unchanged. Each row's revision is its admission revision; a query/snapshot response separately carries the selected canonical revision used for the whole returned view.
- An insight's `canonicalRevision` identifies the projection/admission state that produced the insight. Each event/frame evidence reference separately stores `evidenceCanonicalRevision` for the referenced source row; source revision must not be newer than the insight or selected projection revision. Legacy references without a known source revision remain unreadable and fail closed rather than being assigned an inferred revision. PostgreSQL schema v9 records this distinction.
- Source includes event period and source clock interpretation; adapter maps into monotonic elapsed milliseconds, but retains periodClock, raw provider clock, UTC if supplied, sequence.
- Clock policy handles first/second half, stoppage, extra time/penalty stages when supported; do not globally assume 90:00 is the only possible outcome outside the existing demo.
- For same-timestamp input, strict stable sequence determines order. Reject event `sequence != expected`; explicit quarantine and gap recovery.
- UI context = `(matchId, mode: LiveHead|SelectedTime, atSeconds, canonicalRevision, clientContextRevision, selectedEvidenceId?)`. Every scoreboard/pitch/events/insight/AI response MUST resolve against same snapshot/time+revision.
- The current PostgreSQL read port selects the latest projection with `as_of_clock_seconds <= atSeconds` and `canonical_revision <= requestedRevision`, ordered by elapsed clock, source sequence, admission revision, then projection revision. Same-clock events therefore resolve in their preserved sequence, and future projections are excluded.
- Late correction: append new source revision/supersession event, invalidate projected descendants and provide correction marker. Never patch raw event row; replay by version gives reproducibility.

## C. Pitch coordinates and tracking
Canonical pitch coordinate system = metres, configured length/width (demo 105 × 68 only when feed declares it). Raw stadium orientation and source unit retained with transformation version. Team-relative x increases toward attacking goal:
- `TeamRelativePoint = Transform(raw, teamId, period, attackingDirection, pitchMetres)`. Both teams transformed to the SAME selected reference when computing pressure/space. Mirror x or y invariance tests preserve detector classification.
- Reject NaN/Infinity and out-of-bounds points; admit missing ball/player positions as missing (nullable). Track `sampledAt, sourceVersion, observed|interpolated|missing, uncertainty`.
- 30-second tracking samples represent snapshots, not continuous runs. No line break, body orientation, cover shadow, pressing intensity, pitch control or sprint-speed claim from snapshots alone.
- Map roster participation and subs at selected event time, not shirt-number alone. Player positions, dynamic roles and formation/shape have different inference standards.

## D. Observable canonical event semantics
Event taxonomy includes kickoff, period transition, pass (attempt/complete/incomplete/blocked), carry, reception, pressure attempt, tackle, interception, turnover, ball recovery, shot, goal, foul, card, substitution, offside, set piece and full time where available in feed. Preserve unknown provider event type, quarantine unsupported semantics; never infer it from string fragments.
Per event require participants valid at that time, same match and period, supported outcome, coordinates within pitch and possession transition evidence. Goal must reconcile score: shots do not imply goals, own goal attributed under explicit rules, disallowed/VAR correction append new revision.

## E. Tactical mechanism engine: `press-sequence-v3` design
A **press** is observable pressure that constrains ball carrier space/options; a **trap** requires credible channeling/containment and response; high regain/shot can follow press without proving causality.
Use staged immutable condition evaluations:
1. **Eligibility / trigger:** opponent controlled build-up from goalkeeper/CB, phase classified, source IDs and time; a back-pass is eligibility only, not press.
2. **Pressure exposure:** player-to-carrier separation within governed metres, stable frame timestamps, availability of defender approach trajectory if dense enough; no body orientation inference. Named presser must belong to other team.
3. **Opponent response:** ball action while exposed; supported sequence within `maxResponseMs`, stable sequence/period/possession; pass long/side/failed or dribble; response not proof of forcing.
4. **Regain:** reviewed possession-control event of pressing team, high-zone determined in pressing-team relative coordinates and approved boundary, within `maxRegainMs`.
5. **Exploitation:** same possession, ordered progression/cutback, optional shot and goal, within `maxOutcomeMs`; a goal can strengthen outcome but cannot establish strategy.
6. **Counterexamples and limitations:** press outside exposure window, wrong participant, opponent wins possession, missing frame, incomplete tracking, cross-half boundary, event gap, mislabeled pass, simple turnover without coordinated pressure.
Every stage records `{name,passed,observed,threshold,coverage,evidenceRefs,exclusionReason}`. Scenarios and thresholds come from versioned football policy JSON reviewed by Product Owner and analyst (not unversioned C# literals). Detector output is `evidence-supported-pattern` unless stronger proof is reviewed; never label inferred coach intention as observed fact.

The local simulated v3 implementation also treats an intervening turnover or completed pass as a continuity break while seeking the pressured response and the subsequent regain. It preserves event sequence ordering and the selected-time prefix. This guards a single ordered candidate chain; it does not establish that pressure caused the opponent action, and the policy remains `unapproved-demo` / non-publishable pending football review.
**Earlier `press-sequence-v1`** required seven action events, the same period, arrival within 3 metres, and a goal within 15 seconds of regain. It was a **demonstration-specific policy**, not league-calibrated universal football truth; the current simulated policy is v3 and remains unapproved.

```mermaid
flowchart TD
  E[Validated event + tracking window] --> EL{eligible controlled build-up?}
  EL -->|no| X[No claim / exclusion code]
  EL --> P{credible opponent pressure observed?}
  P -->|missing frames| M[Insufficient evidence]
  P -->|yes| R{response in time window?}
  R -->|no| X
  R --> G{explicit regain and team-relative zone?}
  G -->|no| X
  G --> O{same-possession exploitation?}
  O -->|yes| C[Evidence-supported mechanism]
  O -->|no| Q[Observed pressure/regain only]
  C --> EV[Immutable evidence graph + versions]
  ```

## F. Metrics and measurement policies
Every value: `MetricDefinition {id,version,unit,denominator,clockWindow,teamRelativeDirection,missingness,sourceRevision,algorithmVersion,evidenceIds}`. Never use a metric title alone as spec.
- **PPDA:** qualifying opposition passes in nominated zones / eligible defensive actions there, with zone, event universe and zero-denominator behavior documented. Cannot infer intensity from 1 press event.
- **High regains:** count controlled recoveries in defended opposition zone after coordinate transform, distinct possession IDs; whether an isolated clearance is a recovery is defined.
- **Field tilt:** ratio of eligible final-third touches or possession actions across both teams; explicitly label chosen denominator, not vague possession percentage.
- **Line breaks:** require observed opponent line geometry before-and-after completion; missing/low-frequency tracking -> unavailable. Distinguish nominal formation from actual defensive line.
- **xG:** only if rights-cleared representative calibrated model and required shot features available. Emit unavailable otherwise, not placeholder likelihood.
- **xT:** learned possession-action transition grid with versioned source data, pitch mapping, action success and held-out calibration; prohibited to copy unlicensed `PitchMind_Old` lookup table without provenance.
- **Pitch control/overloads:** dense synchronized tracking, model/arrival-time uncertainty, phase-based orientation and sample coverage; sparse snapshots cannot support quantitative maps.
- **Player impact:** actions attributable to same player and defined minutes/role denominator; no mixing of raw counts and probabilities as an unvalidated rating.
- **Story relevance:** Key Moment candidate with `novelty,importance,matchImpact,evidenceCompleteness,audienceRelevance` per governed policy; duplicate mechanisms deduped, notification cadence bounded. AI never chooses canonical importance by free-text assertion.

## G. Evidence graph and truth classes
Truth hierarchy: `OBSERVED` raw feed and source replay; `DERIVED` reproducible calculation; `SUPPORTED_INTERPRETATION` conditions/alternatives; `HYPOTHESIS` deliberately tentative; `SIMULATED` hypothetical branch; `UNSUPPORTED` suppressed.
`EvidenceGraph` immutable nodes `EventRef, FrameRef, MetricRef, DetectorConditionRef, ClaimRef` and edges `precedes,derived_from,calculated_with,corroborates`. `caused` edge forbidden absent separately documented causal inference evidence. All nodes carry match/clock/revision/schema/detector versions and source hash. Evidence API retrieves exact objects by IDs, enforces selectedTime and auth, caps page size; missing evidence blocks insight publication.
`MatchIntelligence` read-only snapshot: `matchId,selectedTime,revision,score,phase,keyMomentIds,insightSummaries,canonicalEventRefs,evidenceRefs,coverageLimitations`. Agent tool calls cannot update this object.

EP08-S01 local code contract: the internal `MatchIntelligenceContextBuilder` selects a PostgreSQL projection by `(matchId, selectedTime, canonicalRevision?, projectionRevision?)`, then requires the persisted insight's canonical and projection revisions, source hash, as-of sequence and source lineage to match that snapshot. It reads only referenced event/frame payloads through the same selected time and sequence bounds. Persisted summaries over 6 KiB UTF-8 are filtered by PostgreSQL `octet_length` before the summary column is returned; the Application builder enforces the same byte ceiling. `observed`, `derived`, `evidence-supported-pattern`, `supported-interpretation`, `hypothesis` and `simulated` remain separate packet categories; unknown values fail closed. Evidence is capped at 12 items and 6 KiB UTF-8 each; the serialized packet is capped at 32 KiB. The packet retains `sourceCanonicalRevision` per evidence item and distinguishes it from the selected projection's canonical and projection revisions. Unavailable projection, insight, or evidence reads become structured refusals, while cancellation propagates. Scope authorization is a required host port; no allow-all implementation, API route, worker registration, model invocation or canonical write is provided. These unit-tested Application semantics do not prove PostgreSQL integration, trusted host identity binding or an AI response verifier.

## H. PostgreSQL physical schema and transaction plan
```mermaid
erDiagram
  MATCHES ||--o{ MATCH_EVENTS : has
  MATCHES ||--o{ TRACKING_FRAMES : has
  MATCHES ||--o{ MATCH_PROJECTIONS : revisions
  MATCHES ||--o{ INSIGHTS : has
  INSIGHTS ||--o{ INSIGHT_EVIDENCE : references
  MATCH_EVENTS ||--o{ INSIGHT_EVIDENCE : cites
  MATCHES ||--o{ QUARANTINE : records
  MATCHES ||--o{ OUTBOX : emits
  MATCHES ||--o{ CONSUMER_CHECKPOINTS : checkpoints
```
`matches(id PK,fixture_version,source_hash,provenance,pitch_length,width,clock_policy,created_at)`;
`match_events(match_id,event_id,sequence,period,match_ms,team_id,actor_id,type,outcome,source_ptr,payload_hash,schema_version,raw_json,revision,received_at)`, UNIQUE(match_id,sequence), UNIQUE(event_id);
`tracking_frames(match_id,frame_id,match_ms,pitch_version,quality,source_hash,frame_json)`;
`match_projections(match_id,revision,as_of_sequence,as_of_ms,algorithm_version,state_json,sha256)`;
`insights(id,match_id,as_of_sequence,revision,detector_version,truth_class,summary,conditions_json)`;
`insight_evidence(insight_id,kind,event_id?,frame_id?,metric_id?,relation,source_revision)`;
`quarantine(match_id,source_ptr,payload_hash,reason,observed_at,retry_status)`;
`outbox(id,match_id,revision,kind,payload_json,created_at,published_at,attempt_count)`;
`consumer_checkpoints(consumer_id,partition_id,last_offset,last_broker_sequence_number,last_match_id,last_message_id,last_canonical_sequence,updated_at)`.
Indexes begin with `match_id` plus sequence/time/revision; no unlimited cardinality indexes on source JSON; partition only with measured benefit. Schema migrator separate principal, runtime barred from UPDATE/DELETE match_events. Event+projection+evidence+outbox intention writes **one transaction** with per-match advisory lock; checkpoint update after commit; outbox dispatcher publishes at least once and records ack; each consumer inbox idempotent by message ID.

## I. Determinism, reliability, fault handling
- Same source hash/schema + canonical events/frames + clock/transform/algorithm versions -> identical canonical projection hash and evidence graph (exclude wall-clock operational timestamps).
- Duplicate same ID/payload: no-op. Same ID/different payload, out-of-order sequence, impossible period or missing critical participant: quarantine; no silent reordering or gap fill.
- Corrections append a new event/revision, recompute affected windows and invalidate Redis/SignalR cursor presentation; old revision reproducible.
- Replay only reads immutable snapshots and events, uses sandbox session TTL/cost quota; `what-if` actions never write canonical state.
- Privacy: names and licence rights tracked, source pointers may be sensitive; redact secrets in logs and export only authorized evidential payload.
- Observability: input ID, source hash (truncated), match revision, detector version, reason code, lag, idempotency result, evidence coverage and replay hash in traces.

## J. Verification matrix (mandatory for football review)
Positive: both halves and both teams, high press→regain→outcome, no future score leak, correctly ordered same-second events, spatial mirror invariance.
Negative: unpressured back-pass; wrong-team regain; no response; gap/duplicate, event after selectedTime, missing presser frame, invalid pitch; substitution/period boundary; altered orientation; sparse tracking cannot imply speed; excessive causal copy blocked.
Load: 500 eps defined mixture, 1 hot partition + 10 simultaneous matches, duplicate rates and fault injection measured; report actual hardware/p95/p99/quarantine/lag.
Acceptance: football domain PO signs concept/conditions/counterexamples; engineering signs replay determinism/DB integrity; accessibility validates alternative pitch textual explanations; agent red-team validates unsupported claim suppression.
