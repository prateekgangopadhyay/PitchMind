# Canonical event ledger — implemented local slice

`PostgresMatchEventStore` implements `IMatchEventStore`; `PostgresMatchProjectionStore` persists source metadata and reads revisioned state and insight provenance. `PostgresMatchProjectionReader` provides infrastructure-only selected-time snapshot and event-window reads; it bounds by clock, persisted projection sequence and optional maximum revision and validates hashes and metadata before returning data. `PostgresCanonicalUpdateStore` is the aggregate-derived write path: it assigns the next per-match `canonical_revision` while holding a transaction-scoped match lock and writes the event, projection, and stable-ID outbox intent in one transaction. The dispatcher and inbox are separate PostgreSQL adapters and are not registered in the API/EventWorker host. Source admission, public API integration, broker transport and end-to-end feed ingestion remain separate work; the offline API fixture is not backed by this ledger.

Npgsql is pinned to **10.0.3**, verified against the [official release](https://github.com/npgsql/npgsql/releases/tag/v10.0.3). Data source pooling, parameterized SQL and asynchronous transactions follow [official Npgsql usage guidance](https://www.npgsql.org/doc/basic-usage.html).

## Write and read invariants

- Event IDs are globally unique; `(match_id, sequence)` is unique.
- Sequence starts at zero and remains contiguous per match. Gaps, stale sequence and conflicts reject the append; callers must reconcile rather than skip history.
- Each append validates the application contract, takes a transaction-scoped per-match PostgreSQL advisory lock, verifies duplicate payload equality and expected sequence, inserts and commits atomically.
- Exact duplicate appends are successful no-ops. Conflicting duplicates never overwrite history. Cross-match event ID races are rejected by the primary key.
- JSONB payload preserves .NET timestamp precision; the indexed PostgreSQL timestamp normalizes to UTC at PostgreSQL microsecond precision. Reads use the original payload and validate match/sequence integrity.
- Reads order by sequence. A single query sees a consistent PostgreSQL statement snapshot; later appends require another read. The caller cancellation token applies to connection opening, all queries, commit and streaming reads.
- Advisory hash collisions serialize unrelated matches without allowing different histories to merge. This is single-database atomicity, not cross-system exactly-once delivery.

`PostgresMatchProjectionReader.GetAtAsync` returns the latest persisted projection with `as_of_clock_seconds <= selectedSeconds` and, when supplied, `revision <= maximumRevision`; ordering is selected clock, sequence, then revision descending. It re-hashes the deserialized state and checks the stored match, sequence and clock. `ReadEventsAtAsync` re-fetches and validates the referenced projection, uses its persisted sequence as an event ceiling, enforces the selected clock and indexed period/time, and rejects a non-contiguous prefix. Both reads enforce the prototype range 0..5400 seconds. The reader is not wired to an HTTP route or full provider-envelope replay.

`CanonicalUpdate` can be created only from a `MatchAggregate` after the exact latest event was applied. The writer checks contiguous event sequence, assigns `canonical_revision` under the per-match lock, stores that admission revision on event/projection/outbox, and keeps the projection `revision` separately versioned. Exact retries verify event content, projection state hash and outbox payload. `PostgresMatchEventStore.AppendAsync` remains an event-only API and does not create an outbox entry.

## Migration and runtime identities

`PostgresSchemaMigrator.ApplyAsync(migrationDataSource, cancellationToken)` is an **explicit separate migration operation**. Runtime constructors never execute DDL. Version 1 creates the event ledger and schema-version table. Version 2 upgrades the event-type constraint and creates source/match metadata, tracking frames, revisioned projections, insights/evidence, quarantine, checkpoints and outbox tables. Version 3 adds nullable positive `canonical_revision BIGINT` columns and composite lineage constraints; legacy rows remain NULL, while new canonical writes assign a positive revision atomically. The integer `revision` remains the projection revision, distinct from admission revision. Version 4 adds a uniqueness guard for admitted event revisions, ties outbox event IDs to the same canonical event/revision, adds dispatcher lease/retry/dead-letter fields and creates the consumer inbox. Every migration runs under an advisory lock and transaction; v1-v3 semantics remain immutable.
Run migrations with a dedicated migration identity authorized to create schema/tables. The current event API runtime is granted only:

```sql
GRANT USAGE ON SCHEMA pitchmind TO pitchmind_runtime;
GRANT SELECT, INSERT ON pitchmind.match_events TO pitchmind_runtime;
```

Projection, canonical update, dispatcher and inbox adapters require table-specific runtime grants before a host can use them. The migrations do not grant broad access to these tables or add application credentials/role wiring.
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

On the EP04-S02 refreshed branch, `./scripts/test-postgres.sh` executed all seven PostgreSQL integration tests against a uniquely named local database: 7 passed, 0 failed, 0 skipped. Coverage includes fresh/repeated migration, RedCard storage, event-ledger restart/readback, concurrent duplicate/conflict and sequence behavior, cancellation, least-privilege runtime denial, immutable metadata, persisted projection/insight provenance across data-source reopen, and rejection of future-sequence evidence.

On EP04-S03 candidate code commit `44cbee0a275482bd584ff4824b91b9a2496d839a`, the Infrastructure PostgreSQL integration suite passed 15/15 (0 failed, 0 skipped) against a fresh isolated database on local PostgreSQL 16. It covers v2→v4 upgrade, atomic event/projection/outbox rollback, canonical-revision lineage, exact retry, no duplicate goal score, lost-ack retry across dispatcher recreation with inbox dedupe, bounded dead-letter, concurrent claim exclusion, and expired-lease recovery. The full solution suite was not run. Domain 26/26, Application 23/23, Architecture 5/5, operating-model validation, and `git diff --check` passed on this candidate.

The same Infrastructure PostgreSQL suite was rerun after restacking S03 directly on PR #27 head `04685655c7d950252569f255be7468b475da7694`; at local stack commit `a2cc8f7709278993cddc00e7c6b88dececdcb09b`, all 15 tests passed (0 failed, 0 skipped) on a fresh isolated database.

The dispatcher and transaction adapters are not registered in the API/EventWorker. Runtime role grants, broker publisher, checkpoint advancement/rebuild, metrics export/alerts, full source admission, API integration, backup restore, Azure identity and Azure deployment remain unverified. A failed test run during development exposed cross-test interference from earlier unacknowledged outbox rows; each dispatcher scenario now marks pre-existing pending rows complete before setting up its own message. The regression and all 15 PostgreSQL tests then passed on a fresh database.


After adding the per-match predecessor guard and a concurrent-dispatch regression, the Infrastructure PostgreSQL suite passed 16/16 (0 failed, 0 skipped) on a fresh isolated PostgreSQL 16 database. The new test holds revision 1 inside a publisher lease, verifies a second worker cannot claim revision 2, then verifies revision 2 publishes after revision 1 completes. Source change is code commit `c0bb5833ceae51089022796581a996f32850f9a7` on the exact PR #27 base.

On the EP04-S04 restack based on exact PR #28 head `1187428a496d463fbfd76ebe707bf085eb6e69a4`, the Release solution build passed with 0 warnings/errors. The Release solution test run passed 98 tests across Application (23), Architecture (5), Contracts (26), Domain (26) and Infrastructure (18), with 0 failures or skips; PostgreSQL was enabled against a fresh UUID-named local test database and that database was dropped after the run. The focused Infrastructure PostgreSQL run also passed 18/18, 0 failed/skipped. Coverage includes a pass at 3308 and goal at 3309 (no later-goal leakage before 3309), revision ceiling, stable repeated state hash, forged projection-sequence rejection, missing-history null and clock-range rejection. Council operating-model validation passed. The exact tested local code head was `34b7701`; no Azure action was performed and this does not establish human acceptance or full story completion.
