# ADR-015 — Event Hubs ingress and post-commit fan-out

**Status:** Proposed target architecture; implementation and independent review pending. This supersedes ADR-002's outbound-only Event Hubs flow as the desired target, but does not describe or change the current code.

## Context

The simulation/source feed must publish ordered source events to the event service first. Event Hubs is the ingress transport. The current EP04-S03 slice instead dispatches already-persisted PostgreSQL outbox messages to Event Hubs and consumes those messages into a rebuildable projection. That implementation remains unchanged until an independently reviewed implementation slice replaces or extends it.

PostgreSQL remains the canonical football data store. A cache or SignalR message must never become authoritative, and a transport checkpoint must never advance beyond data that is durably admitted or durably quarantined.

## Decision

1. A source adapter streams source events to Event Hubs, partitioned by canonical match identity while preserving source event ID, source sequence, provenance, and source match-time semantics.
2. An EventWorker consumes at least once, validates schema and source profile, and performs canonical mapping/admission.
3. In one PostgreSQL transaction, admission persists the canonical event/revision and its transactional outbox record. The outbox row records the committed revision and stable idempotency identity. The Event Hubs Blob checkpoint advances only after this transaction commits, or after a durable quarantine decision commits. Failed database work leaves the input eligible for redelivery.
4. Outbox handlers run independently and may fan out in parallel after commit. The cache handler writes revision-fenced, rebuildable projections. The realtime handler publishes only match identity, revision, and sequence cursors through authorized SignalR groups. Clients use the BFF to obtain canonical snapshots and evidence.
5. Outbox handlers retry independently. A cache/SignalR outage cannot roll back canonical admission or advance the consumer checkpoint incorrectly. Cache loss falls back to PostgreSQL; realtime loss falls back to polling/backfill.

If a durable pre-admission staging record is required, it is an **inbox/staging ledger**, not the transactional outbox. The transactional outbox belongs in the same PostgreSQL transaction as the canonical change it publishes.

## Consequences and guarantees

- Event Hubs is ingress in the target flow; it is not the canonical store.
- PostgreSQL is the canonical correctness boundary and atomically binds canonical changes to outbound work.
- Delivery is at least once. Consumers and projection handlers must be idempotent; cross-service exactly-once delivery is not claimed.
- Cache and SignalR publication happen after canonical commit and can proceed concurrently. SignalR carries cursors, not football facts.
- Stable source identity, sequence, provenance and match-time meanings are preserved; no unverified feed semantics are silently normalized.
- The existing EP04-S03 publisher/consumer is not evidence that this target has been implemented or deployed. Update code, tests, API/realtime contracts, diagrams and ADRs in the implementation PR.

## Required verification before acceptance

- Duplicate, out-of-order, poison, malformed, and transient DB failure cases preserve idempotency and safe checkpoint behavior.
- Transaction tests prove canonical event/revision and outbox rows commit or roll back together.
- Independent cache and SignalR dispatch tests prove retries, revision fencing, authorization, reconnect/backfill, and outage isolation.
- Source-profile tests validate units, coordinate axes/orientation, player metric units, canonical identity mappings, provenance, and match-clock/period semantics before candidate feed admission.
- No Azure deployment is implied or authorized by this target decision.
