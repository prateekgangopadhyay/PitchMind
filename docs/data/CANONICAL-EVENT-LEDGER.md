# Canonical event ledger — implemented local slice

`PostgresMatchEventStore` implements `IMatchEventStore`; `PostgresMatchProjectionStore` persists source metadata and reads revisioned state and insight provenance. `PostgresCanonicalUpdateStore` is the aggregate-derived write path: while holding a transaction-scoped match lock, it verifies the aggregate’s predecessor state hash, sequence, clock, source identity and provenance against the immediately preceding persisted projection (and validates the initial state for sequence zero). It then assigns the next per-match `canonical_revision` and writes the event, projection, and stable-ID outbox intent in one transaction. EP06-S01 adds a local-only canonical BFF read path; authentication/cloud exposure, broker transport and end-to-end candidate-feed ingestion remain separate work. The offline API fixture is not backed by this ledger.

Npgsql is pinned to **10.0.3**, verified against the [official release](https://github.com/npgsql/npgsql/releases/tag/v10.0.3). Data source pooling, parameterized SQL and asynchronous transactions follow [official Npgsql usage guidance](https://www.npgsql.org/doc/basic-usage.html).

## Write and read invariants

- Event IDs are globally unique; `(match_id, sequence)` is unique.
- Sequence starts at zero and remains contiguous per match. Gaps, stale sequence and conflicts reject the append; callers must reconcile rather than skip history.
- Each append validates the application contract, takes a transaction-scoped per-match PostgreSQL advisory lock, verifies duplicate payload equality and expected sequence, inserts and commits atomically.
- Exact duplicate appends are successful no-ops. Conflicting duplicates never overwrite history. Cross-match event ID races are rejected by the primary key.
- JSONB payload preserves .NET timestamp precision; the indexed PostgreSQL timestamp normalizes to UTC at PostgreSQL microsecond precision. Reads use the original payload and validate match/sequence integrity.
- Reads order by sequence. A single query sees a consistent PostgreSQL statement snapshot; later appends require another read. The caller cancellation token applies to connection opening, all queries, commit and streaming reads.
- Advisory hash collisions serialize unrelated matches without allowing different histories to merge. This is single-database atomicity, not cross-system exactly-once delivery.
- Insight `canonical_revision` identifies the revision that produced the insight; `insight_evidence.evidence_canonical_revision` identifies the admission revision of each referenced event/frame. Reads require a resolvable source row, source revision no later than the insight/selected revision, and source sequence/time visible to both. Schema v9 adds this distinct source-revision key; legacy rows without source-revision metadata are excluded from canonical API responses.

`CanonicalUpdate` can be created only from a `MatchAggregate` after the exact latest event was applied, and carries the aggregate’s immutable predecessor snapshot. The writer holds an aggregate-owned mutation lease across preview, asynchronous commit and apply; ordinary `Apply`, preview and snapshot methods use the same gate. This prevents an in-process caller from changing the aggregate after its preview is persisted but before the writer advances it. The writer checks that snapshot against persisted predecessor projection state under the per-match PostgreSQL lock, checks contiguous event sequence, assigns `canonical_revision`, stores that admission revision on event/projection/outbox, and keeps the projection `revision` separately versioned. The aggregate lease is process-local and does not replace database serialization across writers in different processes. Exact retries verify event content, projection state hash and outbox payload. `PostgresMatchEventStore.AppendAsync` remains an event-only API and does not create an outbox entry.

For EP04-S04, canonical updates retain the exact applied `SimulationEvent` envelope, including team/player/action/outcome fields needed for deterministic replay; replay seed v2 persists the initial roster, source provenance and explicit competition rules. `PostgresMatchProjectionReader` reads the selected projection, source metadata, event envelopes and bounded tracking frames in one PostgreSQL statement snapshot. This is an internal query component and is not wired into the API/EventWorker runtime.

## Migration and runtime identities

`PostgresSchemaMigrator.ApplyAsync(migrationDataSource, cancellationToken)` is an **explicit separate migration operation**. Runtime constructors never execute DDL. Version 1 creates the event ledger and schema-version table. Version 2 upgrades the event-type constraint and creates source/match metadata, tracking frames, revisioned projections, insights/evidence, quarantine, checkpoints and outbox tables. Version 3 adds nullable positive `canonical_revision BIGINT` columns and composite lineage constraints; legacy rows remain NULL, while new canonical writes assign a positive revision atomically. The integer `revision` remains the projection revision, distinct from admission revision. Version 4 adds the `NOT VALID` projection-to-source provenance foreign key. Version 5 adds a uniqueness guard for admitted event revisions, ties outbox event IDs to the same canonical event/revision, adds dispatcher lease/retry/dead-letter fields and creates the consumer inbox. Version 6 adds nullable competition-rules metadata. Version 7 adds nullable replay seed and source-event envelope columns; version 8 adds nullable replay context. Legacy replay fields remain NULL and are not backfilled with inferred facts. Every migration runs under an advisory lock and transaction; v1-v3 semantics remain immutable.
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

Actual local run: existing Compose PostgreSQL 17 container, isolated `pitchmind_ledger_test` database. Evidence covers persistence after opening a new data source, twelve concurrent identical appends, conflicting payloads, sequence gaps/conflicts, cancellation, and runtime write restrictions. Database restart, backup restore, telemetry metadata durability, migration upgrade/rollback, Azure identity and Azure deployment still require separate evidence.

Latest observed run on 2026-10-12: `./scripts/test-postgres.sh` passed the full infrastructure test project (23 passed, 0 failed, 0 skipped), including the five PostgreSQL tests against a uniquely named isolated database removed by the script. The existing local service was healthy before the run; no Compose up/down, migration against the normal database, or Azure operation was performed.
