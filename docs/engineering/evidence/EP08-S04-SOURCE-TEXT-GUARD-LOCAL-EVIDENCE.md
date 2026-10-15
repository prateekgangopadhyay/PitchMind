# EP08-S04 source-text guard — local evidence

**Scope:** A deterministic, non-publishing Application guard rejects model-added wording unless a single claim exactly matches the selected canonical insight text and cites the complete evidence set. This is a follow-up implementation slice, not EP08-S04 acceptance.

**Base:** PR #256 merge `bc08d4b48b13f77cf03a91a4e02f996d967cc770`.
**Implementation code head:** `d5c893e54a78117ed2564877c228d61c7ec00d2c` on `codex/ep08-s04-source-bound-verifier`.
**SDK:** .NET 10.0.401.

## Behavior exercised

- Exact selected-insight wording with the complete evidence-ID set is source-bound, and the source `MatchIntelligenceClaimCategory` is preserved.
- Added claims containing an invented score, player, causal attribution, manager intent or future timestamp are rejected.
- Omitting any source evidence citation is rejected.
- Prompt-injection text inside evidence JSON remains data; echoing it into a claim is rejected.
- More than one candidate claim is rejected by the lineage gate before source-text matching.
- Even a successful source-text result has `CanBePublished == false`.

## Verification

| Command | Result |
|---|---|
| `dotnet test tests/PitchMind.Application.Tests/PitchMind.Application.Tests.csproj -c Release --filter FullyQualifiedName~CandidateExplanationSourceTextGuardTests --no-restore --disable-build-servers -m:1` | 10 passed, 0 failed, 0 skipped. Includes the multi-claim regression. Exit code 0. |
| `dotnet test PitchMind.slnx -c Release --no-restore --disable-build-servers -m:1` | 367 passed, 0 failed, 24 skipped. All skips are opt-in PostgreSQL integration tests; no test connection was configured. Exit code 0. |
| `dotnet build PitchMind.slnx -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` | Succeeded, 0 warnings, 0 errors. |
| `node scripts/validate_operating_model.mjs --self-test` | Passed. |
| `git diff --check` | Passed. |

VSTest used local execution permission for its loopback socket. The full suite built all projects in Release. The test suite does not establish a live PostgreSQL path, hosted CI, Foundry behavior, or cloud integration.

## Remaining blockers

- This guard is exact-source-text binding, not semantic entailment. It trusts the selected persisted insight and does not independently prove that insight is factually/football-valid.
- The guard is not wired to API, UI or persistence; no user-facing candidate is publishable.
- Broader number/player/team/event extraction, approved source semantics, complete false-claim red-team evaluation, independent review of the updated head and Product Owner acceptance remain open. EP08-S04 routing status remains `unverified`.
