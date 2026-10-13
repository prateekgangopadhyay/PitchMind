# Canonical event ledger — implemented local slice

`PostgresMatchEventStore` implements `IMatchEventStore`; `PostgresMatchProjectionStore` persists source metadata and reads revisioned state and insight provenance. `PostgresCanonicalUpdateStore` is the aggregate-derived write path: while holding a transaction-scoped match lock, it verifies the aggregate’s predecessor state hash, sequence, clock, source identity and provenance against the immediately preceding persisted projection (and validates the initial state for sequence zero). It then assigns the next per-match `canonical_revision` and writes the event, projection, and stable-ID outbox intent in one transaction. The dispatcher and inbox are separate PostgreSQL adapters and are not registered in the API/EventWorker host. Source admission, public API integration, broker transport and end-to-end feed ingestion remain separate work; the offline API fixture is not backed by this ledger.

Npgsql is pinned to **10.0.3**, verified against the [official release](https://github.com/npgsql/npgsql/releases/tag/v10.0.3). Data source pooling, parameterized SQL and asynchronous transactions follow [official Npgsql usage guidance](https://www.npgsql.org/doc/basic-usage.html).

## Write and read invariants

- Event IDs are globally unique; `(match_id, sequence)` is unique.
- Sequence starts at zero and remains contiguous per match. Gaps, stale sequence and conflicts reject the append; callers must reconcile rather than skip history.
- Each append validates the application contract, takes a transaction-scoped per-match PostgreSQL advisory lock, verifies duplicate payload equality and expected sequence, inserts and commits atomically.
- Exact duplicate appends are successful no-ops. Conflicting duplicates never overwrite history. Cross-match event ID races are rejected by the primary key.
- JSONB payload preserves .NET timestamp precision; the indexed PostgreSQL timestamp normalizes to UTC at PostgreSQL microsecond precision. Reads use the original payload and validate match/sequence integrity.
- Reads order by sequence. A single query sees a consistent PostgreSQL statement snapshot; later appends require another read. The caller cancellation token applies to connection opening, all queries, commit and streaming reads.
- Advisory hash collisions serialize unrelated matches without allowing different histories to merge. This is single-database atomicity, not cross-system exactly-once delivery.

`CanonicalUpdate` can be created only from a `MatchAggregate` after the exact latest event was applied, and carries the aggregate’s immutable predecessor snapshot. The writer checks that snapshot against persisted predecessor projection state under the per-match lock, checks contiguous event sequence, assigns `canonical_revision`, stores that admission revision on event/projection/outbox, and keeps the projection `revision` separately versioned. Exact retries verify event content, projection state hash and outbox payload. `PostgresMatchEventStore.AppendAsync` remains an event-only API and does not create an outbox entry.

## Migration and runtime identities

`PostgresSchemaMigrator.ApplyAsync(migrationDataSource, cancellationToken)` is an **explicit separate migration operation**. Runtime constructors never execute DDL. Version 1 creates the event ledger and schema-version table. Version 2 upgrades the event-type constraint and creates source/match metadata, tracking frames, revisioned projections, insights/evidence, quarantine, checkpoints and outbox tables. Version 3 adds nullable positive `canonical_revision BIGINT` columns and composite lineage constraints; legacy rows remain NULL, while new canonical writes assign a positive revision atomically. The integer `revision` remains the projection revision, distinct from admission revision. Version 4 adds the `NOT VALID` projection-to-source provenance foreign key. Version 5 adds a uniqueness guard for admitted event revisions, ties outbox event IDs to the same canonical event/revision, adds dispatcher lease/retry/dead-letter fields and creates the consumer inbox. Every migration runs under an advisory lock and transaction; v1-v3 semantics remain immutable.
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


On local follow-up code commit `1991f03e1e22b58fa2f59d3463ce5092169a0f0c`, the Infrastructure PostgreSQL harness passed 18/18 (0 failed, 0 skipped) including rejection of a canonical update built from divergent prior aggregate history; see [follow-up evidence](../engineering/evidence/EP04-S03-RESTACK-2026-10-13.md).

On child code/test revision `ae82807173a2f0b35b5b9bb15f79dd58b260cd5f`, stacked on exact PR #209 head `39c9058eda728da3fa489fd90ff8313c0f2df31e`, the application writer persists an aggregate preview before advancing the in-memory aggregate, and the dispatcher renews its lease during active publication. The full .NET solution suite passed 86, failed 0, with 19 PostgreSQL opt-in tests skipped in the database-free invocation. Separately, the full Infrastructure PostgreSQL suite passed 19/19 (0 failed, 0 skipped) on the preserved local PostgreSQL 16 volume, using a uniquely named isolated database that was dropped after testing. The long-publication regression holds publish 300 ms under a 60 ms lease and confirms a competing dispatcher cannot claim the message. The API/EventWorker still does not register this writer/dispatcher. Lease renewal is not an external fencing token; a publisher that ignores cancellation can still complete an external side effect after lease loss, so consumers must deduplicate stable message IDs.
