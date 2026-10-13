# EP04-S03 aggregate-writer serialization follow-up — 2026-10-13

## Revision and scope

Code revision: `bb8124fa192cbc7914455c1daef51f900d832e49`, based on exact PR #221 head `1b48cf10dfa5aa151e6575e26919966e04b30a03` (itself stacked on PR #209 head `39c9058eda728da3fa489fd90ff8313c0f2df31e`). This is a local feature branch prepared for a draft stacked PR; it has not been pushed or independently approved.

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

## Review and remaining

Independent re-review of `bb8124fa192cbc7914455c1daef51f900d832e49` and exact-head GitHub review remain pending. EP04-S03 remains partial and unaccepted. No API/EventWorker registration, broker transport, external fencing, checkpoint/order enforcement, or Azure integration is claimed. No Azure resources or deployments were changed.
