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

Migration V3 creates the storage table; V4 adds a nonzero record UUID constraint and database triggers that block `UPDATE`, `DELETE`, and `TRUNCATE`, including databases that had already recorded V3. Both run under the existing explicit migration operation and advisory lock and can be applied repeatedly. The adapter exposes append and read only. The canonical event store does not invoke it yet. Runtime role grants and automatic quarantine of classified ingestion failures remain separate work.

Upgrade note: V4 validates existing rows when adding the nonzero UUID constraint. If a V3 database contains a zero record UUID (which the adapter has always rejected, but direct database writes may have bypassed), V4 fails and rolls back without deleting data. Operators should check for that value before applying V4; the migration will not repair or discard the record automatically.

## Verification

The PostgreSQL integration tests prove append/read across a new data source, nullable match/hash behavior, UTC normalization, reserved status remains NULL, malformed hash/reason rejection, database-layer zero UUID rejection, and UPDATE/DELETE/TRUNCATE rejection even for a test role explicitly granted those table privileges. They also prove migration reapplication without duplicate version rows and V3-to-V4 upgrade while preserving a legacy row. A V3 zero-ID row makes V4 fail with a check violation; the test verifies the row and V3 version remain while the V4 version, constraint, function, and triggers are absent, then removes only that test row and confirms a clean V4 retry succeeds. The upgrade tests drop only their owned tables and trigger function, not the schema. They do not prove automatic failure capture, source-specific parsing, deduplication, retry, operator workflow, or reconciliation.
