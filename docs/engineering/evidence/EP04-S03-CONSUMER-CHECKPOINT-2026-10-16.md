# EP04-S03 PostgreSQL consumer partition checkpoint slice — 2026-10-16

**Code commit:** `b7e2af04` (`feat(EP04-S03): persist consumer partition checkpoints`), based on current GitHub `main` `5f6f3367743bfac754d9b6bba7144091f1baed9a`.

## Implemented

- Added the Application `IConsumerCheckpointStore` contract and PostgreSQL implementation, keyed by consumer and Event Hubs partition.
- Stored the opaque broker offset, broker sequence number, last match/message identity and canonical sequence. The first position can be nonzero; later updates must be contiguous. Identical current positions are idempotent. Backwards positions, gaps and conflicting duplicate positions fail without changing the cursor.
- Added transactional schema migration v11. It preserves the unused v10 match-level checkpoint table as `consumer_checkpoints_legacy_v10`; no broker offset is inferred from its old sequence.
- Added PostgreSQL regressions for migration preservation, contiguous/idempotent advancement, stale/gap/conflicting positions, failed inbox effect rollback, and redelivery after an effect commit but before checkpoint advancement.
- Updated the canonical-ledger migration guide, ADR-002, C4 container boundary note, football LLD, EP04-S03 packet and existing execution register.

## Verification observed on the code tree

- `./scripts/test-postgres.sh` against the existing healthy LOCAL PostgreSQL service: Infrastructure **93 passed, 0 failed, 0 skipped**; PostgreSQL readiness/API contracts **154 passed, 0 failed, 0 skipped**. The harness created and dropped a unique test database. The normal database, containers and volumes were not recreated or modified.
- `dotnet test PitchMind.slnx -c Release --no-restore -m:1 --disable-build-servers -v minimal`: **522 passed, 0 failed, 41 skipped** across the solution. The skipped tests were the 40 opt-in Infrastructure PostgreSQL checks and one PostgreSQL readiness/API check; the PostgreSQL harness above executed those project suites without skips.
- `node scripts/validate_operating_model.mjs`: passed; 12 epics, 59 stories, 28 Engineering Council skills, 5 design skills, no dependency cycles, 2 approval gates.
- `git diff --check`: passed before the code commit.

## Remaining limits and status

This is a PostgreSQL cursor persistence primitive. No Event Hubs consumer calls it; no Event Hubs SDK checkpoint operation, checkpoint rebuild/reconciliation, Azure runtime grant, Azure connectivity, hosted CI, load/lag measurement, or cloud deployment was executed. It does not prove Event Hubs delivery or cloud restart recovery. Exact-head independent technical review and Product Owner acceptance remain pending. EP04-S03 remains unverified and unaccepted. No Azure resource or deployment was changed.


## Independent exact-head rerun — 2026-10-16

The following verification was independently repeated on code/evidence head `d06a2e1a10d78b541635c52e0491ca335ed606b2` (code implementation commit `b7e2af04`):

- `./scripts/test-postgres.sh`: Infrastructure **93 passed, 0 failed, 0 skipped**; PostgreSQL readiness/API contracts **154 passed, 0 failed, 0 skipped**. This rerun used disposable Compose project `pm_pr305_20261016_164a`, uniquely named PostgreSQL/Redis volumes and loopback ports. The harness created and removed its uniquely named test database. Then only those scratch containers, network and volumes were removed; the existing owner database and volumes were not contacted. Temporary credentials were deleted.
- `dotnet test PitchMind.slnx -c Release -m:1 --disable-build-servers -p:NuGetAudit=false`: **522 passed, 0 failed, 41 skipped**. The 40 opt-in Infrastructure PostgreSQL tests and one readiness test were executed by the no-skip harness above. The first sandboxed VSTest invocation aborted before test execution because loopback IPC binding was denied; rerunning the same command with local test-host permission passed.
- `node scripts/validate_operating_model.mjs`: passed; `git diff --check`: passed.

This establishes local exact-head test evidence only. It does not close the documented runtime/Event Hubs/cloud integration gaps, independent human review, or Product Owner acceptance.
