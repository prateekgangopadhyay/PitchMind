# Canonical event ledger — implemented local slice

`PostgresMatchEventStore` implements the existing `IMatchEventStore` using a shared `NpgsqlDataSource`. It records **MatchEvent only**. The synthetic fixture's team/player/action metadata and tracking frames are not yet persisted by this adapter. The offline API fixture is not backed by this ledger; do not describe the complete match/evidence read model as durable until those contracts and integrations are implemented.

Npgsql is pinned to **10.0.3**, verified against the [official release](https://github.com/npgsql/npgsql/releases/tag/v10.0.3). Data source pooling, parameterized SQL and asynchronous transactions follow [official Npgsql usage guidance](https://www.npgsql.org/doc/basic-usage.html).

## Write and read invariants

- Event IDs are globally unique; `(match_id, sequence)` is unique.
- Sequence starts at zero and remains contiguous per match. Gaps, stale sequence and conflicts reject the append; callers must reconcile rather than skip history.
- Each append validates the application contract, takes a transaction-scoped per-match PostgreSQL advisory lock, verifies duplicate payload equality and expected sequence, inserts and commits atomically.
- Exact duplicate appends are successful no-ops. Conflicting duplicates never overwrite history. Cross-match event ID races are rejected by the primary key.
- JSONB payload preserves .NET timestamp precision; the indexed PostgreSQL timestamp normalizes to UTC at PostgreSQL microsecond precision. Reads use the original payload and validate match/sequence integrity.
- Reads order by sequence. A single query sees a consistent PostgreSQL statement snapshot; later appends require another read. The caller cancellation token applies to connection opening, all queries, commit and streaming reads.
- Advisory hash collisions serialize unrelated matches without allowing different histories to merge. This is single-database atomicity, not cross-system exactly-once delivery.

`PostgresCanonicalUpdateStore` is the separate aggregate update path: it accepts a `CanonicalUpdate` created only from a `MatchAggregate` after the exact latest event was applied, checks the event sequence and projection revision, and writes the event, projection state/hash and outbox intent in one transaction. The outbox references `(match_id, revision)` in `match_projections`; its stable message ID is the event ID. Exact retries require the matching event, revision state hash and outbox JSON, otherwise they reject as inconsistent. The existing `PostgresMatchEventStore.AppendAsync` remains an event-only API and does not create a projection or outbox entry.

`PostgresMatchProjectionReader.GetAtAsync` returns the newest persisted state whose selected clock is not after the requested time and whose revision is no greater than the optional revision ceiling. It verifies the stored projection hash and match/sequence/clock metadata. `ReadEventsAtAsync` caps by both projection sequence and selected clock, validates each event against its indexed clock/period and rejects a broken prefix. This reader is not yet wired to an HTTP route or to full source-envelope replay.

## Migration and runtime identities

`PostgresSchemaMigrator.ApplyAsync(migrationDataSource, cancellationToken)` is an **explicit separate migration operation**. Runtime constructors never execute DDL. Version 1 creates the event ledger and schema-version table. Version 2 upgrades the event-type constraint to match the stable enum range and creates source/match metadata, tracking-frame, revisioned projection, insight/evidence, quarantine, checkpoint and outbox tables with selected-time indexes. Version 3 adds dispatcher lease/retry/dead-letter state and a consumer inbox. Version 4 adds nullable JSON limitations metadata for persisted insights and constrains it to a JSON array; the read adapter bounds the stored conditions/limitations payloads before exposure. Each migration runs under an advisory lock in a transaction and records its version. Version 1–3 migration semantics are immutable; future changes must use new versions. `PostgresCanonicalUpdateStore` now commits a validated event, one projection revision and a stable-ID outbox intent atomically. `PostgresOutboxDispatcher` uses at-least-once publication with leases and bounded retry; `PostgresConsumerInbox` couples deduplication to a same-database consumer effect. These adapters are not registered in the API/EventWorker host, and no cloud transport adapter is wired.

Run migrations with a dedicated migration identity authorized to create schema/tables. The current event API runtime is granted only:

```sql
GRANT USAGE ON SCHEMA pitchmind TO pitchmind_runtime;
GRANT SELECT, INSERT ON pitchmind.match_events TO pitchmind_runtime;
```

The local transaction and dispatcher adapters need table-specific runtime grants before a host can use them. The migration does not grant broad access to new tables or add application credentials/role wiring.

Do not grant table ownership, schema CREATE, UPDATE, DELETE, TRUNCATE, or the migration identity to deployed runtime. Azure managed identity/token authentication and private networking are separate integration gates. Local owner-role tests do not prove Azure RBAC or network deployment.

## Verification

`scripts/test-postgres.sh` is the mandatory local/CI verification entrypoint after `scripts/dev-up.sh` and `scripts/dev-health.sh`. It reads resolved Compose credentials into memory without printing them, creates a uniquely named isolated database, executes the integration tests, rejects skipped/zero-executed results, and removes the database afterward. Python 3, Docker and the .NET SDK are required. Set `PITCHMIND_TEST_RESULTS_DIRECTORY` to retain `postgres.trx` for CI publication; otherwise result files use a temporary directory. Run:

```sh
./scripts/test-postgres.sh
```

`tests/PitchMind.Infrastructure.Tests` also supports direct opt-in PostgreSQL runs. Set `PITCHMIND_TEST_POSTGRES` to an **isolated local test database** connection string and run:

```sh
dotnet test tests/PitchMind.Infrastructure.Tests/PitchMind.Infrastructure.Tests.csproj
```

Without that environment variable, tests explicitly skip and must not be reported as verified. Test setup runs the migration; the privilege test needs local role-creation permission. Tests create fresh match IDs and never truncate existing history. The role test grants an ephemeral runtime role, verifies append/read, denies UPDATE/DELETE/CREATE TABLE, and removes its grants/role afterward.

Historical evidence on the base branch covered a Compose PostgreSQL 17 container and isolated `pitchmind_ledger_test` database for version 1. On the EP04-S02 branch, `./scripts/test-postgres.sh` executed six PostgreSQL integration tests against a uniquely named local database. On the EP04-S03 branch, it executed 13: 13 passed, 0 failed, 0 skipped. Coverage includes upgrade from populated schema v2; atomic event/projection/outbox rollback on injected outbox failure; exact retry idempotency; lost-ack redelivery after dispatcher recreation; consumer inbox deduplication; bounded dead-letter; concurrent claim exclusion; and expired lease recovery after worker cancellation. Runtime role grants for the new adapters, production broker integration, metrics export/alerting, backup restore, Azure identity and Azure deployment still require separate evidence.
