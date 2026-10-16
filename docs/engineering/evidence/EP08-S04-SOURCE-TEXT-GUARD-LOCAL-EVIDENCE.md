# EP08-S04 source-text guard — local evidence

**Scope:** A deterministic, non-publishing Application guard rejects model-added wording unless a single claim exactly matches the selected canonical insight text and cites the complete evidence set. This is a follow-up implementation slice, not EP08-S04 acceptance.

**Base:** PR #257 was based on `main` at `5b678320ec8c7cd5f04f9a7a3cb8d1749767fb47`, including PR #256 merge `bc08d4b48b13f77cf03a91a4e02f996d967cc770`.
**Merged PR:** [#257](https://github.com/prateekgangopadhyay/PitchMind/pull/257), final reviewed/tested head `217cad5da2c676915ee10cef3e6a157c32146e21`, squash merge `ac3b2a5c2e53473c8fde56ad7bec4da6b82e7fcc`.
**SDK:** .NET 10.0.401.

## Behavior exercised

- Exact selected-insight wording with the complete evidence-ID set is source-bound, and the source `MatchIntelligenceClaimCategory` is preserved.
- Added claims containing an invented score, player, causal attribution, manager intent or future timestamp are rejected.
- Omitting any source evidence citation is rejected.
- Prompt-injection text inside evidence JSON remains data; echoing it into a claim is rejected.
- A well-formed two-claim candidate narrative passes lineage checking but is rejected by the source-text guard.
- Even a successful source-text result has `CanBePublished == false`.

## Verification

| Command | Result |
|---|---|
| `dotnet test tests/PitchMind.Application.Tests/PitchMind.Application.Tests.csproj -c Release --filter FullyQualifiedName~CandidateExplanationSourceTextGuardTests --no-restore --disable-build-servers -m:1` | 10 passed, 0 failed, 0 skipped. Includes the multi-claim regression. Exit code 0. |
| `dotnet test PitchMind.slnx -c Release --no-restore --disable-build-servers -m:1` | 392 passed, 0 failed, 24 skipped. All skips are opt-in PostgreSQL integration tests; no test connection was configured. Exit code 0. |
| `dotnet build PitchMind.slnx -c Release --no-restore -m:1 --disable-build-servers -p:NuGetAudit=false` | Succeeded, 0 warnings, 0 errors. |
| `node scripts/validate_operating_model.mjs --self-test` | Passed. |
| `git diff --check` | Passed. |

VSTest used local execution permission for its loopback socket. The full suite built all projects in Release; the Application suite passed 145/145 on the rebased tree. The test suite does not establish a live PostgreSQL path, hosted CI, Foundry behavior, or cloud integration.

## Remaining blockers

- This guard is exact-source-text binding, not semantic entailment. It trusts the selected persisted insight and does not independently prove that insight is factually/football-valid.
- The guard is not wired to API, UI or persistence; no user-facing candidate is publishable.
- Broader number/player/team/event extraction, approved source semantics, complete false-claim red-team evaluation, caller wiring, hosted CI, and Product Owner acceptance remain open. Independent exact-head review of PR #257 found no blockers; EP08-S04 routing status remains `unverified` and the story is not accepted.
