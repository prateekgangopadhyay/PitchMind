# EP04-S03 consumer composition and poison isolation — 2026-10-16

**Code head:** `21a9a15594b34c838c69f2c852c5fe2b741d8b6d` (PR #309 stacked branch).  
**Status:** Local implementation evidence only. PR remains draft; independent exact-head review and Product Owner acceptance are pending.

## Change

- Registered `IConsumerCheckpointStore` and `IProjectionPoisonStore` in the opt-in projection consumer composition. A composition test resolves the consumer handler without opening Azure connections.
- Added schema v13 poison records keyed by consumer, partition, and broker sequence. Records store the offset, payload SHA-256, stable reason code, exception class, attempt count, and pending/replayed/rejected disposition; raw payload bytes are not stored.
- Known permanent envelope, transport metadata, and projection-state failures are recorded before the PostgreSQL cursor and SDK checkpoint advance. Store or other transient failures still propagate without advancing checkpoints.
- A corrected replay can replace a poison cursor at the same broker sequence. The old poison remains auditable with `replayed` disposition; repeat poison delivery does not move a cursor that has already advanced.

## Executed verification

- Focused Release tests for composition and durable poison-before-broker-checkpoint behavior: **2 passed, 0 failed**.
- `./scripts/test-postgres.sh`: Infrastructure **104 passed, 0 failed, 0 skipped**; Contracts/PostgreSQL readiness **154 passed, 0 failed, 0 skipped**. It created and dropped a uniquely named local test database using the existing Docker PostgreSQL service. The new integration test verifies poison persistence, duplicate attempt count, corrected replay at the same position, and progress to the following event.
- `dotnet test PitchMind.slnx -c Release --no-restore -m:1 --disable-build-servers`: **529 passed, 0 failed, 45 skipped**. Skips were the opt-in PostgreSQL tests in this database-free invocation; the PostgreSQL harness above ran both affected database suites without skips.
- `node scripts/validate_operating_model.mjs`: passed (12 epics, 59 stories, 28 Council skills, 0 dependency cycles).
- `git diff --check`: passed before code commit.

## Limits and outstanding work

No Azure Event Hubs or Blob Storage service was contacted; no SDK checkpoint was executed against a live broker. The runtime grant for the new poison table is not verified. Poison disposition persistence exists, but operator replay tooling and an Azure Blob checkpoint-reset procedure are not implemented or verified. The code only demonstrates that a corrected event at the same broker position can replace the poison marker. No deployment was performed. Story remains partial/unaccepted pending independent review, runtime permissions and operational recovery evidence.
