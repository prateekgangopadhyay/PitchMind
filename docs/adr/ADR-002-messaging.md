# ADR-002 — Messaging and reliable publication
Status: Accepted (target), 2026-10-08

**Decision:** PostgreSQL is the canonical admission boundary. It assigns a positive per-match `canonicalRevision` and commits canonical changes plus outbox rows in the same transaction. Only the outbox dispatcher publishes admitted canonical telemetry to Event Hubs, partitioned by matchId and carrying the assigned revision. Raw source candidates do not use the canonical envelope or telemetry channel. Service Bus transports durable background commands (explanation, recap, exports, recovery). The dispatcher publishes idempotently with stable message IDs.

**Consequences:** At-least-once delivery is expected. Consumers deduplicate, bound retries and dead-letter poison messages. Do not claim cross-system exactly-once delivery.
