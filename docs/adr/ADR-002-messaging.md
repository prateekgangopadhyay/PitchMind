# ADR-002 — Messaging and reliable publication
Status: Accepted (target), 2026-10-08

**Decision:** Event Hubs ingests telemetry, partitioned by matchId. Service Bus transports durable background commands (explanation, recap, exports, recovery). Canonical changes and outbox rows commit in the same PostgreSQL transaction. Outbox dispatcher publishes idempotently with stable message IDs.

**Consequences:** At-least-once delivery is expected. Consumers deduplicate, bound retries and dead-letter poison messages. Do not claim cross-system exactly-once delivery.
