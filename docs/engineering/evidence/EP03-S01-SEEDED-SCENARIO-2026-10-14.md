# EP03-S01 seeded scenario code slice — 2026-10-14

## Change

The current stacked PR head `6f369fd` includes the test-only `SyntheticFixtureGenerator` scenario introduced at `7904b3f` and its review-gap fixes at `4a891de`. After rebasing the child onto PR #234, the focused suites and harness were rerun at `6f369fd`; only evidence wording changed after those runs.

The possession annotation spanning 3240–3290 seconds now takes its team from the seeded circulation event at 3270 seconds. This avoids a mismatch for odd seeds. Four named negative fixture variants cover missing tracking, a completed opponent pass, a regain assigned to the wrong team, and a distant press arrival. Application coverage asserts none produces the positive pressing insight. A scenario replay test asserts score values immediately before and after both goals, including full time, to make the selected-time/no-lookahead requirement explicit.

These are authored fictional **test fixtures**. The candidate `data/simulations/manu-liv-match-dataset.json` was not read by the generator or transformed. Nothing in this slice admits that feed to canonical storage or establishes vendor semantics, football realism, source mappings, or production match data.

## Verification executed locally

| Command | Result |
|---|---:|
| `dotnet test tests/PitchMind.Domain.Tests/PitchMind.Domain.Tests.csproj --no-restore -c Release -m:1 /nodeReuse:false` | 12 passed, 0 failed, 0 skipped |
| `dotnet test tests/PitchMind.Application.Tests/PitchMind.Application.Tests.csproj --no-restore -c Release -m:1 /nodeReuse:false` | 13 passed, 0 failed, 0 skipped |
| `dotnet test tests/PitchMind.Contracts.Tests/PitchMind.Contracts.Tests.csproj --no-restore -c Release -m:1 /nodeReuse:false` | 32 passed, 0 failed, 0 skipped |
| `python3 scripts/api-contract-integration.py` | passed health/fail-closed, validation/media, size boundary, and rate-limit checks |
| `node scripts/validate_operating_model.mjs` | passed: 12 epics, 59 stories, 28 Council skills, 5 design skills, 33 discoverable skills, 0 dependency cycles |
| `git diff --check` | passed |

The .NET test projects used existing restored assets and built the current project sources. VSTest and the API integration harness bind loopback sockets; sandboxed attempts aborted before running, then the same local commands passed with socket permission. NuGet audit was not part of these `--no-restore` test runs. The API integration process confirmed that unimplemented canonical match reads still fail closed with 503.

## Review and acceptance

An initial independent scenario review identified the seed/team mismatch, missing named negative fixtures, and lack of an explicit scenario score-transition assertion. This commit addresses those findings. Exact-head independent review of PR #235 head `0933b801e76b879a756951e9cd906fd51eb8971b` found no actionable P0–P2 issue; the reviewer did not run tests. PR #235 was squash-merged into PR #234’s feature branch as `b1dc998acbf627deea75725cf93437098af6f1be` on 2026-10-14. PR #234 was subsequently squash-merged to `main` as `c38fa3514b4c1c5e66124cb22519db277f5af429` on 2026-10-14. Product Owner football acceptance remains pending. No full solution test, PostgreSQL integration test, hosted CI run, canonical feed admission, Azure operation, or deployment was performed.

The EP03-S01 implementation and acceptance remain open. The remaining feed semantics and source quality gates are documented in the [candidate profile](../../data/MANU-LIV-SOURCE-PROFILE.md) and [story packet](../stories/EP03/EP03-S01.md).
