# Canonical telemetry contracts v1 (EP03-S02)

`schemas/telemetry/v1/` and `PitchMind.Contracts.Telemetry.V1` define version 1 of the transport-neutral event envelope and tracking frame. `schemaVersion` is mandatory and unsupported versions fail with `UNSUPPORTED_SCHEMA_VERSION`; consumers must add an explicit version adapter before accepting a newer version.

## Event envelope

An event has a canonical `matchId`, stable `eventId`, zero-based contiguous canonical `sequence`, explicit match time, known event type, the unmodified `sourceEventType`, optional team/player/related-player/outcome fields, and a required source reference. Player actions require both `teamId` and `playerId`; period markers and kickoff can be playerless. A substitution requires both `playerId` and `relatedPlayerId` to retain the two participant identities without guessing a direction such as outgoing or incoming. Supplied optional text fields must contain a non-whitespace character. `SourceReferenceV1` retains the original source match/record IDs, source sequence, source schema version, source artifact SHA-256, optional UTC-offset-zero source timestamp, provenance status, and independent rights-review status. Canonical sequence conversion never overwrites source sequence or identity.

Match time has an explicit period, non-negative elapsed seconds, and the original numeric clock value plus unit and basis. There is no hard-coded 90-minute or 5,400-second ceiling. A source adapter must not calculate elapsed time from provider clocks until that provider's clock semantics are declared and tested.

## Tracking frame and coordinates

A frame has stable match/frame identity, canonical sequence/time, ball and player points, source reference, pitch length/width in metres, declared point unit (`metres`, `normalized_0_1`, or `percent_0_100`), declared axis origin, and explicit attacking direction for each player team. Validation rejects unsupported/missing units, out-of-range positions, duplicate players, missing team directions, and unsupported schema versions with stable error codes and field paths. Coordinate normalization first converts to bottom-left-origin stadium metres; `ToAttackingXMetres` then uses the declared team direction. The transform is deterministic and does not infer source units or orientation.

## Candidate-feed boundary

The owner-confirmed JSON is still marked `candidate_not_canonical`. Its current manifest leaves coordinate unit/orientation, player metrics, canonical mappings, independent rights/privacy review, and football realism unapproved. This contract does not map any provider event label to a canonical type, approve candidate coordinates, present metric-like fields as player facts, or write data to PostgreSQL. A source-specific adapter remains blocked until those decisions are evidenced. The v1 vocabulary is a contract vocabulary only, not approval of all candidate source semantics.

## Evolution and failure handling

The JSON Schemas are the language-neutral wire definitions and constrain required strings against whitespace-only values and timestamp offsets against non-zero UTC offsets. Runtime validation returns a list of `{code,path,message}` errors for deterministic rejection/quarantine; it performs cross-field checks such as event participants, coordinate ranges, duplicate players, and team-direction coverage. Schema tests parse both JSON documents and assert the declared string, timestamp, and substitution rules; they do not run a general JSON Schema engine. Breaking meaning or shape changes require a new schema version and compatibility adapter. Raw source lineage, event ID, source sequence, source clock, and source hash remain attached when normalized values are created.

## Verification

`tests/PitchMind.Contracts.Tests/TelemetryContractTests.cs` covers serialization identity, schema incompatibility, unknown event type, invalid period, required and whitespace-only values, non-zero timestamp offsets, incomplete substitutions, schema declaration checks, invalid position/unit, missing attacking direction, and explicit left/right second-half transforms. These tests use generic contract values and do not certify the Manchester United–Liverpool candidate feed.
