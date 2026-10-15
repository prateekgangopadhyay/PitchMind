# EP03-S04 — durable quarantine evidence and rejection capture

## Scope and current boundary

This slice persists a classified canonical-ledger rejection only when a caller supplies source lineage. It does not wire the current raw-feed producer/API to the use case, repair ordering gaps, retry/redrive an event, or create a reconciliation workflow. Event Hubs checkpoints and candidate-feed canonicalization remain separate work.

## Record contract

`pitchmind.quarantine` records one rejected or held input observation:

- `record_id`: caller-created non-empty UUID. It is not an idempotency key.
- `match_id`: nullable UUID; null is allowed if the source could not be associated with a valid match.
- `source_ptr`: required for every new append as an opaque locator of 1–2048 characters. Historical schema-v2 rows may have NULL pointers; v10 preserves and exposes that legacy absence as null. It may be sensitive and must not be logged or exposed without authorization.
- `payload_hash`: nullable SHA-256 digest encoded as 64 hexadecimal characters. Null means original source bytes were not available for hashing. The raw payload is never stored.
- `reason`: stable uppercase code, 1–100 characters from `A-Z`, `0-9`, and underscore, beginning with a letter. Exception text is not stored.
- `observed_at`: observation instant, stored in PostgreSQL as UTC.
- `retry_status`: reserved nullable text column. This slice writes NULL and defines no values or transitions.

## Source-aware rejection capture

`SourcedMatchEventIngestionService` requires an `IngestionSourceReference` from its caller. It does not derive a source pointer or substitute a hash of the canonical event when source bytes are unavailable. On a typed `MatchEventIngestionException`, it appends the stable failure code and supplied source reference, then rethrows the original failure. Exact duplicate no-ops do not create quarantine records. If evidence persistence fails, `QuarantineEvidenceWriteException` carries the original rejection and persistence exception; the rejected event is not acknowledged as handled. No raw source or event payload reaches the evidence writer.

This is an application seam, not producer wiring. The current API still uses a separate offline/demo path; no candidate source adapter is approved to call this service. Rights, player/clock/coordinate semantics, canonical mappings and football realism remain unverified.

## Schema v10 migration

Current `main` already created `pitchmind.quarantine` in schema v2 with columns `quarantine_id`, `source_pointer`, `payload_sha256`, `reason_code`, and non-null `retry_status`. Schema v10 renames those columns to the evidence contract, makes the hash and retry status nullable, and preserves all existing row values, including any historical NULL source pointers. A NOT VALID constraint keeps those historical rows readable while requiring every new or updated row to have a 1–2048 character source pointer; the application append API enforces the same requirement. It adds new-record validation and append-only UPDATE/DELETE/TRUNCATE triggers. The migration is transactional and leaves prior schema versions unchanged.

Before adding the nonzero record-ID constraint, PostgreSQL validates existing rows. A legacy zero UUID causes v10 to fail and roll back without rewriting or deleting the row; remove or explicitly remediate such a row through an owner-approved data procedure before retrying. The migration does not guess a hash, source pointer, reason, or retry state for old records.

## Verification scope

Tests must cover a repeated fresh migration, a schema-v9 upgrade with a legacy row and preserved values, null-hash appends, exact duplicate no-op, conflict/gap capture with canonical history unchanged, zero-ID migration rollback, and database immutability under a role explicitly granted table mutation privileges. Application tests also cover write failure and source-field allowlisting. These checks do not establish raw-feed producer wiring, Event Hubs behavior, rights/football approval, hosted CI, Product Owner acceptance, or Azure behavior.
