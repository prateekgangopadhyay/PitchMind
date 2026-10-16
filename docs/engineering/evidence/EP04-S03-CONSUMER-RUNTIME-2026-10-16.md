# EP04-S03 Event Hubs consumer runtime evidence — 2026-10-16

**Status:** Local code slice only. EP04-S03 remains partial, unverified, and unaccepted pending exact-head independent review and Product Owner acceptance.

**Stack:** Draft child PR targets the PR #305 feature branch (`codex/ep04-s03-inbox-checkpoint-atomic`). Base code head before this slice: `d06a2e1a`. Implementation and design commit with the verified code tree: `1c46ca250d0a6aae8c6a4fdb12ee6250bfa49298`. This evidence file is in a documentation-only follow-up commit.

## Implementation

- `ProjectionConsumerWorker` optionally runs `EventProcessorClient` in the Azure-only EventWorker when explicitly enabled and supplied a consumer group, consumer ID, Azure Blob service URI, and checkpoint container.
- `EventHubProjectionEventHandler` validates message ID, UTF-8 JSON, `ProjectionPublishedV1`, and matching Event Hubs application properties before handling.
- Schema v12 adds a payload-bound consumer inbox and a latest-per-match rebuildable snapshot table. `PostgresProjectionPublishedConsumer` commits the receipt and snapshot in one PostgreSQL transaction. A same-ID/same-payload replay is idempotent; changed payload reuse, revision gaps, and conflicting equal revisions fail closed. Old already-recorded revisions do not replace latest state.
- After the database effect commits, the handler advances the PostgreSQL partition cursor, then the EventProcessorClient Azure Blob checkpoint. These are ordered operations across PostgreSQL and Blob Storage, not one distributed transaction. Redelivery after a later checkpoint failure is deduplicated by the inbox. The snapshot is a read model and never mutates canonical football event/projection tables.
- Event Hubs Processor, Blob Storage and Azure Identity package locks were updated. No Azure resource, identity assignment, network connection, or deployment was used.

## Verification performed

- `dotnet build src/PitchMind.EventWorker/PitchMind.EventWorker.csproj -c Release --no-restore --no-incremental -m:1 /p:UseSharedCompilation=false -v:minimal` — passed, 0 warnings and 0 errors.
- Focused Release `EventHubOutboxPublisherTests` — 21 passed, 0 failed, 0 skipped.
- `bash scripts/test-postgres.sh` — Infrastructure 102 passed, 0 failed, 0 skipped; Contracts PostgreSQL readiness/API suite 154 passed, 0 failed, 0 skipped. The harness used the existing LOCAL Docker/PostgreSQL services and an isolated temporary database, then cleaned up the temporary database. It did not alter normal data volumes.
- `dotnet test PitchMind.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false` — 528 passed, 0 failed, 44 skipped. Skips were opt-in PostgreSQL cases and the database readiness check because that full-suite invocation did not set up the isolated database; the PostgreSQL harness above executed its database suites without skips.
- `dotnet build PitchMind.slnx -c Release --no-restore -m:1 -p:UseSharedCompilation=false -v:minimal` — passed, 0 warnings and 0 errors.
- `node scripts/validate_operating_model.mjs` — passed (12 epics, 59 stories, 28 Council skills, no dependency cycles).
- `git diff --check` — passed.

## Not verified / acceptance status

No live Event Hubs delivery, Azure Blob checkpoint, Azure managed-identity auth, cloud PostgreSQL, hosted CI, restart/rebuild operation, measured load/lag, accessibility review, security review, independent exact-head review, or Product Owner acceptance was performed. PR remains draft. Story remains unaccepted. No Azure resources or deployments were changed.
