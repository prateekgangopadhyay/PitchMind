# EP03-S01 seeded scenario code slice — 2026-10-14

## Change

Code head `7b52d6ff949fd41b53e1f8c564cad65a1b71fdd8` extends the test-only `SyntheticFixtureGenerator` scenario introduced at `e4cf50424bb94d593c7f4d58dbb2c87ea4d72d83`.

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

An initial independent scenario review identified the seed/team mismatch, missing named negative fixtures, and lack of an explicit scenario score-transition assertion. This commit addresses those findings. Exact-head independent re-review is pending. Product Owner football acceptance is pending. No full solution test, PostgreSQL integration test, hosted CI run, canonical feed admission, Azure operation, or deployment was performed.

The EP03-S01 implementation and acceptance remain open. The remaining feed semantics and source quality gates are documented in the [candidate profile](../../data/MANU-LIV-SOURCE-PROFILE.md) and [story packet](../stories/EP03/EP03-S01.md).
