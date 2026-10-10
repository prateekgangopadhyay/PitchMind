# EP03-S04 — durable quarantine evidence slice

## Scope

The existing story requires conflicting event IDs to be quarantined and gaps or backwards sequences to be held for reconciliation. The target LLD names `quarantine(match_id,source_ptr,payload_hash,reason,observed_at,retry_status)`, but does not define a source-pointer format, hash algorithm, reason vocabulary, nullability, or retry/reconciliation state machine. This slice defines storage semantics only. It does not change event admission, disposition classification, retry, replay, reconciliation, or canonical event history.

## Record contract

`pitchmind.quarantine` is an append-only record of one rejected/held input observation:

- `record_id`: caller-created non-empty UUID identifying this observation. No deduplication or retry idempotency is implied.
- `match_id`: nullable UUID. Null permits evidence to be recorded before a valid match identity can be parsed.
- `source_ptr`: required opaque locator, 1–2048 characters. It is not interpreted by storage and may be sensitive; do not log or expose it without authorization.
- `payload_hash`: nullable SHA-256 digest encoded as 64 hexadecimal characters. Null means source bytes were unavailable for hashing. Raw payload is not persisted.
- `reason`: required stable uppercase code, 1–100 characters from `A-Z`, `0-9`, and underscore, beginning with a letter. Free-form exception text is not stored.
- `observed_at`: required observation instant; the adapter normalizes it to UTC on write.
- `retry_status`: reserved nullable text column from the target LLD. This slice always writes NULL and defines no legal values or transitions.

The migration is version 3, runs under the existing explicit migration operation and advisory lock, and can be applied repeatedly. Storage can append and read one record; the canonical event store does not invoke it yet. Runtime role grants and automatic quarantine of classified ingestion failures remain separate work.

## Verification

The PostgreSQL integration tests prove append/read across a new data source, nullable match/hash behavior, UTC normalization, reserved status remains NULL, malformed hash/reason rejection, and migration reapplication without duplicate version rows. They do not prove automatic failure capture, source-specific parsing, deduplication, retry, operator workflow, or reconciliation.
