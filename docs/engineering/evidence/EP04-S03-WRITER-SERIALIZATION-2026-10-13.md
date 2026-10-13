# EP04-S03 aggregate-writer serialization follow-up — 2026-10-13

## Revision and scope

Code revision: `bb8124fa192cbc7914455c1daef51f900d832e49`, originally based on PR #221 head `1b48cf10dfa5aa151e6575e26919966e04b30a03` (itself stacked on PR #209 head `39c9058eda728da3fa489fd90ff8313c0f2df31e`). It was published as PR #222 and merged into PR #221 at `2ae55872f3669921a9d1ef04728533ea33657e8c`.

An independent read-only review of PR #221 at `1b48cf10dfa5aa151e6575e26919966e04b30a03` found two P2 gaps: public `MatchAggregate.Apply` could mutate while `CanonicalMatchEventWriter` awaited persistence, and the previous same-aggregate test did not assert that store commits never overlapped.

The follow-up moves the in-process mutation gate into `MatchAggregate`. The writer holds an aggregate-owned lease across preview, async persistence and apply; public `Apply`, `Preview`, and snapshot methods use that same gate. PostgreSQL's per-match advisory lock remains the cross-process serialization boundary. Tests now assert maximum concurrent store commits is one and verify a direct next-event mutation waits until the canonical commit completes, keeping the persisted preview and in-memory aggregate aligned.

## Executed checks

At the exact code tree later committed as `bb8124fa192cbc7914455c1daef51f900d832e49`:

- Focused `CanonicalMatchEventWriterTests`: **5 passed, 0 failed, 0 skipped**.
- Full `PitchMind.Application.Tests` suite: **28 passed, 0 failed, 0 skipped**.
- Full `PitchMind.Domain.Tests` suite: **28 passed, 0 failed, 0 skipped**.
- `node scripts/validate_operating_model.mjs --self-test`: passed.
- `git diff --check`: passed before the code commit.
- Local restore used the existing NuGet package cache as the sole package source, with `NuGetAudit=false`; no live NuGet audit is claimed.

A full solution restore was attempted separately but has not completed; no solution-wide result is claimed. PostgreSQL tests were not rerun for this in-process locking change. The prior PR #221 PostgreSQL evidence remains tied to its recorded revision and does not test this follow-up.

At combined PR #209 head `692da9a6c62b32926fe1c875f33a0ac08a9ad6ee`:

- `dotnet test tests/PitchMind.Application.Tests/PitchMind.Application.Tests.csproj -c Release --no-restore --disable-build-servers -p:NuGetAudit=false -v:q`: **28 passed, 0 failed, 0 skipped**.
- `dotnet test tests/PitchMind.Domain.Tests/PitchMind.Domain.Tests.csproj -c Release --no-restore --disable-build-servers -p:NuGetAudit=false -v:q`: **28 passed, 0 failed, 0 skipped**.
- Initial Infrastructure run without a test connection: **19 skipped**; this did not provide PostgreSQL integration evidence.
- After confirming the existing local PostgreSQL 16 service and its `pitchmind_postgres_data` volume, `COMPOSE_PROJECT_NAME=pitchmind ./scripts/test-postgres.sh` created and dropped a uniquely named test database: **19 passed, 0 failed, 0 skipped**. The real PostgreSQL integration tests ran on this combined code head without touching other databases.
- The full solution restore/test was not completed on this combined head.

The first Docker check ran before elevated socket access was available. A read-only elevated inspection later confirmed the already-running `pitchmind-postgres-1` PostgreSQL 16 container was healthy and mounted on `pitchmind_postgres_data`; the isolated integration script then completed and removed its test database. No service/container configuration or persistent-volume contents were changed, and no Azure resources were changed.

## Review and remaining

An independent read-only re-review of PR #222 at exact head `ece93ffde8af93a4b777f29d4bc9b0d5ccac062d` confirmed both reported gaps were fixed and found no new actionable issue. A second independent read-only review of PR #221 at exact head `2ae55872f3669921a9d1ef04728533ea33657e8c` confirmed those fixes in the full parent diff; no new code correctness issue was found. An independent review of the combined PR #209 head `692da9a6c62b32926fe1c875f33a0ac08a9ad6ee` found no new actionable implementation defect but requested exact-head and PostgreSQL verification; the reported verification has since been completed for the Application, Domain, and Infrastructure projects on that code head. The reviewers did not run tests or submit formal GitHub reviews. EP04-S03 remains partial and unaccepted. No API/EventWorker registration, broker transport, external fencing, checkpoint/order enforcement, or Azure integration is claimed. No Azure deployment was made.
