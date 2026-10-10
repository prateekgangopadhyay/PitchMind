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

## Migration and runtime identities

`PostgresSchemaMigrator.ApplyAsync(migrationDataSource, cancellationToken)` is an **explicit separate migration operation**. Runtime constructors never execute DDL. Version 1 creates `pitchmind.match_events` and the migration version ledger inside an advisory-locked transaction. Version 2 expands the event-type check from the historical v1 values 0–7 to the current `MatchEventType` values 0–10. The v1 SQL remains unchanged so existing installations upgrade through a distinct, recorded migration.

Before a new append, PostgreSQL checks whether the existing match ledger is contiguous from sequence zero. If it finds a gap (for example, legacy rows 0 and 2), it holds every new nonduplicate append at the first missing sequence. This prevents appending 3 and creating a ledger that subsequent reads reject. Writes resume only after an explicit reconciliation repairs the history; this adapter does not perform that repair.

Run migrations with a dedicated migration identity authorized to create schema/tables. Grant runtime only:

```sql
GRANT USAGE ON SCHEMA pitchmind TO pitchmind_runtime;
GRANT SELECT, INSERT ON pitchmind.match_events TO pitchmind_runtime;
```

Do not grant table ownership, schema CREATE, UPDATE, DELETE, TRUNCATE, or the migration identity to deployed runtime. Azure managed identity/token authentication and private networking are separate integration gates. Local owner-role tests do not prove Azure RBAC or network deployment.

## Verification

`scripts/test-postgres.sh` is the mandatory local/CI verification entrypoint after `scripts/dev-up.sh` and `scripts/dev-health.sh`. It reads resolved Compose credentials into memory without printing them, creates a uniquely named isolated database, executes the integration tests, rejects skipped/zero-executed results, and removes the database afterward. Python 3, Docker and the .NET SDK are required. Set `PITCHMIND_TEST_RESULTS_DIRECTORY` to retain `postgres.trx` for CI publication; otherwise result files use a temporary directory. Run:

```sh
./scripts/test-postgres.sh
```

`tests/PitchMind.Infrastructure.Tests` also supports direct opt-in PostgreSQL runs. The test fixture validates the connection before constructing any data source: host must be exactly `localhost`, `127.0.0.1` or `::1`, and database must be `pitchmind_test_<32-hex>`. Remote hosts, host lists, service-file targets, and ordinary database names are rejected; the `Source()` helper repeats the guard before each connection. The guard cannot establish that an operator-created matching database is disposable, so prefer the wrapper below, which creates a fresh random database and drops it in `finally`. Direct runs must point only at a disposable local database:

```sh
dotnet test tests/PitchMind.Infrastructure.Tests/PitchMind.Infrastructure.Tests.csproj
```

Without that environment variable, database integration tests explicitly skip and must not be reported as verified. If a configured target fails the local/disposable-name guard, those facts skip and direct data-source creation throws before connecting. The migration regression removes only its two owned tables (`pitchmind.match_events` and `pitchmind.schema_versions`) without `CASCADE`; it never drops the schema. Test setup runs the migration; the privilege test needs local role-creation permission. Tests create fresh match IDs. The role test grants an ephemeral runtime role, verifies append/read, denies UPDATE/DELETE/CREATE TABLE, and removes its grants/role afterward.

Actual local run: existing Compose PostgreSQL 17 container, isolated `pitchmind_ledger_test` database. Evidence covers persistence after opening a new data source, twelve concurrent identical appends, conflicting payloads, sequence gaps/conflicts, cancellation, and runtime write restrictions. Database restart, backup restore, telemetry metadata durability, migration upgrade/rollback, Azure identity and Azure deployment still require separate evidence.

Current EP03-S04 restack verification on exact PR #24 base `fd164f76a963e3157189115b69f0b824009d8a98` passed the Release solution suite (91/91, 0 failed/skipped; 18 PostgreSQL integration facts executed) and focused Infrastructure PostgreSQL suite (18/18, 0 failed/skipped). Tests used a fresh local `pitchmind_test_<32-hex>` database on the existing Compose service and dropped only that database; the existing Postgres volume was preserved. This verifies local adapter and test-target safeguards, not durable quarantine, reconciliation, feed/broker integration or Azure deployment.


## EP03-S04 quarantine evidence (local slice)

Migration V3 adds `pitchmind.quarantine` for immutable evidence, with a caller-created record UUID, nullable match UUID, opaque sensitive source pointer, optional SHA-256 hash, stable reason code, observation timestamp, and nullable reserved `retry_status`. A database trigger rejects updates/deletes and a check rejects the zero UUID. See [the EP03-S04 quarantine design note](../engineering/stories/EP03/EP03-S04-QUARANTINE-RECORD-DESIGN.md) for exact semantics. This storage API is not yet called by `PostgresMatchEventStore`; no retry, replay, or reconciliation behavior is implemented.
